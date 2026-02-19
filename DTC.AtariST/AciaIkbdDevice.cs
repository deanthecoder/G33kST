// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation;

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
    private readonly byte[] m_lastJoystickStateBytes = new byte[2];
    private readonly byte[] m_pendingCommandParameters = new byte[8];
    private bool m_mouseReportingEnabled = true;
    private bool m_outputPaused;
    private bool m_joystickEventModeEnabled = true;
    private byte m_pendingCommand;
    private int m_pendingCommandParameterCount;
    private int m_pendingCommandParameterIndex;
    private byte m_mouseMode = IkbdMouseModeRelative;
    private byte m_keyboardControl;
    private bool m_keyboardInterruptLineActive;

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
    /// Injects one keyboard data byte into the receive queue.
    /// </summary>
    public void QueueKeyboardByte(byte value)
    {
        lock (m_stateSync)
            EnqueueKeyboardByteNoLock(value);
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

            EnqueueKeyboardByteNoLock(header);
            EnqueueKeyboardByteNoLock((byte)deltaX);
            EnqueueKeyboardByteNoLock((byte)deltaY);
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
            if (m_outputPaused || !m_joystickEventModeEnabled)
                return;

            var header = joystickIndex == 0 ? JoystickPacketHeaderPort0 : JoystickPacketHeaderPort1;
            EnqueueKeyboardByteNoLock(header);
            EnqueueKeyboardByteNoLock(stateByte);
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
            m_keyboardControl = 0;
            m_keyboardInterruptLineActive = false;
            m_pendingCommand = 0;
            m_pendingCommandParameterCount = 0;
            m_pendingCommandParameterIndex = 0;
            m_mouseReportingEnabled = true;
            m_outputPaused = false;
            m_mouseMode = IkbdMouseModeRelative;
            m_joystickEventModeEnabled = true;
            m_lastJoystickStateBytes[0] = 0;
            m_lastJoystickStateBytes[1] = 0;
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
        RefreshInterruptLineNoLock();
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
        m_pendingCommand = 0;
        m_pendingCommandParameterCount = 0;
        m_pendingCommandParameterIndex = 0;
        m_keyboardControl = 0;
        RefreshInterruptLineNoLock();
    }

    private void HandleKeyboardDataWriteNoLock(byte value)
    {
        if (m_pendingCommandParameterCount > 0)
        {
            m_pendingCommandParameters[m_pendingCommandParameterIndex++] = value;
            if (m_pendingCommandParameterIndex < m_pendingCommandParameterCount)
                return;

            ExecuteIkbdCommandNoLock(m_pendingCommand, m_pendingCommandParameters.AsSpan(0, m_pendingCommandParameterCount));
            ClearPendingCommandNoLock();
            return;
        }

        if (!TryGetIkbdCommandParameterCount(value, out var parameterCount))
            return;

        if (parameterCount == 0)
        {
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
                m_mouseReportingEnabled = true;
                m_outputPaused = false;
                m_mouseMode = IkbdMouseModeRelative;
                m_joystickEventModeEnabled = true;
                m_lastJoystickStateBytes[0] = 0;
                m_lastJoystickStateBytes[1] = 0;
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

        if (command == IkbdSetAbsoluteMouseModeCommand)
        {
            m_mouseMode = IkbdSetAbsoluteMouseModeCommand;
            m_mouseReportingEnabled = true;
            return;
        }

        if (command == IkbdSetMouseKeycodeModeCommand)
        {
            m_mouseMode = IkbdSetMouseKeycodeModeCommand;
            m_mouseReportingEnabled = true;
            return;
        }

        if (command == IkbdSetYZeroBottomCommand)
            return;

        if (command == IkbdSetYZeroTopCommand)
            return;

        if (command == IkbdDisableMouseReportingCommand)
        {
            m_mouseReportingEnabled = false;
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
            QueueActiveJoystickStatePacketsNoLock();
            return;
        }

        if (command == IkbdSetJoystickInterrogateModeCommand)
        {
            m_joystickEventModeEnabled = false;
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

    private void EnqueueKeyboardByteNoLock(byte value)
    {
        m_keyboardReceiveQueue.Enqueue(value);
        RefreshInterruptLineNoLock();
        KeyboardDataReady?.Invoke(this, EventArgs.Empty);
    }

    private void QueueJoystickInterrogationResponseNoLock()
    {
        if (m_outputPaused)
            return;

        EnqueueKeyboardByteNoLock(JoystickInterrogationHeader);
        EnqueueKeyboardByteNoLock(m_lastJoystickStateBytes[0]);
        EnqueueKeyboardByteNoLock(m_lastJoystickStateBytes[1]);
    }

    private void QueueActiveJoystickStatePacketsNoLock()
    {
        if (m_outputPaused || !m_joystickEventModeEnabled)
            return;
        if (m_lastJoystickStateBytes[0] != 0)
        {
            EnqueueKeyboardByteNoLock(JoystickPacketHeaderPort0);
            EnqueueKeyboardByteNoLock(m_lastJoystickStateBytes[0]);
        }
        if (m_lastJoystickStateBytes[1] == 0)
            return;

        EnqueueKeyboardByteNoLock(JoystickPacketHeaderPort1);
        EnqueueKeyboardByteNoLock(m_lastJoystickStateBytes[1]);
    }

    private void RefreshInterruptLineNoLock()
    {
        var isActive = m_keyboardReceiveQueue.Count > 0 && (m_keyboardControl & InterruptRequestFlag) != 0;
        if (isActive == m_keyboardInterruptLineActive)
            return;

        m_keyboardInterruptLineActive = isActive;
        KeyboardInterruptLineChanged?.Invoke(isActive);
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

    private enum AciaRegister
    {
        None = 0,
        KeyboardStatus,
        KeyboardData,
        MidiStatus,
        MidiData
    }
}
