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
/// Mirrors the first two longwords of TOS ROM at address $000000.
/// On Atari ST hardware, these 8 bytes stay mapped to ROM so reset vectors remain fetchable there.
/// </summary>
public sealed class RomMirrorDevice : IMemDevice
{
    private readonly IMemDevice m_rom;
    private readonly uint m_windowSize;

    public uint FromAddr => 0x000000;
    public uint ToAddr => m_windowSize - 1;

    public RomMirrorDevice(IMemDevice rom, uint windowSize)
    {
        m_rom = rom ?? throw new ArgumentNullException(nameof(rom));
        if (windowSize == 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "ROM mirror window size must be greater than zero.");
        m_windowSize = windowSize;
    }

    public byte Read8(uint address)
    {
        var romAddress = address + AtariST.RomBaseAddress;
        return m_rom.Read8(romAddress);
    }

    public void Write8(uint address, byte value)
    {
        // Writes are ignored: these first vectors remain ROM-backed.
    }
}
