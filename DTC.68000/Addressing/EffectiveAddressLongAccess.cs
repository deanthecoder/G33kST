// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.M68000.Addressing;

/// <summary>
/// Provides long-sized EA access helpers for instruction execution.
/// EA = effective address, Dn = data register, An = address register,
/// d16/d8 = 16/8-bit displacement, and Xn = index register.
/// </summary>
public static class EffectiveAddressLongAccess
{
    /// <summary>
    /// Returns true when the EA can be read as a source long operand.
    /// </summary>
    public static bool SupportsLongRead(EffectiveAddress ea) =>
        EffectiveAddressSupport.SupportsRead(ea, allowsAddressRegisterDirect: true);

    /// <summary>
    /// Returns true when the EA can be written as a destination long operand.
    /// </summary>
    public static bool SupportsLongWrite(EffectiveAddress ea) =>
        EffectiveAddressSupport.SupportsWrite(ea);

    /// <summary>
    /// Reads a long from the provided EA and applies EA side-effects where required.
    /// </summary>
    public static uint ReadLong(Cpu cpu, EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => cpu.Registers.GetDataRegister(ea.Register),
            EffectiveAddressMode.AddressRegisterDirect => cpu.Registers.GetAddressRegister(ea.Register),
            EffectiveAddressMode.AddressRegisterIndirect => ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(cpu.Registers.GetAddressRegister(ea.Register))),
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => ReadLongPostIncrement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => ReadLongPreDecrement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => ReadLongDisplacement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectIndex => ReadLongIndex(cpu, ea.Register),
            EffectiveAddressMode.Other when ea.Register == 0 => ReadLongAbsoluteShort(cpu),
            EffectiveAddressMode.Other when ea.Register == 1 => ReadLongAbsoluteLong(cpu),
            EffectiveAddressMode.Other when ea.Register == 2 => ReadLongPcDisplacement(cpu),
            EffectiveAddressMode.Other when ea.Register == 3 => ReadLongPcIndex(cpu),
            EffectiveAddressMode.Other when ea.Register == 4 => ReadLongImmediate(cpu),
            _ => throw new NotSupportedException($"Long read EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.")
        };

    /// <summary>
    /// Writes a long to the provided EA and applies EA side-effects where required.
    /// </summary>
    public static void WriteLong(Cpu cpu, EffectiveAddress ea, uint value)
    {
        switch (ea.Mode)
        {
            case EffectiveAddressMode.DataRegisterDirect:
                cpu.Registers.SetDataRegister(ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirect:
                WriteLongToBus(cpu, EffectiveAddressMath.NormalizeAddress24(cpu.Registers.GetAddressRegister(ea.Register)), value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectPostIncrement:
                WriteLongPostIncrement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectPreDecrement:
                WriteLongPreDecrement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectDisplacement:
                WriteLongDisplacement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectIndex:
                WriteLongIndex(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.Other when ea.Register == 0:
                WriteLongAbsoluteShort(cpu, value);
                return;
            case EffectiveAddressMode.Other when ea.Register == 1:
                WriteLongAbsoluteLong(cpu, value);
                return;
            default:
                throw new NotSupportedException($"Long write EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.");
        }
    }

    /// <summary>
    /// Reads via <c>(An)+</c>, then increments <c>An</c> by four.
    /// </summary>
    private static uint ReadLongPostIncrement(Cpu cpu, byte registerIndex)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex);
        var value = ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(address));
        cpu.Registers.SetAddressRegister(registerIndex, address + 4);
        return value;
    }

    /// <summary>
    /// Decrements <c>An</c> by four first, then reads via <c>-(An)</c>.
    /// </summary>
    private static uint ReadLongPreDecrement(Cpu cpu, byte registerIndex)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex) - 4;
        cpu.Registers.SetAddressRegister(registerIndex, address);
        return ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads via <c>(d16,An)</c> using a sign-extended 16-bit displacement extension word.
    /// </summary>
    private static uint ReadLongDisplacement(Cpu cpu, byte registerIndex)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.AddDisplacement(baseAddress, displacement)));
    }

    /// <summary>
    /// Reads via <c>(d8,An,Xn)</c> using the brief extension word for index/displacement.
    /// </summary>
    private static uint ReadLongIndex(Cpu cpu, byte registerIndex)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
        return ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads via absolute short <c>(xxx).w</c>; address is sign-extended then masked to 24-bit bus space.
    /// </summary>
    private static uint ReadLongAbsoluteShort(Cpu cpu)
    {
        var address = EffectiveAddressMath.ReadAbsoluteShortAddress(cpu);
        return ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads via absolute long <c>(xxx).l</c>.
    /// </summary>
    private static uint ReadLongAbsoluteLong(Cpu cpu)
    {
        var address = EffectiveAddressMath.ReadAbsoluteLongAddress(cpu);
        return ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads via <c>(d16,PC)</c> using the 68000 PC-relative base address semantics.
    /// </summary>
    private static uint ReadLongPcDisplacement(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var displacement = (short)cpu.FetchPcWord();
        return ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.AddDisplacement(baseAddress, displacement)));
    }

    /// <summary>
    /// Reads via <c>(d8,PC,Xn)</c> using brief extension decode and PC-relative base.
    /// </summary>
    private static uint ReadLongPcIndex(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var extension = cpu.FetchPcWord();
        var address = EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
        return ReadLongFromBus(cpu, EffectiveAddressMath.NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads an immediate long from two extension words (<c>#&lt;imm32&gt;</c>).
    /// </summary>
    private static uint ReadLongImmediate(Cpu cpu)
    {
        var hi = cpu.FetchPcWord();
        var lo = cpu.FetchPcWord();
        return ((uint)hi << 16) | lo;
    }

    /// <summary>
    /// Writes via <c>(An)+</c>, then increments <c>An</c> by four.
    /// </summary>
    private static void WriteLongPostIncrement(Cpu cpu, byte registerIndex, uint value)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex);
        WriteLongToBus(cpu, EffectiveAddressMath.NormalizeAddress24(address), value);
        cpu.Registers.SetAddressRegister(registerIndex, address + 4);
    }

    /// <summary>
    /// Decrements <c>An</c> by four first, then writes via <c>-(An)</c>.
    /// </summary>
    private static void WriteLongPreDecrement(Cpu cpu, byte registerIndex, uint value)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex) - 4;
        cpu.Registers.SetAddressRegister(registerIndex, address);
        WriteLongToBus(cpu, EffectiveAddressMath.NormalizeAddress24(address), value);
    }

    /// <summary>
    /// Writes via <c>(d16,An)</c> using a sign-extended 16-bit displacement extension word.
    /// </summary>
    private static void WriteLongDisplacement(Cpu cpu, byte registerIndex, uint value)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        WriteLongToBus(cpu, EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.AddDisplacement(baseAddress, displacement)), value);
    }

    /// <summary>
    /// Writes via <c>(d8,An,Xn)</c> using the brief extension word for index/displacement.
    /// </summary>
    private static void WriteLongIndex(Cpu cpu, byte registerIndex, uint value)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
        WriteLongToBus(cpu, EffectiveAddressMath.NormalizeAddress24(address), value);
    }

    /// <summary>
    /// Writes via absolute short <c>(xxx).w</c>; address is sign-extended then masked to 24-bit bus space.
    /// </summary>
    private static void WriteLongAbsoluteShort(Cpu cpu, uint value)
    {
        var address = EffectiveAddressMath.ReadAbsoluteShortAddress(cpu);
        WriteLongToBus(cpu, EffectiveAddressMath.NormalizeAddress24(address), value);
    }

    /// <summary>
    /// Writes via absolute long <c>(xxx).l</c>.
    /// </summary>
    private static void WriteLongAbsoluteLong(Cpu cpu, uint value)
    {
        var address = EffectiveAddressMath.ReadAbsoluteLongAddress(cpu);
        WriteLongToBus(cpu, EffectiveAddressMath.NormalizeAddress24(address), value);
    }

    /// <summary>
    /// Reads a long from memory in big-endian byte order.
    /// </summary>
    private static uint ReadLongFromBus(Cpu cpu, uint address)
    {
        address = EnsureEvenAddress(address);
        var b0 = cpu.Read8(address);
        var b1 = cpu.Read8(EffectiveAddressMath.NormalizeAddress24(address + 1));
        var b2 = cpu.Read8(EffectiveAddressMath.NormalizeAddress24(address + 2));
        var b3 = cpu.Read8(EffectiveAddressMath.NormalizeAddress24(address + 3));
        return ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
    }

    /// <summary>
    /// Writes a long to memory in big-endian byte order.
    /// </summary>
    private static void WriteLongToBus(Cpu cpu, uint address, uint value)
    {
        address = EnsureEvenAddress(address);
        cpu.Write8(address, (byte)(value >> 24));
        cpu.Write8(EffectiveAddressMath.NormalizeAddress24(address + 1), (byte)((value >> 16) & 0xFF));
        cpu.Write8(EffectiveAddressMath.NormalizeAddress24(address + 2), (byte)((value >> 8) & 0xFF));
        cpu.Write8(EffectiveAddressMath.NormalizeAddress24(address + 3), (byte)(value & 0xFF));
    }

    /// <summary>
    /// Validates long alignment and raises a CPU address error on odd addresses.
    /// </summary>
    private static uint EnsureEvenAddress(uint address)
    {
        var normalized = EffectiveAddressMath.NormalizeAddress24(address);
        if ((normalized & 1) == 0)
            return normalized;

        throw new AddressErrorException(normalized, ".l");
    }
}
