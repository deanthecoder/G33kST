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
/// ADDX/SUBX instruction decode and execution helpers.
/// </summary>
public static class AddSubtractWithExtendInstructions
{
    private static readonly Instruction InstrAddxDataRegister = new("ADDX.<size> Dy,Dx", ExecuteAddWithExtendDataRegisters);
    private static readonly Instruction InstrAddxMemoryPredecrement = new("ADDX.<size> -(Ay),-(Ax)", ExecuteAddWithExtendMemoryPredecrement);
    private static readonly Instruction InstrSubxDataRegister = new("SUBX.<size> Dy,Dx", ExecuteSubtractWithExtendDataRegisters);
    private static readonly Instruction InstrSubxMemoryPredecrement = new("SUBX.<size> -(Ay),-(Ax)", ExecuteSubtractWithExtendMemoryPredecrement);

    /// <summary>
    /// Decodes <c>ADDX</c> opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeAddx(ushort opcode)
    {
        // 1101 xxx1 ss00 mrrr = ADDX.<size> Dy,Dx | -(Ay),-(Ax).
        if ((opcode & 0xF130) != 0xD100)
            return null;

        var size = DecodeSize(opcode);
        if (size == null)
            return null;

        return (opcode & 0x0008) == 0
            ? InstrAddxDataRegister
            : InstrAddxMemoryPredecrement;
    }

    /// <summary>
    /// Decodes <c>SUBX</c> opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeSubx(ushort opcode)
    {
        // 1001 xxx1 ss00 mrrr = SUBX.<size> Dy,Dx | -(Ay),-(Ax).
        if ((opcode & 0xF130) != 0x9100)
            return null;

        var size = DecodeSize(opcode);
        if (size == null)
            return null;

        return (opcode & 0x0008) == 0
            ? InstrSubxDataRegister
            : InstrSubxMemoryPredecrement;
    }

    /// <summary>
    /// Executes <c>ADDX.&lt;size&gt; Dy,Dx</c>.
    /// </summary>
    private static void ExecuteAddWithExtendDataRegisters(Cpu cpu, ushort opcode)
    {
        var size = DecodeSize(opcode) ?? throw new InvalidOperationException($"Invalid ADDX size for opcode 0x{opcode:X4}.");
        var sourceRegisterIndex = opcode & 0x07;
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var source = ReadDataRegister(cpu, sourceRegisterIndex, size);
        var destination = ReadDataRegister(cpu, destinationRegisterIndex, size);
        var extendInput = cpu.Registers.ExtendFlag ? 1ul : 0ul;
        var result = AddWithExtend(source, destination, extendInput, size);

        WriteDataRegister(cpu, destinationRegisterIndex, size, result.Result);
        ApplyExtendArithmeticFlags(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>ADDX.&lt;size&gt; -(Ay),-(Ax)</c>.
    /// </summary>
    private static void ExecuteAddWithExtendMemoryPredecrement(Cpu cpu, ushort opcode)
    {
        var size = DecodeSize(opcode) ?? throw new InvalidOperationException($"Invalid ADDX size for opcode 0x{opcode:X4}.");
        var sourceRegisterIndex = (byte)(opcode & 0x07);
        var destinationRegisterIndex = (byte)((opcode >> 9) & 0x07);
        var sourceAddress = PredecrementAddress(cpu, sourceRegisterIndex, size);
        var destinationAddress = PredecrementAddress(cpu, destinationRegisterIndex, size);
        var source = ReadMemory(cpu, sourceAddress, size);
        var destination = ReadMemory(cpu, destinationAddress, size);
        var extendInput = cpu.Registers.ExtendFlag ? 1ul : 0ul;
        var result = AddWithExtend(source, destination, extendInput, size);

        WriteMemory(cpu, destinationAddress, size, result.Result);
        ApplyExtendArithmeticFlags(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>SUBX.&lt;size&gt; Dy,Dx</c>.
    /// </summary>
    private static void ExecuteSubtractWithExtendDataRegisters(Cpu cpu, ushort opcode)
    {
        var size = DecodeSize(opcode) ?? throw new InvalidOperationException($"Invalid SUBX size for opcode 0x{opcode:X4}.");
        var sourceRegisterIndex = opcode & 0x07;
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var source = ReadDataRegister(cpu, sourceRegisterIndex, size);
        var destination = ReadDataRegister(cpu, destinationRegisterIndex, size);
        var extendInput = cpu.Registers.ExtendFlag ? 1ul : 0ul;
        var result = SubtractWithExtend(source, destination, extendInput, size);

        WriteDataRegister(cpu, destinationRegisterIndex, size, result.Result);
        ApplyExtendArithmeticFlags(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>SUBX.&lt;size&gt; -(Ay),-(Ax)</c>.
    /// </summary>
    private static void ExecuteSubtractWithExtendMemoryPredecrement(Cpu cpu, ushort opcode)
    {
        var size = DecodeSize(opcode) ?? throw new InvalidOperationException($"Invalid SUBX size for opcode 0x{opcode:X4}.");
        var sourceRegisterIndex = (byte)(opcode & 0x07);
        var destinationRegisterIndex = (byte)((opcode >> 9) & 0x07);
        var sourceAddress = PredecrementAddress(cpu, sourceRegisterIndex, size);
        var destinationAddress = PredecrementAddress(cpu, destinationRegisterIndex, size);
        var source = ReadMemory(cpu, sourceAddress, size);
        var destination = ReadMemory(cpu, destinationAddress, size);
        var extendInput = cpu.Registers.ExtendFlag ? 1ul : 0ul;
        var result = SubtractWithExtend(source, destination, extendInput, size);

        WriteMemory(cpu, destinationAddress, size, result.Result);
        ApplyExtendArithmeticFlags(cpu.Registers, result);
    }

    /// <summary>
    /// Applies ADDX/SUBX flags.
    /// Z remains set only if all chained results are zero.
    /// </summary>
    private static void ApplyExtendArithmeticFlags(Registers registers, ExtendArithmeticResult result)
    {
        registers.NegativeFlag = result.IsNegative;
        if (result.Result != 0)
            registers.ZeroFlag = false;

        registers.OverflowFlag = result.Overflow;
        registers.CarryFlag = result.Carry;
        registers.ExtendFlag = result.Carry;
    }

    private static ExtendArithmeticResult AddWithExtend(ulong source, ulong destination, ulong extendInput, OperandSize size)
    {
        var mask = OperandMask(size);
        var signBit = OperandSignBit(size);
        var sum = source + destination + extendInput;
        var result = sum & mask;
        var carry = sum > mask;
        var expectedSigned = SignExtend(source, size) + SignExtend(destination, size) + (long)extendInput;
        var actualSigned = SignExtend(result, size);
        var overflow = actualSigned != expectedSigned;
        var isNegative = (result & signBit) != 0;
        return new ExtendArithmeticResult(result, carry, overflow, isNegative);
    }

    private static ExtendArithmeticResult SubtractWithExtend(ulong source, ulong destination, ulong extendInput, OperandSize size)
    {
        var mask = OperandMask(size);
        var signBit = OperandSignBit(size);
        var subtrahend = source + extendInput;
        var difference = destination - subtrahend;
        var result = difference & mask;
        var carry = subtrahend > destination;
        var expectedSigned = SignExtend(destination, size) - SignExtend(source, size) - (long)extendInput;
        var actualSigned = SignExtend(result, size);
        var overflow = actualSigned != expectedSigned;
        var isNegative = (result & signBit) != 0;
        return new ExtendArithmeticResult(result, carry, overflow, isNegative);
    }

    private static ulong ReadDataRegister(Cpu cpu, int registerIndex, OperandSize size)
    {
        var value = cpu.Registers.GetDataRegister(registerIndex);
        return size switch
        {
            OperandSize.Byte => value & 0x0000_00FF,
            OperandSize.Word => value & 0x0000_FFFF,
            _ => value
        };
    }

    private static void WriteDataRegister(Cpu cpu, int registerIndex, OperandSize size, ulong value)
    {
        switch (size)
        {
            case OperandSize.Byte:
            {
                var registerValue = cpu.Registers.GetDataRegister(registerIndex);
                cpu.Registers.SetDataRegister(registerIndex, (registerValue & 0xFFFF_FF00) | ((uint)value & 0xFF));
                return;
            }
            case OperandSize.Word:
            {
                var registerValue = cpu.Registers.GetDataRegister(registerIndex);
                cpu.Registers.SetDataRegister(registerIndex, (registerValue & 0xFFFF_0000) | ((uint)value & 0xFFFF));
                return;
            }
            case OperandSize.Long:
                cpu.Registers.SetDataRegister(registerIndex, (uint)value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }
    }

    private static ulong ReadMemory(Cpu cpu, uint address, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => cpu.Read8(address),
            OperandSize.Word => cpu.Read16(address),
            _ => cpu.Read32(address)
        };

    private static void WriteMemory(Cpu cpu, uint address, OperandSize size, ulong value)
    {
        switch (size)
        {
            case OperandSize.Byte:
                cpu.Write8(address, (byte)value);
                return;
            case OperandSize.Word:
                cpu.Write16(address, (ushort)value);
                return;
            case OperandSize.Long:
                cpu.Write32(address, (uint)value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }
    }

    private static uint PredecrementAddress(Cpu cpu, byte registerIndex, OperandSize size)
    {
        var currentAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var newAddress = currentAddress - AddressStep(size, registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, newAddress);
        return EffectiveAddressMath.NormalizeAddress24(newAddress);
    }

    private static uint AddressStep(OperandSize size, byte registerIndex) =>
        size switch
        {
            OperandSize.Byte => EffectiveAddressMath.ByteAddressStep(registerIndex),
            OperandSize.Word => 2u,
            _ => 4u
        };

    private static OperandSize? DecodeSize(ushort opcode) =>
        ((opcode >> 6) & 0x03) switch
        {
            0 => OperandSize.Byte,
            1 => OperandSize.Word,
            2 => OperandSize.Long,
            _ => null
        };

    private static ulong OperandMask(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => 0x0000_00FF,
            OperandSize.Word => 0x0000_FFFF,
            _ => 0xFFFF_FFFF
        };

    private static ulong OperandSignBit(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => 0x0000_0080,
            OperandSize.Word => 0x0000_8000,
            _ => 0x8000_0000
        };

    private static long SignExtend(ulong value, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => unchecked((sbyte)(byte)value),
            OperandSize.Word => unchecked((short)(ushort)value),
            _ => unchecked((int)(uint)value)
        };

    private readonly record struct ExtendArithmeticResult(ulong Result, bool Carry, bool Overflow, bool IsNegative);
}
