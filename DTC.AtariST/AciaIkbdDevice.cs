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
/// This currently implements basic keyboard ACIA status/data behavior only.
/// </summary>
public sealed class AciaIkbdDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FFFC00;
    private const byte ReceiveDataReadyFlag = 0x01;
    private const byte TransmitDataEmptyFlag = 0x02;
    private const byte InterruptRequestFlag = 0x80;
    private const byte MasterResetControlMask = 0x03;
    private const byte IkbdResetCommand = 0x80;
    private const byte IkbdResetParameter = 0x01;
    private const byte IkbdResetCompleteCode = 0xF1;
    private readonly Queue<byte> m_keyboardReceiveQueue = [];
    private bool m_waitingForResetParameter;
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
    public void QueueKeyboardByte(byte value) =>
        EnqueueKeyboardByte(value);

    /// <summary>
    /// Resets ACIA/IKBD transient state.
    /// </summary>
    public void Reset()
    {
        m_keyboardReceiveQueue.Clear();
        m_waitingForResetParameter = false;
        m_keyboardControl = 0;
        m_keyboardInterruptLineActive = false;
        m_keyboardReceiveQueue.Enqueue(IkbdResetCompleteCode);
        RefreshInterruptLine();
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        var register = GetRegister(address);
        if (register == AciaRegister.None)
            return 0xFF;
        if (register == AciaRegister.KeyboardStatus)
            return BuildStatusByte(isKeyboardPort: true);
        if (register == AciaRegister.MidiStatus)
            return BuildStatusByte(isKeyboardPort: false);
        if (register == AciaRegister.KeyboardData)
            return ReadKeyboardData();
        if (register == AciaRegister.MidiData)
            return 0x00;
        return 0xFF;
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        var register = GetRegister(address);
        if (register == AciaRegister.None)
            return;
        if (register is AciaRegister.KeyboardStatus or AciaRegister.MidiStatus)
        {
            HandleControlWrite(register, value);
            return;
        }

        if (register == AciaRegister.KeyboardData)
            HandleKeyboardDataWrite(value);
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

    private byte ReadKeyboardData()
    {
        if (m_keyboardReceiveQueue.Count == 0)
            return 0x00;

        var value = m_keyboardReceiveQueue.Dequeue();
        RefreshInterruptLine();
        return value;
    }

    private void HandleControlWrite(AciaRegister register, byte value)
    {
        if ((value & MasterResetControlMask) != MasterResetControlMask)
        {
            if (register == AciaRegister.KeyboardStatus)
            {
                m_keyboardControl = value;
                RefreshInterruptLine();
            }
            return;
        }
        if (register != AciaRegister.KeyboardStatus)
            return;

        // Keyboard ACIA master reset.
        m_keyboardReceiveQueue.Clear();
        m_waitingForResetParameter = false;
        m_keyboardControl = 0;
        RefreshInterruptLine();
    }

    private void HandleKeyboardDataWrite(byte value)
    {
        // Minimal IKBD command support:
        // 0x80 0x01 (reset) responds with 0xF0 to acknowledge keyboard reset completion.
        if (m_waitingForResetParameter)
        {
            m_waitingForResetParameter = false;
            if (value == IkbdResetParameter)
            {
                m_keyboardReceiveQueue.Clear();
                EnqueueKeyboardByte(IkbdResetCompleteCode);
            }
            return;
        }

        if (value == IkbdResetCommand)
            m_waitingForResetParameter = true;
    }

    private void EnqueueKeyboardByte(byte value)
    {
        m_keyboardReceiveQueue.Enqueue(value);
        RefreshInterruptLine();
        KeyboardDataReady?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshInterruptLine()
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
