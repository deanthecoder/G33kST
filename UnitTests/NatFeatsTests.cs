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
using DTC.Emulation;
using DTC.M68000;

namespace UnitTests;

/// <summary>
/// Verifies NatFeats opcode handling and machine-level interception.
/// </summary>
[TestFixture]
public sealed class NatFeatsTests
{
    [Test]
    public void TryHandleShouldIgnoreNonNatFeatsOpcode()
    {
        var natFeats = new NatFeats();
        var cpu = CreateCpu();

        Assert.That(natFeats.TryHandle(cpu, 0x4E71), Is.False);
        Assert.That(natFeats.TotalCalls, Is.EqualTo(0));
    }

    [Test]
    public void TryHandleNfIdShouldReturnFeatureIdAndAdvanceProgramCounter()
    {
        var natFeats = new NatFeats();
        var cpu = CreateCpu();
        var featureAddress = 0x00003000u;

        cpu.Registers.ProgramCounter = 0x00002000;
        cpu.Registers.StackPointer = 0x00001000;
        WriteCString(cpu.Bus, featureAddress, "NF_STDERR");
        Write32BigEndian(cpu.Bus, cpu.Registers.StackPointer + 4, featureAddress);

        var handled = natFeats.TryHandle(cpu, 0x7300);

        Assert.That(handled, Is.True);
        Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x00002002u));
        Assert.That(cpu.Registers.GetDataRegister(0), Is.Not.EqualTo(0u));
        Assert.That(natFeats.TotalCalls, Is.EqualTo(1));
        Assert.That(natFeats.IdCalls, Is.EqualTo(1));
    }

    [Test]
    public void TryHandleNfCallForStderrShouldEmitMessage()
    {
        var natFeats = new NatFeats();
        var cpu = CreateCpu();
        var stackPointer = 0x00001000u;
        var featureAddress = 0x00003000u;
        var messageAddress = 0x00003100u;
        var messages = new List<string>();
        natFeats.MessageReceived += (_, e) => messages.Add(e.Message);

        cpu.Registers.StackPointer = stackPointer;
        cpu.Registers.ProgramCounter = 0x00002000;
        WriteCString(cpu.Bus, featureAddress, "NF_STDERR");
        Write32BigEndian(cpu.Bus, stackPointer + 4, featureAddress);
        Assert.That(natFeats.TryHandle(cpu, 0x7300), Is.True);
        var stderrFeatureId = cpu.Registers.GetDataRegister(0);
        Assert.That(stderrFeatureId, Is.Not.EqualTo(0u));

        cpu.Registers.ProgramCounter = 0x00002002;
        Write32BigEndian(cpu.Bus, stackPointer + 4, stderrFeatureId);
        Write32BigEndian(cpu.Bus, stackPointer + 8, messageAddress);
        WriteCString(cpu.Bus, messageAddress, "hello from EmuTOS");

        var handled = natFeats.TryHandle(cpu, 0x7301);

        Assert.That(handled, Is.True);
        Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x00002004u));
        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo((uint)"hello from EmuTOS".Length));
        Assert.That(messages, Is.EquivalentTo(new[] { "hello from EmuTOS" }));
        Assert.That(natFeats.CallCalls, Is.EqualTo(1));
        Assert.That(natFeats.UnknownFeatureCalls, Is.EqualTo(0));
    }

    [Test]
    public void StepCpuShouldInterceptNatFeatsBeforeCpuDecode()
    {
        var atariST = new AtariST();
        var stackPointer = 0x00001000u;
        var featureAddress = 0x00003000u;

        atariST.Cpu.Registers.ProgramCounter = 0x00002000;
        atariST.Cpu.Registers.StackPointer = stackPointer;
        atariST.Cpu.Bus.Write16BigEndian(0x00002000, 0x7300);
        Write32BigEndian(atariST.Cpu.Bus, stackPointer + 4, featureAddress);
        WriteCString(atariST.Cpu.Bus, featureAddress, "NF_STDERR");

        atariST.StepCpu();

        Assert.That(atariST.Cpu.Registers.ProgramCounter, Is.EqualTo(0x00002002u));
        Assert.That(atariST.Cpu.Registers.GetDataRegister(0), Is.Not.EqualTo(0u));
        Assert.That(atariST.NatFeats.TotalCalls, Is.EqualTo(1));
    }

    private static Cpu CreateCpu() =>
        new(new Bus(0x1000000));

    private static void WriteCString(Bus bus, uint address, string value)
    {
        for (var i = 0; i < value.Length; i++)
            bus.Write8(address + (uint)i, (byte)value[i]);

        bus.Write8(address + (uint)value.Length, 0);
    }

    private static void Write32BigEndian(Bus bus, uint address, uint value)
    {
        bus.Write8(address, (byte)(value >> 24));
        bus.Write8(address + 1, (byte)((value >> 16) & 0xFF));
        bus.Write8(address + 2, (byte)((value >> 8) & 0xFF));
        bus.Write8(address + 3, (byte)(value & 0xFF));
    }
}
