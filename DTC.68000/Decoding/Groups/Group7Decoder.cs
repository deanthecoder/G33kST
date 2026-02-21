// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.M68000.Instructions;

namespace DTC.M68000.Decoding.Groups;

/// <summary>
/// Decoder for major opcode group 0x7 (MOVEQ).
/// </summary>
public static class Group7Decoder
{
    private static readonly Instruction InstrMoveQuick = new("MOVEQ #<imm8>,Dn", MoveInstructions.ExecuteMoveQuick);

    /// <summary>
    /// Decodes an opcode in this major group.
    /// </summary>
    public static Instruction Decode(ushort opcode)
    {
        // 0111 ddd0 iiiiiiii = MOVEQ #<imm8>,Dn.
        return (opcode & 0x0100) == 0 ? InstrMoveQuick : null;
    }
}
