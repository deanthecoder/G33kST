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
/// Operand size selector for shared destination-operand helpers.
/// </summary>
public enum DestinationOperandSize
{
    Byte,
    Word,
    Long
}

/// <summary>
/// Represents one resolved data-alterable destination.
/// Stores either a Dn target or a memory address, with optional deferred
/// post-increment metadata for <c>(An)+</c>.
/// </summary>
public readonly struct DestinationOperand
{
    /// <summary>
    /// Gets whether the destination is a data register (Dn) rather than memory.
    /// </summary>
    public bool IsDataRegister { get; }

    /// <summary>
    /// Gets destination data-register index when <see cref="IsDataRegister"/> is true.
    /// </summary>
    public byte RegisterIndex { get; }

    /// <summary>
    /// Gets resolved destination memory address when <see cref="IsDataRegister"/> is false.
    /// </summary>
    public uint Address { get; }

    /// <summary>
    /// Gets whether deferred post-increment must be applied after write-back.
    /// </summary>
    public bool HasPostIncrement { get; }

    /// <summary>
    /// Gets post-increment register index for deferred <c>(An)+</c> handling.
    /// </summary>
    public byte PostIncrementRegisterIndex { get; }

    /// <summary>
    /// Gets post-increment amount for deferred <c>(An)+</c> handling.
    /// </summary>
    public uint PostIncrement { get; }

    /// <summary>
    /// Gets the frame-PC adjustment applied if this operand faults on word/long access.
    /// </summary>
    public int FrameProgramCounterAdjust { get; }

    /// <summary>
    /// Gets whether this operand uses program-space function codes when faulting.
    /// </summary>
    public bool IsProgramAccess { get; }

    private DestinationOperand(
        bool isDataRegister,
        byte registerIndex,
        uint address,
        bool hasPostIncrement,
        byte postIncrementRegisterIndex,
        uint postIncrement,
        int frameProgramCounterAdjust,
        bool isProgramAccess)
    {
        IsDataRegister = isDataRegister;
        RegisterIndex = registerIndex;
        Address = address;
        HasPostIncrement = hasPostIncrement;
        PostIncrementRegisterIndex = postIncrementRegisterIndex;
        PostIncrement = postIncrement;
        FrameProgramCounterAdjust = frameProgramCounterAdjust;
        IsProgramAccess = isProgramAccess;
    }

    /// <summary>
    /// Creates a data-register destination wrapper.
    /// </summary>
    public static DestinationOperand ForDataRegister(byte registerIndex) =>
        new(true, registerIndex, 0, false, 0, 0, 0, false);

    /// <summary>
    /// Creates a direct memory destination wrapper.
    /// </summary>
    public static DestinationOperand ForMemoryAddress(uint address, int frameProgramCounterAdjust = 0, bool isProgramAccess = false) =>
        new(false, 0, address, false, 0, 0, frameProgramCounterAdjust, isProgramAccess);

    /// <summary>
    /// Creates a memory destination wrapper with deferred post-increment metadata.
    /// </summary>
    public static DestinationOperand ForPostIncrement(uint address, byte registerIndex, uint increment, int frameProgramCounterAdjust = 0, bool isProgramAccess = false) =>
        new(false, 0, address, true, registerIndex, increment, frameProgramCounterAdjust, isProgramAccess);
}

/// <summary>
/// Shared resolve/read/write helpers for data-alterable destination operands.
/// </summary>
public static class DestinationOperandAccess
{
    /// <summary>
    /// Resolves a data-alterable destination effective address once, including
    /// extension-word reads and pre-decrement side-effects.
    /// </summary>
    public static DestinationOperand ResolveDataAlterable(Cpu cpu, EffectiveAddress ea, DestinationOperandSize size, string instructionName) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => DestinationOperand.ForDataRegister(ea.Register),
            EffectiveAddressMode.AddressRegisterIndirect => DestinationOperand.ForMemoryAddress(cpu.Registers.GetAddressRegister(ea.Register)),
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => ResolvePostIncrement(cpu, ea.Register, size),
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => ResolvePreDecrement(cpu, ea.Register, size),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => ResolveDisplacement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectIndex => ResolveIndex(cpu, ea.Register),
            EffectiveAddressMode.Other when ea.Register == 0 => ResolveAbsoluteShort(cpu),
            EffectiveAddressMode.Other when ea.Register == 1 => ResolveAbsoluteLong(cpu),
            _ => throw new NotSupportedException($"{instructionName} destination EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.")
        };

    /// <summary>
    /// Reads destination value as unsigned byte/word/long.
    /// </summary>
    public static uint ReadUnsigned(Cpu cpu, DestinationOperand destination, DestinationOperandSize size)
    {
        if (destination.IsDataRegister)
        {
            var registerValue = cpu.Registers.GetDataRegister(destination.RegisterIndex);
            return size switch
            {
                DestinationOperandSize.Byte => registerValue & 0x0000_00FF,
                DestinationOperandSize.Word => registerValue & 0x0000_FFFF,
                DestinationOperandSize.Long => registerValue,
                _ => 0
            };
        }

        return size switch
        {
            DestinationOperandSize.Byte => cpu.Read8(destination.Address),
            DestinationOperandSize.Word => ReadWordFromMemory(cpu, destination),
            DestinationOperandSize.Long => ReadLongFromMemory(cpu, destination),
            _ => 0
        };
    }

    /// <summary>
    /// Writes destination value as byte/word/long with Dn upper-bit preservation for byte/word.
    /// </summary>
    public static void WriteUnsigned(Cpu cpu, DestinationOperand destination, DestinationOperandSize size, uint value)
    {
        if (destination.IsDataRegister)
        {
            switch (size)
            {
                case DestinationOperandSize.Byte:
                {
                    var registerValue = cpu.Registers.GetDataRegister(destination.RegisterIndex);
                    cpu.Registers.SetDataRegister(destination.RegisterIndex, (registerValue & 0xFFFF_FF00) | (value & 0xFF));
                    return;
                }
                case DestinationOperandSize.Word:
                {
                    var registerValue = cpu.Registers.GetDataRegister(destination.RegisterIndex);
                    cpu.Registers.SetDataRegister(destination.RegisterIndex, (registerValue & 0xFFFF_0000) | (value & 0xFFFF));
                    return;
                }
                case DestinationOperandSize.Long:
                    cpu.Registers.SetDataRegister(destination.RegisterIndex, value);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size), size, null);
            }
        }

        switch (size)
        {
            case DestinationOperandSize.Byte:
                cpu.Write8(destination.Address, (byte)value);
                return;
            case DestinationOperandSize.Word:
                WriteWordToMemory(cpu, destination, (ushort)value);
                return;
            case DestinationOperandSize.Long:
                WriteLongToMemory(cpu, destination, value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }
    }

    /// <summary>
    /// Applies deferred post-increment side-effect for <c>(An)+</c> destinations.
    /// </summary>
    public static void ApplyPostIncrement(Cpu cpu, DestinationOperand destination)
    {
        if (!destination.HasPostIncrement)
            return;

        var currentAddress = cpu.Registers.GetAddressRegister(destination.PostIncrementRegisterIndex);
        cpu.Registers.SetAddressRegister(destination.PostIncrementRegisterIndex, currentAddress + destination.PostIncrement);
    }

    /// <summary>
    /// Returns address-register increment/decrement step for the given operand size.
    /// Byte uses A7-special handling to preserve stack alignment.
    /// </summary>
    private static uint AddressStep(DestinationOperandSize size, byte registerIndex) =>
        size switch
        {
            DestinationOperandSize.Byte => EffectiveAddressMath.ByteAddressStep(registerIndex),
            DestinationOperandSize.Word => 2,
            _ => 4
        };

    private static DestinationOperand ResolvePostIncrement(Cpu cpu, byte registerIndex, DestinationOperandSize size)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex);
        var step = AddressStep(size, registerIndex);
        if (size == DestinationOperandSize.Word)
        {
            cpu.Registers.SetAddressRegister(registerIndex, address + step);
            return DestinationOperand.ForMemoryAddress(address);
        }

        return DestinationOperand.ForPostIncrement(address, registerIndex, step);
    }

    private static DestinationOperand ResolvePreDecrement(Cpu cpu, byte registerIndex, DestinationOperandSize size)
    {
        var newAddress = cpu.Registers.GetAddressRegister(registerIndex) - AddressStep(size, registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, newAddress);
        var frameProgramCounterAdjust = size == DestinationOperandSize.Word ? 2 : 0;
        return DestinationOperand.ForMemoryAddress(newAddress, frameProgramCounterAdjust: frameProgramCounterAdjust);
    }

    private static DestinationOperand ResolveDisplacement(Cpu cpu, byte registerIndex)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = EffectiveAddressMath.AddDisplacement(baseAddress, displacement);
        return DestinationOperand.ForMemoryAddress(address, frameProgramCounterAdjust: -2);
    }

    private static DestinationOperand ResolveIndex(Cpu cpu, byte registerIndex)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
        return DestinationOperand.ForMemoryAddress(address, frameProgramCounterAdjust: -2);
    }

    private static DestinationOperand ResolveAbsoluteShort(Cpu cpu) =>
        DestinationOperand.ForMemoryAddress(EffectiveAddressMath.ReadAbsoluteShortAddress(cpu));

    private static DestinationOperand ResolveAbsoluteLong(Cpu cpu) =>
        DestinationOperand.ForMemoryAddress(EffectiveAddressMath.ReadAbsoluteLongAddress(cpu));

    private static ushort ReadWordFromMemory(Cpu cpu, DestinationOperand destination)
    {
        var address = EnsureEvenMemoryAddress(destination, ".w", isRead: true);
        var hi = cpu.Read8(address);
        var lo = cpu.Read8(EffectiveAddressMath.NormalizeAddress24(address + 1));
        return (ushort)((hi << 8) | lo);
    }

    private static uint ReadLongFromMemory(Cpu cpu, DestinationOperand destination)
    {
        var address = EnsureEvenMemoryAddress(destination, ".l", isRead: true);
        var b0 = cpu.Read8(address);
        var b1 = cpu.Read8(EffectiveAddressMath.NormalizeAddress24(address + 1));
        var b2 = cpu.Read8(EffectiveAddressMath.NormalizeAddress24(address + 2));
        var b3 = cpu.Read8(EffectiveAddressMath.NormalizeAddress24(address + 3));
        return ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
    }

    private static void WriteWordToMemory(Cpu cpu, DestinationOperand destination, ushort value)
    {
        var address = EnsureEvenMemoryAddress(destination, ".w", isRead: false);
        cpu.Write8(address, (byte)(value >> 8));
        cpu.Write8(EffectiveAddressMath.NormalizeAddress24(address + 1), (byte)(value & 0xFF));
    }

    private static void WriteLongToMemory(Cpu cpu, DestinationOperand destination, uint value)
    {
        var address = EnsureEvenMemoryAddress(destination, ".l", isRead: false);
        cpu.Write8(address, (byte)(value >> 24));
        cpu.Write8(EffectiveAddressMath.NormalizeAddress24(address + 1), (byte)((value >> 16) & 0xFF));
        cpu.Write8(EffectiveAddressMath.NormalizeAddress24(address + 2), (byte)((value >> 8) & 0xFF));
        cpu.Write8(EffectiveAddressMath.NormalizeAddress24(address + 3), (byte)(value & 0xFF));
    }

    private static uint EnsureEvenMemoryAddress(DestinationOperand destination, string size, bool isRead)
    {
        var normalizedAddress = EffectiveAddressMath.NormalizeAddress24(destination.Address);
        if ((normalizedAddress & 1) == 0)
            return normalizedAddress;

        throw new AddressErrorException(
            destination.Address,
            size,
            isRead,
            isProgramAccess: destination.IsProgramAccess,
            frameProgramCounterAdjust: destination.FrameProgramCounterAdjust);
    }
}
