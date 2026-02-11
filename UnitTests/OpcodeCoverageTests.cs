// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.M68000.Decoding;

namespace UnitTests;

[TestFixture]
public sealed class OpcodeCoverageTests
{
    private const int MinimumImplementedOpcodes = 54777;

    [Test]
    public void DecoderCoverageAudit()
    {
        var unimplemented = new List<ushort>();

        for (var opcode = 0; opcode <= ushort.MaxValue; opcode++)
            if (InstructionDecoder.Decode((ushort)opcode) == null)
                unimplemented.Add((ushort)opcode);

        var implementedCount = (ushort.MaxValue + 1) - unimplemented.Count;
        var implementedPercent = (implementedCount * 100.0) / (ushort.MaxValue + 1);
        TestContext.Progress.WriteLine($"Opcode decode coverage: {implementedCount}/65536 ({implementedPercent:F2}%).");
        TestContext.Progress.WriteLine($"Unimplemented opcode count: {unimplemented.Count}.");

        foreach (var group in GroupByTopNibble(unimplemented))
            TestContext.Progress.WriteLine($"  0x{group.TopNibble:X}: {group.Count}");

        var sample = unimplemented.Take(32).Select(opcode => $"0x{opcode:X4}");
        TestContext.Progress.WriteLine($"Unimplemented opcode sample: {string.Join(", ", sample)}");

        Assert.Multiple(() =>
        {
            Assert.That(implementedCount + unimplemented.Count, Is.EqualTo(65536));
            Assert.That(implementedCount, Is.GreaterThanOrEqualTo(MinimumImplementedOpcodes));
        });
    }

    private static IReadOnlyList<OpcodeGroupCount> GroupByTopNibble(IReadOnlyCollection<ushort> opcodes)
    {
        var counts = new List<OpcodeGroupCount>();
        for (var nibble = 0; nibble <= 0xF; nibble++)
        {
            var count = opcodes.Count(opcode => (opcode >> 12) == nibble);
            if (count == 0)
                continue;
            counts.Add(new OpcodeGroupCount((byte)nibble, count));
        }

        return counts;
    }

    private readonly record struct OpcodeGroupCount(byte TopNibble, int Count);
}
