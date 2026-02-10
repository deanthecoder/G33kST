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
/// EOR-family instruction decode and execution helpers.
/// </summary>
public static class EorInstructions
{
    private static readonly Instruction InstrEorByteDataRegisterToEa = new("EOR.B Dn,<ea>", ExecuteEorByteDataRegisterToEa);
    private static readonly Instruction InstrEorWordDataRegisterToEa = new("EOR.W Dn,<ea>", ExecuteEorWordDataRegisterToEa);
    private static readonly Instruction InstrEorLongDataRegisterToEa = new("EOR.L Dn,<ea>", ExecuteEorLongDataRegisterToEa);
    private static readonly Instruction InstrEoriByte = new("EORI.B #<imm>,<ea>", ExecuteEorImmediateByte);
    private static readonly Instruction InstrEoriWord = new("EORI.W #<imm>,<ea>", ExecuteEorImmediateWord);
    private static readonly Instruction InstrEoriLong = new("EORI.L #<imm>,<ea>", ExecuteEorImmediateLong);

    /// <summary>
    /// Decodes EORI opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeImmediate(ushort opcode)
    {
        // 0000 1010 ss mmm rrr = EORI.<size> #<imm>,<ea>.
        if ((opcode & 0xFF00) != 0x0A00)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var sizeCode = (opcode >> 6) & 0x03;
        return sizeCode switch
        {
            0 => SupportsByteReadWrite(destination) ? InstrEoriByte : null,
            1 => SupportsWordReadWrite(destination) ? InstrEoriWord : null,
            2 => SupportsLongReadWrite(destination) ? InstrEoriLong : null,
            _ => null
        };
    }

    /// <summary>
    /// Decodes EOR opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 1011 ddd 1oo mmm rrr = EOR.<size> Dn,<ea>.
        if ((opcode & 0xF100) != 0xB100)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            4 => SupportsByteReadWrite(destination) ? InstrEorByteDataRegisterToEa : null,
            5 => SupportsWordReadWrite(destination) ? InstrEorWordDataRegisterToEa : null,
            6 => SupportsLongReadWrite(destination) ? InstrEorLongDataRegisterToEa : null,
            _ => null
        };
    }

    private static bool SupportsByteReadWrite(EffectiveAddress ea) =>
        EffectiveAddressByteAccess.SupportsByteRead(ea) && EffectiveAddressByteAccess.SupportsByteWrite(ea);

    private static bool SupportsWordReadWrite(EffectiveAddress ea) =>
        EffectiveAddressWordAccess.SupportsWordRead(ea) && EffectiveAddressWordAccess.SupportsWordWrite(ea);

    private static bool SupportsLongReadWrite(EffectiveAddress ea) =>
        EffectiveAddressLongAccess.SupportsLongRead(ea) && EffectiveAddressLongAccess.SupportsLongWrite(ea);

    /// <summary>
    /// Executes <c>EOR.B Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteEorByteDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteEorDataRegisterToEa(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>EOR.W Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteEorWordDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteEorDataRegisterToEa(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>EOR.L Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteEorLongDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteEorDataRegisterToEa(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>EORI.B #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteEorImmediateByte(Cpu cpu, ushort opcode) =>
        ExecuteEorImmediate(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>EORI.W #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteEorImmediateWord(Cpu cpu, ushort opcode) =>
        ExecuteEorImmediate(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>EORI.L #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteEorImmediateLong(Cpu cpu, ushort opcode) =>
        ExecuteEorImmediate(cpu, opcode, OperandSize.Long);

    private static void ExecuteEorDataRegisterToEa(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceRegisterIndex = (opcode >> 9) & 0x07;
        var source = ReadDataRegister(cpu, sourceRegisterIndex, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, "EOR");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = source ^ destinationValue;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyLogicalFlags(cpu.Registers, size, result);
    }

    private static void ExecuteEorImmediate(Cpu cpu, ushort opcode, OperandSize size)
    {
        var source = ReadImmediate(cpu, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, "EORI");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = source ^ destinationValue;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyLogicalFlags(cpu.Registers, size, result);
    }

    private static uint ReadImmediate(Cpu cpu, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => (byte)cpu.FetchPcWord(),
            OperandSize.Word => cpu.FetchPcWord(),
            OperandSize.Long => ((uint)cpu.FetchPcWord() << 16) | cpu.FetchPcWord(),
            _ => 0
        };

    private static uint ReadDataRegister(Cpu cpu, int registerIndex, OperandSize size)
    {
        var registerValue = cpu.Registers.GetDataRegister(registerIndex);
        return size switch
        {
            OperandSize.Byte => registerValue & 0x0000_00FF,
            OperandSize.Word => registerValue & 0x0000_FFFF,
            OperandSize.Long => registerValue,
            _ => 0
        };
    }

    private static void ApplyLogicalFlags(Registers registers, OperandSize size, uint result)
    {
        switch (size)
        {
            case OperandSize.Byte:
                FlagMath.ApplyLogicalByte(registers, (byte)result);
                return;
            case OperandSize.Word:
                FlagMath.ApplyLogicalWord(registers, (ushort)result);
                return;
            case OperandSize.Long:
                FlagMath.ApplyLogicalLong(registers, result);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }
    }

    private static DestinationOperandSize ToDestinationOperandSize(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => DestinationOperandSize.Byte,
            OperandSize.Word => DestinationOperandSize.Word,
            _ => DestinationOperandSize.Long
        };
}
