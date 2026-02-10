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
            0 => SupportsByteReadWrite(destination) ? InstrAndiByte : null,
            1 => SupportsWordReadWrite(destination) ? InstrAndiWord : null,
            2 => SupportsLongReadWrite(destination) ? InstrAndiLong : null,
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
            4 => SupportsByteReadWrite(sourceOrDestination) ? InstrAndByteDataRegisterToEa : null,
            5 => SupportsWordReadWrite(sourceOrDestination) ? InstrAndWordDataRegisterToEa : null,
            6 => SupportsLongReadWrite(sourceOrDestination) ? InstrAndLongDataRegisterToEa : null,
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

    private static void ExecuteAndEaToDataRegister(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = ReadFromEa(cpu, sourceEa, size);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destination = ReadDataRegister(cpu, destinationRegisterIndex, size);
        var result = destination & source;

        WriteDataRegister(cpu, destinationRegisterIndex, size, result);
        ApplyLogicalFlags(cpu.Registers, size, result);
    }

    private static void ExecuteAndDataRegisterToEa(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceRegisterIndex = (opcode >> 9) & 0x07;
        var source = ReadDataRegister(cpu, sourceRegisterIndex, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, "AND");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = source & destinationValue;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyLogicalFlags(cpu.Registers, size, result);
    }

    private static void ExecuteAndImmediate(Cpu cpu, ushort opcode, OperandSize size)
    {
        var source = ReadImmediate(cpu, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, "ANDI");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = source & destinationValue;

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
}
