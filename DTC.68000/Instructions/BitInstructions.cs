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
/// Bit test/change/clear/set instruction decode and execution helpers.
/// </summary>
public static class BitInstructions
{
    private static readonly Instruction InstrBtstDynamic = new("BTST Dn,<ea>", ExecuteBitTestDynamic);
    private static readonly Instruction InstrBchgDynamic = new("BCHG Dn,<ea>", ExecuteBitChangeDynamic);
    private static readonly Instruction InstrBclrDynamic = new("BCLR Dn,<ea>", ExecuteBitClearDynamic);
    private static readonly Instruction InstrBsetDynamic = new("BSET Dn,<ea>", ExecuteBitSetDynamic);
    private static readonly Instruction InstrBtstImmediate = new("BTST #<imm8>,<ea>", ExecuteBitTestImmediate);
    private static readonly Instruction InstrBchgImmediate = new("BCHG #<imm8>,<ea>", ExecuteBitChangeImmediate);
    private static readonly Instruction InstrBclrImmediate = new("BCLR #<imm8>,<ea>", ExecuteBitClearImmediate);
    private static readonly Instruction InstrBsetImmediate = new("BSET #<imm8>,<ea>", ExecuteBitSetImmediate);

    /// <summary>
    /// Decodes bit-operation opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0000 ddd1 oo mmm rrr = dynamic bit operations (bit number from Dn).
        if ((opcode & 0xF100) == 0x0100)
            return DecodeBitOperation(opcode, isImmediateBitNumber: false);

        // 0000 1000 oo mmm rrr = immediate bit operations (bit number in extension word).
        return (opcode & 0xFF00) != 0x0800 ? null : DecodeBitOperation(opcode, isImmediateBitNumber: true);
    }

    private static Instruction DecodeBitOperation(ushort opcode, bool isImmediateBitNumber)
    {
        var operationCode = (opcode >> 6) & 0x03;
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        return operationCode switch
        {
            0 => SupportsBitTestDestination(destination) ? isImmediateBitNumber ? InstrBtstImmediate : InstrBtstDynamic : null,
            1 => SupportsBitModifyDestination(destination) ? isImmediateBitNumber ? InstrBchgImmediate : InstrBchgDynamic : null,
            2 => SupportsBitModifyDestination(destination) ? isImmediateBitNumber ? InstrBclrImmediate : InstrBclrDynamic : null,
            3 => SupportsBitModifyDestination(destination) ? isImmediateBitNumber ? InstrBsetImmediate : InstrBsetDynamic : null,
            _ => null
        };
    }

    private static bool SupportsBitTestDestination(EffectiveAddress destination) =>
        destination.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterDirect => false,
            _ => EffectiveAddressByteAccess.SupportsByteRead(destination)
        };

    private static bool SupportsBitModifyDestination(EffectiveAddress destination) =>
        destination.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterDirect => false,
            _ => EffectiveAddressByteAccess.SupportsByteWrite(destination)
        };

    /// <summary>
    /// Executes <c>BTST Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteBitTestDynamic(Cpu cpu, ushort opcode)
    {
        var bitRegister = (opcode >> 9) & 0x07;
        var bitNumber = cpu.Registers.GetDataRegister(bitRegister);
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        ApplyBitTest(cpu, destination, (int)bitNumber);
        cpu.InternalWait(InstructionTiming.GetBitOperationCycles(modifiesDestination: false, immediateBitNumber: false, destination));
    }

    /// <summary>
    /// Executes <c>BTST #&lt;imm8&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteBitTestImmediate(Cpu cpu, ushort opcode)
    {
        var bitNumber = (byte)cpu.FetchPcWord();
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        ApplyBitTest(cpu, destination, bitNumber);
        cpu.InternalWait(InstructionTiming.GetBitOperationCycles(modifiesDestination: false, immediateBitNumber: true, destination));
    }

    /// <summary>
    /// Executes <c>BCHG Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteBitChangeDynamic(Cpu cpu, ushort opcode)
    {
        var bitRegister = (opcode >> 9) & 0x07;
        var bitNumber = cpu.Registers.GetDataRegister(bitRegister);
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        ApplyBitModify(cpu, destination, (int)bitNumber, BitOperation.Change);
        cpu.InternalWait(InstructionTiming.GetBitOperationCycles(modifiesDestination: true, immediateBitNumber: false, destination));
    }

    /// <summary>
    /// Executes <c>BCHG #&lt;imm8&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteBitChangeImmediate(Cpu cpu, ushort opcode)
    {
        var bitNumber = (byte)cpu.FetchPcWord();
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        ApplyBitModify(cpu, destination, bitNumber, BitOperation.Change);
        cpu.InternalWait(InstructionTiming.GetBitOperationCycles(modifiesDestination: true, immediateBitNumber: true, destination));
    }

    /// <summary>
    /// Executes <c>BCLR Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteBitClearDynamic(Cpu cpu, ushort opcode)
    {
        var bitRegister = (opcode >> 9) & 0x07;
        var bitNumber = cpu.Registers.GetDataRegister(bitRegister);
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        ApplyBitModify(cpu, destination, (int)bitNumber, BitOperation.Clear);
        cpu.InternalWait(InstructionTiming.GetBitOperationCycles(modifiesDestination: true, immediateBitNumber: false, destination));
    }

    /// <summary>
    /// Executes <c>BCLR #&lt;imm8&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteBitClearImmediate(Cpu cpu, ushort opcode)
    {
        var bitNumber = (byte)cpu.FetchPcWord();
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        ApplyBitModify(cpu, destination, bitNumber, BitOperation.Clear);
        cpu.InternalWait(InstructionTiming.GetBitOperationCycles(modifiesDestination: true, immediateBitNumber: true, destination));
    }

    /// <summary>
    /// Executes <c>BSET Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteBitSetDynamic(Cpu cpu, ushort opcode)
    {
        var bitRegister = (opcode >> 9) & 0x07;
        var bitNumber = cpu.Registers.GetDataRegister(bitRegister);
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        ApplyBitModify(cpu, destination, (int)bitNumber, BitOperation.Set);
        cpu.InternalWait(InstructionTiming.GetBitOperationCycles(modifiesDestination: true, immediateBitNumber: false, destination));
    }

    /// <summary>
    /// Executes <c>BSET #&lt;imm8&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteBitSetImmediate(Cpu cpu, ushort opcode)
    {
        var bitNumber = (byte)cpu.FetchPcWord();
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        ApplyBitModify(cpu, destination, bitNumber, BitOperation.Set);
        cpu.InternalWait(InstructionTiming.GetBitOperationCycles(modifiesDestination: true, immediateBitNumber: true, destination));
    }

    private static void ApplyBitTest(Cpu cpu, EffectiveAddress destination, int bitNumber)
    {
        if (destination.Mode == EffectiveAddressMode.DataRegisterDirect)
        {
            var bitIndex = bitNumber & 31;
            var value = cpu.Registers.GetDataRegister(destination.Register);
            var mask = 1u << bitIndex;
            cpu.Registers.ZeroFlag = (value & mask) == 0;
            return;
        }

        var bit = bitNumber & 7;
        var value8 = EffectiveAddressByteAccess.ReadByte(cpu, destination);
        cpu.Registers.ZeroFlag = (value8 & (1 << bit)) == 0;
    }

    private static void ApplyBitModify(Cpu cpu, EffectiveAddress destination, int bitNumber, BitOperation operation)
    {
        if (destination.Mode == EffectiveAddressMode.DataRegisterDirect)
        {
            var bitIndex = bitNumber & 31;
            var registerValue = cpu.Registers.GetDataRegister(destination.Register);
            var mask = 1u << bitIndex;
            cpu.Registers.ZeroFlag = (registerValue & mask) == 0;
            registerValue = operation switch
            {
                BitOperation.Change => registerValue ^ mask,
                BitOperation.Clear => registerValue & ~mask,
                BitOperation.Set => registerValue | mask,
                _ => registerValue
            };
            cpu.Registers.SetDataRegister(destination.Register, registerValue);
            return;
        }

        var bit = bitNumber & 7;
        var mask8 = (byte)(1 << bit);
        var address = ResolveMemoryAddressForByteModify(cpu, destination);
        var value8 = cpu.Read8(address);
        cpu.Registers.ZeroFlag = (value8 & mask8) == 0;
        value8 = operation switch
        {
            BitOperation.Change => (byte)(value8 ^ mask8),
            BitOperation.Clear => (byte)(value8 & ~mask8),
            BitOperation.Set => (byte)(value8 | mask8),
            _ => value8
        };
        cpu.Write8(address, value8);
    }

    private static uint ResolveMemoryAddressForByteModify(Cpu cpu, EffectiveAddress destination) =>
        destination.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => NormalizeAddress24(cpu.Registers.GetAddressRegister(destination.Register)),
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => ResolvePostIncrement(cpu, destination.Register),
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => ResolvePreDecrement(cpu, destination.Register),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => ResolveDisplacement(cpu, destination.Register),
            EffectiveAddressMode.AddressRegisterIndirectIndex => ResolveIndex(cpu, destination.Register),
            EffectiveAddressMode.Other when destination.Register == 0 => ResolveAbsoluteShort(cpu),
            EffectiveAddressMode.Other when destination.Register == 1 => ResolveAbsoluteLong(cpu),
            _ => throw new NotSupportedException($"Bit modify destination EA not supported: mode {(byte)destination.Mode}, reg {destination.Register}.")
        };

    private static uint ResolvePostIncrement(Cpu cpu, byte registerIndex)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, address + EffectiveAddressMath.ByteAddressStep(registerIndex));
        return NormalizeAddress24(address);
    }

    private static uint ResolvePreDecrement(Cpu cpu, byte registerIndex)
    {
        var newAddress = cpu.Registers.GetAddressRegister(registerIndex) - EffectiveAddressMath.ByteAddressStep(registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, newAddress);
        return NormalizeAddress24(newAddress);
    }

    private static uint ResolveDisplacement(Cpu cpu, byte registerIndex)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return NormalizeAddress24(EffectiveAddressMath.AddDisplacement(baseAddress, displacement));
    }

    private static uint ResolveIndex(Cpu cpu, byte registerIndex)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return NormalizeAddress24(EffectiveAddressMath.AddIndex(cpu, baseAddress, extension));
    }

    private static uint ResolveAbsoluteShort(Cpu cpu)
    {
        var address = EffectiveAddressMath.ReadAbsoluteShortAddress(cpu);
        return NormalizeAddress24(address);
    }

    private static uint ResolveAbsoluteLong(Cpu cpu)
    {
        var address = EffectiveAddressMath.ReadAbsoluteLongAddress(cpu);
        return NormalizeAddress24(address);
    }

    private static uint NormalizeAddress24(uint address) =>
        EffectiveAddressMath.NormalizeAddress24(address);

    private enum BitOperation : byte
    {
        Change = 1,
        Clear = 2,
        Set = 3
    }
}
