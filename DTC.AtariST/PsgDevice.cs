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
/// Minimal YM2149 PSG register window used for ST system control signals.
/// </summary>
/// <remarks>
/// For floppy bring-up we currently model register select plus read/write of
/// port-A bits, which carry drive-select/side lines on Atari ST hardware.
/// </remarks>
public sealed class PsgDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FF8800;
    private const byte PortARegister = 0x0E;
    private readonly byte[] m_registers = new byte[16];
    private byte m_selectedRegister;

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + 3;

    /// <summary>
    /// Raised when PSG port-A output value changes.
    /// </summary>
    public event Action<byte> PortAChanged;

    /// <summary>
    /// Resets PSG register state needed by system devices.
    /// </summary>
    public void Reset()
    {
        Array.Clear(m_registers, 0, m_registers.Length);
        m_selectedRegister = 0;
        m_registers[PortARegister] = 0x07;
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        var offset = address - BaseAddress;
        if (offset > 3)
            return 0xFF;

        if (offset == 0 || offset == 2)
            return m_registers[m_selectedRegister];

        return 0xFF;
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        var offset = address - BaseAddress;
        if (offset > 3)
            return;

        if (offset == 0)
        {
            m_selectedRegister = (byte)(value & 0x0F);
            return;
        }
        if (offset != 2)
            return;

        m_registers[m_selectedRegister] = value;
        if (m_selectedRegister == PortARegister)
            PortAChanged?.Invoke(value);
    }
}
