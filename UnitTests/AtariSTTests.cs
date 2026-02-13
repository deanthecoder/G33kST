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
        Assert.That(atariST.Descriptor.FrameWidth, Is.EqualTo(320));
        Assert.That(atariST.Descriptor.FrameHeight, Is.EqualTo(200));
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
