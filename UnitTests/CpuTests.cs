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
    public void Read16UsesBigEndianOrder()
    {
        var bus = new Bus(0x1000000);
        bus.Write8(0x000200, 0x12);
        bus.Write8(0x000201, 0x34);
        var cpu = new Cpu(bus);

        var value = cpu.Read16(0x000200);

        Assert.That(value, Is.EqualTo(0x1234));
    }

    [Test]
    public void Write16UsesBigEndianOrder()
    {
        var bus = new Bus(0x1000000);
        var cpu = new Cpu(bus);

        cpu.Write16(0x000200, 0xABCD);

        Assert.Multiple(() =>
        {
            Assert.That(bus.Read8(0x000200), Is.EqualTo(0xAB));
            Assert.That(bus.Read8(0x000201), Is.EqualTo(0xCD));
        });
    }

    [Test]
    public void Read32WrapsAcross24BitBusBoundary()
    {
        var bus = new Bus(0x1000000);
        bus.Write8(0xFFFFFE, 0x11);
        bus.Write8(0xFFFFFF, 0x22);
        bus.Write8(0x000000, 0x33);
        bus.Write8(0x000001, 0x44);
        var cpu = new Cpu(bus);

        var value = cpu.Read32(0xFFFFFE);

        Assert.That(value, Is.EqualTo(0x11223344));
    }

    [Test]
    public void Write32WrapsAcross24BitBusBoundary()
    {
        var bus = new Bus(0x1000000);
        var cpu = new Cpu(bus);

        cpu.Write32(0xFFFFFE, 0x55667788);

        Assert.Multiple(() =>
        {
            Assert.That(bus.Read8(0xFFFFFE), Is.EqualTo(0x55));
            Assert.That(bus.Read8(0xFFFFFF), Is.EqualTo(0x66));
            Assert.That(bus.Read8(0x000000), Is.EqualTo(0x77));
            Assert.That(bus.Read8(0x000001), Is.EqualTo(0x88));
        });
    }

    [Test]
    public void RefreshPrefetchQueueWithoutSeededPrefetchDoesNotAdvanceProgramCounter()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1234);
        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;

        cpu.RefreshPrefetchQueue();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000100));
            Assert.That(cpu.FetchPcWord(), Is.EqualTo(0x1234));
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000102));
        });
    }

    [Test]
    public void RefreshPrefetchQueueWithSeededPrefetchConsumesTwoFetchSlots()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1111);
        bus.Write16BigEndian(0x000102, 0x2222);
        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.SeedPrefetch(0xAAAA, 0xBBBB);

        cpu.RefreshPrefetchQueue();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000104));
            Assert.That(cpu.FetchPcWord(), Is.EqualTo(0x1111));
            Assert.That(cpu.FetchPcWord(), Is.EqualTo(0x2222));
        });
    }

    [Test]
    public void Read16WithOddAddressThrowsAddressError()
    {
        var cpu = new Cpu(new Bus(0x1000000));

        var ex = Assert.Throws<AddressErrorException>(() => cpu.Read16(0x000201));

        Assert.Multiple(() =>
        {
            Assert.That(ex.Address, Is.EqualTo(0x000201));
            Assert.That(ex.Size, Is.EqualTo(".w"));
        });
    }

    [Test]
    public void Write32WithOddAddressThrowsAddressError()
    {
        var cpu = new Cpu(new Bus(0x1000000));

        var ex = Assert.Throws<AddressErrorException>(() => cpu.Write32(0x000003, 0x12345678));

        Assert.Multiple(() =>
        {
            Assert.That(ex.Address, Is.EqualTo(0x000003));
            Assert.That(ex.Size, Is.EqualTo(".l"));
        });
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

    [Test]
    public void MoveByteAddressPostIncrementToDataIncrementsSourceRegister()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1019); // MOVE.B (A1)+,D0
        bus.Write8(0x000220, 0x7E);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x000220);
        cpu.Registers.SetDataRegister(0, 0x12345600);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x1234567E));
            Assert.That(cpu.Registers.GetAddressRegister(1), Is.EqualTo(0x000221));
        });
    }

    [Test]
    public void MoveByteDataToAddressPostIncrementWithA7IncrementsByTwo()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1EC0); // MOVE.B D0,(A7)+

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x00000055);
        cpu.Registers.SetAddressRegister(7, 0x000300);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(bus.Read8(0x000300), Is.EqualTo(0x55));
            Assert.That(cpu.Registers.GetAddressRegister(7), Is.EqualTo(0x000302));
        });
    }

    [Test]
    public void MoveByteAddressPreDecrementToDataDecrementsBeforeRead()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1021); // MOVE.B -(A1),D0
        bus.Write8(0x00021F, 0xC3);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x000220);
        cpu.Registers.SetDataRegister(0, 0x11223344);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetAddressRegister(1), Is.EqualTo(0x00021F));
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x112233C3));
        });
    }

    [Test]
    public void MoveByteDataToAddressDisplacementWritesUsingSignedOffset()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1340); // MOVE.B D0,(d16,A1)
        bus.Write16BigEndian(0x000102, 0xFFFE); // -2

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x0000005A);
        cpu.Registers.SetAddressRegister(1, 0x000220);

        cpu.Step();

        Assert.That(bus.Read8(0x00021E), Is.EqualTo(0x5A));
    }

    [Test]
    public void MoveByteAbsoluteShortToDataReadsFromAbsoluteAddress()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1038); // MOVE.B (xxx).w,D0
        bus.Write16BigEndian(0x000102, 0x0200);
        bus.Write8(0x000200, 0x9F);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0xAABBCC00);

        cpu.Step();

        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0xAABBCC9F));
    }

    [Test]
    public void MoveByteDataToAbsoluteLongWritesToAbsoluteAddress()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x13C0); // MOVE.B D0,(xxx).l
        bus.Write16BigEndian(0x000102, 0x0004);
        bus.Write16BigEndian(0x000104, 0x1234);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x123456E1);

        cpu.Step();

        Assert.That(bus.Read8(0x00041234), Is.EqualTo(0xE1));
    }

    [Test]
    public void MoveByteAddressIndexedToDataUsesIndexAndDisplacement()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1031); // MOVE.B (d8,A1,Xn),D0
        bus.Write16BigEndian(0x000102, 0x2004); // D2.w +4
        bus.Write8(0x000214, 0x6B);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x000200);
        cpu.Registers.SetDataRegister(2, 0x00000010);
        cpu.Registers.SetDataRegister(0, 0xABCD1200);

        cpu.Step();

        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0xABCD126B));
    }

    [Test]
    public void MoveByteDataToAddressIndexedUsesIndexAndDisplacement()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1380); // MOVE.B D0,(d8,A1,Xn)
        bus.Write16BigEndian(0x000102, 0x2004); // D2.w +4

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x000200);
        cpu.Registers.SetDataRegister(2, 0x00000010);
        cpu.Registers.SetDataRegister(0, 0x556677A2);

        cpu.Step();

        Assert.That(bus.Read8(0x000214), Is.EqualTo(0xA2));
    }

    [Test]
    public void MoveBytePcDisplacementToDataUsesPcRelativeBase()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x103A); // MOVE.B (d16,PC),D0
        bus.Write16BigEndian(0x000102, 0x0006);
        bus.Write8(0x000108, 0x4C);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x12345600);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000104));
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x1234564C));
        });
    }

    [Test]
    public void MoveBytePcIndexedToDataUsesPcRelativeBaseAndIndex()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x103B); // MOVE.B (d8,PC,Xn),D0
        bus.Write16BigEndian(0x000102, 0x2004); // D2.w +4
        bus.Write8(0x000116, 0x92);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(2, 0x00000010);
        cpu.Registers.SetDataRegister(0, 0x89ABC000);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000104));
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x89ABC092));
        });
    }

    [Test]
    public void MoveByteImmediateToDataUsesLowByteOfImmediateWord()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x103C); // MOVE.B #<imm8>,D0
        bus.Write16BigEndian(0x000102, 0x00E7);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0xA1B2C300);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000104));
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0xA1B2C3E7));
        });
    }

    [Test]
    public void MoveByteAbsoluteShortMasksTo24BitBusAddress()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1038); // MOVE.B (xxx).w,D0
        bus.Write16BigEndian(0x000102, 0xFFFC); // Sign-extends to 0xFFFFFFFC.
        bus.Write8(0xFFFFFC, 0x66);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x01020300);

        cpu.Step();

        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x01020366));
    }

    [Test]
    public void MoveByteAddressIndirectMasksRegisterAddressTo24BitBusAddress()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x1011); // MOVE.B (A1),D0
        bus.Write8(0x000220, 0x5D);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x01000220);
        cpu.Registers.SetDataRegister(0, 0x11223300);

        cpu.Step();

        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x1122335D));
    }

    [Test]
    public void MoveWordDataToDataCopiesLowWordAndSetsFlags()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x3200); // MOVE.W D0,D1

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x00008001);
        cpu.Registers.SetDataRegister(1, 0xAABBCCDD);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.CarryFlag = true;
        cpu.Registers.OverflowFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(1), Is.EqualTo(0xAABB8001));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }

    [Test]
    public void MoveWordAddressRegisterToAddressPostIncrementWritesWordAndIncrementsDestination()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x34C9); // MOVE.W A1,(A2)+

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x12345678);
        cpu.Registers.SetAddressRegister(2, 0x000220);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(bus.Read16BigEndian(0x000220), Is.EqualTo(0x5678));
            Assert.That(cpu.Registers.GetAddressRegister(2), Is.EqualTo(0x000222));
        });
    }

    [Test]
    public void MoveWordImmediateToDataUsesFullImmediateWord()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x303C); // MOVE.W #<imm16>,D0
        bus.Write16BigEndian(0x000102, 0xE7A5);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x11223344);

        cpu.Step();

        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x1122E7A5));
    }

    [Test]
    public void MoveWordPcDisplacementToDataUsesPcRelativeBase()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x303A); // MOVE.W (d16,PC),D0
        bus.Write16BigEndian(0x000102, 0x0006);
        bus.Write16BigEndian(0x000108, 0x4ACE);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x12340000);

        cpu.Step();

        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x12344ACE));
    }

    [Test]
    public void MoveLongDataToDataCopiesFullLongAndSetsFlags()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x2200); // MOVE.L D0,D1

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x8000A5A5);
        cpu.Registers.SetDataRegister(1, 0x11223344);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.CarryFlag = true;
        cpu.Registers.OverflowFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(1), Is.EqualTo(0x8000A5A5));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }

    [Test]
    public void MoveLongAddressRegisterToAddressPostIncrementWritesLongAndIncrementsDestination()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x24C9); // MOVE.L A1,(A2)+

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x12345678);
        cpu.Registers.SetAddressRegister(2, 0x000220);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(bus.Read32BigEndian(0x000220), Is.EqualTo(0x12345678));
            Assert.That(cpu.Registers.GetAddressRegister(2), Is.EqualTo(0x000224));
        });
    }

    [Test]
    public void MoveLongImmediateToDataUsesFullImmediateLong()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x203C); // MOVE.L #<imm32>,D0
        bus.Write16BigEndian(0x000102, 0xE7A5);
        bus.Write16BigEndian(0x000104, 0x1234);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x00000000);

        cpu.Step();

        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0xE7A51234));
    }

    [Test]
    public void MoveLongPcDisplacementToDataUsesPcRelativeBase()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x203A); // MOVE.L (d16,PC),D0
        bus.Write16BigEndian(0x000102, 0x0006);
        bus.Write16BigEndian(0x000108, 0xA1B2);
        bus.Write16BigEndian(0x00010A, 0xC3D4);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x12340000);

        cpu.Step();

        Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0xA1B2C3D4));
    }

    [Test]
    public void MoveQuickSignExtendsImmediateAndSetsFlags()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x78A3); // MOVEQ #-93,D4

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(4, 0x12345678);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.ZeroFlag = true;
        cpu.Registers.CarryFlag = true;
        cpu.Registers.OverflowFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(4), Is.EqualTo(0xFFFFFFA3));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }

    [Test]
    public void MoveQuickZeroImmediateSetsZeroFlag()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x7000); // MOVEQ #0,D0

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0xFFFFFFFF);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.NegativeFlag = true;
        cpu.Registers.CarryFlag = true;
        cpu.Registers.OverflowFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(0), Is.Zero);
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.False);
            Assert.That(cpu.Registers.ZeroFlag, Is.True);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }

    [Test]
    public void AddQuickWordDataRegisterSetsCarryExtendAndZero()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x5240); // ADDQ.W #1,D0

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x1234FFFF);
        cpu.Registers.ExtendFlag = false;
        cpu.Registers.NegativeFlag = true;
        cpu.Registers.ZeroFlag = false;
        cpu.Registers.OverflowFlag = true;
        cpu.Registers.CarryFlag = false;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x12340000));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.False);
            Assert.That(cpu.Registers.ZeroFlag, Is.True);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.True);
        });
    }

    [Test]
    public void AddQuickByteWithEncodedZeroUsesImmediateEight()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x5000); // ADDQ.B #8,D0

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0xAABBCC78);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.NegativeFlag = false;
        cpu.Registers.ZeroFlag = true;
        cpu.Registers.OverflowFlag = false;
        cpu.Registers.CarryFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0xAABBCC80));
            Assert.That(cpu.Registers.ExtendFlag, Is.False);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
            Assert.That(cpu.Registers.OverflowFlag, Is.True);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }

    [Test]
    public void SubQuickByteDataRegisterSetsBorrowAndExtend()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x5300); // SUBQ.B #1,D0

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x11223300);
        cpu.Registers.ExtendFlag = false;
        cpu.Registers.NegativeFlag = false;
        cpu.Registers.ZeroFlag = true;
        cpu.Registers.OverflowFlag = true;
        cpu.Registers.CarryFlag = false;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(0), Is.EqualTo(0x112233FF));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.True);
        });
    }

    [Test]
    public void AddQuickWordAddressRegisterUsesLongArithmeticAndPreservesFlags()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x5248); // ADDQ.W #1,A0

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(0, 0x0000FFFF);
        cpu.Registers.ExtendFlag = true;
        cpu.Registers.NegativeFlag = true;
        cpu.Registers.ZeroFlag = false;
        cpu.Registers.OverflowFlag = true;
        cpu.Registers.CarryFlag = false;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetAddressRegister(0), Is.EqualTo(0x00010000));
            Assert.That(cpu.Registers.ExtendFlag, Is.True);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
            Assert.That(cpu.Registers.OverflowFlag, Is.True);
            Assert.That(cpu.Registers.CarryFlag, Is.False);
        });
    }

    [Test]
    public void SubQuickLongAddressRegisterPreservesFlags()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x5389); // SUBQ.L #1,A1

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetAddressRegister(1, 0x00001000);
        cpu.Registers.ExtendFlag = false;
        cpu.Registers.NegativeFlag = true;
        cpu.Registers.ZeroFlag = true;
        cpu.Registers.OverflowFlag = false;
        cpu.Registers.CarryFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetAddressRegister(1), Is.EqualTo(0x00000FFF));
            Assert.That(cpu.Registers.ExtendFlag, Is.False);
            Assert.That(cpu.Registers.NegativeFlag, Is.True);
            Assert.That(cpu.Registers.ZeroFlag, Is.True);
            Assert.That(cpu.Registers.OverflowFlag, Is.False);
            Assert.That(cpu.Registers.CarryFlag, Is.True);
        });
    }

    [Test]
    public void AbcdWithNonBcdOperandsUsesCurrentCoreSemantics()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0xC300); // ABCD D0,D1

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.SetDataRegister(0, 0x0000008F);
        cpu.Registers.SetDataRegister(1, 0x00000005);
        cpu.Registers.ExtendFlag = false;
        cpu.Registers.CarryFlag = true;
        cpu.Registers.ZeroFlag = true;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.GetDataRegister(1), Is.EqualTo(0x0000009A));
            Assert.That(cpu.Registers.CarryFlag, Is.False);
            Assert.That(cpu.Registers.ExtendFlag, Is.False);
            Assert.That(cpu.Registers.ZeroFlag, Is.False);
        });
    }

    [Test]
    public void SubQuickByteToAddressRegisterDirectEntersIllegalInstructionVector()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x5108); // SUBQ.B #8,A0 (illegal)
        bus.Write16BigEndian(0x000010, 0x0000);
        bus.Write16BigEndian(0x000012, 0x0200);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.StatusRegister = 0x2000;
        cpu.Registers.SupervisorStackPointer = 0x001000;
        cpu.Registers.StackPointer = 0x001000;

        Assert.DoesNotThrow(() => cpu.Step());
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000200));
            Assert.That(cpu.Registers.StackPointer, Is.EqualTo(0x000FFA));
            Assert.That(bus.Read16BigEndian(0x000FFA), Is.EqualTo(0x2000));
            Assert.That(bus.Read16BigEndian(0x000FFC), Is.EqualTo(0x0000));
            Assert.That(bus.Read16BigEndian(0x000FFE), Is.EqualTo(0x0100));
        });
    }

    [Test]
    public void TraceFlagEntersTraceVectorAfterInstructionCompletes()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x4E71); // NOP.
        bus.Write16BigEndian(0x000024, 0x0000);
        bus.Write16BigEndian(0x000026, 0x0200);

        var cpu = new Cpu(bus);
        cpu.EnableTraceExceptions = true;
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.StatusRegister = 0xA000;
        cpu.Registers.SupervisorStackPointer = 0x001000;
        cpu.Registers.StackPointer = 0x001000;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000200));
            Assert.That(cpu.Registers.StackPointer, Is.EqualTo(0x000FFA));
            Assert.That(bus.Read16BigEndian(0x000FFA), Is.EqualTo(0xA000));
            Assert.That(bus.Read16BigEndian(0x000FFC), Is.EqualTo(0x0000));
            Assert.That(bus.Read16BigEndian(0x000FFE), Is.EqualTo(0x0102));
            Assert.That(cpu.Registers.TraceFlag, Is.False);
            Assert.That(cpu.Registers.IsSupervisor, Is.True);
        });
    }

    [Test]
    public void PendingInterruptUsesAutovectorAndUpdatesInterruptMask()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x4E71); // NOP.
        bus.Write16BigEndian(0x000070, 0x0000); // Autovector level 4.
        bus.Write16BigEndian(0x000072, 0x0300);

        var cpu = new Cpu(bus);
        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.StatusRegister = 0x2000;
        cpu.Registers.SupervisorStackPointer = 0x001000;
        cpu.Registers.StackPointer = 0x001000;
        cpu.RequestInterrupt(4);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000300));
            Assert.That(cpu.Registers.StackPointer, Is.EqualTo(0x000FFA));
            Assert.That(bus.Read16BigEndian(0x000FFA), Is.EqualTo(0x2000));
            Assert.That(bus.Read16BigEndian(0x000FFC), Is.EqualTo(0x0000));
            Assert.That(bus.Read16BigEndian(0x000FFE), Is.EqualTo(0x0100));
            Assert.That(cpu.Registers.InterruptPriorityMask, Is.EqualTo(4));
        });
    }

    [Test]
    public void InterruptAcknowledgeCanForceSpuriousVector()
    {
        var bus = new Bus(0x1000000);
        bus.Write16BigEndian(0x000100, 0x4E71); // NOP.
        bus.Write16BigEndian(0x000060, 0x0000); // Spurious vector.
        bus.Write16BigEndian(0x000062, 0x0340);

        var cpu = new Cpu(bus)
        {
            InterruptAcknowledge = _ => InterruptAcknowledgeResult.Spurious()
        };

        cpu.Registers.ProgramCounter = 0x000100;
        cpu.Registers.StatusRegister = 0x2000;
        cpu.Registers.SupervisorStackPointer = 0x001000;
        cpu.Registers.StackPointer = 0x001000;
        cpu.RequestInterrupt(2);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.ProgramCounter, Is.EqualTo(0x000340));
            Assert.That(cpu.Registers.StackPointer, Is.EqualTo(0x000FFA));
            Assert.That(bus.Read16BigEndian(0x000FFA), Is.EqualTo(0x2000));
            Assert.That(bus.Read16BigEndian(0x000FFC), Is.EqualTo(0x0000));
            Assert.That(bus.Read16BigEndian(0x000FFE), Is.EqualTo(0x0100));
            Assert.That(cpu.Registers.InterruptPriorityMask, Is.EqualTo(2));
        });
    }
}
