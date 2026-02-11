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
/// Shared helpers for sized operand access used by multiple instruction families.
/// </summary>
public static class InstructionOperandAccess
{
    /// <summary>
    /// Converts CPU operand size to destination-operand size used by EA resolvers.
    /// </summary>
    public static DestinationOperandSize ToDestinationOperandSize(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => DestinationOperandSize.Byte,
            OperandSize.Word => DestinationOperandSize.Word,
            OperandSize.Long => DestinationOperandSize.Long,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
        };

    /// <summary>
    /// Reads an unsigned value from an effective address at the requested size.
    /// </summary>
    public static uint ReadFromEa(Cpu cpu, EffectiveAddress ea, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => EffectiveAddressByteAccess.ReadByte(cpu, ea),
            OperandSize.Word => EffectiveAddressWordAccess.ReadWord(cpu, ea),
            OperandSize.Long => EffectiveAddressLongAccess.ReadLong(cpu, ea),
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
        };

    /// <summary>
    /// Reads a data register masked to the requested size.
    /// </summary>
    public static uint ReadDataRegister(Cpu cpu, int registerIndex, OperandSize size)
    {
        var registerValue = cpu.Registers.GetDataRegister(registerIndex);
        return size switch
        {
            OperandSize.Byte => registerValue & 0x0000_00FF,
            OperandSize.Word => registerValue & 0x0000_FFFF,
            OperandSize.Long => registerValue,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
        };
    }

    /// <summary>
    /// Writes a value into a data register at the requested size while preserving unaffected bits.
    /// </summary>
    public static void WriteDataRegister(Cpu cpu, int registerIndex, OperandSize size, uint value)
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
}
