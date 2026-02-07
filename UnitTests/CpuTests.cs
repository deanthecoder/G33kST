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
            Assert.That(cpu.CyclesSinceCpuStart, Is.Zero);
        });
    }

    [Test]
    public void InternalWaitAddsCycles()
    {
        var cpu = new Cpu(new Bus(0x1000000));

        cpu.InternalWait(4);
        cpu.InternalWait(12);

        Assert.That(cpu.CyclesSinceCpuStart, Is.EqualTo(16));
    }

    [Test]
    public void InternalWaitWithZeroDoesNotChangeCycles()
    {
        var cpu = new Cpu(new Bus(0x1000000));
        cpu.InternalWait(7);

        cpu.InternalWait(0);

        Assert.That(cpu.CyclesSinceCpuStart, Is.EqualTo(7));
    }

    [Test]
    public void MoveByteDataToDataCopiesLowByteAndSetsFlags()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1200); // MOVE.B D0,D1

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x000000A5);
        cpu.Registers.SetDataRegister(1, 0x12345678);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.CarryFlag = true;
        cpu.Registers.OverflowFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000102));
            Assert.That(cpu.Registers.GetDataRegister(1), Is.EqualTo(0x123456A5));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }

    [Test]
    public void MoveByteDataToAddressWritesByteToMemoryAndSetsFlags()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1280); // MOVE.B D0,(A1)

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x123456A5);
        cpu.Registers.SetAddressRegister(1, 0x000200);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.CarryFlag = true;
        cpu.Registers.OverflowFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000102));
            Assert.That(bus.Read8(0x000200), Is.EqualTo(0xA5));
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x123456A5));
            Assert.That(cpu.Registers.GetAddressRegister(1), Is.EqualTo(0x000200));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }

    [Test]
    public void MoveByteAddressToDataCopiesByteAndSetsFlags()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1011); // MOVE.B (A1),D0
        bus.Write8(0x000220, 0x00);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x000220);
        cpu.Registers.SetDataRegister(0, 0x12345678);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.NegativeFlag = true;
        cpu.Registers.CarryFlag = true;
        cpu.Registers.OverflowFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000102));
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x12345600));
            Assert.That(cpu.Registers.GetAddressRegister(1), Is.EqualTo(0x000220));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.False);
            Assert.That(cpu.Registers.ZeroFlag, Is.True);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }
}
