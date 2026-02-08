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
/// Logical unary instruction decode and execution helpers.
/// </summary>
public static class LogicalInstructions
{
    private static readonly Instruction InstrClrByte = new("CLR.B <ea>", ExecuteClearByte);
    private static readonly Instruction InstrClrWord = new("CLR.W <ea>", ExecuteClearWord);
    private static readonly Instruction InstrClrLong = new("CLR.L <ea>", ExecuteClearLong);
    private static readonly Instruction InstrNegByte = new("NEG.B <ea>", ExecuteNegByte);
    private static readonly Instruction InstrNegWord = new("NEG.W <ea>", ExecuteNegWord);
    private static readonly Instruction InstrNegLong = new("NEG.L <ea>", ExecuteNegLong);
    private static readonly Instruction InstrNegxByte = new("NEGX.B <ea>", ExecuteNegxByte);
    private static readonly Instruction InstrNegxWord = new("NEGX.W <ea>", ExecuteNegxWord);
    private static readonly Instruction InstrNegxLong = new("NEGX.L <ea>", ExecuteNegxLong);
    private static readonly Instruction InstrNotByte = new("NOT.B <ea>", ExecuteNotByte);
    private static readonly Instruction InstrNotWord = new("NOT.W <ea>", ExecuteNotWord);
    private static readonly Instruction InstrNotLong = new("NOT.L <ea>", ExecuteNotLong);
    private static readonly Instruction InstrTstByte = new("TST.B <ea>", ExecuteTestByte);
    private static readonly Instruction InstrTstWord = new("TST.W <ea>", ExecuteTestWord);
    private static readonly Instruction InstrTstLong = new("TST.L <ea>", ExecuteTestLong);

    /// <summary>
    /// Decodes logical-unary opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var size = (byte)((opcode >> 6) & 0x03);
        if (size > 2)
            return null;

        return (opcode & 0xFF00) switch
        {
            0x4000 => DecodeNegx(size, ea),
            0x4200 => DecodeClear(size, ea),
            0x4400 => DecodeNeg(size, ea),
            0x4600 => DecodeNot(size, ea),
            0x4A00 => DecodeTest(size, ea),
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>TST.B &lt;ea&gt;</c> by updating NZVC from a byte read.
    /// </summary>
    private static void ExecuteTestByte(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var value = EffectiveAddressByteAccess.ReadByte(cpu, ea);
        SetLogicalByteFlags(cpu.Registers, value);
    }

    /// <summary>
    /// Executes <c>TST.W &lt;ea&gt;</c> by updating NZVC from a word read.
    /// </summary>
    private static void ExecuteTestWord(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var value = EffectiveAddressWordAccess.ReadWord(cpu, ea);
        SetLogicalWordFlags(cpu.Registers, value);
    }

    /// <summary>
    /// Executes <c>TST.L &lt;ea&gt;</c> by updating NZVC from a long read.
    /// </summary>
    private static void ExecuteTestLong(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var value = EffectiveAddressLongAccess.ReadLong(cpu, ea);
        SetLogicalLongFlags(cpu.Registers, value);
    }

    /// <summary>
    /// Executes <c>CLR.B &lt;ea&gt;</c> by writing zero and updating NZVC.
    /// </summary>
    private static void ExecuteClearByte(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        EffectiveAddressByteAccess.WriteByte(cpu, ea, 0);
        SetClearFlags(cpu.Registers);
    }

    /// <summary>
    /// Executes <c>CLR.W &lt;ea&gt;</c> by writing zero and updating NZVC.
    /// </summary>
    private static void ExecuteClearWord(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        EffectiveAddressWordAccess.WriteWord(cpu, ea, 0);
        SetClearFlags(cpu.Registers);
    }

    /// <summary>
    /// Executes <c>CLR.L &lt;ea&gt;</c> by writing zero and updating NZVC.
    /// </summary>
    private static void ExecuteClearLong(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        EffectiveAddressLongAccess.WriteLong(cpu, ea, 0);
        SetClearFlags(cpu.Registers);
    }

    /// <summary>
    /// Executes <c>NOT.B &lt;ea&gt;</c> with single EA resolution (no duplicated extension/side-effects).
    /// </summary>
    private static void ExecuteNotByte(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, ea, OperandSize.Byte);
        var value = ReadByte(cpu, destination);
        var result = (byte)~value;
        WriteByte(cpu, destination, result);
        ApplyPostIncrement(cpu, destination);
        SetLogicalByteFlags(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>NOT.W &lt;ea&gt;</c> with single EA resolution (no duplicated extension/side-effects).
    /// </summary>
    private static void ExecuteNotWord(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, ea, OperandSize.Word);
        var value = ReadWord(cpu, destination);
        var result = (ushort)~value;
        WriteWord(cpu, destination, result);
        ApplyPostIncrement(cpu, destination);
        SetLogicalWordFlags(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>NOT.L &lt;ea&gt;</c> with single EA resolution (no duplicated extension/side-effects).
    /// </summary>
    private static void ExecuteNotLong(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, ea, OperandSize.Long);
        var value = ReadLong(cpu, destination);
        var result = ~value;
        WriteLong(cpu, destination, result);
        ApplyPostIncrement(cpu, destination);
        SetLogicalLongFlags(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>NEG.B &lt;ea&gt;</c> by replacing destination with <c>0 - destination</c>.
    /// </summary>
    private static void ExecuteNegByte(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Byte, useExtend: false);

    /// <summary>
    /// Executes <c>NEG.W &lt;ea&gt;</c> by replacing destination with <c>0 - destination</c>.
    /// </summary>
    private static void ExecuteNegWord(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Word, useExtend: false);

    /// <summary>
    /// Executes <c>NEG.L &lt;ea&gt;</c> by replacing destination with <c>0 - destination</c>.
    /// </summary>
    private static void ExecuteNegLong(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Long, useExtend: false);

    /// <summary>
    /// Executes <c>NEGX.B &lt;ea&gt;</c> by replacing destination with <c>0 - destination - X</c>.
    /// </summary>
    private static void ExecuteNegxByte(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Byte, useExtend: true);

    /// <summary>
    /// Executes <c>NEGX.W &lt;ea&gt;</c> by replacing destination with <c>0 - destination - X</c>.
    /// </summary>
    private static void ExecuteNegxWord(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Word, useExtend: true);

    /// <summary>
    /// Executes <c>NEGX.L &lt;ea&gt;</c> by replacing destination with <c>0 - destination - X</c>.
    /// </summary>
    private static void ExecuteNegxLong(Cpu cpu, ushort opcode) =>
        ExecuteNeg(cpu, opcode, OperandSize.Long, useExtend: true);

    private static Instruction DecodeClear(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrClrByte,
            1 => InstrClrWord,
            2 => InstrClrLong,
            _ => null
        };
    }

    private static Instruction DecodeNeg(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrNegByte,
            1 => InstrNegWord,
            2 => InstrNegLong,
            _ => null
        };
    }

    private static Instruction DecodeNegx(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrNegxByte,
            1 => InstrNegxWord,
            2 => InstrNegxLong,
            _ => null
        };
    }

    private static Instruction DecodeNot(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrNotByte,
            1 => InstrNotWord,
            2 => InstrNotLong,
            _ => null
        };
    }

    private static Instruction DecodeTest(byte size, EffectiveAddress ea)
    {
        if (!SupportsDataAlterableDestination(size, ea))
            return null;

        return size switch
        {
            0 => InstrTstByte,
            1 => InstrTstWord,
            2 => InstrTstLong,
            _ => null
        };
    }

    private static bool SupportsDataAlterableDestination(byte size, EffectiveAddress ea) =>
        size switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteWrite(ea),
            1 => EffectiveAddressWordAccess.SupportsWordWrite(ea),
            2 => EffectiveAddressLongAccess.SupportsLongWrite(ea),
            _ => false
        };

    private static void SetClearFlags(Registers registers)
    {
        registers.NegativeFlag = false;
        registers.ZeroFlag = true;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    private static void SetLogicalByteFlags(Registers registers, byte value)
    {
        registers.NegativeFlag = (value & 0x80) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    private static void SetLogicalWordFlags(Registers registers, ushort value)
    {
        registers.NegativeFlag = (value & 0x8000) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    private static void SetLogicalLongFlags(Registers registers, uint value)
    {
        registers.NegativeFlag = (value & 0x8000_0000) != 0;
        registers.ZeroFlag = value == 0;
        registers.OverflowFlag = false;
        registers.CarryFlag = false;
    }

    private static void ExecuteNeg(Cpu cpu, ushort opcode, OperandSize size, bool useExtend)
    {
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = ResolveDestination(cpu, destinationEa, size);
        var source = ReadUnsigned(cpu, destination, size);
        var extendInput = useExtend && cpu.Registers.ExtendFlag ? 1ul : 0ul;
        var result = (0ul - source - extendInput) & OperandMask(size);

        WriteUnsigned(cpu, destination, size, result);
        ApplyPostIncrement(cpu, destination);
        ApplyNegFlags(cpu.Registers, source, result, size, useExtend, extendInput);
    }

    private static void ApplyNegFlags(Registers registers, ulong source, ulong result, OperandSize size, bool useExtend, ulong extendInput)
    {
        var signBit = OperandSignBit(size);
        var isNegative = (result & signBit) != 0;
        var isZero = result == 0;
        var hasBorrow = source + extendInput != 0;
        var signedMathResult = -SignExtendToLong(source, size) - (long)extendInput;
        var signedResult = SignExtendToLong(result, size);

        registers.NegativeFlag = isNegative;
        if (useExtend)
        {
            if (!isZero)
                registers.ZeroFlag = false;
        }
        else
        {
            registers.ZeroFlag = isZero;
        }

        registers.OverflowFlag = signedResult != signedMathResult;
        registers.CarryFlag = hasBorrow;
        registers.ExtendFlag = hasBorrow;
    }

    private static DestinationOperand ResolveDestination(Cpu cpu, EffectiveAddress ea, OperandSize size) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => DestinationOperand.ForDataRegister(ea.Register),
            EffectiveAddressMode.AddressRegisterIndirect => DestinationOperand.ForMemoryAddress(NormalizeAddress24(cpu.Registers.GetAddressRegister(ea.Register))),
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => ResolvePostIncrement(cpu, ea.Register, size),
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => ResolvePreDecrement(cpu, ea.Register, size),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => ResolveDisplacement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectIndex => ResolveIndex(cpu, ea.Register),
            EffectiveAddressMode.Other when ea.Register == 0 => ResolveAbsoluteShort(cpu),
            EffectiveAddressMode.Other when ea.Register == 1 => ResolveAbsoluteLong(cpu),
            _ => throw new NotSupportedException($"NOT destination EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.")
        };

    private static DestinationOperand ResolvePostIncrement(Cpu cpu, byte registerIndex, OperandSize size)
    {
        var address = NormalizeAddress24(cpu.Registers.GetAddressRegister(registerIndex));
        var step = AddressStep(size, registerIndex);
        return DestinationOperand.ForPostIncrement(address, registerIndex, step);
    }

    private static DestinationOperand ResolvePreDecrement(Cpu cpu, byte registerIndex, OperandSize size)
    {
        var newAddress = cpu.Registers.GetAddressRegister(registerIndex) - AddressStep(size, registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, newAddress);
        return DestinationOperand.ForMemoryAddress(NormalizeAddress24(newAddress));
    }

    private static DestinationOperand ResolveDisplacement(Cpu cpu, byte registerIndex)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = NormalizeAddress24(unchecked((uint)(baseAddress + displacement)));
        return DestinationOperand.ForMemoryAddress(address);
    }

    private static DestinationOperand ResolveIndex(Cpu cpu, byte registerIndex)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = NormalizeAddress24(AddIndex(cpu, baseAddress, extension));
        return DestinationOperand.ForMemoryAddress(address);
    }

    private static DestinationOperand ResolveAbsoluteShort(Cpu cpu)
    {
        var address = NormalizeAddress24((uint)(short)cpu.FetchPcWord());
        return DestinationOperand.ForMemoryAddress(address);
    }

    private static DestinationOperand ResolveAbsoluteLong(Cpu cpu)
    {
        var hi = cpu.FetchPcWord();
        var lo = cpu.FetchPcWord();
        var address = NormalizeAddress24(((uint)hi << 16) | lo);
        return DestinationOperand.ForMemoryAddress(address);
    }

    private static uint AddIndex(Cpu cpu, uint baseAddress, ushort extensionWord)
    {
        var displacement = (sbyte)(extensionWord & 0x00FF);
        var indexValue = ResolveIndexValue(cpu, extensionWord);
        return unchecked((uint)(baseAddress + displacement + indexValue));
    }

    private static int ResolveIndexValue(Cpu cpu, ushort extensionWord)
    {
        var usesAddressRegister = (extensionWord & 0x8000) != 0;
        var registerIndex = (extensionWord >> 12) & 0x07;
        var isLongIndex = (extensionWord & 0x0800) != 0;
        var registerValue = usesAddressRegister
            ? cpu.Registers.GetAddressRegister(registerIndex)
            : cpu.Registers.GetDataRegister(registerIndex);

        return isLongIndex ? unchecked((int)registerValue) : (short)registerValue;
    }

    private static ulong ReadUnsigned(Cpu cpu, DestinationOperand destination, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => ReadByte(cpu, destination),
            OperandSize.Word => ReadWord(cpu, destination),
            OperandSize.Long => ReadLong(cpu, destination),
            _ => 0
        };

    private static void WriteUnsigned(Cpu cpu, DestinationOperand destination, OperandSize size, ulong value)
    {
        switch (size)
        {
            case OperandSize.Byte:
                WriteByte(cpu, destination, (byte)value);
                return;
            case OperandSize.Word:
                WriteWord(cpu, destination, (ushort)value);
                return;
            case OperandSize.Long:
                WriteLong(cpu, destination, (uint)value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }
    }

    private static byte ReadByte(Cpu cpu, DestinationOperand destination)
    {
        if (destination.IsDataRegister)
            return (byte)cpu.Registers.GetDataRegister(destination.RegisterIndex);

        return cpu.Read8(destination.Address);
    }

    private static ushort ReadWord(Cpu cpu, DestinationOperand destination)
    {
        if (destination.IsDataRegister)
            return (ushort)cpu.Registers.GetDataRegister(destination.RegisterIndex);

        return cpu.Read16(destination.Address);
    }

    private static uint ReadLong(Cpu cpu, DestinationOperand destination)
    {
        if (destination.IsDataRegister)
            return cpu.Registers.GetDataRegister(destination.RegisterIndex);

        return cpu.Read32(destination.Address);
    }

    private static void WriteByte(Cpu cpu, DestinationOperand destination, byte value)
    {
        if (destination.IsDataRegister)
        {
            var registerValue = cpu.Registers.GetDataRegister(destination.RegisterIndex);
            cpu.Registers.SetDataRegister(destination.RegisterIndex, (registerValue & 0xFFFF_FF00) | value);
            return;
        }

        cpu.Write8(destination.Address, value);
    }

    private static void WriteWord(Cpu cpu, DestinationOperand destination, ushort value)
    {
        if (destination.IsDataRegister)
        {
            var registerValue = cpu.Registers.GetDataRegister(destination.RegisterIndex);
            cpu.Registers.SetDataRegister(destination.RegisterIndex, (registerValue & 0xFFFF_0000) | value);
            return;
        }

        cpu.Write16(destination.Address, value);
    }

    private static void WriteLong(Cpu cpu, DestinationOperand destination, uint value)
    {
        if (destination.IsDataRegister)
        {
            cpu.Registers.SetDataRegister(destination.RegisterIndex, value);
            return;
        }

        cpu.Write32(destination.Address, value);
    }

    private static void ApplyPostIncrement(Cpu cpu, DestinationOperand destination)
    {
        if (!destination.HasPostIncrement)
            return;

        var currentAddress = cpu.Registers.GetAddressRegister(destination.PostIncrementRegisterIndex);
        cpu.Registers.SetAddressRegister(destination.PostIncrementRegisterIndex, currentAddress + destination.PostIncrement);
    }

    private static uint AddressStep(OperandSize size, byte registerIndex)
    {
        if (size == OperandSize.Byte)
            return registerIndex == 7 ? 2u : 1u;
        if (size == OperandSize.Word)
            return 2;

        return 4;
    }

    private static uint NormalizeAddress24(uint address) =>
        address & 0x00FF_FFFF;

    private static ulong OperandMask(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => 0x0000_00FF,
            OperandSize.Word => 0x0000_FFFF,
            OperandSize.Long => 0xFFFF_FFFF,
            _ => 0
        };

    private static ulong OperandSignBit(OperandSize size) =>
        size switch
        {
            OperandSize.Byte => 0x0000_0080,
            OperandSize.Word => 0x0000_8000,
            OperandSize.Long => 0x8000_0000,
            _ => 0
        };

    private static long SignExtendToLong(ulong value, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => unchecked((sbyte)(byte)value),
            OperandSize.Word => unchecked((short)(ushort)value),
            OperandSize.Long => unchecked((int)(uint)value),
            _ => 0
        };

    private enum OperandSize
    {
        Byte,
        Word,
        Long
    }

    /// <summary>
    /// Represents one resolved unary destination operand.
    /// Stores either a Dn target or an absolute memory address,
    /// with optional deferred post-increment metadata for <c>(An)+</c>.
    /// </summary>
    private readonly struct DestinationOperand
    {
        /// <summary>
        /// Gets whether the destination is a data register (Dn) rather than memory.
        /// </summary>
        public bool IsDataRegister { get; }

        /// <summary>
        /// Gets the destination data-register index when <see cref="IsDataRegister"/> is true.
        /// </summary>
        public byte RegisterIndex { get; }

        /// <summary>
        /// Gets the resolved memory address when <see cref="IsDataRegister"/> is false.
        /// </summary>
        public uint Address { get; }

        /// <summary>
        /// Gets whether this destination must apply deferred post-increment after write-back.
        /// </summary>
        public bool HasPostIncrement { get; }

        /// <summary>
        /// Gets the address-register index to update for deferred <c>(An)+</c> handling.
        /// </summary>
        public byte PostIncrementRegisterIndex { get; }

        /// <summary>
        /// Gets the increment amount to apply for deferred <c>(An)+</c> handling.
        /// </summary>
        public uint PostIncrement { get; }

        private DestinationOperand(bool isDataRegister, byte registerIndex, uint address, bool hasPostIncrement, byte postIncrementRegisterIndex, uint postIncrement)
        {
            IsDataRegister = isDataRegister;
            RegisterIndex = registerIndex;
            Address = address;
            HasPostIncrement = hasPostIncrement;
            PostIncrementRegisterIndex = postIncrementRegisterIndex;
            PostIncrement = postIncrement;
        }

        /// <summary>
        /// Creates a data-register destination wrapper.
        /// </summary>
        public static DestinationOperand ForDataRegister(byte registerIndex) =>
            new(true, registerIndex, 0, false, 0, 0);

        /// <summary>
        /// Creates a direct memory destination wrapper.
        /// </summary>
        public static DestinationOperand ForMemoryAddress(uint address) =>
            new(false, 0, address, false, 0, 0);

        /// <summary>
        /// Creates a memory destination wrapper with deferred post-increment metadata.
        /// </summary>
        public static DestinationOperand ForPostIncrement(uint address, byte registerIndex, uint increment) =>
            new(false, 0, address, true, registerIndex, increment);
    }
}
