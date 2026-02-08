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
using DTC.M68000.Instructions;

namespace DTC.M68000.Decoding.Groups;

/// <summary>
/// Decoder for major opcode group 0x2.
/// </summary>
public static class Group2Decoder
{
    private static readonly Instruction InstrMoveLong = new("MOVE.L <ea>,<ea>", MoveInstructions.ExecuteMoveLong);

    /// <summary>
    /// Decodes an opcode in this major group.
    /// </summary>
    public static Instruction Decode(ushort opcode)
    {
        var src = EffectiveAddressDecoder.DecodeSource(opcode);
        var dst = EffectiveAddressDecoder.DecodeMoveDestination(opcode);

        // Destination mode=001 belongs to MOVEA.L, not MOVE.L.
        if (dst.Mode == EffectiveAddressMode.AddressRegisterDirect)
            return null;
        if (!EffectiveAddressLongAccess.SupportsLongRead(src))
            return null;
        if (!EffectiveAddressLongAccess.SupportsLongWrite(dst))
            return null;

        return InstrMoveLong;
    }
}
