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
    /// Executes <c>MOVE.B (An),Dn</c> for address-register indirect source and data-register destination.
    /// </summary>
    public static void ExecuteMoveByteAddressToData(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeSource(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceAddress = cpu.Registers.GetAddressRegister(source.Register);
        var sourceValue = cpu.Read8(sourceAddress);

        WriteByteToDataRegister(cpu.Registers, destination.Register, sourceValue);
        SetMoveByteFlags(cpu.Registers, sourceValue);
    }

    /// <summary>
    /// Executes <c>MOVE.B Dn,Dn</c> for data-register direct source and destination.
    /// </summary>
    public static void ExecuteMoveByteDataToData(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeSource(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = (byte)cpu.Registers.GetDataRegister(source.Register);

        WriteByteToDataRegister(cpu.Registers, destination.Register, sourceValue);
        SetMoveByteFlags(cpu.Registers, sourceValue);
    }

    /// <summary>
    /// Executes <c>MOVE.B Dn,(An)</c> for data-register source and address-register indirect destination.
    /// </summary>
    public static void ExecuteMoveByteDataToAddress(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeSource(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = (byte)cpu.Registers.GetDataRegister(source.Register);
        var destinationAddress = cpu.Registers.GetAddressRegister(destination.Register);

        cpu.Write8(destinationAddress, sourceValue);
        SetMoveByteFlags(cpu.Registers, sourceValue);
    }

    private static void WriteByteToDataRegister(Registers registers, int destinationRegister, byte value)
    {
        // Byte-sized move only replaces the low byte of the destination data register.
        var destinationValue = registers.GetDataRegister(destinationRegister);
        registers.SetDataRegister(destinationRegister, (destinationValue & 0xFFFFFF00u) | value);
    }

    private static void SetMoveByteFlags(Registers registers, byte value)
    {
        registers.NegativeFlag = (value & 0x80) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }
}
