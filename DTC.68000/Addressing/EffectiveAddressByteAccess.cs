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
/// Provides byte-sized EA access helpers for instruction execution.
/// EA = effective address, Dn = data register, An = address register,
/// d16/d8 = 16/8-bit displacement, and Xn = index register.
/// </summary>
public static class EffectiveAddressByteAccess
{
    /// <summary>
    /// Returns true when the EA can be read as a source byte operand.
    /// </summary>
    public static bool SupportsByteRead(EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => true,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => ea.Register is 0 or 1 or 2 or 3 or 4,
            _ => false
        };

    /// <summary>
    /// Returns true when the EA can be written as a destination byte operand.
    /// </summary>
    public static bool SupportsByteWrite(EffectiveAddress ea) =>
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
    /// Reads a byte from the provided EA and applies EA side-effects where required.
    /// </summary>
    public static byte ReadByte(Cpu cpu, EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => (byte)cpu.Registers.GetDataRegister(ea.Register),
            EffectiveAddressMode.AddressRegisterIndirect => cpu.Read8(NormalizeAddress24(cpu.Registers.GetAddressRegister(ea.Register))),
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => ReadBytePostIncrement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => ReadBytePreDecrement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => ReadByteDisplacement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectIndex => ReadByteIndex(cpu, ea.Register),
            EffectiveAddressMode.Other when ea.Register == 0 => ReadByteAbsoluteShort(cpu),
            EffectiveAddressMode.Other when ea.Register == 1 => ReadByteAbsoluteLong(cpu),
            EffectiveAddressMode.Other when ea.Register == 2 => ReadBytePcDisplacement(cpu),
            EffectiveAddressMode.Other when ea.Register == 3 => ReadBytePcIndex(cpu),
            EffectiveAddressMode.Other when ea.Register == 4 => ReadByteImmediate(cpu),
            _ => throw new NotSupportedException($"Byte read EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.")
        };

    /// <summary>
    /// Writes a byte to the provided EA and applies EA side-effects where required.
    /// </summary>
    public static void WriteByte(Cpu cpu, EffectiveAddress ea, byte value)
    {
        switch (ea.Mode)
        {
            case EffectiveAddressMode.DataRegisterDirect:
                WriteByteToDataRegister(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirect:
                cpu.Write8(NormalizeAddress24(cpu.Registers.GetAddressRegister(ea.Register)), value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectPostIncrement:
                WriteBytePostIncrement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectPreDecrement:
                WriteBytePreDecrement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectDisplacement:
                WriteByteDisplacement(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.AddressRegisterIndirectIndex:
                WriteByteIndex(cpu, ea.Register, value);
                return;
            case EffectiveAddressMode.Other when ea.Register == 0:
                WriteByteAbsoluteShort(cpu, value);
                return;
            case EffectiveAddressMode.Other when ea.Register == 1:
                WriteByteAbsoluteLong(cpu, value);
                return;
            default:
                throw new NotSupportedException($"Byte write EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.");
        }
    }

    /// <summary>
    /// Reads via <c>(An)+</c>, then increments <c>An</c> (A7 increments by 2 for byte accesses).
    /// </summary>
    private static byte ReadBytePostIncrement(Cpu cpu, byte registerIndex)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex);
        var value = cpu.Read8(NormalizeAddress24(address));
        cpu.Registers.SetAddressRegister(registerIndex, address + ByteAddressStep(registerIndex));
        return value;
    }

    /// <summary>
    /// Decrements <c>An</c> first, then reads via <c>-(An)</c> (A7 decrements by 2 for byte accesses).
    /// </summary>
    private static byte ReadBytePreDecrement(Cpu cpu, byte registerIndex)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex) - ByteAddressStep(registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, address);
        return cpu.Read8(NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads via <c>(d16,An)</c> using a sign-extended 16-bit displacement extension word.
    /// </summary>
    private static byte ReadByteDisplacement(Cpu cpu, byte registerIndex)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return cpu.Read8(NormalizeAddress24(AddDisplacement(baseAddress, displacement)));
    }

    /// <summary>
    /// Reads via <c>(d8,An,Xn)</c> using the brief extension word for index/displacement.
    /// </summary>
    private static byte ReadByteIndex(Cpu cpu, byte registerIndex)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = AddIndex(cpu, baseAddress, extension);
        return cpu.Read8(NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads via absolute short <c>(xxx).w</c>; address is sign-extended then masked to 24-bit bus space.
    /// </summary>
    private static byte ReadByteAbsoluteShort(Cpu cpu)
    {
        var address = (uint)(short)cpu.FetchPcWord();
        return cpu.Read8(NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads via absolute long <c>(xxx).l</c>.
    /// </summary>
    private static byte ReadByteAbsoluteLong(Cpu cpu)
    {
        var hi = cpu.FetchPcWord();
        var lo = cpu.FetchPcWord();
        var address = ((uint)hi << 16) | lo;
        return cpu.Read8(NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads via <c>(d16,PC)</c> using the 68000 PC-relative base address semantics.
    /// </summary>
    private static byte ReadBytePcDisplacement(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var displacement = (short)cpu.FetchPcWord();
        return cpu.Read8(NormalizeAddress24(AddDisplacement(baseAddress, displacement)));
    }

    /// <summary>
    /// Reads via <c>(d8,PC,Xn)</c> using brief extension decode and PC-relative base.
    /// </summary>
    private static byte ReadBytePcIndex(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var extension = cpu.FetchPcWord();
        var address = AddIndex(cpu, baseAddress, extension);
        return cpu.Read8(NormalizeAddress24(address));
    }

    /// <summary>
    /// Reads an immediate byte from the extension word (<c>#&lt;imm8&gt;</c>, low byte used).
    /// </summary>
    private static byte ReadByteImmediate(Cpu cpu) =>
        (byte)cpu.FetchPcWord();

    /// <summary>
    /// Writes via <c>(An)+</c>, then increments <c>An</c> (A7 increments by 2 for byte accesses).
    /// </summary>
    private static void WriteBytePostIncrement(Cpu cpu, byte registerIndex, byte value)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex);
        cpu.Write8(NormalizeAddress24(address), value);
        cpu.Registers.SetAddressRegister(registerIndex, address + ByteAddressStep(registerIndex));
    }

    /// <summary>
    /// Decrements <c>An</c> first, then writes via <c>-(An)</c> (A7 decrements by 2 for byte accesses).
    /// </summary>
    private static void WriteBytePreDecrement(Cpu cpu, byte registerIndex, byte value)
    {
        var address = cpu.Registers.GetAddressRegister(registerIndex) - ByteAddressStep(registerIndex);
        cpu.Registers.SetAddressRegister(registerIndex, address);
        cpu.Write8(NormalizeAddress24(address), value);
    }

    /// <summary>
    /// Writes via <c>(d16,An)</c> using a sign-extended 16-bit displacement extension word.
    /// </summary>
    private static void WriteByteDisplacement(Cpu cpu, byte registerIndex, byte value)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        cpu.Write8(NormalizeAddress24(AddDisplacement(baseAddress, displacement)), value);
    }

    /// <summary>
    /// Writes via <c>(d8,An,Xn)</c> using the brief extension word for index/displacement.
    /// </summary>
    private static void WriteByteIndex(Cpu cpu, byte registerIndex, byte value)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var address = AddIndex(cpu, baseAddress, extension);
        cpu.Write8(NormalizeAddress24(address), value);
    }

    /// <summary>
    /// Writes via absolute short <c>(xxx).w</c>; address is sign-extended then masked to 24-bit bus space.
    /// </summary>
    private static void WriteByteAbsoluteShort(Cpu cpu, byte value)
    {
        var address = (uint)(short)cpu.FetchPcWord();
        cpu.Write8(NormalizeAddress24(address), value);
    }

    /// <summary>
    /// Writes via absolute long <c>(xxx).l</c>.
    /// </summary>
    private static void WriteByteAbsoluteLong(Cpu cpu, byte value)
    {
        var hi = cpu.FetchPcWord();
        var lo = cpu.FetchPcWord();
        var address = ((uint)hi << 16) | lo;
        cpu.Write8(NormalizeAddress24(address), value);
    }

    /// <summary>
    /// Returns byte post-inc/pre-dec step size (A7 uses 2 to preserve word alignment).
    /// </summary>
    private static uint ByteAddressStep(int registerIndex) =>
        registerIndex == 7 ? 2u : 1u;

    /// <summary>
    /// Adds a sign-extended displacement to a base address with 32-bit wrap semantics.
    /// </summary>
    private static uint AddDisplacement(uint baseAddress, short displacement) =>
        unchecked((uint)(baseAddress + displacement));

    /// <summary>
    /// Computes address for brief index extension forms: base + d8 + Xn.
    /// </summary>
    private static uint AddIndex(Cpu cpu, uint baseAddress, ushort extensionWord)
    {
        var displacement = (sbyte)(extensionWord & 0x00FF);
        var indexValue = ResolveIndexValue(cpu, extensionWord);
        return unchecked((uint)(baseAddress + displacement + indexValue));
    }

    /// <summary>
    /// Resolves the index value from the extension word (Dn/An, word/long size).
    /// </summary>
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

    /// <summary>
    /// Masks an address to the 24-bit external bus space.
    /// </summary>
    private static uint NormalizeAddress24(uint address) =>
        address & 0x00FF_FFFF;

    /// <summary>
    /// Writes a byte into the low 8 bits of a data register, preserving upper bits.
    /// </summary>
    private static void WriteByteToDataRegister(Cpu cpu, int registerIndex, byte value)
    {
        var destinationValue = cpu.Registers.GetDataRegister(registerIndex);
        cpu.Registers.SetDataRegister(registerIndex, (destinationValue & 0xFFFFFF00u) | value);
    }
}
