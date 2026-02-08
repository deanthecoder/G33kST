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

    private static void SetMoveByteFlags(Registers registers, byte value)
    {
        registers.NegativeFlag = (value & 0x80) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }
}
