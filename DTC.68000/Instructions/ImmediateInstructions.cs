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
/// Immediate-operation instruction decode and execution helpers.
/// </summary>
public static class ImmediateInstructions
{
    private const ushort ValidStatusRegisterMask = 0xA71F;
    private const ushort ConditionCodeRegisterMask = 0x001F;

    private static readonly Instruction InstrOriToCcr = new("ORI #<imm>,CCR", ExecuteOriToCcr);
    private static readonly Instruction InstrOriToSr = new("ORI #<imm>,SR", ExecuteOriToSr);
    private static readonly Instruction InstrAndiToCcr = new("ANDI #<imm>,CCR", ExecuteAndiToCcr);
    private static readonly Instruction InstrAndiToSr = new("ANDI #<imm>,SR", ExecuteAndiToSr);
    private static readonly Instruction InstrEoriToCcr = new("EORI #<imm>,CCR", ExecuteEoriToCcr);
    private static readonly Instruction InstrEoriToSr = new("EORI #<imm>,SR", ExecuteEoriToSr);
    private static readonly Instruction InstrCmpiByte = new("CMPI.B #<imm>,<ea>", ExecuteCompareImmediateByte);
    private static readonly Instruction InstrCmpiWord = new("CMPI.W #<imm>,<ea>", ExecuteCompareImmediateWord);
    private static readonly Instruction InstrCmpiLong = new("CMPI.L #<imm>,<ea>", ExecuteCompareImmediateLong);

    /// <summary>
    /// Decodes immediate opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode) =>
        TryDecodeCompareImmediate(opcode) ??
        opcode switch
        {
            0x003C => InstrOriToCcr,
            0x007C => InstrOriToSr,
            0x023C => InstrAndiToCcr,
            0x027C => InstrAndiToSr,
            0x0A3C => InstrEoriToCcr,
            0x0A7C => InstrEoriToSr,
            _ => null
        };

    private static Instruction TryDecodeCompareImmediate(ushort opcode)
    {
        // 0000 1100 ss mmm rrr = CMPI.<size> #<imm>,<ea>.
        if ((opcode & 0xFF00) != 0x0C00)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var size = (opcode >> 6) & 0x03;
        return size switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteWrite(destination) ? InstrCmpiByte : null,
            1 => EffectiveAddressWordAccess.SupportsWordWrite(destination) ? InstrCmpiWord : null,
            2 => EffectiveAddressLongAccess.SupportsLongWrite(destination) ? InstrCmpiLong : null,
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>ORI #&lt;imm&gt;,CCR</c>.
    /// </summary>
    private static void ExecuteOriToCcr(Cpu cpu, ushort opcode)
    {
        var immediate = (ushort)(cpu.FetchPcWord() & ConditionCodeRegisterMask);
        var currentCcr = (ushort)(cpu.Registers.StatusRegister & ConditionCodeRegisterMask);
        var resultCcr = (ushort)(currentCcr | immediate);
        cpu.Registers.StatusRegister = (ushort)((cpu.Registers.StatusRegister & ~ConditionCodeRegisterMask) | resultCcr);
    }

    /// <summary>
    /// Executes <c>ORI #&lt;imm&gt;,SR</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteOriToSr(Cpu cpu, ushort opcode)
    {
        if (!cpu.Registers.IsSupervisor)
        {
            cpu.EnterPrivilegeViolation();
            return;
        }

        var immediate = cpu.FetchPcWord();
        var result = (ushort)((cpu.Registers.StatusRegister | immediate) & ValidStatusRegisterMask);
        cpu.Registers.StatusRegister = result;
    }

    /// <summary>
    /// Executes <c>ANDI #&lt;imm&gt;,CCR</c>.
    /// </summary>
    private static void ExecuteAndiToCcr(Cpu cpu, ushort opcode)
    {
        var immediate = (ushort)(cpu.FetchPcWord() & ConditionCodeRegisterMask);
        var currentCcr = (ushort)(cpu.Registers.StatusRegister & ConditionCodeRegisterMask);
        var resultCcr = (ushort)(currentCcr & immediate);
        cpu.Registers.StatusRegister = (ushort)((cpu.Registers.StatusRegister & ~ConditionCodeRegisterMask) | resultCcr);
    }

    /// <summary>
    /// Executes <c>ANDI #&lt;imm&gt;,SR</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteAndiToSr(Cpu cpu, ushort opcode)
    {
        if (!cpu.Registers.IsSupervisor)
        {
            cpu.EnterPrivilegeViolation();
            return;
        }

        var immediate = cpu.FetchPcWord();
        var result = (ushort)((cpu.Registers.StatusRegister & immediate) & ValidStatusRegisterMask);
        cpu.Registers.StatusRegister = result;
    }

    /// <summary>
    /// Executes <c>EORI #&lt;imm&gt;,CCR</c>.
    /// </summary>
    private static void ExecuteEoriToCcr(Cpu cpu, ushort opcode)
    {
        var immediate = (ushort)(cpu.FetchPcWord() & ConditionCodeRegisterMask);
        var currentCcr = (ushort)(cpu.Registers.StatusRegister & ConditionCodeRegisterMask);
        var resultCcr = (ushort)(currentCcr ^ immediate);
        cpu.Registers.StatusRegister = (ushort)((cpu.Registers.StatusRegister & ~ConditionCodeRegisterMask) | resultCcr);
    }

    /// <summary>
    /// Executes <c>EORI #&lt;imm&gt;,SR</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteEoriToSr(Cpu cpu, ushort opcode)
    {
        if (!cpu.Registers.IsSupervisor)
        {
            cpu.EnterPrivilegeViolation();
            return;
        }

        var immediate = cpu.FetchPcWord();
        var result = (ushort)((cpu.Registers.StatusRegister ^ immediate) & ValidStatusRegisterMask);
        cpu.Registers.StatusRegister = result;
    }

    /// <summary>
    /// Executes <c>CMPI.B #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteCompareImmediateByte(Cpu cpu, ushort opcode)
    {
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = (byte)cpu.FetchPcWord();
        var destination = EffectiveAddressByteAccess.ReadByte(cpu, destinationEa);
        var result = (byte)(destination - source);
        FlagMath.ApplySubtractByte(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMPI.W #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteCompareImmediateWord(Cpu cpu, ushort opcode)
    {
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = cpu.FetchPcWord();
        var destination = EffectiveAddressWordAccess.ReadWord(cpu, destinationEa);
        var result = (ushort)(destination - source);
        FlagMath.ApplySubtractWord(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMPI.L #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteCompareImmediateLong(Cpu cpu, ushort opcode)
    {
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var sourceHi = cpu.FetchPcWord();
        var sourceLo = cpu.FetchPcWord();
        var source = ((uint)sourceHi << 16) | sourceLo;
        var destination = EffectiveAddressLongAccess.ReadLong(cpu, destinationEa);
        var result = destination - source;
        FlagMath.ApplySubtractLong(cpu.Registers, destination, source, result);
    }
}
