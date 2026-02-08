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
/// Compare-family instruction decode and execution helpers.
/// </summary>
public static class CompareInstructions
{
    private static readonly Instruction InstrCmpByte = new("CMP.B <ea>,Dn", ExecuteCompareByte);
    private static readonly Instruction InstrCmpWord = new("CMP.W <ea>,Dn", ExecuteCompareWord);
    private static readonly Instruction InstrCmpLong = new("CMP.L <ea>,Dn", ExecuteCompareLong);
    private static readonly Instruction InstrCmpAddressWord = new("CMPA.W <ea>,An", ExecuteCompareAddressWord);
    private static readonly Instruction InstrCmpAddressLong = new("CMPA.L <ea>,An", ExecuteCompareAddressLong);
    private static readonly Instruction InstrCmpMemoryByte = new("CMPM.B (Ay)+,(Ax)+", ExecuteCompareMemoryByte);
    private static readonly Instruction InstrCmpMemoryWord = new("CMPM.W (Ay)+,(Ax)+", ExecuteCompareMemoryWord);
    private static readonly Instruction InstrCmpMemoryLong = new("CMPM.L (Ay)+,(Ax)+", ExecuteCompareMemoryLong);

    /// <summary>
    /// Decodes compare-family opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 1011 xxx1 ss001 = CMPM.<size> (Ay)+,(Ax)+.
        if ((opcode & 0xF138) == 0xB108)
        {
            var sizeCode = (opcode >> 6) & 0x03;
            if (sizeCode < 3)
            {
                return sizeCode switch
                {
                    0 => InstrCmpMemoryByte,
                    1 => InstrCmpMemoryWord,
                    2 => InstrCmpMemoryLong,
                    _ => null
                };
            }
        }

        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteRead(source) ? InstrCmpByte : null,
            1 => EffectiveAddressWordAccess.SupportsWordRead(source) ? InstrCmpWord : null,
            2 => EffectiveAddressLongAccess.SupportsLongRead(source) ? InstrCmpLong : null,
            3 => EffectiveAddressWordAccess.SupportsWordRead(source) ? InstrCmpAddressWord : null,
            7 => EffectiveAddressLongAccess.SupportsLongRead(source) ? InstrCmpAddressLong : null,
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>CMP.B &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteCompareByte(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = EffectiveAddressByteAccess.ReadByte(cpu, sourceEa);
        var destinationRegister = (opcode >> 9) & 0x07;
        var destination = (byte)cpu.Registers.GetDataRegister(destinationRegister);
        var result = (byte)(destination - source);
        FlagMath.ApplySubtractByte(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMP.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteCompareWord(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = EffectiveAddressWordAccess.ReadWord(cpu, sourceEa);
        var destinationRegister = (opcode >> 9) & 0x07;
        var destination = (ushort)cpu.Registers.GetDataRegister(destinationRegister);
        var result = (ushort)(destination - source);
        FlagMath.ApplySubtractWord(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMP.L &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteCompareLong(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = EffectiveAddressLongAccess.ReadLong(cpu, sourceEa);
        var destinationRegister = (opcode >> 9) & 0x07;
        var destination = cpu.Registers.GetDataRegister(destinationRegister);
        var result = destination - source;
        FlagMath.ApplySubtractLong(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMPA.W &lt;ea&gt;,An</c> using sign-extended 16-bit source.
    /// </summary>
    private static void ExecuteCompareAddressWord(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var sourceWord = EffectiveAddressWordAccess.ReadWord(cpu, sourceEa);
        var source = unchecked((uint)(short)sourceWord);
        var destinationRegister = (opcode >> 9) & 0x07;
        var destination = cpu.Registers.GetAddressRegister(destinationRegister);
        var result = destination - source;
        FlagMath.ApplySubtractLong(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMPA.L &lt;ea&gt;,An</c>.
    /// </summary>
    private static void ExecuteCompareAddressLong(Cpu cpu, ushort opcode)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = EffectiveAddressLongAccess.ReadLong(cpu, sourceEa);
        var destinationRegister = (opcode >> 9) & 0x07;
        var destination = cpu.Registers.GetAddressRegister(destinationRegister);
        var result = destination - source;
        FlagMath.ApplySubtractLong(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMPM.B (Ay)+,(Ax)+</c>.
    /// </summary>
    private static void ExecuteCompareMemoryByte(Cpu cpu, ushort opcode)
    {
        var sourceRegister = (byte)(opcode & 0x07);
        var destinationRegister = (byte)((opcode >> 9) & 0x07);
        var sourceEa = new EffectiveAddress(EffectiveAddressMode.AddressRegisterIndirectPostIncrement, sourceRegister);
        var destinationEa = new EffectiveAddress(EffectiveAddressMode.AddressRegisterIndirectPostIncrement, destinationRegister);
        var source = EffectiveAddressByteAccess.ReadByte(cpu, sourceEa);
        var destination = EffectiveAddressByteAccess.ReadByte(cpu, destinationEa);
        var result = (byte)(destination - source);
        FlagMath.ApplySubtractByte(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMPM.W (Ay)+,(Ax)+</c>.
    /// </summary>
    private static void ExecuteCompareMemoryWord(Cpu cpu, ushort opcode)
    {
        var sourceRegister = (byte)(opcode & 0x07);
        var destinationRegister = (byte)((opcode >> 9) & 0x07);
        var sourceEa = new EffectiveAddress(EffectiveAddressMode.AddressRegisterIndirectPostIncrement, sourceRegister);
        var destinationEa = new EffectiveAddress(EffectiveAddressMode.AddressRegisterIndirectPostIncrement, destinationRegister);
        var source = EffectiveAddressWordAccess.ReadWord(cpu, sourceEa);
        var destination = EffectiveAddressWordAccess.ReadWord(cpu, destinationEa);
        var result = (ushort)(destination - source);
        FlagMath.ApplySubtractWord(cpu.Registers, destination, source, result);
    }

    /// <summary>
    /// Executes <c>CMPM.L (Ay)+,(Ax)+</c>.
    /// </summary>
    private static void ExecuteCompareMemoryLong(Cpu cpu, ushort opcode)
    {
        var sourceRegister = (byte)(opcode & 0x07);
        var destinationRegister = (byte)((opcode >> 9) & 0x07);
        var sourceEa = new EffectiveAddress(EffectiveAddressMode.AddressRegisterIndirectPostIncrement, sourceRegister);
        var destinationEa = new EffectiveAddress(EffectiveAddressMode.AddressRegisterIndirectPostIncrement, destinationRegister);
        var source = EffectiveAddressLongAccess.ReadLong(cpu, sourceEa);
        var destination = EffectiveAddressLongAccess.ReadLong(cpu, destinationEa);
        var result = destination - source;
        FlagMath.ApplySubtractLong(cpu.Registers, destination, source, result);
    }
}
