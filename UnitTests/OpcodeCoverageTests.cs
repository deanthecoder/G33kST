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
    private const int ExpectedImplementedOpcodes = 54777;
    private const int ExpectedUnimplementedOpcodes = 10759;

    private static readonly IReadOnlyDictionary<byte, int> ExpectedUnimplementedByTopNibble = new Dictionary<byte, int>
    {
        [0x0] = 1107,
        [0x1] = 1446,
        [0x2] = 558,
        [0x3] = 558,
        [0x4] = 2074,
        [0x5] = 512,
        [0x7] = 2048,
        [0x8] = 456,
        [0x9] = 328,
        [0xB] = 328,
        [0xC] = 328,
        [0xD] = 328,
        [0xE] = 688
    };

    [Test]
    public void DecoderCoverageAudit()
    {
        var unimplemented = new List<ushort>();

        for (var opcode = 0; opcode <= ushort.MaxValue; opcode++)
            if (InstructionDecoder.Decode((ushort)opcode) == null)
                unimplemented.Add((ushort)opcode);

        var totalOpcodes = ushort.MaxValue + 1;
        var implementedCount = totalOpcodes - unimplemented.Count;
        var implementedPercent = (implementedCount * 100.0) / (ushort.MaxValue + 1);
        TestContext.Progress.WriteLine($"Opcode decode coverage: {implementedCount}/65536 ({implementedPercent:F2}%).");
        TestContext.Progress.WriteLine($"Unimplemented opcode count: {unimplemented.Count}.");

        var byNibble = GroupByTopNibble(unimplemented);
        foreach (var group in byNibble)
            TestContext.Progress.WriteLine($"  0x{group.Key:X}: {group.Value}");

        var sample = unimplemented.Take(32).Select(opcode => $"0x{opcode:X4}");
        TestContext.Progress.WriteLine($"Unimplemented opcode sample: {string.Join(", ", sample)}");

        Assert.Multiple(() =>
        {
            Assert.That(implementedCount + unimplemented.Count, Is.EqualTo(totalOpcodes));
            Assert.That(implementedCount, Is.EqualTo(ExpectedImplementedOpcodes));
            Assert.That(unimplemented.Count, Is.EqualTo(ExpectedUnimplementedOpcodes));
            Assert.That(byNibble.Count, Is.EqualTo(ExpectedUnimplementedByTopNibble.Count));

            foreach (var expected in ExpectedUnimplementedByTopNibble.OrderBy(o => o.Key))
            {
                Assert.That(
                    byNibble.TryGetValue(expected.Key, out var actualCount),
                    Is.True,
                    $"Missing nibble 0x{expected.Key:X} coverage bucket.");
                Assert.That(actualCount, Is.EqualTo(expected.Value), $"Unexpected count for nibble 0x{expected.Key:X}.");
            }
        });
    }

    private static IReadOnlyDictionary<byte, int> GroupByTopNibble(IReadOnlyCollection<ushort> opcodes)
    {
        var counts = new Dictionary<byte, int>();
        for (var nibble = 0; nibble <= 0xF; nibble++)
        {
            var count = opcodes.Count(opcode => (opcode >> 12) == nibble);
            if (count == 0)
                continue;
            counts[(byte)nibble] = count;
        }

        return counts;
    }
}
