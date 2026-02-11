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
/// Shared helpers for AND/OR/EOR style logical instruction execution.
/// </summary>
public static class BitwiseLogicalInstructionHelpers
{
    /// <summary>
    /// Returns true if an effective address supports both read and write for the given size.
    /// </summary>
    public static bool SupportsReadWrite(EffectiveAddress ea, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => EffectiveAddressByteAccess.SupportsByteRead(ea) && EffectiveAddressByteAccess.SupportsByteWrite(ea),
            OperandSize.Word => EffectiveAddressWordAccess.SupportsWordRead(ea) && EffectiveAddressWordAccess.SupportsWordWrite(ea),
            OperandSize.Long => EffectiveAddressLongAccess.SupportsLongRead(ea) && EffectiveAddressLongAccess.SupportsLongWrite(ea),
            _ => false
        };

    /// <summary>
    /// Executes logical form <c>OP.&lt;size&gt; &lt;ea&gt;,Dn</c>.
    /// </summary>
    public static void ExecuteEaToDataRegister(Cpu cpu, ushort opcode, OperandSize size, Func<uint, uint, uint> operation)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = ReadFromEa(cpu, sourceEa, size);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destination = ReadDataRegister(cpu, destinationRegisterIndex, size);
        var result = operation(destination, source);

        WriteDataRegister(cpu, destinationRegisterIndex, size, result);
        ApplyLogicalFlags(cpu.Registers, size, result);
    }

    /// <summary>
    /// Executes logical form <c>OP.&lt;size&gt; Dn,&lt;ea&gt;</c>.
    /// </summary>
    public static void ExecuteDataRegisterToEa(Cpu cpu, ushort opcode, OperandSize size, string instructionName, Func<uint, uint, uint> operation)
    {
        var sourceRegisterIndex = (opcode >> 9) & 0x07;
        var source = ReadDataRegister(cpu, sourceRegisterIndex, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, instructionName);
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = operation(source, destinationValue);

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyLogicalFlags(cpu.Registers, size, result);
    }

    /// <summary>
    /// Executes logical immediate form <c>OPI.&lt;size&gt; #&lt;imm&gt;,&lt;ea&gt;</c>.
    /// </summary>
    public static void ExecuteImmediateToEa(Cpu cpu, ushort opcode, OperandSize size, string instructionName, Func<uint, uint, uint> operation)
    {
        var source = ReadImmediate(cpu, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, instructionName);
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = operation(source, destinationValue);

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
