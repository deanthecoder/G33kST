// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.M68000.Instructions;

/// <summary>
/// Shared 68000 flag math helpers used by instruction implementations.
/// </summary>
public static class FlagMath
{
    /// <summary>
    /// Applies logical-result flags for an 8-bit value (N/Z set from result, V/C cleared).
    /// </summary>
    public static void ApplyLogicalByte(Registers registers, byte value)
    {
        registers.NegativeFlag = (value & 0x80) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    /// <summary>
    /// Applies logical-result flags for a 16-bit value (N/Z set from result, V/C cleared).
    /// </summary>
    public static void ApplyLogicalWord(Registers registers, ushort value)
    {
        registers.NegativeFlag = (value & 0x8000) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    /// <summary>
    /// Applies logical-result flags for a 32-bit value (N/Z set from result, V/C cleared).
    /// </summary>
    public static void ApplyLogicalLong(Registers registers, uint value)
    {
        registers.NegativeFlag = (value & 0x8000_0000) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    /// <summary>
    /// Applies ADD-style flags for an 8-bit addition.
    /// </summary>
    public static void ApplyAddByte(Registers registers, byte destination, byte source, byte result)
    {
        registers.NegativeFlag = (result & 0x80) != 0;
        registers.ZeroFlag = result == 0;
        registers.OverflowFlag = ((~(destination ^ source) & (destination ^ result)) & 0x80) != 0;
        registers.CarryFlag = destination + source > 0xFF;
    }

    /// <summary>
    /// Applies ADD-style flags for a 16-bit addition.
    /// </summary>
    public static void ApplyAddWord(Registers registers, ushort destination, ushort source, ushort result)
    {
        registers.NegativeFlag = (result & 0x8000) != 0;
        registers.ZeroFlag = result == 0;
        registers.OverflowFlag = ((~(destination ^ source) & (destination ^ result)) & 0x8000) != 0;
        registers.CarryFlag = destination + source > 0xFFFF;
    }

    /// <summary>
    /// Applies ADD-style flags for a 32-bit addition.
    /// </summary>
    public static void ApplyAddLong(Registers registers, uint destination, uint source, uint result)
    {
        registers.NegativeFlag = (result & 0x8000_0000) != 0;
        registers.ZeroFlag = result == 0;
        registers.OverflowFlag = ((~(destination ^ source) & (destination ^ result)) & 0x8000_0000) != 0;
        registers.CarryFlag = ((ulong)destination + source) > 0xFFFF_FFFF;
    }

    /// <summary>
    /// Applies flags for CLR-style operations (N=0, Z=1, V=0, C=0).
    /// </summary>
    public static void ApplyClear(Registers registers)
    {
        registers.NegativeFlag = false;
        registers.ZeroFlag = true;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    /// <summary>
    /// Applies CMP/SUB-style flags for an 8-bit subtraction.
    /// </summary>
    public static void ApplySubtractByte(Registers registers, byte destination, byte source, byte result)
    {
        registers.NegativeFlag = (result & 0x80) != 0;
        registers.ZeroFlag = result == 0;
        registers.OverflowFlag = ((destination ^ source) & (destination ^ result) & 0x80) != 0;
        registers.CarryFlag = source > destination;
    }

    /// <summary>
    /// Applies CMP/SUB-style flags for a 16-bit subtraction.
    /// </summary>
    public static void ApplySubtractWord(Registers registers, ushort destination, ushort source, ushort result)
    {
        registers.NegativeFlag = (result & 0x8000) != 0;
        registers.ZeroFlag = result == 0;
        registers.OverflowFlag = ((destination ^ source) & (destination ^ result) & 0x8000) != 0;
        registers.CarryFlag = source > destination;
    }

    /// <summary>
    /// Applies CMP/SUB-style flags for a 32-bit subtraction.
    /// </summary>
    public static void ApplySubtractLong(Registers registers, uint destination, uint source, uint result)
    {
        registers.NegativeFlag = (result & 0x8000_0000) != 0;
        registers.ZeroFlag = result == 0;
        registers.OverflowFlag = ((destination ^ source) & (destination ^ result) & 0x8000_0000) != 0;
        registers.CarryFlag = source > destination;
    }

    /// <summary>
    /// Applies CHK flags (N from signed comparison, Z/V/C cleared, X preserved).
    /// </summary>
    public static void ApplyCheck(Registers registers, bool negative)
    {
        registers.NegativeFlag = negative;
        registers.ZeroFlag = false;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }
}
