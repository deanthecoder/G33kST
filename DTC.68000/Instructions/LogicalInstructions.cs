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
/// Logical unary instruction decode and execution helpers.
/// </summary>
public static class LogicalInstructions
{
    private static readonly Instruction InstrClrByte = new("CLR.B <ea>", ExecuteClearByte);
    private static readonly Instruction InstrClrWord = new("CLR.W <ea>", ExecuteClearWord);
    private static readonly Instruction InstrClrLong = new("CLR.L <ea>", ExecuteClearLong);
    private static readonly Instruction InstrNegByte = new("NEG.B <ea>", ExecuteNegByte);
    private static readonly Instruction InstrNegWord = new("NEG.W <ea>", ExecuteNegWord);
    private static readonly Instruction InstrNegLong = new("NEG.L <ea>", ExecuteNegLong);
    private static readonly Instruction InstrNegxByte = new("NEGX.B <ea>", ExecuteNegxByte);
    private static readonly Instruction InstrNegxWord = new("NEGX.W <ea>", ExecuteNegxWord);
    private static readonly Instruction InstrNegxLong = new("NEGX.L <ea>", ExecuteNegxLong);
    private static readonly Instruction InstrNotByte = new("NOT.B <ea>", ExecuteNotByte);
    private static readonly Instruction InstrNotWord = new("NOT.W <ea>", ExecuteNotWord);
    private static readonly Instruction InstrNotLong = new("NOT.L <ea>", ExecuteNotLong);
    private static readonly Instruction InstrTas = new("TAS <ea>", ExecuteTestAndSetByte);
    private static readonly Instruction InstrTstByte = new("TST.B <ea>", ExecuteTestByte);
    private static readonly Instruction InstrTstWord = new("TST.W <ea>", ExecuteTestWord);
    private static readonly Instruction InstrTstLong = new("TST.L <ea>", ExecuteTestLong);

    /// <summary>
    /// Decodes logical-unary opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0100 1010 11 mmm rrr = TAS <ea>.
        if ((opcode & 0xFFC0) == 0x4AC0)
        {
            var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
            return SupportsTasDestination(destinationEa) ? InstrTas : null;
        }

        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var size = (byte)((opcode >> 6) & 0x03);
        if (size > 2)
            return null;

        return (opcode & 0xFF00) switch
        {
            0x4000 => DecodeNegx(size, ea),
            0x4200 => DecodeClear(size, ea),
            0x4400 => DecodeNeg(size, ea),
            0x4600 => DecodeNot(size, ea),
            0x4A00 => DecodeTest(size, ea),
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>TAS &lt;ea&gt;</c> by setting bit 7 in destination and updating NZVC from the original byte.
    /// </summary>
    private static void ExecuteTestAndSetByte(Cpu cpu, ushort opcode)
    {
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, destinationEa, OperandSize.Byte);
        var sourceValue = (byte)ReadUnsigned(cpu, destination, OperandSize.Byte);
        var result = (byte)(sourceValue | 0x80);
        WriteUnsigned(cpu, destination, OperandSize.Byte, result);
        ApplyPostIncrement(cpu, destination);
        FlagMath.ApplyLogicalByte(cpu.Registers, sourceValue);
    }

    /// <summary>
    /// Executes <c>TST.B &lt;ea&gt;</c> by updating NZVC from a byte read.
    /// </summary>
    private static void ExecuteTestByte(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var value = EffectiveAddressByteAccess.ReadByte(cpu, ea);
        FlagMath.ApplyLogicalByte(cpu.Registers, value);
    }

    /// <summary>
    /// Executes <c>TST.W &lt;ea&gt;</c> by updating NZVC from a word read.
    /// </summary>
    private static void ExecuteTestWord(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var value = EffectiveAddressWordAccess.ReadWord(cpu, ea);
        FlagMath.ApplyLogicalWord(cpu.Registers, value);
    }

    /// <summary>
    /// Executes <c>TST.L &lt;ea&gt;</c> by updating NZVC from a long read.
    /// </summary>
    private static void ExecuteTestLong(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var value = EffectiveAddressLongAccess.ReadLong(cpu, ea);
        FlagMath.ApplyLogicalLong(cpu.Registers, value);
    }

    /// <summary>
    /// Executes <c>CLR.B &lt;ea&gt;</c> by writing zero and updating NZVC.
    /// </summary>
    private static void ExecuteClearByte(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        EffectiveAddressByteAccess.WriteByte(cpu, ea, 0);
        FlagMath.ApplyClear(cpu.Registers);
    }

    /// <summary>
    /// Executes <c>CLR.W &lt;ea&gt;</c> by writing zero and updating NZVC.
    /// </summary>
    private static void ExecuteClearWord(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        EffectiveAddressWordAccess.WriteWord(cpu, ea, 0);
        FlagMath.ApplyClear(cpu.Registers);
    }

    /// <summary>
    /// Executes <c>CLR.L &lt;ea&gt;</c> by writing zero and updating NZVC.
    /// </summary>
    private static void ExecuteClearLong(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        EffectiveAddressLongAccess.WriteLong(cpu, ea, 0);
        FlagMath.ApplyClear(cpu.Registers);
    }

    /// <summary>
    /// Executes <c>NOT.B &lt;ea&gt;</c> with single EA resolution (no duplicated extension/side-effects).
    /// </summary>
    private static void ExecuteNotByte(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, ea, OperandSize.Byte);
        var value = (byte)ReadUnsigned(cpu, destination, OperandSize.Byte);
        var result = (byte)~value;
        WriteUnsigned(cpu, destination, OperandSize.Byte, result);
        ApplyPostIncrement(cpu, destination);
        FlagMath.ApplyLogicalByte(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>NOT.W &lt;ea&gt;</c> with single EA resolution (no duplicated extension/side-effects).
    /// </summary>
    private static void ExecuteNotWord(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, ea, OperandSize.Word);
        var value = (ushort)ReadUnsigned(cpu, destination, OperandSize.Word);
        var result = (ushort)~value;
        WriteUnsigned(cpu, destination, OperandSize.Word, result);
        ApplyPostIncrement(cpu, destination);
        FlagMath.ApplyLogicalWord(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>NOT.L &lt;ea&gt;</c> with single EA resolution (no duplicated extension/side-effects).
    /// </summary>
    private static void ExecuteNotLong(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, ea, OperandSize.Long);
        var value = (uint)ReadUnsigned(cpu, destination, OperandSize.Long);
        var result = ~value;
        WriteUnsigned(cpu, destination, OperandSize.Long, result);
        ApplyPostIncrement(cpu, destination);
        FlagMath.ApplyLogicalLong(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>NEG.B &lt;ea&gt;</c> by replacing destination with <c>0 - destination</c>.
    /// </summary>
    private static void ExecuteNegByte(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Byte, useExtend: false);

    /// <summary>
    /// Executes <c>NEG.W &lt;ea&gt;</c> by replacing destination with <c>0 - destination</c>.
    /// </summary>
    private static void ExecuteNegWord(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Word, useExtend: false);

    /// <summary>
    /// Executes <c>NEG.L &lt;ea&gt;</c> by replacing destination with <c>0 - destination</c>.
    /// </summary>
    private static void ExecuteNegLong(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Long, useExtend: false);

    /// <summary>
    /// Executes <c>NEGX.B &lt;ea&gt;</c> by replacing destination with <c>0 - destination - X</c>.
    /// </summary>
    private static void ExecuteNegxByte(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Byte, useExtend: true);

    /// <summary>
    /// Executes <c>NEGX.W &lt;ea&gt;</c> by replacing destination with <c>0 - destination - X</c>.
    /// </summary>
    private static void ExecuteNegxWord(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Word, useExtend: true);

    /// <summary>
    /// Executes <c>NEGX.L &lt;ea&gt;</c> by replacing destination with <c>0 - destination - X</c>.
    /// </summary>
    private static void ExecuteNegxLong(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Long, useExtend: true);

    private static Instruction DecodeClear(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrClrByte,
            1 => InstrClrWord,
            2 => InstrClrLong,
            _ => null
        };
    }

    private static Instruction DecodeNeg(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrNegByte,
            1 => InstrNegWord,
            2 => InstrNegLong,
            _ => null
        };
    }

    private static Instruction DecodeNegx(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrNegxByte,
            1 => InstrNegxWord,
            2 => InstrNegxLong,
            _ => null
        };
    }

    private static Instruction DecodeNot(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrNotByte,
            1 => InstrNotWord,
            2 => InstrNotLong,
            _ => null
        };
    }

    private static Instruction DecodeTest(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrTstByte,
            1 => InstrTstWord,
            2 => InstrTstLong,
            _ => null
        };
    }

    private static bool SupportsDataAlterableDestination(byte size, EffectiveAddress ea) =>
        size switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteWrite(ea),
            1 => EffectiveAddressWordAccess.SupportsWordWrite(ea),
            2 => EffectiveAddressLongAccess.SupportsLongWrite(ea),
            _ => false
        };

    private static bool SupportsTasDestination(EffectiveAddress destinationEa) =>
        EffectiveAddressByteAccess.SupportsByteRead(destinationEa)
        && EffectiveAddressByteAccess.SupportsByteWrite(destinationEa);

    private static void ExecuteNeg(Cpu cpu, ushort opcode, OperandSize size, bool useExtend)
    {
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, destinationEa, size);
        var source = ReadUnsigned(cpu, destination, size);
        var extendInput = useExtend && cpu.Registers.ExtendFlag ? 1ul : 0ul;
        var result = (0ul - source - extendInput) & OperandMask(size);

        WriteUnsigned(cpu, destination, size, result);
        ApplyPostIncrement(cpu, destination);
        ApplyNegFlags(cpu.Registers, source, result, size, useExtend, extendInput);
    }

    private static void ApplyNegFlags(Registers registers, ulong source, ulong result, OperandSize size, bool useExtend, ulong extendInput)
    {
        var signBit = OperandSignBit(size);
        var isNegative = (result & signBit) != 0;
        var isZero = result == 0;
        var hasBorrow = source + extendInput != 0;
        var signedMathResult = -SignExtendToLong(source, size) - (long)extendInput;
        var signedResult = SignExtendToLong(result, size);

        registers.NegativeFlag = isNegative;
        if (useExtend)
        {
            if (!isZero)
                registers.ZeroFlag = false;
        }
        else
        {
            registers.ZeroFlag = isZero;
        }

        registers.OverflowFlag = signedResult != signedMathResult;
        registers.CarryFlag = hasBorrow;
        registers.ExtendFlag = hasBorrow;
    }

    private static DestinationOperand ResolveDestination(Cpu cpu, EffectiveAddress ea, OperandSize size) =>
        DestinationOperandAccess.ResolveDataAlterable(cpu, ea, ToDestinationOperandSize(size), "Logical unary");

    private static ulong ReadUnsigned(Cpu cpu, DestinationOperand destination, OperandSize size) =>
        DestinationOperandAccess.ReadUnsigned(cpu, destination, ToDestinationOperandSize(size));

    private static void WriteUnsigned(Cpu cpu, DestinationOperand destination, OperandSize size, ulong value) =>
        DestinationOperandAccess.WriteUnsigned(cpu, destination, ToDestinationOperandSize(size), (uint)value);

    private static void ApplyPostIncrement(Cpu cpu, DestinationOperand destination) =>
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);

    private static DestinationOperandSize ToDestinationOperandSize(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => DestinationOperandSize.Byte,
            OperandSize.Word => DestinationOperandSize.Word,
            _ => DestinationOperandSize.Long
        };

    private static ulong OperandMask(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => 0x0000_00FF,
            OperandSize.Word => 0x0000_FFFF,
            OperandSize.Long => 0xFFFF_FFFF,
            _ => 0
        };

    private static ulong OperandSignBit(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => 0x0000_0080,
            OperandSize.Word => 0x0000_8000,
            OperandSize.Long => 0x8000_0000,
            _ => 0
        };

    private static long SignExtendToLong(ulong value, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => unchecked((sbyte)(byte)value),
            OperandSize.Word => unchecked((short)(ushort)value),
            OperandSize.Long => unchecked((int)(uint)value),
            _ => 0
        };
}
