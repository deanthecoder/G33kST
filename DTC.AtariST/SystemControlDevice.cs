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
/// Minimal ST system-control register block.
/// This currently only exposes enough behavior to observe writes to the memory configuration
/// register at $FF8001 so boot-time ROM overlay can be disabled at the same point as real hardware.
/// </summary>
public sealed class SystemControlDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FF8000;
    private readonly byte m_defaultMemoryConfiguration;
    private readonly byte[] m_registers = new byte[16];

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + 0x0F;

    /// <summary>
    /// Raised when the memory configuration register ($FF8001) is written.
    /// </summary>
    public event Action<byte> MemoryConfigurationWritten;

    public SystemControlDevice(byte defaultMemoryConfiguration = 0x05)
    {
        m_defaultMemoryConfiguration = defaultMemoryConfiguration;
        Reset();
    }

    /// <summary>
    /// Restores power-on register defaults.
    /// </summary>
    public void Reset()
    {
        Array.Clear(m_registers, 0, m_registers.Length);
        m_registers[1] = m_defaultMemoryConfiguration;
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        var index = (int)(address - BaseAddress);
        if (index is < 0 or >= 16)
            return 0xFF;
        return m_registers[index];
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        var index = (int)(address - BaseAddress);
        if (index is < 0 or >= 16)
            return;
        m_registers[index] = value;
        if (index == 1)
            MemoryConfigurationWritten?.Invoke(value);
    }
}
