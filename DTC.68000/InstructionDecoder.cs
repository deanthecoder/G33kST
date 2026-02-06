// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.M68000;

/// <summary>
/// Decodes 68000 opcodes using a high-nibble router with per-group handlers.
/// </summary>
public static class InstructionDecoder
{
    private static readonly Func<ushort, Instruction>[] Decoders = BuildGroupDecoders();
    private static readonly Instruction NOP = new("NOP", static (_, _) => { });

    /// <summary>
    /// Resolves an opcode to an instruction handler, or null if unsupported.
    /// </summary>
    public static Instruction Decode(ushort opcode)
    {
        var group = opcode >> 12;
        return Decoders[group](opcode);
    }

    private static Func<ushort, Instruction>[] BuildGroupDecoders()
    {
        var decoders = new Func<ushort, Instruction>[16];
        for (var i = 0; i < decoders.Length; i++)
            decoders[i] = static _ => null;

        decoders[0x4] = DecodeGroup4;
        return decoders;
    }

    private static Instruction DecodeGroup4(ushort opcode) =>
        opcode == 0x4E71 ? NOP : null;
}
