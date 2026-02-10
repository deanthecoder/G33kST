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
            0 => SupportsByteReadWrite(destination) ? InstrOriByte : null,
            1 => SupportsWordReadWrite(destination) ? InstrOriWord : null,
            2 => SupportsLongReadWrite(destination) ? InstrOriLong : null,
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
            4 => SupportsByteReadWrite(sourceOrDestination) ? InstrOrByteDataRegisterToEa : null,
            5 => SupportsWordReadWrite(sourceOrDestination) ? InstrOrWordDataRegisterToEa : null,
            6 => SupportsLongReadWrite(sourceOrDestination) ? InstrOrLongDataRegisterToEa : null,
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

    private static void ExecuteOrEaToDataRegister(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = ReadFromEa(cpu, sourceEa, size);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destination = ReadDataRegister(cpu, destinationRegisterIndex, size);
        var result = destination | source;

        WriteDataRegister(cpu, destinationRegisterIndex, size, result);
        ApplyLogicalFlags(cpu.Registers, size, result);
    }

    private static void ExecuteOrDataRegisterToEa(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceRegisterIndex = (opcode >> 9) & 0x07;
        var source = ReadDataRegister(cpu, sourceRegisterIndex, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, ToDestinationOperandSize(size), "OR");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, ToDestinationOperandSize(size));
        var result = source | destinationValue;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, ToDestinationOperandSize(size), result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyLogicalFlags(cpu.Registers, size, result);
    }

    private static void ExecuteOrImmediate(Cpu cpu, ushort opcode, OperandSize size)
    {
        var source = ReadImmediate(cpu, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, ToDestinationOperandSize(size), "ORI");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, ToDestinationOperandSize(size));
        var result = source | destinationValue;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, ToDestinationOperandSize(size), result);
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

    private static uint ReadFromEa(Cpu cpu, EffectiveAddress ea, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => EffectiveAddressByteAccess.ReadByte(cpu, ea),
            OperandSize.Word => EffectiveAddressWordAccess.ReadWord(cpu, ea),
            OperandSize.Long => EffectiveAddressLongAccess.ReadLong(cpu, ea),
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

    private static void WriteDataRegister(Cpu cpu, int registerIndex, OperandSize size, uint value)
    {
        switch (size)
        {
            case OperandSize.Byte:
            {
                var registerValue = cpu.Registers.GetDataRegister(registerIndex);
                cpu.Registers.SetDataRegister(registerIndex, (registerValue & 0xFFFF_FF00) | (value & 0xFF));
                return;
            }
            case OperandSize.Word:
            {
                var registerValue = cpu.Registers.GetDataRegister(registerIndex);
                cpu.Registers.SetDataRegister(registerIndex, (registerValue & 0xFFFF_0000) | (value & 0xFFFF));
                return;
            }
            case OperandSize.Long:
                cpu.Registers.SetDataRegister(registerIndex, value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }
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

    private enum OperandSize
    {
        Byte,
        Word,
        Long
    }
}
