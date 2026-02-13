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
using DTC.Emulation.Devices;
using DTC.M68000;

namespace DTC.AtariST;

/// <summary>
/// Wires together Atari ST-specific devices into a single emulated machine.
/// </summary>
public sealed class AtariST : IMachine
{
    // Atari ST memory map:
    // $000000-$007FFF: ROM (first 32KB, typically for TOS)
    // $FC0000-$FEFFFF: ROM (192KB, main TOS ROM area)
    // $000000-$0FFFFF: RAM (1MB for ST 1040)
    // Note: ROM is typically mapped at $FC0000 but also appears at $000000 on reset

    private const int RamSize = 1024 * 1024; // 1MB for ST 1040
    internal const uint RomBaseAddress = 0xFC0000;
    private const int RomSize = 192 * 1024; // 192KB ROM
    private const byte SyntheticVblInterruptLevel = 4;
    private readonly Shifter m_video;
    private int m_pendingVblInterrupts;
    private byte m_latchedInterruptLevel;
    private bool m_hasLatchedInterrupt;

    public IMachineDescriptor Descriptor { get; } = new AtariSTDescriptor();

    public string Name => Descriptor.Name;

    public long CpuTicks => Cpu.CyclesSinceCpuStart;

    public bool HasLoadedCartridge => Rom.Data.Any(b => b != 0);

    // Minimal Shifter-backed video path.
    public IVideoSource Video => m_video;

    public IAudioSource Audio => null;

    public IMachineSnapshotter Snapshotter => null;

    public Cpu Cpu { get; }

    public Memory Ram { get; }

    public RomDevice Rom { get; }

    public RomMirrorDevice RomMirror { get; }

    public AtariST()
    {
        // Create main RAM and ROM
        Ram = new Memory(RamSize);
        Rom = new RomDevice(RomSize, RomBaseAddress);

        // Create ROM mirror for boot-time reset vector access
        RomMirror = new RomMirrorDevice(Rom);

        // Create bus with full 24-bit address space (16MB)
        // The 68000 has a 24-bit address bus, so create a dummy memory device for the full space
        var fullAddressSpace = new Memory(0x1000000); // 16MB to cover full 24-bit space
        var bus = new Bus(fullAddressSpace);

        // Attach RAM and ROM to the bus (they will override the full address space in their ranges)
        bus.Attach(Ram);
        bus.Attach(Rom);

        // Attach ROM mirror last so it takes priority over RAM at $000000-$000007
        bus.Attach(RomMirror);

        // Create CPU
        Cpu = new Cpu(bus);

        // Create minimal low-resolution video source.
        m_video = new Shifter(bus, Descriptor.CpuHz, Descriptor.VideoHz);
    }

    public void Reset()
    {
        m_video.Reset();
        m_pendingVblInterrupts = 0;
        m_hasLatchedInterrupt = false;
        m_latchedInterruptLevel = 0;
        Cpu.Reset();
    }

    public void LoadRom(byte[] romData, string romName)
    {
        if (romData == null || romData.Length == 0)
            throw new ArgumentException($"'{romName}' ROM data is empty.", nameof(romData));

        if (romData.Length > RomSize)
            throw new ArgumentException($"'{romName}'' ROM data is too large. Maximum size is {RomSize} bytes.", nameof(romData));

        // Copy ROM data into ROM memory
        Array.Copy(romData, 0, Rom.Data, 0, romData.Length);

        // Reset CPU to load vectors from ROM
        Reset();
    }

    public void StepCpu()
    {
        Cpu.Step();
    }

    public void AdvanceDevices(long deltaTicks)
    {
        m_video.Advance(deltaTicks, OnHblank, OnVblank);
    }

    public bool TryConsumeInterrupt()
    {
        if (m_hasLatchedInterrupt)
            return true;

        if (m_pendingVblInterrupts <= 0)
            return false;

        m_pendingVblInterrupts--;
        m_latchedInterruptLevel = SyntheticVblInterruptLevel;
        m_hasLatchedInterrupt = true;
        return true;
    }

    public void RequestInterrupt()
    {
        if (!m_hasLatchedInterrupt)
            return;

        Cpu.RequestInterrupt(m_latchedInterruptLevel);
        m_hasLatchedInterrupt = false;
        m_latchedInterruptLevel = 0;
    }

    public void SetInputActive(bool isActive)
    {
        // TODO: Implement input handling
    }

    private void OnHblank()
    {
        // Hook point for future HBlank-timed devices.
    }

    private void OnVblank()
    {
        if (m_pendingVblInterrupts < int.MaxValue)
            m_pendingVblInterrupts++;
    }
}
