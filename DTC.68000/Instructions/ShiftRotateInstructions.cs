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
/// Group-E shift/rotate instruction decode and execution helpers.
/// </summary>
public static class ShiftRotateInstructions
{
    private static readonly Instruction InstrAsl = new("ASL.<size> <count>,<dest>", ExecuteAsl);
    private static readonly Instruction InstrAsr = new("ASR.<size> <count>,<dest>", ExecuteAsr);
    private static readonly Instruction InstrLsl = new("LSL.<size> <count>,<dest>", ExecuteLsl);
    private static readonly Instruction InstrLsr = new("LSR.<size> <count>,<dest>", ExecuteLsr);
    private static readonly Instruction InstrRol = new("ROL.<size> <count>,<dest>", ExecuteRol);
    private static readonly Instruction InstrRor = new("ROR.<size> <count>,<dest>", ExecuteRor);
    private static readonly Instruction InstrRoxl = new("ROXL.<size> <count>,<dest>", ExecuteRoxl);
    private static readonly Instruction InstrRoxr = new("ROXR.<size> <count>,<dest>", ExecuteRoxr);

    /// <summary>
    /// Decodes shift/rotate opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        if ((opcode & 0xF000) != 0xE000)
            return null;

        return IsMemoryForm(opcode)
            ? TryDecodeMemoryForm(opcode)
            : TryDecodeRegisterForm(opcode);
    }

    private static Instruction TryDecodeRegisterForm(ushort opcode)
    {
        if (DecodeRegisterSize(opcode) == null)
            return null;

        var operationFamily = (opcode >> 3) & 0x03;
        var isLeft = (opcode & 0x0100) != 0;
        var operation = DecodeOperation(operationFamily, isLeft);
        return InstructionFor(operation);
    }

    private static Instruction TryDecodeMemoryForm(ushort opcode)
    {
        var operationFamily = (opcode >> 9) & 0x07;
        if (operationFamily > 3)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        if (!SupportsWordMemoryDestination(destination))
            return null;

        var isLeft = (opcode & 0x0100) != 0;
        var operation = DecodeOperation(operationFamily, isLeft);
        return InstructionFor(operation);
    }

    private static bool SupportsWordMemoryDestination(EffectiveAddress destination) =>
        destination.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => true,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => destination.Register is 0 or 1,
            _ => false
        };

    private static void ExecuteAsl(Cpu cpu, ushort opcode) =>
        Execute(cpu, opcode, ShiftRotateOperation.Asl);

    private static void ExecuteAsr(Cpu cpu, ushort opcode) =>
        Execute(cpu, opcode, ShiftRotateOperation.Asr);

    private static void ExecuteLsl(Cpu cpu, ushort opcode) =>
        Execute(cpu, opcode, ShiftRotateOperation.Lsl);

    private static void ExecuteLsr(Cpu cpu, ushort opcode) =>
        Execute(cpu, opcode, ShiftRotateOperation.Lsr);

    private static void ExecuteRol(Cpu cpu, ushort opcode) =>
        Execute(cpu, opcode, ShiftRotateOperation.Rol);

    private static void ExecuteRor(Cpu cpu, ushort opcode) =>
        Execute(cpu, opcode, ShiftRotateOperation.Ror);

    private static void ExecuteRoxl(Cpu cpu, ushort opcode) =>
        Execute(cpu, opcode, ShiftRotateOperation.Roxl);

    private static void ExecuteRoxr(Cpu cpu, ushort opcode) =>
        Execute(cpu, opcode, ShiftRotateOperation.Roxr);

    private static void Execute(Cpu cpu, ushort opcode, ShiftRotateOperation operation)
    {
        if (IsMemoryForm(opcode))
        {
            ExecuteMemoryForm(cpu, opcode, operation);
            return;
        }

        ExecuteRegisterForm(cpu, opcode, operation);
    }

    private static void ExecuteRegisterForm(Cpu cpu, ushort opcode, ShiftRotateOperation operation)
    {
        var size = DecodeRegisterSize(opcode) ?? throw new InvalidOperationException($"Invalid shift/rotate size for opcode 0x{opcode:X4}.");
        var destinationRegisterIndex = opcode & 0x07;
        var count = DecodeRegisterCount(cpu, opcode);
        var countFromRegister = (opcode & 0x0020) != 0;
        var destinationValue = ReadDataRegister(cpu, destinationRegisterIndex, size);
        var outcome = ExecuteShiftRotate(destinationValue, size, count, operation, cpu.Registers.ExtendFlag);

        WriteDataRegister(cpu, destinationRegisterIndex, size, outcome.Result);
        ApplyFlags(cpu.Registers, outcome, size);
        cpu.InternalWait(InstructionTiming.GetRegisterShiftRotateCycles(size, countFromRegister, count));
    }

    private static void ExecuteMemoryForm(Cpu cpu, ushort opcode, ShiftRotateOperation operation)
    {
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, DestinationOperandSize.Word, GetMnemonic(operation));
        if (destination.IsDataRegister)
            throw new InvalidOperationException($"Invalid {GetMnemonic(operation)} memory destination for opcode 0x{opcode:X4}.");

        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, DestinationOperandSize.Word);
        var outcome = ExecuteShiftRotate(destinationValue, OperandSize.Word, 1, operation, cpu.Registers.ExtendFlag);

        DestinationOperandAccess.WriteUnsigned(cpu, destination, DestinationOperandSize.Word, outcome.Result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyFlags(cpu.Registers, outcome, OperandSize.Word);
        cpu.InternalWait(InstructionTiming.GetMemoryShiftRotateCycles(destinationEa));
    }

    private static ShiftRotateOutcome ExecuteShiftRotate(uint value, OperandSize size, int count, ShiftRotateOperation operation, bool initialExtend)
    {
        var mask = OperandMask(size);
        var signBit = OperandSignBit(size);
        var result = value & mask;

        if (count == 0)
        {
            if (operation is ShiftRotateOperation.Roxl or ShiftRotateOperation.Roxr)
                return new ShiftRotateOutcome(result, initialExtend, false, false, initialExtend);

            return new ShiftRotateOutcome(result, false, false, false, initialExtend);
        }

        var carry = false;
        var overflow = false;
        switch (operation)
        {
            case ShiftRotateOperation.Asl:
                for (var i = 0; i < count; i++)
                {
                    var before = result;
                    carry = (before & signBit) != 0;
                    result = (before << 1) & mask;
                    if (((before ^ result) & signBit) != 0)
                        overflow = true;
                }

                return new ShiftRotateOutcome(result, carry, overflow, true, carry);

            case ShiftRotateOperation.Asr:
                for (var i = 0; i < count; i++)
                {
                    carry = (result & 1) != 0;
                    var sign = (result & signBit) != 0;
                    result = (result >> 1) | (sign ? signBit : 0);
                }

                return new ShiftRotateOutcome(result, carry, false, true, carry);

            case ShiftRotateOperation.Lsl:
                for (var i = 0; i < count; i++)
                {
                    carry = (result & signBit) != 0;
                    result = (result << 1) & mask;
                }

                return new ShiftRotateOutcome(result, carry, false, true, carry);

            case ShiftRotateOperation.Lsr:
                for (var i = 0; i < count; i++)
                {
                    carry = (result & 1) != 0;
                    result >>= 1;
                }

                return new ShiftRotateOutcome(result, carry, false, true, carry);

            case ShiftRotateOperation.Rol:
                for (var i = 0; i < count; i++)
                {
                    carry = (result & signBit) != 0;
                    result = ((result << 1) & mask) | (carry ? 1u : 0u);
                }

                return new ShiftRotateOutcome(result, carry, false, false, initialExtend);

            case ShiftRotateOperation.Ror:
                for (var i = 0; i < count; i++)
                {
                    carry = (result & 1) != 0;
                    result = (result >> 1) | (carry ? signBit : 0u);
                }

                return new ShiftRotateOutcome(result, carry, false, false, initialExtend);

            case ShiftRotateOperation.Roxl:
            {
                var extendIn = initialExtend;
                for (var i = 0; i < count; i++)
                {
                    carry = (result & signBit) != 0;
                    result = ((result << 1) & mask) | (extendIn ? 1u : 0u);
                    extendIn = carry;
                }

                return new ShiftRotateOutcome(result, carry, false, true, carry);
            }

            case ShiftRotateOperation.Roxr:
            {
                var extendIn = initialExtend;
                for (var i = 0; i < count; i++)
                {
                    carry = (result & 1) != 0;
                    result = (result >> 1) | (extendIn ? signBit : 0u);
                    extendIn = carry;
                }

                return new ShiftRotateOutcome(result, carry, false, true, carry);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static void ApplyFlags(Registers registers, ShiftRotateOutcome outcome, OperandSize size)
    {
        registers.NegativeFlag = (outcome.Result & OperandSignBit(size)) != 0;
        registers.ZeroFlag = (outcome.Result & OperandMask(size)) == 0;
        registers.OverflowFlag = outcome.Overflow;
        registers.CarryFlag = outcome.Carry;
        if (outcome.UpdateExtend)
            registers.ExtendFlag = outcome.ExtendValue;
    }

    private static int DecodeRegisterCount(Cpu cpu, ushort opcode)
    {
        if ((opcode & 0x0020) == 0)
        {
            var immediateCount = (opcode >> 9) & 0x07;
            return immediateCount == 0 ? 8 : immediateCount;
        }

        var sourceRegisterIndex = (opcode >> 9) & 0x07;
        return (int)(cpu.Registers.GetDataRegister(sourceRegisterIndex) & 0x3F);
    }

    private static ShiftRotateOperation DecodeOperation(int operationFamily, bool isLeft) =>
        operationFamily switch
        {
            0 => isLeft ? ShiftRotateOperation.Asl : ShiftRotateOperation.Asr,
            1 => isLeft ? ShiftRotateOperation.Lsl : ShiftRotateOperation.Lsr,
            2 => isLeft ? ShiftRotateOperation.Roxl : ShiftRotateOperation.Roxr,
            3 => isLeft ? ShiftRotateOperation.Rol : ShiftRotateOperation.Ror,
            _ => throw new ArgumentOutOfRangeException(nameof(operationFamily), operationFamily, null)
        };

    private static Instruction InstructionFor(ShiftRotateOperation operation) =>
        operation switch
        {
            ShiftRotateOperation.Asl => InstrAsl,
            ShiftRotateOperation.Asr => InstrAsr,
            ShiftRotateOperation.Lsl => InstrLsl,
            ShiftRotateOperation.Lsr => InstrLsr,
            ShiftRotateOperation.Rol => InstrRol,
            ShiftRotateOperation.Ror => InstrRor,
            ShiftRotateOperation.Roxl => InstrRoxl,
            ShiftRotateOperation.Roxr => InstrRoxr,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static string GetMnemonic(ShiftRotateOperation operation) =>
        operation switch
        {
            ShiftRotateOperation.Asl => "ASL",
            ShiftRotateOperation.Asr => "ASR",
            ShiftRotateOperation.Lsl => "LSL",
            ShiftRotateOperation.Lsr => "LSR",
            ShiftRotateOperation.Rol => "ROL",
            ShiftRotateOperation.Ror => "ROR",
            ShiftRotateOperation.Roxl => "ROXL",
            ShiftRotateOperation.Roxr => "ROXR",
            _ => string.Empty
        };

    private static OperandSize? DecodeRegisterSize(ushort opcode) =>
        ((opcode >> 6) & 0x03) switch
        {
            0 => OperandSize.Byte,
            1 => OperandSize.Word,
            2 => OperandSize.Long,
            _ => null
        };

    private static uint ReadDataRegister(Cpu cpu, int registerIndex, OperandSize size)
    {
        var value = cpu.Registers.GetDataRegister(registerIndex);
        return size switch
        {
            OperandSize.Byte => value & 0x0000_00FF,
            OperandSize.Word => value & 0x0000_FFFF,
            _ => value
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

    private static uint OperandMask(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => 0x0000_00FF,
            OperandSize.Word => 0x0000_FFFF,
            _ => 0xFFFF_FFFF
        };

    private static uint OperandSignBit(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => 0x0000_0080,
            OperandSize.Word => 0x0000_8000,
            _ => 0x8000_0000
        };

    private static bool IsMemoryForm(ushort opcode) =>
        (opcode & 0x00C0) == 0x00C0;

    private readonly record struct ShiftRotateOutcome(uint Result, bool Carry, bool Overflow, bool UpdateExtend, bool ExtendValue);

    private enum ShiftRotateOperation : byte
    {
        Asr,
        Asl,
        Lsr,
        Lsl,
        Roxr,
        Roxl,
        Ror,
        Rol
    }
}
