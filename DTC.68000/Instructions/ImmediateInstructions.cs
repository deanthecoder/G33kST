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
    private static readonly Instruction InstrAddiByte = new("ADDI.B #<imm>,<ea>", ExecuteAddImmediateByte);
    private static readonly Instruction InstrAddiWord = new("ADDI.W #<imm>,<ea>", ExecuteAddImmediateWord);
    private static readonly Instruction InstrAddiLong = new("ADDI.L #<imm>,<ea>", ExecuteAddImmediateLong);
    private static readonly Instruction InstrSubiByte = new("SUBI.B #<imm>,<ea>", ExecuteSubtractImmediateByte);
    private static readonly Instruction InstrSubiWord = new("SUBI.W #<imm>,<ea>", ExecuteSubtractImmediateWord);
    private static readonly Instruction InstrSubiLong = new("SUBI.L #<imm>,<ea>", ExecuteSubtractImmediateLong);
    private static readonly Instruction InstrCmpiByte = new("CMPI.B #<imm>,<ea>", ExecuteCompareImmediateByte);
    private static readonly Instruction InstrCmpiWord = new("CMPI.W #<imm>,<ea>", ExecuteCompareImmediateWord);
    private static readonly Instruction InstrCmpiLong = new("CMPI.L #<imm>,<ea>", ExecuteCompareImmediateLong);

    /// <summary>
    /// Decodes immediate opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode) =>
        TryDecodeAddOrSubtractImmediate(opcode)
        ?? TryDecodeCompareImmediate(opcode)
        ??
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

    private static Instruction TryDecodeAddOrSubtractImmediate(ushort opcode)
    {
        var baseOpcode = opcode & 0xFF00;
        if (baseOpcode != 0x0400 && baseOpcode != 0x0600)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var size = (opcode >> 6) & 0x03;
        if (!SupportsReadWrite(destination, size))
            return null;

        return (baseOpcode, size) switch
        {
            (0x0600, 0) => InstrAddiByte,
            (0x0600, 1) => InstrAddiWord,
            (0x0600, 2) => InstrAddiLong,
            (0x0400, 0) => InstrSubiByte,
            (0x0400, 1) => InstrSubiWord,
            (0x0400, 2) => InstrSubiLong,
            _ => null
        };
    }

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

    private static bool SupportsReadWrite(EffectiveAddress ea, int sizeCode) =>
        sizeCode switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteRead(ea) && EffectiveAddressByteAccess.SupportsByteWrite(ea),
            1 => EffectiveAddressWordAccess.SupportsWordRead(ea) && EffectiveAddressWordAccess.SupportsWordWrite(ea),
            2 => EffectiveAddressLongAccess.SupportsLongRead(ea) && EffectiveAddressLongAccess.SupportsLongWrite(ea),
            _ => false
        };

    /// <summary>
    /// Executes <c>ORI #&lt;imm&gt;,CCR</c>.
    /// </summary>
    private static void ExecuteOriToCcr(Cpu cpu, ushort opcode)
    {
        var immediate = (ushort)(cpu.FetchPcWord() & ConditionCodeRegisterMask);
        var currentCcr = (ushort)(cpu.Registers.StatusRegister & ConditionCodeRegisterMask);
        var resultCcr = (ushort)(currentCcr | immediate);
        cpu.Registers.StatusRegister = (ushort)((cpu.Registers.StatusRegister & ~ConditionCodeRegisterMask) | resultCcr);
        cpu.InternalWait(InstructionTiming.GetImmediateStatusCycles(targetsStatusRegister: false));
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
        cpu.InternalWait(InstructionTiming.GetImmediateStatusCycles(targetsStatusRegister: true));
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
        cpu.InternalWait(InstructionTiming.GetImmediateStatusCycles(targetsStatusRegister: false));
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
        var result = (ushort)(cpu.Registers.StatusRegister & immediate & ValidStatusRegisterMask);
        cpu.Registers.StatusRegister = result;
        cpu.InternalWait(InstructionTiming.GetImmediateStatusCycles(targetsStatusRegister: true));
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
        cpu.InternalWait(InstructionTiming.GetImmediateStatusCycles(targetsStatusRegister: false));
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
        cpu.InternalWait(InstructionTiming.GetImmediateStatusCycles(targetsStatusRegister: true));
    }

    /// <summary>
    /// Executes <c>ADDI.B #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddImmediateByte(Cpu cpu, ushort opcode) =>
        ExecuteAddOrSubtractImmediate(cpu, opcode, OperandSize.Byte, isAdd: true);

    /// <summary>
    /// Executes <c>ADDI.W #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddImmediateWord(Cpu cpu, ushort opcode) =>
        ExecuteAddOrSubtractImmediate(cpu, opcode, OperandSize.Word, isAdd: true);

    /// <summary>
    /// Executes <c>ADDI.L #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddImmediateLong(Cpu cpu, ushort opcode) =>
        ExecuteAddOrSubtractImmediate(cpu, opcode, OperandSize.Long, isAdd: true);

    /// <summary>
    /// Executes <c>SUBI.B #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubtractImmediateByte(Cpu cpu, ushort opcode) =>
        ExecuteAddOrSubtractImmediate(cpu, opcode, OperandSize.Byte, isAdd: false);

    /// <summary>
    /// Executes <c>SUBI.W #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubtractImmediateWord(Cpu cpu, ushort opcode) =>
        ExecuteAddOrSubtractImmediate(cpu, opcode, OperandSize.Word, isAdd: false);

    /// <summary>
    /// Executes <c>SUBI.L #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubtractImmediateLong(Cpu cpu, ushort opcode) =>
        ExecuteAddOrSubtractImmediate(cpu, opcode, OperandSize.Long, isAdd: false);

    private static void ExecuteAddOrSubtractImmediate(Cpu cpu, ushort opcode, OperandSize size, bool isAdd)
    {
        var source = ReadImmediate(cpu, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, isAdd ? "ADDI" : "SUBI");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = isAdd ? destinationValue + source : destinationValue - source;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyArithmeticFlags(cpu.Registers, size, destinationValue, source, result, isAdd);
        cpu.Registers.ExtendFlag = cpu.Registers.CarryFlag;
        cpu.InternalWait(InstructionTiming.GetAddSubtractImmediateCycles(size, destinationEa));
    }

    private static uint ReadImmediate(Cpu cpu, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => (byte)cpu.FetchPcWord(),
            OperandSize.Word => cpu.FetchPcWord(),
            OperandSize.Long => ((uint)cpu.FetchPcWord() << 16) | cpu.FetchPcWord(),
            _ => 0
        };

    private static DestinationOperandSize ToDestinationOperandSize(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => DestinationOperandSize.Byte,
            OperandSize.Word => DestinationOperandSize.Word,
            _ => DestinationOperandSize.Long
        };

    private static void ApplyArithmeticFlags(Registers registers, OperandSize size, uint destination, uint source, uint result, bool isAdd)
    {
        if (isAdd)
        {
            switch (size)
            {
                case OperandSize.Byte:
                    FlagMath.ApplyAddByte(registers, (byte)destination, (byte)source, (byte)result);
                    return;
                case OperandSize.Word:
                    FlagMath.ApplyAddWord(registers, (ushort)destination, (ushort)source, (ushort)result);
                    return;
                case OperandSize.Long:
                    FlagMath.ApplyAddLong(registers, destination, source, result);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size), size, null);
            }
        }

        switch (size)
        {
            case OperandSize.Byte:
                FlagMath.ApplySubtractByte(registers, (byte)destination, (byte)source, (byte)result);
                return;
            case OperandSize.Word:
                FlagMath.ApplySubtractWord(registers, (ushort)destination, (ushort)source, (ushort)result);
                return;
            case OperandSize.Long:
                FlagMath.ApplySubtractLong(registers, destination, source, result);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }
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
        cpu.InternalWait(InstructionTiming.GetCompareImmediateCycles(OperandSize.Byte, destinationEa));
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
        cpu.InternalWait(InstructionTiming.GetCompareImmediateCycles(OperandSize.Word, destinationEa));
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
        cpu.InternalWait(InstructionTiming.GetCompareImmediateCycles(OperandSize.Long, destinationEa));
    }
}
