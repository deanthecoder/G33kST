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
using DTC.Emulation.Snapshot;

namespace DTC.AtariST;

/// <summary>
/// Provides a writable register window for ST Shifter/MMU video registers.
/// </summary>
/// <remarks>
/// This device backs addresses in the $FF82xx range that are used for video
/// base, palette and mode control so software can read/write them consistently.
/// </remarks>
public sealed class ShifterRegistersDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FF8200;
    private const int RegisterCount = 0x80;
    private readonly byte[] m_registers = new byte[RegisterCount];

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + RegisterCount - 1;

    /// <summary>
    /// Resets register contents to power-on values.
    /// </summary>
    public void Reset() =>
        Array.Clear(m_registers, 0, m_registers.Length);

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        var index = (int)(address - BaseAddress);
        if (index < 0 || index >= RegisterCount)
            return 0xFF;
        return m_registers[index];
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        var index = (int)(address - BaseAddress);
        if (index < 0 || index >= RegisterCount)
            return;
        m_registers[index] = value;
    }

    internal int GetStateSize() => m_registers.Length;

    internal void SaveState(ref StateWriter writer) =>
        writer.WriteBytes(m_registers);

    internal void LoadState(ref StateReader reader) =>
        reader.ReadBytes(m_registers);
}
