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
    /// Executes <c>MOVE.B Dn,Dn</c> for data-register direct source and destination.
    /// </summary>
    public static void ExecuteMoveByteDataToData(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeSource(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = (byte)cpu.Registers.GetDataRegister(source.Register);

        // Byte-sized move only replaces the low byte of the destination data register.
        var destinationValue = cpu.Registers.GetDataRegister(destination.Register);
        cpu.Registers.SetDataRegister(destination.Register, (destinationValue & 0xFFFFFF00u) | sourceValue);

        cpu.Registers.NegativeFlag = (sourceValue & 0x80) != 0;
        cpu.Registers.ZeroFlag = sourceValue == 0;
        cpu.Registers.OverflowFlag = false;
        cpu.Registers.CarryFlag = false;
    }
}
