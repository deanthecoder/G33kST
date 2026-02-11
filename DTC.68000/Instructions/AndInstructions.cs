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
/// AND-family instruction decode and execution helpers.
/// </summary>
public static class AndInstructions
{
    private static readonly Instruction InstrAndByteEaToDataRegister = new("AND.B <ea>,Dn", ExecuteAndByteEaToDataRegister);
    private static readonly Instruction InstrAndWordEaToDataRegister = new("AND.W <ea>,Dn", ExecuteAndWordEaToDataRegister);
    private static readonly Instruction InstrAndLongEaToDataRegister = new("AND.L <ea>,Dn", ExecuteAndLongEaToDataRegister);
    private static readonly Instruction InstrAndByteDataRegisterToEa = new("AND.B Dn,<ea>", ExecuteAndByteDataRegisterToEa);
    private static readonly Instruction InstrAndWordDataRegisterToEa = new("AND.W Dn,<ea>", ExecuteAndWordDataRegisterToEa);
    private static readonly Instruction InstrAndLongDataRegisterToEa = new("AND.L Dn,<ea>", ExecuteAndLongDataRegisterToEa);
    private static readonly Instruction InstrAndiByte = new("ANDI.B #<imm>,<ea>", ExecuteAndImmediateByte);
    private static readonly Instruction InstrAndiWord = new("ANDI.W #<imm>,<ea>", ExecuteAndImmediateWord);
    private static readonly Instruction InstrAndiLong = new("ANDI.L #<imm>,<ea>", ExecuteAndImmediateLong);

    /// <summary>
    /// Decodes ANDI opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeImmediate(ushort opcode)
    {
        // 0000 0010 ss mmm rrr = ANDI.<size> #<imm>,<ea>.
        if ((opcode & 0xFF00) != 0x0200)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var sizeCode = (opcode >> 6) & 0x03;
        return sizeCode switch
        {
            0 => SupportsReadWrite(destination, OperandSize.Byte) ? InstrAndiByte : null,
            1 => SupportsReadWrite(destination, OperandSize.Word) ? InstrAndiWord : null,
            2 => SupportsReadWrite(destination, OperandSize.Long) ? InstrAndiLong : null,
            _ => null
        };
    }

    /// <summary>
    /// Decodes AND opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 1100 ddd ooo mmm rrr = AND.<size> forms.
        if ((opcode & 0xF000) != 0xC000)
            return null;

        var sourceOrDestination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteRead(sourceOrDestination) ? InstrAndByteEaToDataRegister : null,
            1 => EffectiveAddressWordAccess.SupportsWordRead(sourceOrDestination) ? InstrAndWordEaToDataRegister : null,
            2 => EffectiveAddressLongAccess.SupportsLongRead(sourceOrDestination) ? InstrAndLongEaToDataRegister : null,
            4 => SupportsReadWrite(sourceOrDestination, OperandSize.Byte) ? InstrAndByteDataRegisterToEa : null,
            5 => SupportsReadWrite(sourceOrDestination, OperandSize.Word) ? InstrAndWordDataRegisterToEa : null,
            6 => SupportsReadWrite(sourceOrDestination, OperandSize.Long) ? InstrAndLongDataRegisterToEa : null,
            _ => null
        };
    }

    private static bool SupportsReadWrite(EffectiveAddress ea, OperandSize size) =>
        BitwiseLogicalInstructionHelpers.SupportsReadWrite(ea, size);

    /// <summary>
    /// Executes <c>AND.B &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteAndByteEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteAndEaToDataRegister(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>AND.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteAndWordEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteAndEaToDataRegister(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>AND.L &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteAndLongEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteAndEaToDataRegister(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>AND.B Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAndByteDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteAndDataRegisterToEa(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>AND.W Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAndWordDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteAndDataRegisterToEa(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>AND.L Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAndLongDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteAndDataRegisterToEa(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>ANDI.B #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAndImmediateByte(Cpu cpu, ushort opcode) =>
        ExecuteAndImmediate(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>ANDI.W #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAndImmediateWord(Cpu cpu, ushort opcode) =>
        ExecuteAndImmediate(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>ANDI.L #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAndImmediateLong(Cpu cpu, ushort opcode) =>
        ExecuteAndImmediate(cpu, opcode, OperandSize.Long);

    private static void ExecuteAndEaToDataRegister(Cpu cpu, ushort opcode, OperandSize size) =>
        BitwiseLogicalInstructionHelpers.ExecuteEaToDataRegister(cpu, opcode, size, static (destination, source) => destination & source);

    private static void ExecuteAndDataRegisterToEa(Cpu cpu, ushort opcode, OperandSize size) =>
        BitwiseLogicalInstructionHelpers.ExecuteDataRegisterToEa(cpu, opcode, size, "AND", static (source, destination) => source & destination);

    private static void ExecuteAndImmediate(Cpu cpu, ushort opcode, OperandSize size) =>
        BitwiseLogicalInstructionHelpers.ExecuteImmediateToEa(cpu, opcode, size, "ANDI", static (source, destination) => source & destination);
}
