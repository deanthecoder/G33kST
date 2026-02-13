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
using DTC.M68000.Decoding;

namespace UnitTests;

[TestFixture]
public sealed class InstructionTraceTextFormatterTests : TestsBase
{
    [Test]
    public void FormatExpandsGenericDataRegistersToResolvedValues()
    {
        var cpu = new Cpu(new Bus(0x1000000));
        cpu.Registers.SetDataRegister(2, 0x11223344);
        cpu.Registers.SetDataRegister(5, 0x99AABBCC);
        var instruction = new Instruction("EXG Dn,Dm", (_, _) => { });
        var opcode = (ushort)0xC545; // high register = D2, low register = D5.

        var text = InstructionTraceTextFormatter.Format(opcode, instruction, cpu, 0x000100);

        Assert.That(text, Is.EqualTo("EXG D2=11223344,D5=99AABBCC"));
    }

    [Test]
    public void FormatUsesLowAddressRegisterForLinkMnemonic()
    {
        var cpu = new Cpu(new Bus(0x1000000));
        cpu.Registers.SetAddressRegister(5, 0x00123456);
        var instruction = new Instruction("LINK An,#0002", (_, _) => { });
        const ushort opcode = 0x4E55; // low register = A5.

        var text = InstructionTraceTextFormatter.Format(opcode, instruction, cpu, 0x000100);

        Assert.That(text, Is.EqualTo("LINK A5=00123456,#0002"));
    }

    [Test]
    public void FormatLeavesMnemonicsWithoutRegisterTokensUnchanged()
    {
        var cpu = new Cpu(new Bus(0x1000000));
        var instruction = new Instruction("NOP", (_, _) => { });

        var text = InstructionTraceTextFormatter.Format(0x4E71, instruction, cpu, 0x000100);

        Assert.That(text, Is.EqualTo("NOP"));
    }

    [Test]
    public void FormatRespectsWordBoundariesWhenExpandingRegisters()
    {
        var cpu = new Cpu(new Bus(0x1000000));
        cpu.Registers.SetDataRegister(0, 0xAABBCCDD);
        var instruction = new Instruction("ADDI #0001,D0", (_, _) => { });

        var text = InstructionTraceTextFormatter.Format(0x0600, instruction, cpu, 0x000100);

        Assert.That(text, Is.EqualTo("ADDI #0001,D0=AABBCCDD"));
    }
}
