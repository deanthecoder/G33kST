// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.M68000;

namespace UnitTests;

[TestFixture]
public sealed class RegistersTests
{
    [Test]
    public void InitialStateIsZero()
    {
        var registers = new Registers();

        Assert.Multiple(() =>
        {
            Assert.That(registers.StatusRegister, Is.Zero);
            Assert.That(registers.ProgramCounter, Is.Zero);
            Assert.That(registers.UserStackPointer, Is.Zero);
            Assert.That(registers.SupervisorStackPointer, Is.Zero);
            Assert.That(registers.FlagsAsString(), Is.EqualTo("----------"));

            for (var i = 0; i < 8; i++)
            {
                Assert.That(registers.GetDataRegister(i), Is.Zero);
                Assert.That(registers.GetAddressRegister(i), Is.Zero);
            }
        });
    }

    [Test]
    public void ResetClearsState()
    {
        var registers = new Registers
        {
            ProgramCounter = 0x12345678,
            UserStackPointer = 0x01020304,
            SupervisorStackPointer = 0xA1A2A3A4,
            InterruptPriorityMask = 7,
            CarryFlag = true,
            OverflowFlag = true,
            ZeroFlag = true,
            NegativeFlag = true,
            ExtendFlag = true,
            TraceFlag = true
        };

        for (var i = 0; i < 8; i++)
        {
            registers.SetDataRegister(i, (uint)(0x11111111 * (i + 1)));
            registers.SetAddressRegister(i, (uint)(0x01010101 * (i + 1)));
        }

        registers.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(registers.StatusRegister, Is.Zero);
            Assert.That(registers.ProgramCounter, Is.Zero);
            Assert.That(registers.UserStackPointer, Is.Zero);
            Assert.That(registers.SupervisorStackPointer, Is.Zero);
            Assert.That(registers.FlagsAsString(), Is.EqualTo("----------"));

            for (var i = 0; i < 8; i++)
            {
                Assert.That(registers.GetDataRegister(i), Is.Zero);
                Assert.That(registers.GetAddressRegister(i), Is.Zero);
            }
        });
    }

    [Test]
    public void FlagsSetAndClearCorrectly()
    {
        var registers = new Registers
        {
            CarryFlag = true,
            OverflowFlag = true,
            ZeroFlag = true,
            NegativeFlag = true,
            ExtendFlag = true,
            TraceFlag = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(registers.CarryFlag, Is.True);
            Assert.That(registers.OverflowFlag, Is.True);
            Assert.That(registers.ZeroFlag, Is.True);
            Assert.That(registers.NegativeFlag, Is.True);
            Assert.That(registers.ExtendFlag, Is.True);
            Assert.That(registers.TraceFlag, Is.True);
        });

        registers.CarryFlag = false;
        registers.OverflowFlag = false;
        registers.ZeroFlag = false;
        registers.NegativeFlag = false;
        registers.ExtendFlag = false;
        registers.TraceFlag = false;

        Assert.Multiple(() =>
        {
            Assert.That(registers.CarryFlag, Is.False);
            Assert.That(registers.OverflowFlag, Is.False);
            Assert.That(registers.ZeroFlag, Is.False);
            Assert.That(registers.NegativeFlag, Is.False);
            Assert.That(registers.ExtendFlag, Is.False);
            Assert.That(registers.TraceFlag, Is.False);
        });
    }

    [Test]
    public void SupervisorModeSwapsStackPointers()
    {
        var registers = new Registers();

        registers.SetAddressRegister(7, 0x11111111);
        registers.UserStackPointer = 0x22222222;
        registers.SupervisorStackPointer = 0x33333333;

        registers.SetSupervisorMode(true);
        Assert.Multiple(() =>
        {
            Assert.That(registers.IsSupervisor, Is.True);
            Assert.That(registers.StackPointer, Is.EqualTo(0x33333333));
            Assert.That(registers.UserStackPointer, Is.EqualTo(0x11111111));
        });

        registers.SetSupervisorMode(false);
        Assert.Multiple(() =>
        {
            Assert.That(registers.IsSupervisor, Is.False);
            Assert.That(registers.StackPointer, Is.EqualTo(0x11111111));
            Assert.That(registers.SupervisorStackPointer, Is.EqualTo(0x33333333));
        });
    }

    [Test]
    public void StatusRegisterUpdatesSupervisorMode()
    {
        var registers = new Registers();

        registers.SetAddressRegister(7, 0x0000ABCD);
        registers.SupervisorStackPointer = 0x00001234;

        registers.StatusRegister = 0x2000;

        Assert.Multiple(() =>
        {
            Assert.That(registers.IsSupervisor, Is.True);
            Assert.That(registers.StackPointer, Is.EqualTo(0x00001234));
            Assert.That(registers.UserStackPointer, Is.EqualTo(0x0000ABCD));
        });
    }

    [Test]
    public void InterruptPriorityMaskIsClamped()
    {
        var registers = new Registers
        {
            InterruptPriorityMask = 0x0F
        };

        Assert.That(registers.InterruptPriorityMask, Is.EqualTo(7));
    }

    [Test]
    public void FlagsAsStringReflectsStatusBits()
    {
        var registers = new Registers
        {
            TraceFlag = true,
            IsSupervisor = true,
            InterruptPriorityMask = 5,
            ExtendFlag = true,
            NegativeFlag = true,
            OverflowFlag = true
        };

        Assert.That(registers.FlagsAsString(), Is.EqualTo("TS2-0XN-V-"));
    }

    [Test]
    public void DataRegistersGetAndSet()
    {
        var registers = new Registers();

        registers.SetDataRegister(0, 0x12345678);
        registers.SetDataRegister(7, 0x89ABCDEF);

        Assert.Multiple(() =>
        {
            Assert.That(registers.GetDataRegister(0), Is.EqualTo(0x12345678));
            Assert.That(registers.GetDataRegister(7), Is.EqualTo(0x89ABCDEF));
        });
    }

    [Test]
    public void StackPointerMapsToA7()
    {
        var registers = new Registers();

        registers.SetAddressRegister(7, 0x11223344);
        registers.StackPointer = 0x55667788;

        Assert.Multiple(() =>
        {
            Assert.That(registers.StackPointer, Is.EqualTo(0x55667788));
            Assert.That(registers.GetAddressRegister(7), Is.EqualTo(0x55667788));
        });
    }

    [Test]
    public void SettingA7UpdatesActiveStackPointerMirror()
    {
        var registers = new Registers();

        registers.SetAddressRegister(7, 0x11111111);
        registers.SupervisorStackPointer = 0x22222222;
        registers.SetSupervisorMode(true);
        registers.SetAddressRegister(7, 0x33333333);

        Assert.Multiple(() =>
        {
            Assert.That(registers.UserStackPointer, Is.EqualTo(0x11111111));
            Assert.That(registers.SupervisorStackPointer, Is.EqualTo(0x33333333));
            Assert.That(registers.StackPointer, Is.EqualTo(0x33333333));
        });
    }

    [TestCase(-1)]
    [TestCase(8)]
    public void DataRegisterIndexOutsideRangeThrows(int index)
    {
        var registers = new Registers();

        Assert.Multiple(() =>
        {
            Assert.That(() => registers.GetDataRegister(index), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => registers.SetDataRegister(index, 0x12345678), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [TestCase(-1)]
    [TestCase(8)]
    public void AddressRegisterIndexOutsideRangeThrows(int index)
    {
        var registers = new Registers();

        Assert.Multiple(() =>
        {
            Assert.That(() => registers.GetAddressRegister(index), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => registers.SetAddressRegister(index, 0x12345678), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void ToStringIncludesCoreState()
    {
        var registers = new Registers
        {
            ProgramCounter = 0x00123456,
            SupervisorStackPointer = 0x00112233
        };

        registers.StatusRegister = 0x2000;
        registers.UserStackPointer = 0xABCDEF00;
        registers.SetDataRegister(0, 0x11112222);
        registers.SetAddressRegister(7, 0xDEADBEEF);

        var text = registers.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("PC:00123456"));
            Assert.That(text, Does.Contain("SR:2000"));
            Assert.That(text, Does.Contain("D0:11112222"));
            Assert.That(text, Does.Contain("A7:DEADBEEF"));
            Assert.That(text, Does.Contain("USP:ABCDEF00"));
            Assert.That(text, Does.Contain("SSP:DEADBEEF"));
            Assert.That(text, Does.Contain("F:-S--------"));
        });
    }
}
