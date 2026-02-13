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
/// Mirrors the TOS ROM at address $000000 for boot-time reset vector access.
/// On real hardware, the ROM appears at $000000 during reset, then the memory
/// controller switches to map RAM there after TOS initializes.
/// To disable the mirror, simply detach this device from the bus.
/// </summary>
public sealed class RomMirrorDevice : IMemDevice
{
    private readonly IMemDevice m_rom;

    public uint FromAddr => 0x000000;
    public uint ToAddr => 0x000007; // Only mirror the reset vectors (8 bytes)

    public RomMirrorDevice(IMemDevice rom)
    {
        m_rom = rom ?? throw new ArgumentNullException(nameof(rom));
    }

    public byte Read8(uint address)
    {
        // Translate $000000-$000007 to $FC0000-$FC0007 in ROM
        var romAddress = address + AtariST.RomBaseAddress;
        return m_rom.Read8(romAddress);
    }

    public void Write8(uint address, byte value)
    {
        // ROM mirror is read-only, ignore writes.
    }
}
