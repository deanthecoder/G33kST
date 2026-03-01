// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;
using DTC.Emulation;
using DTC.Emulation.Snapshot;
using JetBrains.Annotations;
using System.Diagnostics;

namespace DTC.AtariST;

/// <summary>
/// Minimal memory-mapped ACIA/IKBD block for early ST bring-up.
/// Supports keyboard byte injection and basic relative mouse packet reporting.
/// </summary>
public sealed class AciaIkbdDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FFFC00;
    private const byte ReceiveDataReadyFlag = 0x01;
    private const byte TransmitDataEmptyFlag = 0x02;
    private const byte InterruptRequestFlag = 0x80;
    private const byte MasterResetControlMask = 0x03;
    private const byte MousePacketHeaderBase = 0xF8;
    private const byte IkbdSetMouseButtonActionCommand = 0x07;
    private const byte IkbdSetRelativeMouseModeCommand = 0x08;
    private const byte IkbdSetAbsoluteMouseModeCommand = 0x09;
    private const byte IkbdSetMouseKeycodeModeCommand = 0x0A;
    private const byte IkbdSetMouseThresholdCommand = 0x0B;
    private const byte IkbdSetMouseScaleCommand = 0x0C;
    private const byte IkbdInterrogateMousePositionCommand = 0x0D;
    private const byte IkbdLoadMousePositionCommand = 0x0E;
    private const byte IkbdSetYZeroBottomCommand = 0x0F;
    private const byte IkbdSetYZeroTopCommand = 0x10;
    private const byte IkbdResumeOutputCommand = 0x11;
    private const byte IkbdDisableMouseReportingCommand = 0x12;
    private const byte IkbdPauseOutputCommand = 0x13;
    private const byte IkbdSetJoystickEventModeCommand = 0x14;
    private const byte IkbdSetJoystickInterrogateModeCommand = 0x15;
    private const byte IkbdInterrogateJoystickStateCommand = 0x16;
    private const byte IkbdSetJoystickMonitoringModeCommand = 0x17;
    private const byte IkbdSetJoystickFireButtonMonitoringCommand = 0x18;
    private const byte IkbdSetJoystickCursorKeycodesCommand = 0x19;
    private const byte IkbdDisableJoysticksCommand = 0x1A;
    private const byte IkbdSetClockCommand = 0x1B;
    private const byte IkbdReadClockCommand = 0x1C;
    private const byte IkbdMemoryLoadCommand = 0x20;
    private const byte IkbdMemoryReadCommand = 0x21;
    private const byte IkbdExecuteCommand = 0x22;
    private const byte IkbdReportMouseButtonActionCommand = 0x87;
    private const byte IkbdReportMouseModeRelativeCommand = 0x88;
    private const byte IkbdReportMouseModeAbsoluteCommand = 0x89;
    private const byte IkbdReportMouseModeKeycodeCommand = 0x8A;
    private const byte IkbdReportMouseThresholdCommand = 0x8B;
    private const byte IkbdReportMouseScaleCommand = 0x8C;
    private const byte IkbdReportMouseVerticalBottomCommand = 0x8F;
    private const byte IkbdReportMouseVerticalTopCommand = 0x90;
    private const byte IkbdReportMouseAvailabilityCommand = 0x92;
    private const byte IkbdReportJoystickModeEventCommand = 0x94;
    private const byte IkbdReportJoystickModeInterrogateCommand = 0x95;
    private const byte IkbdReportJoystickModeKeycodeCommand = 0x99;
    private const byte IkbdReportJoystickAvailabilityCommand = 0x9A;
    private const byte IkbdStatusResponseHeader = 0xF6;
    private const byte IkbdClockResponseHeader = 0xFC;
    private const byte JoystickInterrogationHeader = 0xFD;
    private const byte JoystickPacketHeaderPort0 = 0xFE;
    private const byte JoystickPacketHeaderPort1 = 0xFF;
    private const byte JoystickBitUp = 0x01;
    private const byte JoystickBitDown = 0x02;
    private const byte JoystickBitLeft = 0x04;
    private const byte JoystickBitRight = 0x08;
    private const byte JoystickBitFire = 0x80;
    private const byte CursorUpScanCode = 0x48;
    private const byte CursorLeftScanCode = 0x4B;
    private const byte CursorRightScanCode = 0x4D;
    private const byte CursorDownScanCode = 0x50;
    private const byte Joystick0FireScanCode = 0x74;
    private const byte IkbdResetCommand = 0x80;
    private const byte IkbdResetParameter = 0x01;
    private const byte IkbdResetCompleteCode = 0xF1;
    private const byte IkbdMouseModeRelative = 0x08;
    private const byte IkbdMouseButtonActionKeycodeBit = 0x04;
    private const byte IkbdMouseLeftButtonScanCode = 0x74;
    private const byte IkbdMouseRightButtonScanCode = 0x75;
    private const long ClockResponseHeaderDelayCpuTicks = 56_000; // ~7ms at 8MHz.
    private const long ClockResponseInterByteDelayCpuTicks = 10_500; // ~1.3ms at 8MHz (rough IKBD serial byte time).
    private readonly Lock m_stateSync = new();
    private readonly Queue<byte> m_keyboardReceiveQueue = [];
    private readonly Queue<ReceiveByteKind> m_keyboardReceiveKinds = [];
    private readonly ulong[] m_loggedIkbdCommandCodes = new ulong[4];
    private readonly byte[] m_lastJoystickStateBytes = new byte[2];
    private readonly byte[] m_clockBytes = [0x87, 0x01, 0x01, 0x00, 0x00, 0x00];
    private readonly byte[] m_pendingClockResponseBytes = new byte[6];
    private readonly byte[] m_pendingCommandParameters = new byte[8];
    private byte[] m_receiveQueueScratch = new byte[16];
    private ReceiveByteKind[] m_receiveKindScratch = new ReceiveByteKind[16];
    private bool m_mouseReportingEnabled = true;
    private bool m_outputPaused;
    private bool m_joystickEventModeEnabled = true;
    private bool m_joystickMonitoringModeEnabled;
    private bool m_joystickDisabled;
    private byte m_pendingCommand;
    private int m_pendingCommandParameterCount;
    private int m_pendingCommandParameterIndex;
    private int m_pendingMemoryLoadByteCount;
    private byte m_mouseMode = IkbdMouseModeRelative;
    private byte m_mouseButtonActionMode;
    private byte m_mouseThresholdX = 1;
    private byte m_mouseThresholdY = 1;
    private byte m_mouseScaleX = 1;
    private byte m_mouseScaleY = 1;
    private byte m_keyboardControl;
    private byte m_keyboardDataRegisterLatch;
    private bool m_isMouseYZeroTop = true;
    private bool m_lastMouseLeftButtonPressed;
    private bool m_lastMouseRightButtonPressed;
    private volatile bool m_keyboardInterruptLineActive;
    private volatile bool m_keyboardInterruptReassertPending;
    private volatile bool m_hasDeferredAdvanceWork;
    private volatile bool m_isDeferredInterruptReassertEnabled = true;
    private volatile bool m_isJoystickInterrogateCoalescingEnabled = true;
    private uint m_loggedUnsupportedFeatureWarnings;
    private int m_queuedKeyboardInjectedByteCount;
    private int m_queuedMousePacketByteCount;
    private int m_queuedJoystickEventByteCount;
    private int m_queuedJoystickInterrogateResponseByteCount;
    private int m_queuedClockResponseByteCount;
    private int m_peakReceiveQueueCount;
    private long m_clockLastHostTimestamp = Stopwatch.GetTimestamp();
    private long m_pendingClockResponseDelayCpuTicks;
    private int m_pendingClockResponseNextByteIndex;
    private bool m_hasPendingClockResponse;

    public AciaIkbdDevice()
    {
        SeedClockFromHostTimeNoLock();
    }

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + 7;

    /// <summary>
    /// Raised when keyboard receive data becomes available.
    /// </summary>
    public event EventHandler KeyboardDataReady;

    /// <summary>
    /// Raised when the keyboard ACIA interrupt line changes state.
    /// The value is <c>true</c> when the line is active (asserted low).
    /// </summary>
    public event Action<bool> KeyboardInterruptLineChanged;

    /// <summary>
    /// Gets the number of pending IKBD receive bytes.
    /// </summary>
    public int PendingReceiveQueueCount
    {
        get
        {
            lock (m_stateSync)
                return m_keyboardReceiveQueue.Count;
        }
    }

    /// <summary>
    /// Gets the number of queued bytes that are joystick interrogate responses.
    /// </summary>
    public int PendingJoystickInterrogateResponseByteCount
    {
        get
        {
            lock (m_stateSync)
                return m_queuedJoystickInterrogateResponseByteCount;
        }
    }

    /// <summary>
    /// Gets the number of queued bytes originating from injected keyboard scan codes.
    /// </summary>
    public int PendingKeyboardInjectedByteCount
    {
        get
        {
            lock (m_stateSync)
                return m_queuedKeyboardInjectedByteCount;
        }
    }

    /// <summary>
    /// Gets the number of queued bytes belonging to IKBD mouse packets.
    /// </summary>
    public int PendingMousePacketByteCount
    {
        get
        {
            lock (m_stateSync)
                return m_queuedMousePacketByteCount;
        }
    }

    /// <summary>
    /// Gets the number of queued bytes belonging to IKBD joystick event packets.
    /// </summary>
    public int PendingJoystickEventByteCount
    {
        get
        {
            lock (m_stateSync)
                return m_queuedJoystickEventByteCount;
        }
    }

    /// <summary>
    /// Gets the number of queued bytes belonging to IKBD clock response packets.
    /// </summary>
    public int PendingClockResponseByteCount
    {
        get
        {
            lock (m_stateSync)
                return m_queuedClockResponseByteCount;
        }
    }

    /// <summary>
    /// Gets the peak receive-queue depth since the last reset/clear.
    /// </summary>
    public int PeakReceiveQueueCount
    {
        get
        {
            lock (m_stateSync)
                return m_peakReceiveQueueCount;
        }
    }

    /// <summary>
    /// Enables or disables deferred keyboard ACIA interrupt reassertion.
    /// </summary>
    public void SetDeferredInterruptReassertEnabled(bool isEnabled)
    {
        lock (m_stateSync)
        {
            m_isDeferredInterruptReassertEnabled = isEnabled;
            if (isEnabled)
                return;

            m_keyboardInterruptReassertPending = false;
            m_hasDeferredAdvanceWork = false;
            RefreshInterruptLineNoLock();
        }
    }

    /// <summary>
    /// Enables or disables queued joystick interrogation-response coalescing.
    /// </summary>
    public void SetJoystickInterrogateCoalescingEnabled(bool isEnabled) =>
        m_isJoystickInterrogateCoalescingEnabled = isEnabled;

    /// <summary>
    /// Clears any pending IKBD output bytes.
    /// </summary>
    public void ClearReceiveQueue()
    {
        lock (m_stateSync)
        {
            m_keyboardReceiveQueue.Clear();
            m_keyboardReceiveKinds.Clear();
            m_keyboardInterruptReassertPending = false;
            m_hasDeferredAdvanceWork = false;
            m_hasPendingClockResponse = false;
            m_pendingClockResponseDelayCpuTicks = 0;
            m_pendingClockResponseNextByteIndex = 0;
            ResetQueueByteCountersNoLock();
            RefreshInterruptLineNoLock();
        }
    }

    /// <summary>
    /// Advances deferred ACIA line transitions.
    /// This keeps multi-byte IKBD responses flowing one IRQ edge at a time rather than
    /// collapsing low->high reasserts into the same read call.
    /// </summary>
    public void Advance()
    {
        AdvanceDelayedOutputNoLock(0);
        AdvanceDeferredInterruptReassertOnly();
    }

    /// <summary>
    /// Advances delayed IKBD output scheduling and deferred ACIA line transitions using CPU ticks.
    /// </summary>
    public void Advance(long deltaCpuTicks)
    {
        if (deltaCpuTicks < 0)
            deltaCpuTicks = 0;

        AdvanceDelayedOutputNoLock(deltaCpuTicks);
        AdvanceDeferredInterruptReassertOnly();
    }

    private void AdvanceDeferredInterruptReassertOnly()
    {
        if (!m_isDeferredInterruptReassertEnabled)
            return;
        if (!m_hasDeferredAdvanceWork)
            return;
        TryReassertKeyboardInterruptFast();
    }

    /// <summary>
    /// Injects one keyboard data byte into the receive queue.
    /// </summary>
    public void QueueKeyboardByte(byte value)
    {
        lock (m_stateSync)
        {
            DropStaleTransientInputBacklogForKeyboardNoLock();
            EnqueueKeyboardByteNoLock(value, ReceiveByteKind.KeyboardInjected);
        }
    }

    /// <summary>
    /// Queues one IKBD relative mouse packet (header + signed X/Y deltas).
    /// </summary>
    public void QueueRelativeMousePacket(sbyte deltaX, sbyte deltaY, bool isLeftButtonPressed, bool isRightButtonPressed)
    {
        lock (m_stateSync)
        {
            if (!m_mouseReportingEnabled || m_outputPaused || m_mouseMode != IkbdMouseModeRelative)
                return;

            if ((m_mouseButtonActionMode & IkbdMouseButtonActionKeycodeBit) != 0)
            {
                QueueMouseButtonActionKeycodesNoLock(isLeftButtonPressed, isRightButtonPressed);
                return;
            }

            m_lastMouseLeftButtonPressed = isLeftButtonPressed;
            m_lastMouseRightButtonPressed = isRightButtonPressed;
            var header = MousePacketHeaderBase;
            if (isRightButtonPressed)
                header |= 0x01;
            if (isLeftButtonPressed)
                header |= 0x02;

            EnqueueKeyboardByteNoLock(header, ReceiveByteKind.MousePacket);
            EnqueueKeyboardByteNoLock((byte)deltaX, ReceiveByteKind.MousePacket);
            EnqueueKeyboardByteNoLock((byte)deltaY, ReceiveByteKind.MousePacket);
        }
    }

    private void QueueMouseButtonActionKeycodesNoLock(bool isLeftButtonPressed, bool isRightButtonPressed)
    {
        if (isLeftButtonPressed != m_lastMouseLeftButtonPressed)
        {
            var leftCode = isLeftButtonPressed
                ? IkbdMouseLeftButtonScanCode
                : (byte)(IkbdMouseLeftButtonScanCode | 0x80);
            EnqueueKeyboardByteNoLock(leftCode);
        }

        if (isRightButtonPressed != m_lastMouseRightButtonPressed)
        {
            var rightCode = isRightButtonPressed
                ? IkbdMouseRightButtonScanCode
                : (byte)(IkbdMouseRightButtonScanCode | 0x80);
            EnqueueKeyboardByteNoLock(rightCode);
        }

        m_lastMouseLeftButtonPressed = isLeftButtonPressed;
        m_lastMouseRightButtonPressed = isRightButtonPressed;
    }

    /// <summary>
    /// Queues one IKBD joystick event packet for joystick 0 or 1.
    /// </summary>
    public void QueueJoystickState(byte joystickIndex, JoystickState state)
    {
        if (joystickIndex > 1)
            return;

        state = state.NormalizeOpposingDirections();

        var stateByte = (byte)0;
        if (state.IsUpPressed)
            stateByte |= JoystickBitUp;
        if (state.IsDownPressed)
            stateByte |= JoystickBitDown;
        if (state.IsLeftPressed)
            stateByte |= JoystickBitLeft;
        if (state.IsRightPressed)
            stateByte |= JoystickBitRight;
        if (state.IsFirePressed)
            stateByte |= JoystickBitFire;

        lock (m_stateSync)
        {
            DropStaleTransientInputBacklogForJoystickNoLock();
            var previousStateByte = m_lastJoystickStateBytes[joystickIndex];
            m_lastJoystickStateBytes[joystickIndex] = stateByte;
            if (m_outputPaused)
                return;
            if (m_joystickDisabled)
                return;
            if (IsJoystickCursorKeycodeModeNoLock())
            {
                QueueJoystickCursorKeycodesNoLock(joystickIndex, previousStateByte, stateByte);
                return;
            }

            if (m_joystickMonitoringModeEnabled)
            {
                QueueJoystickMonitoringPacketNoLock();
                return;
            }

            if (!m_joystickEventModeEnabled)
                return;

            var header = joystickIndex == 0 ? JoystickPacketHeaderPort0 : JoystickPacketHeaderPort1;
            EnqueueKeyboardByteNoLock(header, ReceiveByteKind.JoystickEvent);
            EnqueueKeyboardByteNoLock(stateByte, ReceiveByteKind.JoystickEvent);
        }
    }

    /// <summary>
    /// Resets ACIA/IKBD transient state.
    /// </summary>
    public void Reset()
    {
        lock (m_stateSync)
        {
            m_keyboardReceiveQueue.Clear();
            m_keyboardReceiveKinds.Clear();
            m_keyboardControl = 0;
            m_keyboardInterruptLineActive = false;
            m_pendingCommand = 0;
            m_pendingCommandParameterCount = 0;
            m_pendingCommandParameterIndex = 0;
            m_mouseReportingEnabled = true;
            m_outputPaused = false;
            m_mouseMode = IkbdMouseModeRelative;
            m_keyboardDataRegisterLatch = 0;
            m_mouseButtonActionMode = 0;
            m_mouseThresholdX = 1;
            m_mouseThresholdY = 1;
            m_mouseScaleX = 1;
            m_mouseScaleY = 1;
            m_isMouseYZeroTop = true;
            m_lastMouseLeftButtonPressed = false;
            m_lastMouseRightButtonPressed = false;
            m_joystickEventModeEnabled = true;
            m_joystickMonitoringModeEnabled = false;
            m_joystickDisabled = false;
            m_pendingMemoryLoadByteCount = 0;
            m_lastJoystickStateBytes[0] = 0;
            m_lastJoystickStateBytes[1] = 0;
            m_keyboardInterruptReassertPending = false;
            m_hasDeferredAdvanceWork = false;
            m_hasPendingClockResponse = false;
            m_pendingClockResponseDelayCpuTicks = 0;
            m_pendingClockResponseNextByteIndex = 0;
            if (!TryGetClockDateTimeNoLock(out _))
                SeedClockFromHostTimeNoLock();
            ResetQueueByteCountersNoLock();
            Array.Clear(m_loggedIkbdCommandCodes, 0, m_loggedIkbdCommandCodes.Length);
            EnqueueKeyboardByteNoLock(IkbdResetCompleteCode);
        }
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        lock (m_stateSync)
        {
            var register = GetRegister(address);
            if (register == AciaRegister.None)
                return 0xFF;
            if (register == AciaRegister.KeyboardStatus)
                return BuildStatusByte(isKeyboardPort: true);
            if (register == AciaRegister.MidiStatus)
                return BuildStatusByte(isKeyboardPort: false);
            if (register == AciaRegister.KeyboardData)
                return ReadKeyboardDataNoLock();
            if (register == AciaRegister.MidiData)
                return 0x00;
            return 0xFF;
        }
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        lock (m_stateSync)
        {
            var register = GetRegister(address);
            if (register == AciaRegister.None)
                return;
            if (register is AciaRegister.KeyboardStatus or AciaRegister.MidiStatus)
            {
                HandleControlWriteNoLock(register, value);
                return;
            }

            if (register == AciaRegister.KeyboardData)
                HandleKeyboardDataWriteNoLock(value);
        }
    }

    private byte BuildStatusByte(bool isKeyboardPort)
    {
        var status = TransmitDataEmptyFlag;
        if (!isKeyboardPort)
            return status;

        if (m_keyboardReceiveQueue.Count > 0)
        {
            status |= ReceiveDataReadyFlag;
            if ((m_keyboardControl & InterruptRequestFlag) != 0)
                status |= InterruptRequestFlag;
        }

        return status;
    }

    private byte ReadKeyboardDataNoLock()
    {
        if (m_keyboardReceiveQueue.Count == 0)
            return m_keyboardDataRegisterLatch;

        var value = m_keyboardReceiveQueue.Dequeue();
        var byteKind = m_keyboardReceiveKinds.Dequeue();
        DecrementQueuedByteKindCountNoLock(byteKind);
        if (m_isDeferredInterruptReassertEnabled && ShouldDeferInterruptReassertNoLock(byteKind))
        {
            m_keyboardInterruptReassertPending = true;
            m_hasDeferredAdvanceWork = true;
            if (m_keyboardInterruptLineActive)
            {
                m_keyboardInterruptLineActive = false;
                KeyboardInterruptLineChanged?.Invoke(false);
            }
        }
        else
        {
            RefreshInterruptLineNoLock();
        }

        m_keyboardDataRegisterLatch = value;

        return value;
    }

    private void HandleControlWriteNoLock(AciaRegister register, byte value)
    {
        if ((value & MasterResetControlMask) != MasterResetControlMask)
        {
            if (register == AciaRegister.KeyboardStatus)
            {
                m_keyboardControl = value;
                RefreshInterruptLineNoLock();
            }
            return;
        }
        if (register != AciaRegister.KeyboardStatus)
            return;

        m_keyboardReceiveQueue.Clear();
        m_keyboardReceiveKinds.Clear();
        m_pendingCommand = 0;
        m_pendingCommandParameterCount = 0;
        m_pendingCommandParameterIndex = 0;
        m_pendingMemoryLoadByteCount = 0;
        m_keyboardControl = 0;
        m_keyboardDataRegisterLatch = 0;
        m_lastMouseLeftButtonPressed = false;
        m_lastMouseRightButtonPressed = false;
        m_keyboardInterruptReassertPending = false;
        m_hasDeferredAdvanceWork = false;
        m_hasPendingClockResponse = false;
        m_pendingClockResponseDelayCpuTicks = 0;
        m_pendingClockResponseNextByteIndex = 0;
        ResetQueueByteCountersNoLock();
        Array.Clear(m_loggedIkbdCommandCodes, 0, m_loggedIkbdCommandCodes.Length);
        RefreshInterruptLineNoLock();
    }

    private void HandleKeyboardDataWriteNoLock(byte value)
    {
        if (m_pendingMemoryLoadByteCount > 0)
        {
            m_pendingMemoryLoadByteCount--;
            return;
        }

        if (m_pendingCommandParameterCount > 0)
        {
            m_pendingCommandParameters[m_pendingCommandParameterIndex++] = value;
            if (m_pendingCommandParameterIndex < m_pendingCommandParameterCount)
                return;

            m_outputPaused = false; // Any valid command resumes output after 0x13.
            ExecuteIkbdCommandNoLock(m_pendingCommand, m_pendingCommandParameters.AsSpan(0, m_pendingCommandParameterCount));
            ClearPendingCommandNoLock();
            return;
        }

        if (!TryGetIkbdCommandParameterCount(value, out var parameterCount))
        {
            WarnUnknownIkbdCommandOnceNoLock(value);
            return;
        }

        if (parameterCount == 0)
        {
            m_outputPaused = false; // Any valid command resumes output after 0x13.
            ExecuteIkbdCommandNoLock(value, []);
            return;
        }

        m_pendingCommand = value;
        m_pendingCommandParameterCount = parameterCount;
        m_pendingCommandParameterIndex = 0;
    }

    private void ExecuteIkbdCommandNoLock(byte command, ReadOnlySpan<byte> parameters)
    {
        if (command == IkbdResetCommand)
        {
            if (parameters.Length == 1 && parameters[0] == IkbdResetParameter)
            {
                m_keyboardReceiveQueue.Clear();
                m_keyboardReceiveKinds.Clear();
                m_mouseReportingEnabled = true;
                m_outputPaused = false;
                m_mouseMode = IkbdMouseModeRelative;
                m_keyboardDataRegisterLatch = 0;
                m_mouseButtonActionMode = 0;
                m_mouseThresholdX = 1;
                m_mouseThresholdY = 1;
                m_mouseScaleX = 1;
                m_mouseScaleY = 1;
                m_isMouseYZeroTop = true;
                m_lastMouseLeftButtonPressed = false;
                m_lastMouseRightButtonPressed = false;
                m_joystickEventModeEnabled = true;
                m_joystickMonitoringModeEnabled = false;
                m_joystickDisabled = false;
                m_pendingMemoryLoadByteCount = 0;
                m_lastJoystickStateBytes[0] = 0;
                m_lastJoystickStateBytes[1] = 0;
                m_keyboardInterruptReassertPending = false;
                m_hasDeferredAdvanceWork = false;
                m_hasPendingClockResponse = false;
                m_pendingClockResponseDelayCpuTicks = 0;
                m_pendingClockResponseNextByteIndex = 0;
                ResetQueueByteCountersNoLock();
                EnqueueKeyboardByteNoLock(IkbdResetCompleteCode);
            }
            return;
        }

        if (command == IkbdSetRelativeMouseModeCommand)
        {
            m_mouseMode = IkbdMouseModeRelative;
            m_mouseReportingEnabled = true;
            return;
        }

        if (command == IkbdInterrogateMousePositionCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.InterrogateMousePosition,
                "IKBD interrogate mouse position command (0x0D) received, but absolute mouse position response is not implemented.");
            return;
        }

        if (command == IkbdSetMouseButtonActionCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse button action command handled.");
            m_mouseButtonActionMode = parameters.Length > 0 ? parameters[0] : (byte)0;
            if ((m_mouseButtonActionMode & ~IkbdMouseButtonActionKeycodeBit) != 0)
            {
                WarnUnsupportedFeatureOnceNoLock(
                    UnsupportedFeatureWarningFlag.MouseButtonAction,
                    "IKBD mouse button action command uses unsupported bits; only keycode mode bit 0x04 is currently applied.");
            }
            return;
        }

        if (command == IkbdSetAbsoluteMouseModeCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.AbsoluteMouseModeLimited,
                "IKBD absolute mouse mode enabled. Current support may be incomplete.");
            m_mouseMode = IkbdSetAbsoluteMouseModeCommand;
            m_mouseReportingEnabled = true;
            return;
        }

        if (command == IkbdSetMouseKeycodeModeCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.MouseKeycodeMode,
                "IKBD mouse keycode mode (0x0A) selected, but mouse keycode event output is not implemented.");
            m_mouseMode = IkbdSetMouseKeycodeModeCommand;
            m_mouseReportingEnabled = true;
            return;
        }

        if (command == IkbdSetMouseThresholdCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse threshold command handled (values stored).");
            if (parameters.Length > 0)
                m_mouseThresholdX = parameters[0];
            if (parameters.Length > 1)
                m_mouseThresholdY = parameters[1];
            return;
        }

        if (command == IkbdSetMouseScaleCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.MouseScale,
                "IKBD mouse scale command (0x0C) values are stored for status reporting, but scale behavior is not currently applied.");
            if (parameters.Length > 0)
                m_mouseScaleX = parameters[0];
            if (parameters.Length > 1)
                m_mouseScaleY = parameters[1];
            return;
        }

        if (command == IkbdLoadMousePositionCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.LoadMousePosition,
                "IKBD load mouse position command (0x0E) received, but loading the internal mouse position is not implemented.");
            return;
        }

        if (command == IkbdSetYZeroBottomCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse Y-origin set to bottom.");
            m_isMouseYZeroTop = false;
            return;
        }

        if (command == IkbdSetYZeroTopCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse Y-origin set to top.");
            m_isMouseYZeroTop = true;
            return;
        }

        if (command == IkbdDisableMouseReportingCommand)
        {
            m_mouseReportingEnabled = false;
            DropQueuedBytesByKindNoLock(ReceiveByteKind.MousePacket);
            return;
        }

        if (command == IkbdResumeOutputCommand)
        {
            m_outputPaused = false;
            return;
        }

        if (command == IkbdPauseOutputCommand)
        {
            m_outputPaused = true;
            return;
        }

        if (command == IkbdSetJoystickEventModeCommand)
        {
            m_joystickEventModeEnabled = true;
            m_joystickMonitoringModeEnabled = false;
            m_joystickDisabled = false;
            QueueActiveJoystickStatePacketsNoLock();
            return;
        }

        if (command == IkbdSetJoystickInterrogateModeCommand)
        {
            m_joystickEventModeEnabled = false;
            m_joystickMonitoringModeEnabled = false;
            m_joystickDisabled = false;
            return;
        }

        if (command == IkbdSetJoystickMonitoringModeCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.JoystickMonitoringTiming,
                "IKBD joystick monitoring mode (0x17) enabled. Packet output is supported, but the command parameter/timing behavior may be incomplete.");
            m_joystickEventModeEnabled = false;
            m_joystickMonitoringModeEnabled = true;
            m_joystickDisabled = false;
            return;
        }

        if (command == IkbdSetJoystickFireButtonMonitoringCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.JoystickFireButtonMonitoring,
                "IKBD joystick fire-button monitoring command (0x18) received, but this mode is not implemented.");
            return;
        }

        if (command == IkbdSetJoystickCursorKeycodesCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.JoystickCursorKeycodes,
                "IKBD joystick cursor-keycode mode (0x19) enabled. Keycode repeat timing parameters are currently not applied.");
            m_joystickEventModeEnabled = true;
            m_joystickMonitoringModeEnabled = true;
            m_joystickDisabled = false;
            QueueJoystickCursorKeycodesNoLock(0, 0, m_lastJoystickStateBytes[0]);
            return;
        }

        if (command == IkbdDisableJoysticksCommand)
        {
            m_joystickEventModeEnabled = false;
            m_joystickMonitoringModeEnabled = false;
            m_joystickDisabled = true;
            return;
        }

        if (command == IkbdSetClockCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD set clock command handled (raw clock bytes stored).");
            for (var i = 0; i < m_clockBytes.Length; i++)
            {
                if (i >= parameters.Length)
                    break;
                if (!IsPackedBcd(parameters[i]))
                    continue;
                m_clockBytes[i] = parameters[i];
            }
            m_clockLastHostTimestamp = Stopwatch.GetTimestamp();
            return;
        }

        if (command == IkbdReadClockCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD read clock command handled (raw stored clock bytes returned).");
            AdvanceClockFromHostTimeNoLock();
            ScheduleClockResponseNoLock();
            return;
        }

        if (command == IkbdMemoryLoadCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD memory load command received. Data bytes are accepted and discarded.");
            m_pendingMemoryLoadByteCount = parameters.Length > 2 ? parameters[2] : 0;
            return;
        }

        if (command == IkbdMemoryReadCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD memory read command handled with placeholder status response.");
            QueueStatusResponseNoLock(0x20, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdExecuteCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD execute command received, but execution is not implemented.");
            return;
        }

        if (command == IkbdReportMouseButtonActionCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse button-action report request handled.");
            QueueStatusResponseNoLock(0x07, m_mouseButtonActionMode, 0, 0, 0, 0, 0);
            return;
        }

        if (command is IkbdReportMouseModeRelativeCommand or IkbdReportMouseModeAbsoluteCommand or IkbdReportMouseModeKeycodeCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse mode report request handled.");
            var mode = m_mouseMode switch
            {
                IkbdMouseModeRelative when m_mouseReportingEnabled => (byte)0x08,
                IkbdSetAbsoluteMouseModeCommand when m_mouseReportingEnabled => (byte)0x09,
                IkbdSetMouseKeycodeModeCommand when m_mouseReportingEnabled => (byte)0x0A,
                _ => (byte)0x00
            };
            QueueStatusResponseNoLock(mode, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdReportMouseThresholdCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse threshold report request handled.");
            QueueStatusResponseNoLock(0x0B, m_mouseThresholdX, m_mouseThresholdY, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdReportMouseScaleCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse scale report request handled.");
            QueueStatusResponseNoLock(0x0C, m_mouseScaleX, m_mouseScaleY, 0, 0, 0, 0);
            return;
        }

        if (command is IkbdReportMouseVerticalBottomCommand or IkbdReportMouseVerticalTopCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse vertical-origin report request handled.");
            QueueStatusResponseNoLock(m_isMouseYZeroTop ? (byte)0x10 : (byte)0x0F, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdReportMouseAvailabilityCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD mouse availability report request handled.");
            var availability = m_mouseMode == 0 || !m_mouseReportingEnabled ? (byte)0x12 : (byte)0x00;
            QueueStatusResponseNoLock(availability, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command is IkbdReportJoystickModeEventCommand or IkbdReportJoystickModeInterrogateCommand or IkbdReportJoystickModeKeycodeCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD joystick mode report request handled.");
            var mode = IsJoystickCursorKeycodeModeNoLock()
                ? (byte)0x19
                : m_joystickEventModeEnabled
                    ? (byte)0x14
                    : (byte)0x15;
            if (m_joystickMonitoringModeEnabled && !IsJoystickCursorKeycodeModeNoLock())
                mode = 0x17;
            QueueStatusResponseNoLock(mode, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdReportJoystickAvailabilityCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD joystick availability report request handled.");
            var availability = m_joystickDisabled ? (byte)0x1A : (byte)0x00;
            QueueStatusResponseNoLock(availability, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdInterrogateJoystickStateCommand)
        {
            LogIkbdCommandOnceNoLock(command, parameters, "IKBD joystick interrogate request handled.");
            QueueJoystickInterrogationResponseNoLock();
        }
    }

    private static bool TryGetIkbdCommandParameterCount(byte command, out int parameterCount)
    {
        parameterCount = command switch
        {
            IkbdSetMouseButtonActionCommand => 1,
            IkbdSetRelativeMouseModeCommand => 0,
            IkbdSetAbsoluteMouseModeCommand => 4,
            IkbdSetMouseKeycodeModeCommand => 2,
            IkbdSetMouseThresholdCommand => 2,
            IkbdSetMouseScaleCommand => 2,
            IkbdInterrogateMousePositionCommand => 0,
            IkbdLoadMousePositionCommand => 5,
            IkbdSetYZeroBottomCommand => 0,
            IkbdSetYZeroTopCommand => 0,
            IkbdResumeOutputCommand => 0,
            IkbdDisableMouseReportingCommand => 0,
            IkbdPauseOutputCommand => 0,
            IkbdSetJoystickEventModeCommand => 0,
            IkbdSetJoystickInterrogateModeCommand => 0,
            IkbdInterrogateJoystickStateCommand => 0,
            IkbdSetJoystickMonitoringModeCommand => 1,
            IkbdSetJoystickFireButtonMonitoringCommand => 0,
            IkbdSetJoystickCursorKeycodesCommand => 6,
            IkbdDisableJoysticksCommand => 0,
            IkbdSetClockCommand => 6,
            IkbdReadClockCommand => 0,
            IkbdMemoryLoadCommand => 3,
            IkbdMemoryReadCommand => 2,
            IkbdExecuteCommand => 2,
            IkbdReportMouseButtonActionCommand => 0,
            IkbdReportMouseModeRelativeCommand => 0,
            IkbdReportMouseModeAbsoluteCommand => 0,
            IkbdReportMouseModeKeycodeCommand => 0,
            IkbdReportMouseThresholdCommand => 0,
            IkbdReportMouseScaleCommand => 0,
            IkbdReportMouseVerticalBottomCommand => 0,
            IkbdReportMouseVerticalTopCommand => 0,
            IkbdReportMouseAvailabilityCommand => 0,
            IkbdReportJoystickModeEventCommand => 0,
            IkbdReportJoystickModeInterrogateCommand => 0,
            IkbdReportJoystickModeKeycodeCommand => 0,
            IkbdReportJoystickAvailabilityCommand => 0,
            IkbdResetCommand => 1,
            _ => -1
        };
        return parameterCount >= 0;
    }

    private void ClearPendingCommandNoLock()
    {
        m_pendingCommand = 0;
        m_pendingCommandParameterCount = 0;
        m_pendingCommandParameterIndex = 0;
    }

    private void EnqueueKeyboardByteNoLock(byte value, ReceiveByteKind byteKind = ReceiveByteKind.Generic)
    {
        m_keyboardReceiveQueue.Enqueue(value);
        m_keyboardReceiveKinds.Enqueue(byteKind);
        IncrementQueuedByteKindCountNoLock(byteKind);
        if (m_keyboardReceiveQueue.Count > m_peakReceiveQueueCount)
            m_peakReceiveQueueCount = m_keyboardReceiveQueue.Count;
        if (m_keyboardReceiveQueue.Count == 1)
        {
            m_keyboardInterruptReassertPending = false;
            m_hasDeferredAdvanceWork = false;
        }
        RefreshInterruptLineNoLock();
        KeyboardDataReady?.Invoke(this, EventArgs.Empty);
    }

    private void DropStaleTransientInputBacklogForKeyboardNoLock()
    {
        if (m_keyboardReceiveQueue.Count == 0)
            return;
        if (m_queuedKeyboardInjectedByteCount != 0)
            return;

        if (!HasOnlyDroppableBacklogBeforeKeyboardNoLock())
            return;

        DropQueuedBytesByKindNoLock(ReceiveByteKind.MousePacket);
        DropQueuedBytesByKindNoLock(ReceiveByteKind.JoystickEvent);
        DropQueuedBytesByKindNoLock(ReceiveByteKind.JoystickInterrogateResponse);
        DropQueuedBytesByKindNoLock(ReceiveByteKind.JoystickMonitoring);
    }

    private void DropStaleTransientInputBacklogForJoystickNoLock()
    {
        if (m_keyboardReceiveQueue.Count == 0)
            return;

        if (!HasOnlyDroppableBacklogBeforeJoystickNoLock())
            return;

        DropQueuedBytesByKindNoLock(ReceiveByteKind.MousePacket);
        DropQueuedBytesByKindNoLock(ReceiveByteKind.JoystickEvent);
        DropQueuedBytesByKindNoLock(ReceiveByteKind.JoystickMonitoring);
    }

    private bool HasOnlyDroppableBacklogBeforeKeyboardNoLock()
    {
        foreach (var kind in m_keyboardReceiveKinds)
        {
            if (kind is ReceiveByteKind.MousePacket or
                ReceiveByteKind.JoystickEvent or
                ReceiveByteKind.JoystickInterrogateResponse or
                ReceiveByteKind.JoystickMonitoring)
                continue;

            return false;
        }

        return m_keyboardReceiveKinds.Count > 0;
    }

    private bool HasOnlyDroppableBacklogBeforeJoystickNoLock()
    {
        foreach (var kind in m_keyboardReceiveKinds)
        {
            if (kind is ReceiveByteKind.MousePacket or
                ReceiveByteKind.JoystickEvent or
                ReceiveByteKind.JoystickMonitoring)
                continue;

            return false;
        }

        return m_keyboardReceiveKinds.Count > 0;
    }

    private void IncrementQueuedByteKindCountNoLock(ReceiveByteKind byteKind)
    {
        if (byteKind == ReceiveByteKind.KeyboardInjected)
            m_queuedKeyboardInjectedByteCount++;
        else if (byteKind == ReceiveByteKind.MousePacket)
            m_queuedMousePacketByteCount++;
        else if (byteKind == ReceiveByteKind.JoystickEvent)
            m_queuedJoystickEventByteCount++;
        else if (byteKind == ReceiveByteKind.JoystickInterrogateResponse)
            m_queuedJoystickInterrogateResponseByteCount++;
        else if (byteKind == ReceiveByteKind.ClockResponse)
            m_queuedClockResponseByteCount++;
        else if (byteKind == ReceiveByteKind.JoystickMonitoring)
            m_queuedJoystickEventByteCount++;
    }

    private void DecrementQueuedByteKindCountNoLock(ReceiveByteKind byteKind)
    {
        if (byteKind == ReceiveByteKind.KeyboardInjected)
            m_queuedKeyboardInjectedByteCount = Math.Max(0, m_queuedKeyboardInjectedByteCount - 1);
        else if (byteKind == ReceiveByteKind.MousePacket)
            m_queuedMousePacketByteCount = Math.Max(0, m_queuedMousePacketByteCount - 1);
        else if (byteKind == ReceiveByteKind.JoystickEvent)
            m_queuedJoystickEventByteCount = Math.Max(0, m_queuedJoystickEventByteCount - 1);
        else if (byteKind == ReceiveByteKind.JoystickInterrogateResponse)
            m_queuedJoystickInterrogateResponseByteCount = Math.Max(0, m_queuedJoystickInterrogateResponseByteCount - 1);
        else if (byteKind == ReceiveByteKind.ClockResponse)
            m_queuedClockResponseByteCount = Math.Max(0, m_queuedClockResponseByteCount - 1);
        else if (byteKind == ReceiveByteKind.JoystickMonitoring)
            m_queuedJoystickEventByteCount = Math.Max(0, m_queuedJoystickEventByteCount - 1);
    }

    private void ResetQueueByteCountersNoLock()
    {
        m_queuedKeyboardInjectedByteCount = 0;
        m_queuedMousePacketByteCount = 0;
        m_queuedJoystickEventByteCount = 0;
        m_queuedJoystickInterrogateResponseByteCount = 0;
        m_queuedClockResponseByteCount = 0;
        m_peakReceiveQueueCount = 0;
    }

    private void QueueJoystickInterrogationResponseNoLock()
    {
        if (m_outputPaused)
            return;
        if (m_isJoystickInterrogateCoalescingEnabled)
            DropQueuedJoystickInterrogationResponsesNoLock();

        EnqueueKeyboardByteNoLock(JoystickInterrogationHeader, ReceiveByteKind.JoystickInterrogateResponse);
        EnqueueKeyboardByteNoLock(m_lastJoystickStateBytes[0], ReceiveByteKind.JoystickInterrogateResponse);
        EnqueueKeyboardByteNoLock(m_lastJoystickStateBytes[1], ReceiveByteKind.JoystickInterrogateResponse);
    }

    private void QueueStatusResponseNoLock(byte value1, byte value2, byte value3, byte value4, byte value5, byte value6, byte value7)
    {
        if (m_outputPaused)
            return;

        EnqueueKeyboardByteNoLock(IkbdStatusResponseHeader);
        EnqueueKeyboardByteNoLock(value1);
        EnqueueKeyboardByteNoLock(value2);
        EnqueueKeyboardByteNoLock(value3);
        EnqueueKeyboardByteNoLock(value4);
        EnqueueKeyboardByteNoLock(value5);
        EnqueueKeyboardByteNoLock(value6);
        EnqueueKeyboardByteNoLock(value7);
    }
    
    private void ScheduleClockResponseNoLock()
    {
        DropQueuedClockResponsesNoLock();
        for (var i = 0; i < m_clockBytes.Length; i++)
            m_pendingClockResponseBytes[i] = m_clockBytes[i];

        m_pendingClockResponseNextByteIndex = -1; // Emit 0xFC header first.
        m_pendingClockResponseDelayCpuTicks = ClockResponseHeaderDelayCpuTicks;
        m_hasPendingClockResponse = true;
    }

    private void AdvanceDelayedOutputNoLock(long deltaCpuTicks)
    {
        lock (m_stateSync)
        {
            if (!m_hasPendingClockResponse)
                return;
            if (m_outputPaused)
                return;

            if (deltaCpuTicks > 0 && m_pendingClockResponseDelayCpuTicks > 0)
                m_pendingClockResponseDelayCpuTicks -= deltaCpuTicks;

            if (m_pendingClockResponseDelayCpuTicks > 0)
                return;
            if (m_keyboardReceiveQueue.Count > 0)
                return;

            if (m_pendingClockResponseNextByteIndex < 0)
            {
                EnqueueKeyboardByteNoLock(IkbdClockResponseHeader, ReceiveByteKind.ClockResponse);
                m_pendingClockResponseNextByteIndex = 0;
                m_pendingClockResponseDelayCpuTicks = ClockResponseInterByteDelayCpuTicks;
                return;
            }

            if (m_pendingClockResponseNextByteIndex < m_pendingClockResponseBytes.Length)
            {
                EnqueueKeyboardByteNoLock(m_pendingClockResponseBytes[m_pendingClockResponseNextByteIndex], ReceiveByteKind.ClockResponse);
                m_pendingClockResponseNextByteIndex++;
                if (m_pendingClockResponseNextByteIndex < m_pendingClockResponseBytes.Length)
                {
                    m_pendingClockResponseDelayCpuTicks = ClockResponseInterByteDelayCpuTicks;
                    return;
                }
            }

            m_hasPendingClockResponse = false;
            m_pendingClockResponseDelayCpuTicks = 0;
            m_pendingClockResponseNextByteIndex = 0;
        }
    }

    private void QueueActiveJoystickStatePacketsNoLock()
    {
        if (m_outputPaused || !m_joystickEventModeEnabled || m_joystickMonitoringModeEnabled || m_joystickDisabled)
            return;
        if (m_lastJoystickStateBytes[0] != 0)
        {
            EnqueueKeyboardByteNoLock(JoystickPacketHeaderPort0, ReceiveByteKind.JoystickEvent);
            EnqueueKeyboardByteNoLock(m_lastJoystickStateBytes[0], ReceiveByteKind.JoystickEvent);
        }
        if (m_lastJoystickStateBytes[1] == 0)
            return;

        EnqueueKeyboardByteNoLock(JoystickPacketHeaderPort1, ReceiveByteKind.JoystickEvent);
        EnqueueKeyboardByteNoLock(m_lastJoystickStateBytes[1], ReceiveByteKind.JoystickEvent);
    }

    private void QueueJoystickMonitoringPacketNoLock()
    {
        if (m_joystickDisabled)
            return;

        var fireBits = (byte)(((m_lastJoystickStateBytes[0] & JoystickBitFire) >> 6) | ((m_lastJoystickStateBytes[1] & JoystickBitFire) >> 7));
        var directionBits = (byte)(((m_lastJoystickStateBytes[0] & 0x0F) << 4) | (m_lastJoystickStateBytes[1] & 0x0F));
        EnqueueKeyboardByteNoLock(fireBits, ReceiveByteKind.JoystickMonitoring);
        EnqueueKeyboardByteNoLock(directionBits, ReceiveByteKind.JoystickMonitoring);
    }

    private bool IsJoystickCursorKeycodeModeNoLock() =>
        m_joystickEventModeEnabled && m_joystickMonitoringModeEnabled && !m_joystickDisabled;

    private void QueueJoystickCursorKeycodesNoLock(byte joystickIndex, byte previousStateByte, byte currentStateByte)
    {
        if (joystickIndex != 0)
            return;

        QueueJoystickKeycodeTransitionNoLock(previousStateByte, currentStateByte, JoystickBitUp, CursorUpScanCode);
        QueueJoystickKeycodeTransitionNoLock(previousStateByte, currentStateByte, JoystickBitDown, CursorDownScanCode);
        QueueJoystickKeycodeTransitionNoLock(previousStateByte, currentStateByte, JoystickBitLeft, CursorLeftScanCode);
        QueueJoystickKeycodeTransitionNoLock(previousStateByte, currentStateByte, JoystickBitRight, CursorRightScanCode);
        QueueJoystickKeycodeTransitionNoLock(previousStateByte, currentStateByte, JoystickBitFire, Joystick0FireScanCode);
    }

    private void QueueJoystickKeycodeTransitionNoLock(byte previousStateByte, byte currentStateByte, byte stateMask, byte scanCode)
    {
        var wasPressed = (previousStateByte & stateMask) != 0;
        var isPressed = (currentStateByte & stateMask) != 0;
        if (wasPressed == isPressed)
            return;

        EnqueueKeyboardByteNoLock(isPressed ? scanCode : (byte)(scanCode | 0x80));
    }

    private void WarnUnsupportedFeatureOnceNoLock(UnsupportedFeatureWarningFlag flag, string message)
    {
        var bit = (uint)flag;
        if ((m_loggedUnsupportedFeatureWarnings & bit) != 0)
            return;

        m_loggedUnsupportedFeatureWarnings |= bit;
        Logger.Instance.Warn(message);
    }

    private void WarnUnknownIkbdCommandOnceNoLock(byte command)
    {
        if (!TryMarkIkbdCommandLoggedNoLock(command))
            return;

        Logger.Instance.Warn($"Unknown IKBD command 0x{command:X2} received.");
    }

    private void LogIkbdCommandOnceNoLock(byte command, ReadOnlySpan<byte> parameters, string message)
    {
        if (!TryMarkIkbdCommandLoggedNoLock(command))
            return;

        if (parameters.Length == 0)
        {
            Logger.Instance.Info($"IKBD command 0x{command:X2}: {message}");
            return;
        }

        var parameterBytes = string.Join(" ", parameters.ToArray().Select(p => p.ToString("X2")));
        Logger.Instance.Info($"IKBD command 0x{command:X2} [{parameterBytes}]: {message}");
    }

    private bool TryMarkIkbdCommandLoggedNoLock(byte command)
    {
        var bucket = command >> 6;
        var bit = 1UL << (command & 0x3F);
        if ((m_loggedIkbdCommandCodes[bucket] & bit) != 0)
            return false;

        m_loggedIkbdCommandCodes[bucket] |= bit;
        return true;
    }

    private void RefreshInterruptLineNoLock()
    {
        var isActive = m_keyboardReceiveQueue.Count > 0 && (m_keyboardControl & InterruptRequestFlag) != 0;
        if (isActive == m_keyboardInterruptLineActive)
            return;

        m_keyboardInterruptLineActive = isActive;
        KeyboardInterruptLineChanged?.Invoke(isActive);
    }

    private bool ShouldDeferInterruptReassertNoLock(ReceiveByteKind consumedByteKind)
    {
        if ((m_keyboardControl & InterruptRequestFlag) == 0)
            return false;
        if (!IsJoystickDeferredReassertKind(consumedByteKind))
            return false;
        if (m_keyboardReceiveQueue.Count == 0)
            return false;

        var nextByteKind = m_keyboardReceiveKinds.Peek();
        return IsJoystickDeferredReassertKind(nextByteKind);
    }

    private static bool IsJoystickDeferredReassertKind(ReceiveByteKind byteKind) =>
        byteKind is ReceiveByteKind.JoystickEvent or
        ReceiveByteKind.JoystickInterrogateResponse or
        ReceiveByteKind.JoystickMonitoring or
        ReceiveByteKind.ClockResponse;

    private void TryReassertKeyboardInterruptFast()
    {
        if (!m_keyboardInterruptReassertPending)
        {
            m_hasDeferredAdvanceWork = false;
            return;
        }
        if (m_keyboardInterruptLineActive)
        {
            m_keyboardInterruptReassertPending = false;
            m_hasDeferredAdvanceWork = false;
            return;
        }
        if ((m_keyboardControl & InterruptRequestFlag) == 0)
            return;

        m_keyboardInterruptReassertPending = false;
        m_hasDeferredAdvanceWork = false;
        m_keyboardInterruptLineActive = true;
        KeyboardInterruptLineChanged?.Invoke(true);
    }

    private void DropQueuedJoystickInterrogationResponsesNoLock()
    {
        if (m_queuedJoystickInterrogateResponseByteCount <= 0)
            return;

        var queueCount = m_keyboardReceiveQueue.Count;
        EnsureReceiveQueueScratchCapacity(queueCount);
        var keepCount = 0;
        while (m_keyboardReceiveQueue.Count > 0)
        {
            var queuedByte = m_keyboardReceiveQueue.Dequeue();
            var queuedKind = m_keyboardReceiveKinds.Dequeue();
            DecrementQueuedByteKindCountNoLock(queuedKind);
            if (queuedKind == ReceiveByteKind.JoystickInterrogateResponse)
                continue;

            m_receiveQueueScratch[keepCount] = queuedByte;
            m_receiveKindScratch[keepCount] = queuedKind;
            keepCount++;
        }

        for (var i = 0; i < keepCount; i++)
        {
            m_keyboardReceiveQueue.Enqueue(m_receiveQueueScratch[i]);
            m_keyboardReceiveKinds.Enqueue(m_receiveKindScratch[i]);
            IncrementQueuedByteKindCountNoLock(m_receiveKindScratch[i]);
        }

        if (keepCount == 0)
        {
            m_keyboardInterruptReassertPending = false;
            m_hasDeferredAdvanceWork = false;
            RefreshInterruptLineNoLock();
        }
    }

    private void DropQueuedClockResponsesNoLock()
    {
        if (m_queuedClockResponseByteCount <= 0)
            return;

        DropQueuedBytesByKindNoLock(ReceiveByteKind.ClockResponse);
    }

    private static bool IsPackedBcd(byte value) =>
        (value & 0x0F) <= 9 && ((value >> 4) & 0x0F) <= 9;

    private void AdvanceClockFromHostTimeNoLock()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedTicks = now - m_clockLastHostTimestamp;
        if (elapsedTicks <= 0)
            return;

        var elapsedSeconds = elapsedTicks / Stopwatch.Frequency;
        if (elapsedSeconds <= 0)
            return;

        m_clockLastHostTimestamp += elapsedSeconds * Stopwatch.Frequency;
        AdvanceClockSecondsNoLock((int)Math.Min(int.MaxValue, elapsedSeconds));
    }

    private void AdvanceClockSecondsNoLock(int seconds)
    {
        if (seconds <= 0)
            return;
        if (!TryGetClockDateTimeNoLock(out var clock))
            return;

        clock = clock.AddSeconds(seconds);
        SetClockBytesFromDateTimeNoLock(clock);
    }

    private bool TryGetClockDateTimeNoLock(out DateTime value)
    {
        value = default;

        if (!TryGetBcdInt(m_clockBytes[0], out var year) ||
            !TryGetBcdInt(m_clockBytes[1], out var month) ||
            !TryGetBcdInt(m_clockBytes[2], out var day) ||
            !TryGetBcdInt(m_clockBytes[3], out var hour) ||
            !TryGetBcdInt(m_clockBytes[4], out var minute) ||
            !TryGetBcdInt(m_clockBytes[5], out var second))
            return false;

        var fullYear = year >= 80 ? 1900 + year : 2000 + year;
        try
        {
            value = new DateTime(fullYear, month, day, hour, minute, second, DateTimeKind.Local);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private void SetClockBytesFromDateTimeNoLock(DateTime value)
    {
        m_clockBytes[0] = ToBcdByte(value.Year % 100);
        m_clockBytes[1] = ToBcdByte(value.Month);
        m_clockBytes[2] = ToBcdByte(value.Day);
        m_clockBytes[3] = ToBcdByte(value.Hour);
        m_clockBytes[4] = ToBcdByte(value.Minute);
        m_clockBytes[5] = ToBcdByte(value.Second);
    }

    private void SeedClockFromHostTimeNoLock()
    {
        SetClockBytesFromDateTimeNoLock(DateTime.Now);
        m_clockLastHostTimestamp = Stopwatch.GetTimestamp();
    }

    private static bool TryGetBcdInt(byte bcd, out int value)
    {
        value = 0;
        if (!IsPackedBcd(bcd))
            return false;

        value = ((bcd >> 4) * 10) + (bcd & 0x0F);
        return true;
    }

    private static byte ToBcdByte(int value)
    {
        value = Math.Clamp(value, 0, 99);
        return (byte)(((value / 10) << 4) | (value % 10));
    }

    private void DropQueuedBytesByKindNoLock(ReceiveByteKind kindToDrop)
    {
        var queueCount = m_keyboardReceiveQueue.Count;
        if (queueCount == 0)
            return;

        EnsureReceiveQueueScratchCapacity(queueCount);
        var keepCount = 0;
        while (m_keyboardReceiveQueue.Count > 0)
        {
            var queuedByte = m_keyboardReceiveQueue.Dequeue();
            var queuedKind = m_keyboardReceiveKinds.Dequeue();
            DecrementQueuedByteKindCountNoLock(queuedKind);
            if (queuedKind == kindToDrop)
                continue;

            m_receiveQueueScratch[keepCount] = queuedByte;
            m_receiveKindScratch[keepCount] = queuedKind;
            keepCount++;
        }

        for (var i = 0; i < keepCount; i++)
        {
            m_keyboardReceiveQueue.Enqueue(m_receiveQueueScratch[i]);
            m_keyboardReceiveKinds.Enqueue(m_receiveKindScratch[i]);
            IncrementQueuedByteKindCountNoLock(m_receiveKindScratch[i]);
        }

        if (keepCount == 0)
        {
            m_keyboardInterruptReassertPending = false;
            m_hasDeferredAdvanceWork = false;
            RefreshInterruptLineNoLock();
        }
    }

    private void EnsureReceiveQueueScratchCapacity(int minimumLength)
    {
        if (m_receiveQueueScratch.Length >= minimumLength)
            return;

        var newLength = m_receiveQueueScratch.Length;
        while (newLength < minimumLength)
            newLength *= 2;

        m_receiveQueueScratch = new byte[newLength];
        m_receiveKindScratch = new ReceiveByteKind[newLength];
    }

    private static AciaRegister GetRegister(uint address)
    {
        if (address < BaseAddress || address > BaseAddress + 7)
            return AciaRegister.None;

        var offset = (int)(address - BaseAddress);
        return offset switch
        {
            0x00 => AciaRegister.KeyboardStatus,
            0x02 => AciaRegister.KeyboardData,
            0x04 => AciaRegister.MidiStatus,
            0x06 => AciaRegister.MidiData,
            _ => AciaRegister.None
        };
    }

    internal int GetStateSize()
    {
        lock (m_stateSync)
        {
            return sizeof(int) + // queue count
                   m_keyboardReceiveQueue.Count + // queue bytes
                   m_keyboardReceiveKinds.Count + // queue kinds
                   m_lastJoystickStateBytes.Length +
                   m_clockBytes.Length +
                   m_pendingClockResponseBytes.Length +
                   m_pendingCommandParameters.Length +
                   8 + // mode/state booleans
                   sizeof(byte) * 9 + // pending command/mouse state bytes/kbd ctrl + data latch
                   sizeof(int) * 9 + // pending counts + queued counters + peak
                   sizeof(long) +
                   sizeof(int) +
                   6; // volatile/scheduled booleans
        }
    }

    internal void SaveState(ref StateWriter writer)
    {
        lock (m_stateSync)
        {
            var queuedBytes = m_keyboardReceiveQueue.ToArray();
            var queuedKinds = m_keyboardReceiveKinds.ToArray();
            writer.WriteInt32(queuedBytes.Length);
            writer.WriteBytes(queuedBytes);
            foreach (var b in queuedKinds)
                writer.WriteByte((byte)b);

            writer.WriteBytes(m_lastJoystickStateBytes);
            writer.WriteBytes(m_clockBytes);
            writer.WriteBytes(m_pendingClockResponseBytes);
            writer.WriteBytes(m_pendingCommandParameters);
            writer.WriteBool(m_mouseReportingEnabled);
            writer.WriteBool(m_outputPaused);
            writer.WriteBool(m_joystickEventModeEnabled);
            writer.WriteBool(m_joystickMonitoringModeEnabled);
            writer.WriteBool(m_joystickDisabled);
            writer.WriteByte(m_pendingCommand);
            writer.WriteInt32(m_pendingCommandParameterCount);
            writer.WriteInt32(m_pendingCommandParameterIndex);
            writer.WriteInt32(m_pendingMemoryLoadByteCount);
            writer.WriteByte(m_mouseMode);
            writer.WriteByte(m_mouseButtonActionMode);
            writer.WriteByte(m_mouseThresholdX);
            writer.WriteByte(m_mouseThresholdY);
            writer.WriteByte(m_mouseScaleX);
            writer.WriteByte(m_mouseScaleY);
            writer.WriteByte(m_keyboardControl);
            writer.WriteByte(m_keyboardDataRegisterLatch);
            writer.WriteBool(m_isMouseYZeroTop);
            writer.WriteBool(m_lastMouseLeftButtonPressed);
            writer.WriteBool(m_lastMouseRightButtonPressed);
            writer.WriteBool(m_keyboardInterruptLineActive);
            writer.WriteBool(m_keyboardInterruptReassertPending);
            writer.WriteBool(m_hasDeferredAdvanceWork);
            writer.WriteBool(m_isDeferredInterruptReassertEnabled);
            writer.WriteBool(m_isJoystickInterrogateCoalescingEnabled);
            writer.WriteBool(m_hasPendingClockResponse);
            writer.WriteInt64(m_pendingClockResponseDelayCpuTicks);
            writer.WriteInt32(m_pendingClockResponseNextByteIndex);
            writer.WriteInt32(m_queuedKeyboardInjectedByteCount);
            writer.WriteInt32(m_queuedMousePacketByteCount);
            writer.WriteInt32(m_queuedJoystickEventByteCount);
            writer.WriteInt32(m_queuedJoystickInterrogateResponseByteCount);
            writer.WriteInt32(m_queuedClockResponseByteCount);
            writer.WriteInt32(m_peakReceiveQueueCount);
        }
    }

    internal void LoadState(ref StateReader reader)
    {
        lock (m_stateSync)
        {
            var queueCount = reader.ReadInt32();
            if (queueCount < 0)
                throw new InvalidOperationException("Invalid IKBD state (negative queue count).");

            var queuedBytes = new byte[queueCount];
            var queuedKinds = new ReceiveByteKind[queueCount];
            reader.ReadBytes(queuedBytes);
            for (var i = 0; i < queueCount; i++)
                queuedKinds[i] = (ReceiveByteKind)reader.ReadByte();

            m_keyboardReceiveQueue.Clear();
            m_keyboardReceiveKinds.Clear();
            EnsureReceiveQueueScratchCapacity(queueCount);
            for (var i = 0; i < queueCount; i++)
            {
                m_keyboardReceiveQueue.Enqueue(queuedBytes[i]);
                m_keyboardReceiveKinds.Enqueue(queuedKinds[i]);
            }

            reader.ReadBytes(m_lastJoystickStateBytes);
            reader.ReadBytes(m_clockBytes);
            reader.ReadBytes(m_pendingClockResponseBytes);
            reader.ReadBytes(m_pendingCommandParameters);
            m_mouseReportingEnabled = reader.ReadBool();
            m_outputPaused = reader.ReadBool();
            m_joystickEventModeEnabled = reader.ReadBool();
            m_joystickMonitoringModeEnabled = reader.ReadBool();
            m_joystickDisabled = reader.ReadBool();
            m_pendingCommand = reader.ReadByte();
            m_pendingCommandParameterCount = reader.ReadInt32();
            m_pendingCommandParameterIndex = reader.ReadInt32();
            m_pendingMemoryLoadByteCount = reader.ReadInt32();
            m_mouseMode = reader.ReadByte();
            m_mouseButtonActionMode = reader.ReadByte();
            m_mouseThresholdX = reader.ReadByte();
            m_mouseThresholdY = reader.ReadByte();
            m_mouseScaleX = reader.ReadByte();
            m_mouseScaleY = reader.ReadByte();
            m_keyboardControl = reader.ReadByte();
            m_keyboardDataRegisterLatch = reader.ReadByte();
            m_isMouseYZeroTop = reader.ReadBool();
            m_lastMouseLeftButtonPressed = reader.ReadBool();
            m_lastMouseRightButtonPressed = reader.ReadBool();
            m_keyboardInterruptLineActive = reader.ReadBool();
            m_keyboardInterruptReassertPending = reader.ReadBool();
            m_hasDeferredAdvanceWork = reader.ReadBool();
            m_isDeferredInterruptReassertEnabled = reader.ReadBool();
            m_isJoystickInterrogateCoalescingEnabled = reader.ReadBool();
            m_hasPendingClockResponse = reader.ReadBool();
            m_pendingClockResponseDelayCpuTicks = reader.ReadInt64();
            m_pendingClockResponseNextByteIndex = reader.ReadInt32();
            m_loggedUnsupportedFeatureWarnings = 0;
            Array.Clear(m_loggedIkbdCommandCodes, 0, m_loggedIkbdCommandCodes.Length);
            m_queuedKeyboardInjectedByteCount = reader.ReadInt32();
            m_queuedMousePacketByteCount = reader.ReadInt32();
            m_queuedJoystickEventByteCount = reader.ReadInt32();
            m_queuedJoystickInterrogateResponseByteCount = reader.ReadInt32();
            m_queuedClockResponseByteCount = reader.ReadInt32();
            m_peakReceiveQueueCount = reader.ReadInt32();
        }
    }

    private enum AciaRegister
    {
        None = 0,
        KeyboardStatus,
        KeyboardData,
        MidiStatus,
        MidiData
    }

    private enum ReceiveByteKind : byte
    {
        Generic = 0,
        KeyboardInjected = 1,
        MousePacket = 2,
        JoystickEvent = 3,
        JoystickInterrogateResponse = 4,
        JoystickMonitoring = 5,
        ClockResponse = 6
    }

    [Flags]
    private enum UnsupportedFeatureWarningFlag : uint
    {
        [UsedImplicitly] None = 0,
        AbsoluteMouseModeLimited = 1 << 0,
        MouseKeycodeMode = 1 << 1,
        [UsedImplicitly] MouseThreshold = 1 << 2,
        MouseScale = 1 << 3,
        LoadMousePosition = 1 << 4,
        [UsedImplicitly] MouseYOrigin = 1 << 5,
        JoystickFireButtonMonitoring = 1 << 6,
        JoystickCursorKeycodes = 1 << 7,
        MouseButtonAction = 1 << 8,
        JoystickMonitoringTiming = 1 << 9,
        InterrogateMousePosition = 1 << 10
    }
}
