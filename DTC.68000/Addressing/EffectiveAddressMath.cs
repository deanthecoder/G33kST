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
/// Shared helpers for 68000 effective-address arithmetic and normalization.
/// </summary>
public static class EffectiveAddressMath
{
    /// <summary>
    /// Masks an address to the 24-bit external bus space.
    /// </summary>
    public static uint NormalizeAddress24(uint address) =>
        address & 0x00FF_FFFF;

    /// <summary>
    /// Adds a sign-extended displacement to a base address with 32-bit wrap semantics.
    /// </summary>
    public static uint AddDisplacement(uint baseAddress, short displacement) =>
        unchecked((uint)(baseAddress + displacement));

    /// <summary>
    /// Computes brief indexed address forms: base + d8 + Xn.
    /// </summary>
    public static uint AddIndex(Cpu cpu, uint baseAddress, ushort extensionWord)
    {
        var displacement = (sbyte)(extensionWord & 0x00FF);
        var indexValue = ResolveIndexValue(cpu, extensionWord);
        return unchecked((uint)(baseAddress + displacement + indexValue));
    }

    /// <summary>
    /// Resolves index register value from a brief extension word.
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
    /// Returns byte post-inc/pre-dec step size (A7 uses 2 to preserve word alignment).
    /// </summary>
    public static uint ByteAddressStep(int registerIndex) =>
        registerIndex == 7 ? 2u : 1u;

    /// <summary>
    /// Reads an absolute short address extension and sign-extends it to 32-bit.
    /// </summary>
    public static uint ReadAbsoluteShortAddress(Cpu cpu) =>
        (uint)(short)cpu.FetchPcWord();

    /// <summary>
    /// Reads an absolute long address extension from two consecutive words.
    /// </summary>
    public static uint ReadAbsoluteLongAddress(Cpu cpu)
    {
        var hi = cpu.FetchPcWord();
        var lo = cpu.FetchPcWord();
        return ((uint)hi << 16) | lo;
    }
}
