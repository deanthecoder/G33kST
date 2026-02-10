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
/// Decimal adjust instruction decode and execution helpers (NBCD/SBCD/ABCD).
/// </summary>
public static class DecimalInstructions
{
    private static readonly Instruction InstrNbcd = new("NBCD <ea>", ExecuteNegateBcd);
    private static readonly Instruction InstrSbcdDataRegisters = new("SBCD Dn,Dn", ExecuteSubtractBcdDataRegisters);
    private static readonly Instruction InstrSbcdMemoryPredecrement = new("SBCD -(An),-(An)", ExecuteSubtractBcdMemoryPredecrement);
    private static readonly Instruction InstrAbcdDataRegisters = new("ABCD Dn,Dn", ExecuteAddBcdDataRegisters);
    private static readonly Instruction InstrAbcdMemoryPredecrement = new("ABCD -(An),-(An)", ExecuteAddBcdMemoryPredecrement);

    /// <summary>
    /// Decodes <c>NBCD</c> opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeNbcd(ushort opcode)
    {
        // 0100 1000 00 mmm rrr = NBCD <ea>.
        if ((opcode & 0xFFC0) != 0x4800)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        return SupportsByteReadWrite(destination) ? InstrNbcd : null;
    }

    /// <summary>
    /// Decodes <c>SBCD</c> opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeSbcd(ushort opcode)
    {
        // 1000 ddd1 0000 mrrr = SBCD forms.
        if ((opcode & 0xF1F0) != 0x8100)
            return null;

        return (opcode & 0x0008) == 0
            ? InstrSbcdDataRegisters
            : InstrSbcdMemoryPredecrement;
    }

    /// <summary>
    /// Decodes <c>ABCD</c> opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeAbcd(ushort opcode)
    {
        // 1100 ddd1 0000 mrrr = ABCD forms.
        if ((opcode & 0xF1F0) != 0xC100)
            return null;

        return (opcode & 0x0008) == 0
            ? InstrAbcdDataRegisters
            : InstrAbcdMemoryPredecrement;
    }

    /// <summary>
    /// Executes <c>NBCD &lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteNegateBcd(Cpu cpu, ushort opcode)
    {
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, DestinationOperandSize.Byte, "NBCD");
        var source = (byte)DestinationOperandAccess.ReadUnsigned(cpu, destination, DestinationOperandSize.Byte);
        var extendIn = cpu.Registers.ExtendFlag ? 1 : 0;
        var resultState = SubtractPackedBcd(0, source, extendIn);

        DestinationOperandAccess.WriteUnsigned(cpu, destination, DestinationOperandSize.Byte, resultState.Result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        ApplyDecimalFlags(cpu.Registers, resultState);
    }

    /// <summary>
    /// Executes <c>SBCD Dn,Dn</c>.
    /// </summary>
    private static void ExecuteSubtractBcdDataRegisters(Cpu cpu, ushort opcode)
    {
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var sourceRegisterIndex = opcode & 0x07;
        var source = (byte)cpu.Registers.GetDataRegister(sourceRegisterIndex);
        var destination = (byte)cpu.Registers.GetDataRegister(destinationRegisterIndex);
        var extendIn = cpu.Registers.ExtendFlag ? 1 : 0;
        var resultState = SubtractPackedBcd(destination, source, extendIn);

        var destinationValue = cpu.Registers.GetDataRegister(destinationRegisterIndex);
        cpu.Registers.SetDataRegister(destinationRegisterIndex, (destinationValue & 0xFFFF_FF00) | resultState.Result);
        ApplyDecimalFlags(cpu.Registers, resultState);
    }

    /// <summary>
    /// Executes <c>SBCD -(An),-(An)</c>.
    /// </summary>
    private static void ExecuteSubtractBcdMemoryPredecrement(Cpu cpu, ushort opcode)
    {
        var destinationRegisterIndex = (byte)((opcode >> 9) & 0x07);
        var sourceRegisterIndex = (byte)(opcode & 0x07);
        var sourceAddress = PredecrementByteAddress(cpu, sourceRegisterIndex);
        var destinationAddress = PredecrementByteAddress(cpu, destinationRegisterIndex);
        var source = cpu.Read8(sourceAddress);
        var destination = cpu.Read8(destinationAddress);
        var extendIn = cpu.Registers.ExtendFlag ? 1 : 0;
        var resultState = SubtractPackedBcd(destination, source, extendIn);

        cpu.Write8(destinationAddress, resultState.Result);
        ApplyDecimalFlags(cpu.Registers, resultState);
    }

    /// <summary>
    /// Executes <c>ABCD Dn,Dn</c>.
    /// </summary>
    private static void ExecuteAddBcdDataRegisters(Cpu cpu, ushort opcode)
    {
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var sourceRegisterIndex = opcode & 0x07;
        var source = (byte)cpu.Registers.GetDataRegister(sourceRegisterIndex);
        var destination = (byte)cpu.Registers.GetDataRegister(destinationRegisterIndex);
        var extendIn = cpu.Registers.ExtendFlag ? 1 : 0;
        var resultState = AddPackedBcd(destination, source, extendIn);

        var destinationValue = cpu.Registers.GetDataRegister(destinationRegisterIndex);
        cpu.Registers.SetDataRegister(destinationRegisterIndex, (destinationValue & 0xFFFF_FF00) | resultState.Result);
        ApplyDecimalFlags(cpu.Registers, resultState);
    }

    /// <summary>
    /// Executes <c>ABCD -(An),-(An)</c>.
    /// </summary>
    private static void ExecuteAddBcdMemoryPredecrement(Cpu cpu, ushort opcode)
    {
        var destinationRegisterIndex = (byte)((opcode >> 9) & 0x07);
        var sourceRegisterIndex = (byte)(opcode & 0x07);
        var sourceAddress = PredecrementByteAddress(cpu, sourceRegisterIndex);
        var destinationAddress = PredecrementByteAddress(cpu, destinationRegisterIndex);
        var source = cpu.Read8(sourceAddress);
        var destination = cpu.Read8(destinationAddress);
        var extendIn = cpu.Registers.ExtendFlag ? 1 : 0;
        var resultState = AddPackedBcd(destination, source, extendIn);

        cpu.Write8(destinationAddress, resultState.Result);
        ApplyDecimalFlags(cpu.Registers, resultState);
    }

    /// <summary>
    /// Applies decimal-operation flags.
    /// X and C mirror decimal carry/borrow.
    /// Z is sticky across chained precision operations.
    /// </summary>
    private static void ApplyDecimalFlags(Registers registers, BcdResult result)
    {
        registers.NegativeFlag = (result.Result & 0x80) != 0;
        if (result.Result != 0)
            registers.ZeroFlag = false;

        registers.OverflowFlag = result.Overflow;
        registers.CarryFlag = result.Carry;
        registers.ExtendFlag = result.Carry;
    }

    private static BcdResult AddPackedBcd(byte destination, byte source, int extendIn)
    {
        var lowNibbleSum = (destination & 0x0F) + (source & 0x0F) + extendIn;
        var binaryResult = destination + source + extendIn;
        var result = binaryResult;
        if (lowNibbleSum > 9)
            result += 6;

        // Carry/extend follows the unadjusted packed-byte sum.
        var carry = binaryResult > 0x99;
        if (carry)
            result -= 0xA0;

        result &= 0xFF;
        var overflow = ((~binaryResult & result) & 0x80) != 0;
        return new BcdResult((byte)result, carry, overflow);
    }

    private static BcdResult SubtractPackedBcd(byte destination, byte source, int extendIn)
    {
        var lowNibbleDifference = (destination & 0x0F) - (source & 0x0F) - extendIn;
        var binaryResult = destination - source - extendIn;
        var result = binaryResult;
        if (lowNibbleDifference < 0)
            result -= 0x06;

        var hasHighBorrow = binaryResult < 0;
        if (hasHighBorrow)
            result -= 0x60;

        // Carry/extend follows decimal borrow semantics.
        var carry = binaryResult < (lowNibbleDifference < 0 ? 6 : 0);
        result &= 0xFF;
        var overflow = ((binaryResult & ~result) & 0x80) != 0;
        return new BcdResult((byte)result, carry, overflow);
    }

    private static uint PredecrementByteAddress(Cpu cpu, byte registerIndex)
    {
        var currentAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var newAddress = currentAddress - EffectiveAddressMath.ByteAddressStep(registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, newAddress);
        return EffectiveAddressMath.NormalizeAddress24(newAddress);
    }

    private static bool SupportsByteReadWrite(EffectiveAddress ea) =>
        EffectiveAddressByteAccess.SupportsByteRead(ea) && EffectiveAddressByteAccess.SupportsByteWrite(ea);

    private readonly record struct BcdResult(byte Result, bool Carry, bool Overflow);
}
