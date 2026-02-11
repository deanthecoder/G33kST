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
/// ADDQ/SUBQ instruction decode and execution helpers.
/// </summary>
public static class AddSubtractQuickInstructions
{
    private static readonly Instruction InstrAddQuickByte = new("ADDQ.B #<data>,<ea>", ExecuteAddQuickByte);
    private static readonly Instruction InstrAddQuickWord = new("ADDQ.W #<data>,<ea>", ExecuteAddQuickWord);
    private static readonly Instruction InstrAddQuickLong = new("ADDQ.L #<data>,<ea>", ExecuteAddQuickLong);
    private static readonly Instruction InstrSubQuickByte = new("SUBQ.B #<data>,<ea>", ExecuteSubQuickByte);
    private static readonly Instruction InstrSubQuickWord = new("SUBQ.W #<data>,<ea>", ExecuteSubQuickWord);
    private static readonly Instruction InstrSubQuickLong = new("SUBQ.L #<data>,<ea>", ExecuteSubQuickLong);

    /// <summary>
    /// Decodes ADDQ/SUBQ opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0101 ddd ooo mmm rrr = ADDQ/SUBQ forms.
        if ((opcode & 0xF000) != 0x5000)
            return null;

        var operationMode = (opcode >> 6) & 0x07;
        var size = DecodeSize(operationMode);
        if (size == null)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        if (!SupportsDestination(destination, size.Value))
            return null;

        return operationMode switch
        {
            0 => InstrAddQuickByte,
            1 => InstrAddQuickWord,
            2 => InstrAddQuickLong,
            4 => InstrSubQuickByte,
            5 => InstrSubQuickWord,
            6 => InstrSubQuickLong,
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>ADDQ.B #&lt;data&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddQuickByte(Cpu cpu, ushort opcode) =>
        ExecuteAddQuick(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>ADDQ.W #&lt;data&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddQuickWord(Cpu cpu, ushort opcode) =>
        ExecuteAddQuick(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>ADDQ.L #&lt;data&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddQuickLong(Cpu cpu, ushort opcode) =>
        ExecuteAddQuick(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>SUBQ.B #&lt;data&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubQuickByte(Cpu cpu, ushort opcode) =>
        ExecuteSubQuick(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>SUBQ.W #&lt;data&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubQuickWord(Cpu cpu, ushort opcode) =>
        ExecuteSubQuick(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>SUBQ.L #&lt;data&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubQuickLong(Cpu cpu, ushort opcode) =>
        ExecuteSubQuick(cpu, opcode, OperandSize.Long);

    private static void ExecuteAddQuick(Cpu cpu, ushort opcode, OperandSize size)
    {
        var quickImmediate = DecodeQuickImmediate(opcode);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        if (destinationEa.Mode == EffectiveAddressMode.AddressRegisterDirect)
        {
            var addressValue = cpu.Registers.GetAddressRegister(destinationEa.Register);
            cpu.Registers.SetAddressRegister(destinationEa.Register, addressValue + quickImmediate);
            return;
        }

        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, "ADDQ");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = destinationValue + quickImmediate;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyAddFlags(cpu.Registers, size, destinationValue, quickImmediate, result);
        cpu.Registers.ExtendFlag = cpu.Registers.CarryFlag;
    }

    private static void ExecuteSubQuick(Cpu cpu, ushort opcode, OperandSize size)
    {
        var quickImmediate = DecodeQuickImmediate(opcode);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        if (destinationEa.Mode == EffectiveAddressMode.AddressRegisterDirect)
        {
            var addressValue = cpu.Registers.GetAddressRegister(destinationEa.Register);
            cpu.Registers.SetAddressRegister(destinationEa.Register, addressValue - quickImmediate);
            return;
        }

        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, "SUBQ");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = destinationValue - quickImmediate;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplySubFlags(cpu.Registers, size, destinationValue, quickImmediate, result);
        cpu.Registers.ExtendFlag = cpu.Registers.CarryFlag;
    }

    private static void ApplyAddFlags(Registers registers, OperandSize size, uint destination, uint source, uint result)
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

    private static void ApplySubFlags(Registers registers, OperandSize size, uint destination, uint source, uint result)
    {
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

    private static bool SupportsDestination(EffectiveAddress destination, OperandSize size)
    {
        if (destination.Mode == EffectiveAddressMode.AddressRegisterDirect)
            return size != OperandSize.Byte;

        return size switch
        {
            OperandSize.Byte => EffectiveAddressByteAccess.SupportsByteRead(destination) && EffectiveAddressByteAccess.SupportsByteWrite(destination),
            OperandSize.Word => EffectiveAddressWordAccess.SupportsWordRead(destination) && EffectiveAddressWordAccess.SupportsWordWrite(destination),
            OperandSize.Long => EffectiveAddressLongAccess.SupportsLongRead(destination) && EffectiveAddressLongAccess.SupportsLongWrite(destination),
            _ => false
        };
    }

    private static uint DecodeQuickImmediate(ushort opcode)
    {
        var value = (uint)((opcode >> 9) & 0x07);
        return value == 0 ? 8u : value;
    }

    private static OperandSize? DecodeSize(int operationMode) =>
        operationMode switch
        {
            0 or 4 => OperandSize.Byte,
            1 or 5 => OperandSize.Word,
            2 or 6 => OperandSize.Long,
            _ => null
        };

    private static DestinationOperandSize ToDestinationOperandSize(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => DestinationOperandSize.Byte,
            OperandSize.Word => DestinationOperandSize.Word,
            _ => DestinationOperandSize.Long
        };
}
