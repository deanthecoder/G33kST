// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.M68000.Decoding.Groups;

namespace DTC.M68000.Decoding;

/// <summary>
/// Decodes 68000 opcodes by high-nibble group and delegates to per-group decoders.
/// </summary>
public static class InstructionDecoder
{
    private static readonly Func<ushort, Instruction>[] m_decoders =
    [
        Group0Decoder.Decode,
        Group1Decoder.Decode,
        Group2Decoder.Decode,
        Group3Decoder.Decode,
        Group4Decoder.Decode,
        Group5Decoder.Decode,
        Group6Decoder.Decode,
        Group7Decoder.Decode,
        Group8Decoder.Decode,
        Group9Decoder.Decode,
        GroupADecoder.Decode,
        GroupBDecoder.Decode,
        GroupCDecoder.Decode,
        GroupDDecoder.Decode,
        GroupEDecoder.Decode,
        GroupFDecoder.Decode
    ];

    /// <summary>
    /// Resolves an opcode to an instruction handler, or null if unsupported.
    /// </summary>
    public static Instruction Decode(ushort opcode)
    {
        var group = opcode >> 12;
        return m_decoders[group](opcode);
    }
}
