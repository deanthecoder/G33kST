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
using DTC.M68000;

namespace UnitTests;

[TestFixture]
public sealed class CpuTests
{
    [Test]
    public void ResetLoadsSupervisorStackPointerAndProgramCounterFromVectors()
    {
        var bus = new Bus(0x1000000);
        bus.Write8(0x000000, 0x00);
        bus.Write8(0x000001, 0xF0);
        bus.Write8(0x000002, 0x12);
        bus.Write8(0x000003, 0x34);
        bus.Write8(0x000004, 0x00);
        bus.Write8(0x000005, 0xAB);
        bus.Write8(0x000006, 0xCD);
        bus.Write8(0x000007, 0xEF);

        var cpu = new Cpu(bus);

        cpu.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.SupervisorStackPointer, Is.EqualTo(0x00F01234));
            Assert.That(cpu.Registers.StackPointer, Is.EqualTo(0x00F01234));
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x00ABCDEF));
            Assert.That(cpu.Registers.StatusRegister, Is.EqualTo(0x2700));
            Assert.That(cpu.Registers.IsSupervisor, Is.True);
            Assert.That(cpu.Registers.InterruptPriorityMask, Is.EqualTo(7));
        });
    }

    [Test]
    public void ResetClearsGeneralRegisters()
    {
        var bus = new Bus(0x1000000);
        var cpu = new Cpu(bus);
        cpu.Registers.SetDataRegister(0, 0xCAFEBABE);
        cpu.Registers.SetAddressRegister(7, 0x11223344);
        cpu.Registers.StatusRegister = 0xA71F;
        cpu.Registers.ProgramCounter = 0x12345678;
        cpu.Registers.UserStackPointer = 0x87654321;
        cpu.Registers.SupervisorStackPointer = 0x99887766;

        cpu.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(0), Is.Zero);
            Assert.That(cpu.Registers.GetAddressRegister(7), Is.Zero);
            Assert.That(cpu.Registers.StatusRegister, Is.EqualTo(0x2700));
            Assert.That(cpu.Registers.ProgramCounter, Is.Zero);
            Assert.That(cpu.Registers.UserStackPointer, Is.Zero);
            Assert.That(cpu.Registers.SupervisorStackPointer, Is.Zero);
        });
    }
}
