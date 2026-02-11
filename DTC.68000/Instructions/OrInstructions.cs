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
/// OR-family instruction decode and execution helpers.
/// </summary>
public static class OrInstructions
{
    private static readonly Instruction InstrOrByteEaToDataRegister = new("OR.B <ea>,Dn", ExecuteOrByteEaToDataRegister);
    private static readonly Instruction InstrOrWordEaToDataRegister = new("OR.W <ea>,Dn", ExecuteOrWordEaToDataRegister);
    private static readonly Instruction InstrOrLongEaToDataRegister = new("OR.L <ea>,Dn", ExecuteOrLongEaToDataRegister);
    private static readonly Instruction InstrOrByteDataRegisterToEa = new("OR.B Dn,<ea>", ExecuteOrByteDataRegisterToEa);
    private static readonly Instruction InstrOrWordDataRegisterToEa = new("OR.W Dn,<ea>", ExecuteOrWordDataRegisterToEa);
    private static readonly Instruction InstrOrLongDataRegisterToEa = new("OR.L Dn,<ea>", ExecuteOrLongDataRegisterToEa);
    private static readonly Instruction InstrOriByte = new("ORI.B #<imm>,<ea>", ExecuteOrImmediateByte);
    private static readonly Instruction InstrOriWord = new("ORI.W #<imm>,<ea>", ExecuteOrImmediateWord);
    private static readonly Instruction InstrOriLong = new("ORI.L #<imm>,<ea>", ExecuteOrImmediateLong);

    /// <summary>
    /// Decodes ORI opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeImmediate(ushort opcode)
    {
        // 0000 0000 ss mmm rrr = ORI.<size> #<imm>,<ea>.
        if ((opcode & 0xFF00) != 0x0000)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var sizeCode = (opcode >> 6) & 0x03;
        return sizeCode switch
        {
            0 => SupportsReadWrite(destination, OperandSize.Byte) ? InstrOriByte : null,
            1 => SupportsReadWrite(destination, OperandSize.Word) ? InstrOriWord : null,
            2 => SupportsReadWrite(destination, OperandSize.Long) ? InstrOriLong : null,
            _ => null
        };
    }

    /// <summary>
    /// Decodes OR opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 1000 ddd ooo mmm rrr = OR.<size> forms.
        if ((opcode & 0xF000) != 0x8000)
            return null;

        var sourceOrDestination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteRead(sourceOrDestination) ? InstrOrByteEaToDataRegister : null,
            1 => EffectiveAddressWordAccess.SupportsWordRead(sourceOrDestination) ? InstrOrWordEaToDataRegister : null,
            2 => EffectiveAddressLongAccess.SupportsLongRead(sourceOrDestination) ? InstrOrLongEaToDataRegister : null,
            4 => SupportsReadWrite(sourceOrDestination, OperandSize.Byte) ? InstrOrByteDataRegisterToEa : null,
            5 => SupportsReadWrite(sourceOrDestination, OperandSize.Word) ? InstrOrWordDataRegisterToEa : null,
            6 => SupportsReadWrite(sourceOrDestination, OperandSize.Long) ? InstrOrLongDataRegisterToEa : null,
            _ => null
        };
    }

    private static bool SupportsReadWrite(EffectiveAddress ea, OperandSize size) =>
        BitwiseLogicalInstructionHelpers.SupportsReadWrite(ea, size);

    /// <summary>
    /// Executes <c>OR.B &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteOrByteEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteOrEaToDataRegister(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>OR.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteOrWordEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteOrEaToDataRegister(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>OR.L &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteOrLongEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteOrEaToDataRegister(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>OR.B Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteOrByteDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteOrDataRegisterToEa(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>OR.W Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteOrWordDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteOrDataRegisterToEa(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>OR.L Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteOrLongDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteOrDataRegisterToEa(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>ORI.B #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteOrImmediateByte(Cpu cpu, ushort opcode) =>
        ExecuteOrImmediate(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>ORI.W #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteOrImmediateWord(Cpu cpu, ushort opcode) =>
        ExecuteOrImmediate(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>ORI.L #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteOrImmediateLong(Cpu cpu, ushort opcode) =>
        ExecuteOrImmediate(cpu, opcode, OperandSize.Long);

    private static void ExecuteOrEaToDataRegister(Cpu cpu, ushort opcode, OperandSize size) =>
        BitwiseLogicalInstructionHelpers.ExecuteEaToDataRegister(cpu, opcode, size, static (destination, source) => destination | source);

    private static void ExecuteOrDataRegisterToEa(Cpu cpu, ushort opcode, OperandSize size) =>
        BitwiseLogicalInstructionHelpers.ExecuteDataRegisterToEa(cpu, opcode, size, "OR", static (source, destination) => source | destination);

    private static void ExecuteOrImmediate(Cpu cpu, ushort opcode, OperandSize size) =>
        BitwiseLogicalInstructionHelpers.ExecuteImmediateToEa(cpu, opcode, size, "ORI", static (source, destination) => source | destination);
}
