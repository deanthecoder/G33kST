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
/// MOVE-family instruction decode and execution helpers.
/// </summary>
public static class MoveInstructions
{
    /// <summary>
    /// Executes <c>MOVEQ #&lt;imm8&gt;,Dn</c>, sign-extending the 8-bit literal to 32-bit.
    /// </summary>
    public static void ExecuteMoveQuick(Cpu cpu, ushort opcode)
    {
        var registerIndex = (opcode >> 9) & 0x7;
        var immediate = (sbyte)(opcode & 0xFF);
        var value = (uint)immediate;

        cpu.Registers.SetDataRegister(registerIndex, value);
        SetMoveLongFlags(cpu.Registers, value);
    }

    /// <summary>
    /// Executes <c>MOVE.B &lt;ea&gt;,&lt;ea&gt;</c> for supported byte-sized EA combinations.
    /// ea = effective address.
    /// </summary>
    public static void ExecuteMoveByte(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeSource(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = EffectiveAddressByteAccess.ReadByte(cpu, source);
        EffectiveAddressByteAccess.WriteByte(cpu, destination, sourceValue);
        SetMoveByteFlags(cpu.Registers, sourceValue);
    }

    /// <summary>
    /// Executes <c>MOVE.W &lt;ea&gt;,&lt;ea&gt;</c> for supported word-sized EA combinations.
    /// ea = effective address.
    /// </summary>
    public static void ExecuteMoveWord(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeSource(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = EffectiveAddressWordAccess.ReadWord(cpu, source);
        EffectiveAddressWordAccess.WriteWord(cpu, destination, sourceValue);
        SetMoveWordFlags(cpu.Registers, sourceValue);
    }

    /// <summary>
    /// Executes <c>MOVE.L &lt;ea&gt;,&lt;ea&gt;</c> for supported long-sized EA combinations.
    /// ea = effective address.
    /// </summary>
    public static void ExecuteMoveLong(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeSource(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = EffectiveAddressLongAccess.ReadLong(cpu, source);
        EffectiveAddressLongAccess.WriteLong(cpu, destination, sourceValue);
        SetMoveLongFlags(cpu.Registers, sourceValue);
    }

    private static void SetMoveByteFlags(Registers registers, byte value)
    {
        registers.NegativeFlag = (value & 0x80) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    private static void SetMoveWordFlags(Registers registers, ushort value)
    {
        registers.NegativeFlag = (value & 0x8000) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    private static void SetMoveLongFlags(Registers registers, uint value)
    {
        registers.NegativeFlag = (value & 0x80000000) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }
}
