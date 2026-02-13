// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using System.Text.Json;
using DTC.Core.Extensions;

namespace UnitTests.SingleStep;

/// <summary>
/// Decodes single-step binary test data into JSON for unit testing.
/// </summary>
public static class SingleStepDecoder
{
    private const uint FileMagic = 0x1A3F5D71;
    private const uint TestMagic = 0xABC12367;
    private const uint NameMagic = 0x89ABCDEF;
    private const uint StateMagic = 0x01234567;
    private const uint TransactionsMagic = 0x456789AB;

    public static SingleStepDecodeResult DecodeGroup(SingleStepInstructionGroup group) =>
        DecodeGroup(GetGroupName(group), GetPattern(group));

    public static SingleStepDecodeResult DecodeGroup(string groupName, string pattern)
    {
        var sourceFiles = SingleStepPaths.TestDataRoot.GetFiles(pattern, SearchOption.TopDirectoryOnly);
        return DecodeFiles(groupName, sourceFiles);
    }

    public static SingleStepDecodeResult DecodeFiles(string groupName, IReadOnlyList<FileInfo> sourceFiles)
    {
        SingleStepPaths.EnsureTestDataAvailable();
        if (sourceFiles == null || sourceFiles.Count == 0)
            throw new InvalidOperationException($"No test files found for group '{groupName}'.");

        var decodedGroupDir = SingleStepPaths.DecodedRoot.GetDir(groupName);
        decodedGroupDir.Create();

        var decodedFiles = new List<SingleStepTestFile>();
        foreach (var sourceFile in sourceFiles.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var outputFile = decodedGroupDir.GetFile(sourceFile.LeafName());
            var decoded = DecodeFileIfNeeded(sourceFile, outputFile);
            decodedFiles.Add(decoded);
        }

        return new SingleStepDecodeResult(groupName, decodedFiles, decodedGroupDir);
    }

    public static IReadOnlyList<SingleStepTestCase> LoadDecodedTests(SingleStepInstructionGroup group)
    {
        var decodeResult = DecodeGroup(group);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var tests = new List<SingleStepTestCase>();

        foreach (var file in decodeResult.Files)
        {
            var fileTests = ReadJson(file.DecodedFile, options);
            tests.AddRange(fileTests);
        }

        return tests;
    }

    public static IReadOnlyList<SingleStepTestCase> LoadDecodedTests(FileInfo decodedFile)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return ReadJson(decodedFile, options);
    }

    private static SingleStepTestFile DecodeFileIfNeeded(FileInfo sourceFile, FileInfo outputFile)
    {
        if (!outputFile.Exists() || outputFile.LastWriteTimeUtc < sourceFile.LastWriteTimeUtc)
        {
            var tests = DecodeBinary(sourceFile);
            WriteJson(outputFile, tests);
            return new SingleStepTestFile(sourceFile, outputFile, tests.Count);
        }

        var count = ReadTestCount(outputFile);
        return new SingleStepTestFile(sourceFile, outputFile, count);
    }

    private static List<SingleStepTestCase> DecodeBinary(FileInfo sourceFile)
    {
        var data = sourceFile.ReadAllBytes();
        if (data == null || data.Length == 0)
            throw new InvalidDataException($"Test data file is empty: {sourceFile.FullName}.");

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        var fileMagic = reader.ReadUInt32();
        ValidateMagic(fileMagic, FileMagic, "File");

        var testCount = reader.ReadUInt32();
        var tests = new List<SingleStepTestCase>((int)testCount);

        for (var i = 0; i < testCount; i++)
            tests.Add(ReadTest(reader));

        return tests;
    }

    private static SingleStepTestCase ReadTest(BinaryReader reader)
    {
        _ = reader.ReadUInt32();
        var magic = reader.ReadUInt32();
        ValidateMagic(magic, TestMagic, "Test");

        var name = ReadName(reader);
        var initial = ReadState(reader);
        var final = ReadState(reader);
        var transactions = ReadTransactions(reader, out var length);

        return new SingleStepTestCase
        {
            Name = name,
            Initial = initial,
            Final = final,
            Transactions = transactions,
            Length = length
        };
    }

    private static string ReadName(BinaryReader reader)
    {
        _ = reader.ReadUInt32();
        var magic = reader.ReadUInt32();
        ValidateMagic(magic, NameMagic, "Name");

        var length = reader.ReadUInt32();
        if (length == 0)
            return string.Empty;

        var bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static SingleStepCpuState ReadState(BinaryReader reader)
    {
        _ = reader.ReadUInt32();
        var magic = reader.ReadUInt32();
        ValidateMagic(magic, StateMagic, "State");

        var d = new uint[8];
        for (var i = 0; i < d.Length; i++)
            d[i] = reader.ReadUInt32();

        var a = new uint[7];
        for (var i = 0; i < a.Length; i++)
            a[i] = reader.ReadUInt32();

        var usp = reader.ReadUInt32();
        var ssp = reader.ReadUInt32();
        var sr = reader.ReadUInt32();
        var pc = reader.ReadUInt32();

        var pf0 = reader.ReadUInt32();
        var pf1 = reader.ReadUInt32();

        var ramCount = reader.ReadUInt32();
        var ram = new List<SingleStepRamByte>((int)ramCount * 2);

        for (var i = 0; i < ramCount; i++)
        {
            var addr = reader.ReadUInt32();
            var data = reader.ReadUInt16();

            if (addr >= 0x1000000)
                throw new InvalidDataException($"RAM address out of range: 0x{addr:X}.");

            ram.Add(new SingleStepRamByte { Address = addr, Value = (byte)(data >> 8) });
            ram.Add(new SingleStepRamByte { Address = addr | 1, Value = (byte)(data & 0xFF) });
        }

        return new SingleStepCpuState
        {
            D = d,
            A = a,
            Usp = usp,
            Ssp = ssp,
            Sr = sr,
            Pc = pc,
            Prefetch = [pf0, pf1],
            Ram = ram
        };
    }

    private static List<SingleStepTransaction> ReadTransactions(BinaryReader reader, out uint length)
    {
        _ = reader.ReadUInt32();
        var magic = reader.ReadUInt32();
        ValidateMagic(magic, TransactionsMagic, "Transactions");

        var numCycles = reader.ReadUInt32();
        var numTransactions = reader.ReadUInt32();
        length = numCycles;

        var transactions = new List<SingleStepTransaction>((int)numTransactions);
        for (var i = 0; i < numTransactions; i++)
        {
            var tw = reader.ReadByte();
            var cycles = reader.ReadUInt32();

            if (tw == 0)
            {
                transactions.Add(new SingleStepTransaction
                {
                    Kind = "n",
                    Cycles = cycles
                });
                continue;
            }

            var fc = reader.ReadUInt32();
            var addrBus = reader.ReadUInt32();
            var dataBus = reader.ReadUInt32();
            var uds = reader.ReadUInt32();
            var lds = reader.ReadUInt32();

            var size = uds + lds == 2 ? ".w" : ".b";

            transactions.Add(new SingleStepTransaction
            {
                Kind = GetTransactionKind(tw),
                Cycles = cycles,
                Fc = fc,
                Address = addrBus,
                Size = size,
                Data = dataBus,
                Uds = uds,
                Lds = lds
            });
        }

        return transactions;
    }

    private static string GetTransactionKind(byte kind) =>
        kind switch
        {
            1 => "w",
            2 => "r",
            3 => "t",
            4 => "re",
            5 => "we",
            _ => throw new InvalidDataException($"Unknown transaction kind: {kind}.")
        };

    private static void ValidateMagic(uint actual, uint expected, string label)
    {
        if (actual != expected)
            throw new InvalidDataException($"{label} magic mismatch. Expected 0x{expected:X}, got 0x{actual:X}.");
    }

    private static int ReadTestCount(FileInfo decodedFile)
    {
        using var stream = decodedFile.OpenRead();
        using var doc = JsonDocument.Parse(stream);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Decoded JSON root is not an array: {decodedFile.FullName}.");

        return doc.RootElement.GetArrayLength();
    }

    private static List<SingleStepTestCase> ReadJson(FileInfo decodedFile, JsonSerializerOptions options)
    {
        using var stream = decodedFile.OpenRead();
        var tests = JsonSerializer.Deserialize<List<SingleStepTestCase>>(stream, options);
        return tests ?? [];
    }

    private static void WriteJson(FileInfo outputFile, List<SingleStepTestCase> tests)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(tests, options);
        outputFile.WriteAllText(json);
    }

    private static string GetGroupName(SingleStepInstructionGroup group) =>
        group switch
        {
            SingleStepInstructionGroup.Move => "MOVE",
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
        };

    private static string GetPattern(SingleStepInstructionGroup group) =>
        group switch
        {
            SingleStepInstructionGroup.Move => "MOVE*.json.bin",
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
        };
}
