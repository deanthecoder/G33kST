// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.AtariST;

namespace UnitTests;

/// <summary>
/// Tests for the Atari ST emulator machine.
/// </summary>
[TestFixture]
public sealed class AtariSTTests : TestsBase
{
    [Test]
    public void Constructor_ShouldInitializeMachineWithDefaultDescriptor()
    {
        // Arrange & Act
        var atariST = new AtariST();

        // Assert
        Assert.That(atariST, Is.Not.Null);
        Assert.That(atariST.Name, Is.EqualTo("Atari ST 1040 STFM"));
        Assert.That(atariST.Descriptor, Is.Not.Null);
        Assert.That(atariST.Descriptor.CpuHz, Is.EqualTo(8_000_000.0));
        Assert.That(atariST.Descriptor.VideoHz, Is.EqualTo(60.0));
        Assert.That(atariST.Descriptor.FrameWidth, Is.EqualTo(640));
        Assert.That(atariST.Descriptor.FrameHeight, Is.EqualTo(400));
        Assert.That(atariST.Video, Is.Not.Null);
        Assert.That(atariST.Video.FrameWidth, Is.EqualTo(640));
        Assert.That(atariST.Video.FrameHeight, Is.EqualTo(400));
    }

    [Test]
    public void Constructor_ShouldInitializeCpuAndMemory()
    {
        // Arrange & Act
        var atariST = new AtariST();

        // Assert
        Assert.That(atariST.Cpu, Is.Not.Null);
        Assert.That(atariST.Ram, Is.Not.Null);
        Assert.That(atariST.Rom, Is.Not.Null);
        Assert.That(atariST.Ram.Data.Length, Is.EqualTo(1024 * 1024)); // 1MB RAM
        Assert.That(atariST.Rom.Data.Length, Is.EqualTo(192 * 1024)); // 192KB ROM
    }

    [Test]
    public void Constructor_ShouldInitializeWithZeroCycles()
    {
        // Arrange & Act
        var atariST = new AtariST();

        // Assert
        Assert.That(atariST.CpuTicks, Is.EqualTo(0));
    }

    [Test]
    public void HasLoadedCartridge_ShouldBeFalseInitially()
    {
        // Arrange & Act
        var atariST = new AtariST();

        // Assert
        Assert.That(atariST.HasLoadedCartridge, Is.False);
    }

    [Test]
    public void LoadRom_ShouldThrowWhenRomDataIsNull()
    {
        // Arrange
        var atariST = new AtariST();

        // Act & Assert
        Assert.That(() => atariST.LoadRom(null, "test.rom"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void LoadRom_ShouldThrowWhenRomDataIsEmpty()
    {
        // Arrange
        var atariST = new AtariST();

        // Act & Assert
        Assert.That(() => atariST.LoadRom(Array.Empty<byte>(), "test.rom"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void LoadRom_ShouldThrowWhenRomDataIsTooLarge()
    {
        // Arrange
        var atariST = new AtariST();
        var tooLargeRom = new byte[200 * 1024]; // 200KB, larger than 192KB max

        // Act & Assert
        Assert.That(() => atariST.LoadRom(tooLargeRom, "test.rom"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void LoadRom_ShouldLoadValidRomData()
    {
        // Arrange
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();

        // Act
        atariST.LoadRom(romData, "tos.img");

        // Assert
        Assert.That(atariST.HasLoadedCartridge, Is.True);
        // Verify ROM data was copied
        for (int i = 0; i < romData.Length; i++)
        {
            Assert.That(atariST.Rom.Data[i], Is.EqualTo(romData[i]), $"ROM byte at offset {i} should match");
        }
    }

    [Test]
    public void Reset_ShouldResetCpuState()
    {
        // Arrange
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");

        // Step CPU to change state
        atariST.StepCpu();
        var ticksAfterStep = atariST.CpuTicks;

        // Act
        atariST.Reset();

        // Assert
        Assert.That(atariST.CpuTicks, Is.EqualTo(0));
        Assert.That(atariST.Cpu.Registers.ProgramCounter, Is.Not.EqualTo(0xFC0000)); // Should have loaded from reset vector
    }

    [Test]
    public void RomDevice_ShouldBeReadOnly()
    {
        // Arrange
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");

        // Act - Try to write to ROM via the device
        var originalValue = atariST.Rom.Data[0];
        atariST.Rom.Write8(0xFC0000, 0xFF);

        // Assert - Value should remain unchanged
        Assert.That(atariST.Rom.Data[0], Is.EqualTo(originalValue));
    }

    [Test]
    public void RomDevice_ShouldBeAccessibleAtCorrectAddress()
    {
        // Arrange
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");

        // Act - Read from ROM address
        var value = atariST.Rom.Read8(0xFC0000);

        // Assert
        Assert.That(value, Is.EqualTo(romData[0]));
    }

    [Test]
    public void StepCpu_ShouldExecuteInstruction()
    {
        // Arrange
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");

        var initialTicks = atariST.CpuTicks;

        // Act
        atariST.StepCpu();

        // Assert
        Assert.That(atariST.CpuTicks, Is.GreaterThan(initialTicks), "CPU should have advanced cycles");
    }

    [Test]
    public void RomMirror_ShouldBeAttachedToBus()
    {
        // Arrange & Act
        var atariST = new AtariST();

        // Assert
        Assert.That(atariST.RomMirror, Is.Not.Null);
    }

    [Test]
    public void AdvanceDevices_ShouldScheduleVblankInterrupt()
    {
        // Arrange
        var atariST = new AtariST();
        atariST.Reset();
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);

        // Act
        atariST.AdvanceDevices(oneFrameTicks);

        // Assert
        Assert.That(atariST.TryConsumeInterrupt(), Is.True);
    }

    [Test]
    public void Video_ShouldRenderLowResolutionBitplanesToFrameBuffer()
    {
        // Arrange
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint screenBaseAddress = 0x00000100;
        const uint videoBaseHighRegister = 0x00FF8201;
        const uint videoBaseMidRegister = 0x00FF8203;
        const uint videoModeRegister = 0x00FF8260;
        const uint paletteBaseRegister = 0x00FF8240;
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);
        var frameRendered = false;
        atariST.Video.FrameRendered += (_, _) => frameRendered = true;

        // Low-res mode.
        bus.Write8(videoModeRegister, 0x00);

        // Screen base = $000100.
        bus.Write8(videoBaseHighRegister, 0x00);
        bus.Write8(videoBaseMidRegister, 0x01);

        // Palette index 1 -> full red (3-bit channel max).
        bus.Write16BigEndian(paletteBaseRegister + 2, 0x0700);

        // First 16-pixel chunk: pixel 0 has index 1, the rest use index 0.
        bus.Write16BigEndian(screenBaseAddress, 0x8000); // Plane 0
        bus.Write16BigEndian(screenBaseAddress + 2, 0x0000); // Plane 1
        bus.Write16BigEndian(screenBaseAddress + 4, 0x0000); // Plane 2
        bus.Write16BigEndian(screenBaseAddress + 6, 0x0000); // Plane 3

        // Act
        atariST.AdvanceDevices(oneFrameTicks);
        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * 4];
        atariST.Video.CopyToFrameBuffer(frameBuffer);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(frameRendered, Is.True);

            // Pixel 0: red.
            Assert.That(frameBuffer[0], Is.EqualTo(255));
            Assert.That(frameBuffer[1], Is.EqualTo(0));
            Assert.That(frameBuffer[2], Is.EqualTo(0));
            Assert.That(frameBuffer[3], Is.EqualTo(255));

            // Pixel 1: duplicated low-res pixel 0 (also red due 2x horizontal scaling).
            Assert.That(frameBuffer[4], Is.EqualTo(255));
            Assert.That(frameBuffer[5], Is.EqualTo(0));
            Assert.That(frameBuffer[6], Is.EqualTo(0));
            Assert.That(frameBuffer[7], Is.EqualTo(255));

            // Pixel 2: first black pixel after 2x horizontal scaling.
            Assert.That(frameBuffer[8], Is.EqualTo(0));
            Assert.That(frameBuffer[9], Is.EqualTo(0));
            Assert.That(frameBuffer[10], Is.EqualTo(0));
            Assert.That(frameBuffer[11], Is.EqualTo(255));
        });
    }

    [Test]
    public void Video_ShouldRenderHighResolutionMonochromeWithZeroAsWhite()
    {
        // Arrange
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint screenBaseAddress = 0x00000100;
        const uint videoBaseHighRegister = 0x00FF8201;
        const uint videoBaseMidRegister = 0x00FF8203;
        const uint videoModeRegister = 0x00FF8260;
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);

        // High-res mono mode.
        bus.Write8(videoModeRegister, 0x02);

        // Screen base = $000100.
        bus.Write8(videoBaseHighRegister, 0x00);
        bus.Write8(videoBaseMidRegister, 0x01);

        // Bit 15 set (pixel 0 "ink"), remaining bits clear (background).
        bus.Write16BigEndian(screenBaseAddress, 0x8000);

        // Act
        atariST.AdvanceDevices(oneFrameTicks);
        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * 4];
        atariST.Video.CopyToFrameBuffer(frameBuffer);

        // Assert
        Assert.Multiple(() =>
        {
            // Pixel 0 should be black (set bit).
            Assert.That(frameBuffer[0], Is.EqualTo(0));
            Assert.That(frameBuffer[1], Is.EqualTo(0));
            Assert.That(frameBuffer[2], Is.EqualTo(0));
            Assert.That(frameBuffer[3], Is.EqualTo(255));

            // Pixel 1 should be white (clear bit).
            Assert.That(frameBuffer[4], Is.EqualTo(255));
            Assert.That(frameBuffer[5], Is.EqualTo(255));
            Assert.That(frameBuffer[6], Is.EqualTo(255));
            Assert.That(frameBuffer[7], Is.EqualTo(255));
        });
    }

    [Test]
    public void Video_ShouldRenderMediumResolutionWithVerticalScaling()
    {
        // Arrange
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint screenBaseAddress = 0x00000100;
        const uint videoBaseHighRegister = 0x00FF8201;
        const uint videoBaseMidRegister = 0x00FF8203;
        const uint videoModeRegister = 0x00FF8260;
        const uint paletteBaseRegister = 0x00FF8240;
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);

        // Medium-res mode.
        bus.Write8(videoModeRegister, 0x01);

        // Screen base = $000100.
        bus.Write8(videoBaseHighRegister, 0x00);
        bus.Write8(videoBaseMidRegister, 0x01);

        // Palette index 2 -> full green.
        bus.Write16BigEndian(paletteBaseRegister + 4, 0x0070);

        // First 16-pixel chunk: pixel 0 has index 2, others index 0.
        bus.Write16BigEndian(screenBaseAddress, 0x0000); // Plane 0
        bus.Write16BigEndian(screenBaseAddress + 2, 0x8000); // Plane 1

        // Act
        atariST.AdvanceDevices(oneFrameTicks);
        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * 4];
        atariST.Video.CopyToFrameBuffer(frameBuffer);

        // Assert
        Assert.Multiple(() =>
        {
            // Pixel (0,0): green.
            Assert.That(frameBuffer[0], Is.EqualTo(0));
            Assert.That(frameBuffer[1], Is.EqualTo(255));
            Assert.That(frameBuffer[2], Is.EqualTo(0));
            Assert.That(frameBuffer[3], Is.EqualTo(255));

            // Pixel (0,1): also green (2x vertical scaling in medium mode).
            var secondLineOffset = atariST.Video.FrameWidth * 4;
            Assert.That(frameBuffer[secondLineOffset], Is.EqualTo(0));
            Assert.That(frameBuffer[secondLineOffset + 1], Is.EqualTo(255));
            Assert.That(frameBuffer[secondLineOffset + 2], Is.EqualTo(0));
            Assert.That(frameBuffer[secondLineOffset + 3], Is.EqualTo(255));

            // Pixel (1,0): black (next source bit is clear).
            Assert.That(frameBuffer[4], Is.EqualTo(0));
            Assert.That(frameBuffer[5], Is.EqualTo(0));
            Assert.That(frameBuffer[6], Is.EqualTo(0));
            Assert.That(frameBuffer[7], Is.EqualTo(255));
        });
    }

    /// <summary>
    /// Creates a minimal TOS ROM image with valid reset vectors.
    /// The 68000 expects the initial SSP at $000000 and initial PC at $000004.
    /// For the Atari ST, ROM appears at $FC0000, so we create vectors there.
    /// </summary>
    private static byte[] CreateMinimalTosRom()
    {
        var rom = new byte[192 * 1024]; // 192KB

        // Set initial SSP (supervisor stack pointer) at $FC0000: $00001000
        rom[0] = 0x00;
        rom[1] = 0x00;
        rom[2] = 0x10;
        rom[3] = 0x00;

        // Set initial PC (program counter) at $FC0004: $00FC0100 (start of code in ROM)
        rom[4] = 0x00;
        rom[5] = 0xFC;
        rom[6] = 0x01;
        rom[7] = 0x00;

        // At $FC0100 (offset $100 in ROM), place a simple infinite loop: 0x4E71 (NOP)
        rom[0x100] = 0x4E;
        rom[0x101] = 0x71;

        return rom;
    }
}
