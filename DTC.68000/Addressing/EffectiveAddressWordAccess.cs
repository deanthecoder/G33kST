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
/// Provides word-sized EA access helpers for instruction execution.
/// EA = effective address, Dn = data register, An = address register,
/// d16/d8 = 16/8-bit displacement, and Xn = index register.
/// </summary>
public static class EffectiveAddressWordAccess
{
    /// <summary>
    /// Returns true when the EA can be read as a source word operand.
    /// </summary>
    public static bool SupportsWordRead(EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => true,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => ea.Register is 0 or 1 or 2 or 3 or 4,
            _ => false
        };

    /// <summary>
    /// Returns true when the EA can be written as a destination word operand.
    /// </summary>
    public static bool SupportsWordWrite(EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => true,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => ea.Register is 0 or 1,
            _ => false
        };

    /// <summary>
    /// Reads a word from the provided EA and applies EA side-effects where required.
    /// </summary>
    public static ushort ReadWord(Cpu cpu, EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => (ushort)cpu.Registers.GetDataRegister(ea.Register),
            EffectiveAddressMode.AddressRegisterDirect => (ushort)cpu.Registers.GetAddressRegister(ea.Register),
            EffectiveAddressMode.AddressRegisterIndirect => ReadWordFromBus(cpu, cpu.Registers.GetAddressRegister(ea.Register)),
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => ReadWordPostIncrement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => ReadWordPreDecrement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => ReadWordDisplacement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectIndex => ReadWordIndex(cpu, ea.Register),
            EffectiveAddressMode.Other when ea.Register == 0 => ReadWordAbsoluteShort(cpu),
            EffectiveAddressMode.Other when ea.Register == 1 => ReadWordAbsoluteLong(cpu),
            EffectiveAddressMode.Other when ea.Register == 2 => ReadWordPcDisplacement(cpu),
            EffectiveAddressMode.Other when ea.Register == 3 => ReadWordPcIndex(cpu),
            EffectiveAddressMode.Other when ea.Register == 4 => ReadWordImmediate(cpu),
            _ => throw new NotSupportedException($"Word read EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.")
        };

    /// <summary>
    /// Writes a word to the provided EA and applies EA side-effects where required.
    /// </summary>
    public static void WriteWord(Cpu cpu, EffectiveAddress ea, ushort value)
    {
        switch (ea.Mode)
        {
            case EffectiveAddressMode.DataRegisterDirect:
                WriteWordToDataRegister(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirect:
                WriteWordToBus(cpu, cpu.Registers.GetAddressRegister(ea.Register), value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectPostIncrement:
                WriteWordPostIncrement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectPreDecrement:
                WriteWordPreDecrement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectDisplacement:
                WriteWordDisplacement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectIndex:
                WriteWordIndex(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.Other when ea.Register == 0:
                WriteWordAbsoluteShort(cpu, value);
                return;
            case EffectiveAddressMode.Other when ea.Register == 1:
                WriteWordAbsoluteLong(cpu, value);
                return;
            default:
                throw new NotSupportedException($"Word write EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.");
        }
    }

    /// <summary>
    /// Reads via <c>(An)+</c>, then increments <c>An</c> by two.
    /// </summary>
    private static ushort ReadWordPostIncrement(Cpu cpu, byte registerIndex)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, address + 2);
        return ReadWordFromBus(cpu, address);
    }

    /// <summary>
    /// Decrements <c>An</c> by two first, then reads via <c>-(An)</c>.
    /// </summary>
    private static ushort ReadWordPreDecrement(Cpu cpu, byte registerIndex)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex) - 2;
        cpu.Registers.SetAddressRegister(registerIndex, address);
        return ReadWordFromBus(cpu, address);
    }

    /// <summary>
    /// Reads via <c>(d16,An)</c> using a sign-extended 16-bit displacement extension word.
    /// </summary>
    private static ushort ReadWordDisplacement(Cpu cpu, byte registerIndex)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return ReadWordFromBus(cpu, EffectiveAddressMath.AddDisplacement(baseAddress, displacement));
    }

    /// <summary>
    /// Reads via <c>(d8,An,Xn)</c> using the brief extension word for index/displacement.
    /// </summary>
    private static ushort ReadWordIndex(Cpu cpu, byte registerIndex)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
        return ReadWordFromBus(cpu, address);
    }

    /// <summary>
    /// Reads via absolute short <c>(xxx).w</c>; address is sign-extended then masked to 24-bit bus space.
    /// </summary>
    private static ushort ReadWordAbsoluteShort(Cpu cpu)
    {
        var address = EffectiveAddressMath.ReadAbsoluteShortAddress(cpu);
        return ReadWordFromBus(cpu, address);
    }

    /// <summary>
    /// Reads via absolute long <c>(xxx).l</c>.
    /// </summary>
    private static ushort ReadWordAbsoluteLong(Cpu cpu)
    {
        var address = EffectiveAddressMath.ReadAbsoluteLongAddress(cpu);
        return ReadWordFromBus(cpu, address);
    }

    /// <summary>
    /// Reads via <c>(d16,PC)</c> using the 68000 PC-relative base address semantics.
    /// </summary>
    private static ushort ReadWordPcDisplacement(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var displacement = (short)cpu.FetchPcWord();
        return ReadWordFromBus(cpu, EffectiveAddressMath.AddDisplacement(baseAddress, displacement));
    }

    /// <summary>
    /// Reads via <c>(d8,PC,Xn)</c> using brief extension decode and PC-relative base.
    /// </summary>
    private static ushort ReadWordPcIndex(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var extension = cpu.FetchPcWord();
        var address = EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
        return ReadWordFromBus(cpu, address);
    }

    /// <summary>
    /// Reads an immediate word from the extension word (<c>#&lt;imm16&gt;</c>).
    /// </summary>
    private static ushort ReadWordImmediate(Cpu cpu) =>
        cpu.FetchPcWord();

    /// <summary>
    /// Writes via <c>(An)+</c>, then increments <c>An</c> by two.
    /// </summary>
    private static void WriteWordPostIncrement(Cpu cpu, byte registerIndex, ushort value)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex);
        WriteWordToBus(cpu, address, value);
        cpu.Registers.SetAddressRegister(registerIndex, address + 2);
    }

    /// <summary>
    /// Decrements <c>An</c> by two first, then writes via <c>-(An)</c>.
    /// </summary>
    private static void WriteWordPreDecrement(Cpu cpu, byte registerIndex, ushort value)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex) - 2;
        cpu.Registers.SetAddressRegister(registerIndex, address);
        WriteWordToBus(cpu, address, value);
    }

    /// <summary>
    /// Writes via <c>(d16,An)</c> using a sign-extended 16-bit displacement extension word.
    /// </summary>
    private static void WriteWordDisplacement(Cpu cpu, byte registerIndex, ushort value)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        WriteWordToBus(cpu, EffectiveAddressMath.AddDisplacement(baseAddress, displacement), value);
    }

    /// <summary>
    /// Writes via <c>(d8,An,Xn)</c> using the brief extension word for index/displacement.
    /// </summary>
    private static void WriteWordIndex(Cpu cpu, byte registerIndex, ushort value)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
        WriteWordToBus(cpu, address, value);
    }

    /// <summary>
    /// Writes via absolute short <c>(xxx).w</c>; address is sign-extended then masked to 24-bit bus space.
    /// </summary>
    private static void WriteWordAbsoluteShort(Cpu cpu, ushort value)
    {
        var address = EffectiveAddressMath.ReadAbsoluteShortAddress(cpu);
        WriteWordToBus(cpu, address, value);
    }

    /// <summary>
    /// Writes via absolute long <c>(xxx).l</c>.
    /// </summary>
    private static void WriteWordAbsoluteLong(Cpu cpu, ushort value)
    {
        var address = EffectiveAddressMath.ReadAbsoluteLongAddress(cpu);
        WriteWordToBus(cpu, address, value);
    }

    /// <summary>
    /// Reads a word from memory in big-endian byte order.
    /// </summary>
    private static ushort ReadWordFromBus(Cpu cpu, uint address)
    {
        address = EnsureEvenAddress(address);
        var hi = cpu.Read8(address);
        var lo = cpu.Read8(EffectiveAddressMath.NormalizeAddress24(address + 1));
        return (ushort)((hi << 8) | lo);
    }

    /// <summary>
    /// Writes a word to memory in big-endian byte order.
    /// </summary>
    private static void WriteWordToBus(Cpu cpu, uint address, ushort value)
    {
        address = EnsureEvenAddress(address);
        cpu.Write8(address, (byte)(value >> 8));
        cpu.Write8(EffectiveAddressMath.NormalizeAddress24(address + 1), (byte)(value & 0xFF));
    }

    /// <summary>
    /// Writes a word into the low 16 bits of a data register, preserving upper bits.
    /// </summary>
    private static void WriteWordToDataRegister(Cpu cpu, int registerIndex, ushort value)
    {
        var destinationValue = cpu.Registers.GetDataRegister(registerIndex);
        cpu.Registers.SetDataRegister(registerIndex, (destinationValue & 0xFFFF0000u) | value);
    }

    /// <summary>
    /// Validates word alignment and raises a CPU address error on odd addresses.
    /// </summary>
    private static uint EnsureEvenAddress(uint address)
    {
        var normalized = EffectiveAddressMath.NormalizeAddress24(address);
        if ((normalized & 1) == 0)
            return normalized;

        throw new AddressErrorException(address, ".w");
    }
}
