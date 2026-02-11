// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.M68000.Addressing;

namespace DTC.M68000.Instructions;

/// <summary>
/// MULU/MULS/DIVU/DIVS instruction decode and execution helpers.
/// </summary>
public static class MultiplyDivideInstructions
{
    private const ushort TraceFlagMask = 0x8000;
    private const ushort SupervisorFlagMask = 0x2000;
    private const uint DivideByZeroVectorAddress = 0x000014;

    private static readonly Instruction InstrMuluWord = new("MULU.W <ea>,Dn", ExecuteMuluWord);
    private static readonly Instruction InstrMulsWord = new("MULS.W <ea>,Dn", ExecuteMulsWord);
    private static readonly Instruction InstrDivuWord = new("DIVU.W <ea>,Dn", ExecuteDivuWord);
    private static readonly Instruction InstrDivsWord = new("DIVS.W <ea>,Dn", ExecuteDivsWord);

    /// <summary>
    /// Decodes MULU/MULS opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeMultiply(ushort opcode)
    {
        // 1100 ddd ooo mmm rrr = MULx.W <ea>,Dn where ooo is 011 (MULU) or 111 (MULS).
        if ((opcode & 0xF000) != 0xC000)
            return null;

        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        if (!EffectiveAddressWordAccess.SupportsWordRead(source))
            return null;

        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            3 => InstrMuluWord,
            7 => InstrMulsWord,
            _ => null
        };
    }

    /// <summary>
    /// Decodes DIVU/DIVS opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeDivide(ushort opcode)
    {
        // 1000 ddd ooo mmm rrr = DIVx.W <ea>,Dn where ooo is 011 (DIVU) or 111 (DIVS).
        if ((opcode & 0xF000) != 0x8000)
            return null;

        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        if (!EffectiveAddressWordAccess.SupportsWordRead(source))
            return null;

        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            3 => InstrDivuWord,
            7 => InstrDivsWord,
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>MULU.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteMuluWord(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = EffectiveAddressWordAccess.ReadWord(cpu, sourceEa);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destination = (ushort)cpu.Registers.GetDataRegister(destinationRegisterIndex);
        var result = (uint)(destination * source);

        cpu.Registers.SetDataRegister(destinationRegisterIndex, result);
        FlagMath.ApplyLogicalLong(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>MULS.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteMulsWord(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = (short)EffectiveAddressWordAccess.ReadWord(cpu, sourceEa);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destination = (short)cpu.Registers.GetDataRegister(destinationRegisterIndex);
        var result = (uint)(source * destination);

        cpu.Registers.SetDataRegister(destinationRegisterIndex, result);
        FlagMath.ApplyLogicalLong(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>DIVU.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteDivuWord(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var divisor = EffectiveAddressWordAccess.ReadWord(cpu, sourceEa);
        if (divisor == 0)
        {
            EnterDivideByZero(cpu);
            return;
        }

        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var dividend = cpu.Registers.GetDataRegister(destinationRegisterIndex);
        var quotient = dividend / divisor;
        if (quotient > 0xFFFF)
        {
            cpu.Registers.NegativeFlag = true;
            cpu.Registers.ZeroFlag = false;
            cpu.Registers.OverflowFlag = true;
            cpu.Registers.CarryFlag = false;
            return;
        }

        var remainder = dividend % divisor;
        var result = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
        cpu.Registers.SetDataRegister(destinationRegisterIndex, result);
        cpu.Registers.NegativeFlag = (quotient & 0x8000) != 0;
        cpu.Registers.ZeroFlag = (quotient & 0xFFFF) == 0;
        cpu.Registers.OverflowFlag = false;
        cpu.Registers.CarryFlag = false;
    }

    /// <summary>
    /// Executes <c>DIVS.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteDivsWord(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var divisor = (short)EffectiveAddressWordAccess.ReadWord(cpu, sourceEa);
        if (divisor == 0)
        {
            EnterDivideByZero(cpu);
            return;
        }

        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var dividend = unchecked((int)cpu.Registers.GetDataRegister(destinationRegisterIndex));
        var quotient = dividend / divisor;
        if (quotient is < short.MinValue or > short.MaxValue)
        {
            cpu.Registers.NegativeFlag = true;
            cpu.Registers.ZeroFlag = false;
            cpu.Registers.OverflowFlag = true;
            cpu.Registers.CarryFlag = false;
            return;
        }

        var remainder = dividend % divisor;
        var result = ((uint)(ushort)remainder << 16) | (ushort)quotient;
        cpu.Registers.SetDataRegister(destinationRegisterIndex, result);
        cpu.Registers.NegativeFlag = (quotient & 0x8000) != 0;
        cpu.Registers.ZeroFlag = quotient == 0;
        cpu.Registers.OverflowFlag = false;
        cpu.Registers.CarryFlag = false;
    }

    private static void EnterDivideByZero(Cpu cpu)
    {
        var oldStatus = cpu.Registers.StatusRegister;
        var oldPc = cpu.GetPcRelativeBaseAddress();

        cpu.Registers.IsSupervisor = true;
        cpu.Push32(oldPc);
        cpu.Push16(oldStatus);
        cpu.Registers.StatusRegister = (ushort)((oldStatus & ~TraceFlagMask) | SupervisorFlagMask);
        cpu.Registers.ProgramCounter = cpu.Read32(DivideByZeroVectorAddress);
        cpu.RefreshPrefetchQueue();
    }
}
