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
/// Represents a 68000 effective-address field (mode + register).
/// </summary>
public readonly record struct EffectiveAddress(byte Mode, byte Register);

/// <summary>
/// Decodes effective-address fields from instruction opcodes.
/// </summary>
public static class EffectiveAddressDecoder
{
    /// <summary>
    /// Decodes source effective-address bits from a standard opcode layout.
    /// </summary>
    public static EffectiveAddress DecodeSource(ushort opcode) =>
        new((byte)((opcode >> 3) & 0x07), (byte)(opcode & 0x07));

    /// <summary>
    /// Decodes destination effective-address bits from MOVE opcode layout.
    /// </summary>
    public static EffectiveAddress DecodeMoveDestination(ushort opcode) =>
        new((byte)((opcode >> 6) & 0x07), (byte)((opcode >> 9) & 0x07));
}
