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
    private const byte JoystickInterrogationHeader = 0xFD;
    private const byte JoystickPacketHeaderPort0 = 0xFE;
    private const byte JoystickPacketHeaderPort1 = 0xFF;
    private const byte JoystickBitUp = 0x01;
    private const byte JoystickBitDown = 0x02;
    private const byte JoystickBitLeft = 0x04;
    private const byte JoystickBitRight = 0x08;
    private const byte JoystickBitFire = 0x80;
    private const byte IkbdResetCommand = 0x80;
    private const byte IkbdResetParameter = 0x01;
    private const byte IkbdResetCompleteCode = 0xF1;
    private const byte IkbdMouseModeRelative = 0x08;
    private readonly Lock m_stateSync = new();
    private readonly Queue<byte> m_keyboardReceiveQueue = [];
    private readonly Queue<ReceiveByteKind> m_keyboardReceiveKinds = [];
    private readonly byte[] m_lastJoystickStateBytes = new byte[2];
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
    private byte m_keyboardControl;
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
    private int m_peakReceiveQueueCount;

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
    /// Gets whether deferred interrupt reassertion is enabled.
    /// </summary>
    public bool IsDeferredInterruptReassertEnabled => m_isDeferredInterruptReassertEnabled;

    /// <summary>
    /// Gets whether queued joystick interrogation replies are coalesced.
    /// </summary>
    public bool IsJoystickInterrogateCoalescingEnabled => m_isJoystickInterrogateCoalescingEnabled;

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
            EnqueueKeyboardByteNoLock(value, ReceiveByteKind.KeyboardInjected);
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
            m_lastJoystickStateBytes[joystickIndex] = stateByte;
            if (m_outputPaused)
                return;
            if (m_joystickDisabled)
                return;

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
            m_joystickEventModeEnabled = true;
            m_joystickMonitoringModeEnabled = false;
            m_joystickDisabled = false;
            m_pendingMemoryLoadByteCount = 0;
            m_lastJoystickStateBytes[0] = 0;
            m_lastJoystickStateBytes[1] = 0;
            m_keyboardInterruptReassertPending = false;
            m_hasDeferredAdvanceWork = false;
            ResetQueueByteCountersNoLock();
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
            return 0x00;

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
        m_keyboardInterruptReassertPending = false;
        m_hasDeferredAdvanceWork = false;
        ResetQueueByteCountersNoLock();
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
            return;

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
                m_joystickEventModeEnabled = true;
                m_joystickMonitoringModeEnabled = false;
                m_joystickDisabled = false;
                m_pendingMemoryLoadByteCount = 0;
                m_lastJoystickStateBytes[0] = 0;
                m_lastJoystickStateBytes[1] = 0;
                m_keyboardInterruptReassertPending = false;
                m_hasDeferredAdvanceWork = false;
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

        if (command == IkbdSetMouseButtonActionCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.MouseButtonAction,
                "IKBD mouse button action command (0x07) received, but button-action mode semantics are not implemented (only button bits in mouse packets are supported).");
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
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.MouseThreshold,
                "IKBD mouse threshold command (0x0B) received, but threshold behavior is not currently applied.");
            return;
        }

        if (command == IkbdSetMouseScaleCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.MouseScale,
                "IKBD mouse scale command (0x0C) received, but scale behavior is not currently applied.");
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
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.MouseYOrigin,
                "IKBD mouse Y-origin commands (0x0F/0x10) are accepted but currently not applied.");
            return;
        }

        if (command == IkbdSetYZeroTopCommand)
        {
            WarnUnsupportedFeatureOnceNoLock(
                UnsupportedFeatureWarningFlag.MouseYOrigin,
                "IKBD mouse Y-origin commands (0x0F/0x10) are accepted but currently not applied.");
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
                "IKBD joystick cursor-keycode mode (0x19) received, but keycode output mode is not implemented.");
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
            return;

        if (command == IkbdReadClockCommand)
            return;

        if (command == IkbdMemoryLoadCommand)
        {
            m_pendingMemoryLoadByteCount = parameters.Length > 2 ? parameters[2] : 0;
            return;
        }

        if (command == IkbdMemoryReadCommand)
        {
            QueueStatusResponseNoLock(0x20, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdExecuteCommand)
            return;

        if (command == IkbdReportMouseButtonActionCommand)
        {
            QueueStatusResponseNoLock(0x07, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command is IkbdReportMouseModeRelativeCommand or IkbdReportMouseModeAbsoluteCommand or IkbdReportMouseModeKeycodeCommand)
        {
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
            QueueStatusResponseNoLock(0x0B, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdReportMouseScaleCommand)
        {
            QueueStatusResponseNoLock(0x0C, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command is IkbdReportMouseVerticalBottomCommand or IkbdReportMouseVerticalTopCommand)
        {
            QueueStatusResponseNoLock(0x10, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdReportMouseAvailabilityCommand)
        {
            var availability = m_mouseMode == 0 || !m_mouseReportingEnabled ? (byte)0x12 : (byte)0x00;
            QueueStatusResponseNoLock(availability, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command is IkbdReportJoystickModeEventCommand or IkbdReportJoystickModeInterrogateCommand or IkbdReportJoystickModeKeycodeCommand)
        {
            var mode = m_joystickEventModeEnabled ? (byte)0x14 : (byte)0x15;
            if (m_joystickMonitoringModeEnabled)
                mode = 0x17;
            QueueStatusResponseNoLock(mode, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdReportJoystickAvailabilityCommand)
        {
            var availability = m_joystickDisabled ? (byte)0x1A : (byte)0x00;
            QueueStatusResponseNoLock(availability, 0, 0, 0, 0, 0, 0);
            return;
        }

        if (command == IkbdInterrogateJoystickStateCommand)
            QueueJoystickInterrogationResponseNoLock();
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
        else if (byteKind == ReceiveByteKind.JoystickMonitoring)
            m_queuedJoystickEventByteCount = Math.Max(0, m_queuedJoystickEventByteCount - 1);
    }

    private void ResetQueueByteCountersNoLock()
    {
        m_queuedKeyboardInjectedByteCount = 0;
        m_queuedMousePacketByteCount = 0;
        m_queuedJoystickEventByteCount = 0;
        m_queuedJoystickInterrogateResponseByteCount = 0;
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

    private void WarnUnsupportedFeatureOnceNoLock(UnsupportedFeatureWarningFlag flag, string message)
    {
        var bit = (uint)flag;
        if ((m_loggedUnsupportedFeatureWarnings & bit) != 0)
            return;

        m_loggedUnsupportedFeatureWarnings |= bit;
        Logger.Instance.Warn(message);
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
        byteKind is ReceiveByteKind.JoystickEvent or ReceiveByteKind.JoystickInterrogateResponse or ReceiveByteKind.JoystickMonitoring;

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
                   m_pendingCommandParameters.Length +
                   5 + // mode/state booleans
                   sizeof(byte) * 3 + // pending command/mouse mode/kbd ctrl
                   sizeof(int) * 8 + // pending counts + queued counters + peak
                   5; // volatile bools
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
            writer.WriteByte(m_keyboardControl);
            writer.WriteBool(m_keyboardInterruptLineActive);
            writer.WriteBool(m_keyboardInterruptReassertPending);
            writer.WriteBool(m_hasDeferredAdvanceWork);
            writer.WriteBool(m_isDeferredInterruptReassertEnabled);
            writer.WriteBool(m_isJoystickInterrogateCoalescingEnabled);
            writer.WriteInt32(m_queuedKeyboardInjectedByteCount);
            writer.WriteInt32(m_queuedMousePacketByteCount);
            writer.WriteInt32(m_queuedJoystickEventByteCount);
            writer.WriteInt32(m_queuedJoystickInterrogateResponseByteCount);
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
            m_keyboardControl = reader.ReadByte();
            m_keyboardInterruptLineActive = reader.ReadBool();
            m_keyboardInterruptReassertPending = reader.ReadBool();
            m_hasDeferredAdvanceWork = reader.ReadBool();
            m_isDeferredInterruptReassertEnabled = reader.ReadBool();
            m_isJoystickInterrogateCoalescingEnabled = reader.ReadBool();
            m_loggedUnsupportedFeatureWarnings = 0;
            m_queuedKeyboardInjectedByteCount = reader.ReadInt32();
            m_queuedMousePacketByteCount = reader.ReadInt32();
            m_queuedJoystickEventByteCount = reader.ReadInt32();
            m_queuedJoystickInterrogateResponseByteCount = reader.ReadInt32();
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
        JoystickMonitoring = 5
    }

    [Flags]
    private enum UnsupportedFeatureWarningFlag : uint
    {
        [UsedImplicitly] None = 0,
        AbsoluteMouseModeLimited = 1 << 0,
        MouseKeycodeMode = 1 << 1,
        MouseThreshold = 1 << 2,
        MouseScale = 1 << 3,
        LoadMousePosition = 1 << 4,
        MouseYOrigin = 1 << 5,
        JoystickFireButtonMonitoring = 1 << 6,
        JoystickCursorKeycodes = 1 << 7,
        MouseButtonAction = 1 << 8,
        JoystickMonitoringTiming = 1 << 9
    }
}
