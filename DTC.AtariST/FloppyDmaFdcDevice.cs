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
/// Minimal Atari ST DMA/FDC register block for floppy-drive detection.
/// </summary>
/// <remarks>
/// This is intentionally narrow: it implements enough command/status behavior
/// for TOS to detect attached floppy drives and continue booting gracefully
/// with no media inserted.
/// </remarks>
public sealed class FloppyDmaFdcDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FF8600;
    private const uint DataRegisterAddress = BaseAddress + 0x04;
    private const uint ControlRegisterAddress = BaseAddress + 0x06;
    private const uint ModeControlRegisterAddress = BaseAddress + 0x0F;
    private const ushort DmaA0 = 0x0002;
    private const ushort DmaA1 = 0x0004;
    private const ushort DmaScReg = 0x0010;
    private const ushort DmaCsAcsi = 0x0008;
    private const ushort DmaDrqFloppy = 0x0080;
    private const ushort DmaWriteBit = 0x0100;
    private const byte FdcTrackZeroMask = 0x04;
    private const byte FdcRecordNotFoundMask = 0x10;
    private const byte FdcWriteProtectMask = 0x40;
    private readonly bool[] m_drivePresent;
    private ushort m_controlRegister;
    private byte m_dataHighByte;
    private byte m_controlHighByte;
    private byte m_fdcStatusRegister;
    private byte m_fdcTrackRegister;
    private byte m_fdcSectorRegister;
    private byte m_fdcDataRegister;
    private byte m_modeControlRegister;
    private int m_selectedDrive = -1;
    private bool m_interruptLineIsActiveLow;

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + 0x0F;

    /// <summary>
    /// Raised when the emulated FDC interrupt/completion line changes.
    /// </summary>
    public event Action<bool> InterruptLineChanged;

    public FloppyDmaFdcDevice(bool driveAPresent = true, bool driveBPresent = false)
    {
        m_drivePresent = [driveAPresent, driveBPresent];
        Reset();
    }

    /// <summary>
    /// Resets DMA/FDC state to power-on defaults.
    /// </summary>
    public void Reset()
    {
        m_controlRegister = 0;
        m_dataHighByte = 0;
        m_controlHighByte = 0;
        m_fdcStatusRegister = 0;
        m_fdcTrackRegister = 0;
        m_fdcSectorRegister = 0;
        m_fdcDataRegister = 0;
        m_modeControlRegister = 0;
        m_selectedDrive = -1;
        SetInterruptLine(activeLow: false);
    }

    /// <summary>
    /// Applies the PSG port-A drive-select/side lines to the controller.
    /// </summary>
    public void ApplyPortA(byte portAValue)
    {
        var driveASelected = (portAValue & 0x02) == 0;
        var driveBSelected = (portAValue & 0x04) == 0;

        if (driveASelected && !driveBSelected)
            m_selectedDrive = 0;
        else if (driveBSelected && !driveASelected)
            m_selectedDrive = 1;
        else if (driveASelected && driveBSelected)
            m_selectedDrive = 0;
        else
            m_selectedDrive = -1;
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        if (address < BaseAddress || address > ToAddr)
            return 0xFF;
        if (address == DataRegisterAddress)
            return (byte)(ReadDataRegister() >> 8);
        if (address == DataRegisterAddress + 1)
            return (byte)(ReadDataRegister() & 0xFF);
        if (address == ControlRegisterAddress)
            return (byte)(m_controlRegister >> 8);
        if (address == ControlRegisterAddress + 1)
            return (byte)(m_controlRegister & 0xFF);
        if (address == ModeControlRegisterAddress)
            return m_modeControlRegister;
        return 0xFF;
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        if (address < BaseAddress || address > ToAddr)
            return;
        if (address == DataRegisterAddress)
        {
            m_dataHighByte = value;
            return;
        }
        if (address == DataRegisterAddress + 1)
        {
            var wordValue = (ushort)((m_dataHighByte << 8) | value);
            WriteDataRegister(wordValue);
            return;
        }
        if (address == ControlRegisterAddress)
        {
            m_controlHighByte = value;
            return;
        }
        if (address == ControlRegisterAddress + 1)
        {
            m_controlRegister = (ushort)((m_controlHighByte << 8) | value);
            return;
        }
        if (address == ModeControlRegisterAddress)
            m_modeControlRegister = value;
    }

    private ushort ReadDataRegister()
    {
        if (!IsFloppyRegisterAccess(m_controlRegister))
            return 0;

        var registerSelect = m_controlRegister & (DmaA1 | DmaA0);
        return registerSelect switch
        {
            0 => ReadStatusWord(),
            DmaA0 => m_fdcTrackRegister,
            DmaA1 => m_fdcSectorRegister,
            DmaA1 | DmaA0 => m_fdcDataRegister,
            _ => 0
        };
    }

    private ushort ReadStatusWord()
    {
        var status = BuildDriveStatus();

        // Reading status acknowledges command completion on ST software paths.
        SetInterruptLine(activeLow: false);
        return status;
    }

    private void WriteDataRegister(ushort value)
    {
        if (!IsFloppyRegisterAccess(m_controlRegister))
            return;
        if ((m_controlRegister & DmaScReg) != 0)
            return;

        var registerSelect = m_controlRegister & (DmaA1 | DmaA0);
        var byteValue = (byte)(value & 0xFF);
        switch (registerSelect)
        {
            case 0:
                ExecuteCommand(byteValue);
                return;
            case DmaA0:
                m_fdcTrackRegister = byteValue;
                return;
            case DmaA1:
                m_fdcSectorRegister = byteValue;
                return;
            case DmaA1 | DmaA0:
                m_fdcDataRegister = byteValue;
                return;
        }
    }

    private void ExecuteCommand(byte command)
    {
        var opcode = (byte)(command & 0xF0);
        var driveConnected = IsSelectedDrivePresent();
        switch (opcode)
        {
            case 0xD0: // Force interrupt.
                m_fdcStatusRegister = BuildDriveStatus();
                SetInterruptLine(activeLow: true);
                return;

            case 0x00: // Restore.
                if (driveConnected)
                {
                    m_fdcTrackRegister = 0;
                    m_fdcStatusRegister = BuildDriveStatus();
                }
                else
                    m_fdcStatusRegister = FdcRecordNotFoundMask;
                SetInterruptLine(activeLow: true);
                return;

            case 0x80: // Read sector.
            case 0xA0: // Write sector.
            case 0xC0: // Read address.
            case 0xE0: // Read track.
            case 0xF0: // Write track.
                m_fdcStatusRegister = driveConnected ? FdcRecordNotFoundMask : (byte)(FdcRecordNotFoundMask | FdcWriteProtectMask);
                SetInterruptLine(activeLow: true);
                return;

            default:
                m_fdcStatusRegister = BuildDriveStatus();
                SetInterruptLine(activeLow: true);
                return;
        }
    }

    private byte BuildDriveStatus()
    {
        if (!IsSelectedDrivePresent())
            return 0;

        var status = m_fdcStatusRegister;
        status |= FdcTrackZeroMask;
        return status;
    }

    private bool IsSelectedDrivePresent()
    {
        if (m_selectedDrive < 0 || m_selectedDrive >= m_drivePresent.Length)
            return false;
        return m_drivePresent[m_selectedDrive];
    }

    private static bool IsFloppyRegisterAccess(ushort controlRegister)
    {
        var isAcsiAccess = (controlRegister & DmaCsAcsi) != 0;
        var isFloppyDrqSelected = (controlRegister & DmaDrqFloppy) != 0;
        return !isAcsiAccess && isFloppyDrqSelected;
    }

    private void SetInterruptLine(bool activeLow)
    {
        if (m_interruptLineIsActiveLow == activeLow)
            return;

        m_interruptLineIsActiveLow = activeLow;
        InterruptLineChanged?.Invoke(activeLow);
    }
}
