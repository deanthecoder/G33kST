// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Extensions;
using DTC.Emulation;
using DTC.M68000;

namespace UnitTests.SingleStep;

/// <summary>
/// Shared helpers for single-step CPU test fixtures.
/// </summary>
public abstract class CpuTestBase
{
    private static readonly Lazy<IReadOnlyDictionary<string, FileInfo[]>> m_filesByBase = new(BuildFilesByBase);
    private IReadOnlyDictionary<string, SingleStepTestFile> m_decodedLookup;

    protected Bus Bus { get; private set; }
    protected Cpu Cpu { get; private set; }

    protected abstract string GroupName { get; }
    protected abstract IReadOnlyList<FileInfo> SourceFiles { get; }

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        var decodeResult = SingleStepDecoder.DecodeFiles(GroupName, SourceFiles);
        m_decodedLookup = decodeResult.Files.ToDictionary(
            o => o.SourceFile.FullName,
            StringComparer.OrdinalIgnoreCase);
    }

    [SetUp]
    public void SetupCpu()
    {
        Bus = CreateBus();
        Cpu = new Cpu(Bus);
    }

    protected void AssertFileDecoded(FileInfo sourceFile)
    {
        var decoded = GetDecodedFile(sourceFile);
        Assert.That(decoded.DecodedFile.Exists(), Is.True);
        Assert.That(decoded.TestCount, Is.GreaterThan(0));
    }

    protected IReadOnlyList<SingleStepTestCase> LoadTests(FileInfo sourceFile)
    {
        var decoded = GetDecodedFile(sourceFile);
        return SingleStepDecoder.LoadDecodedTests(decoded.DecodedFile);
    }

    protected void ApplyInitialRamState(SingleStepTestCase testCase)
    {
        if (Bus == null)
            throw new InvalidOperationException("Bus has not been created for this test.");

        var data = Bus.MainMemory.Data;
        Array.Clear(data, 0, data.Length);

        foreach (var entry in testCase.Initial.Ram)
        {
            if (entry.Address >= (uint)data.Length)
                throw new ArgumentOutOfRangeException(
                    nameof(testCase),
                    $"RAM address 0x{entry.Address:X} is outside bus space (0x{data.Length:X}).");

            data[(int)entry.Address] = entry.Value;
        }
    }

    protected static IReadOnlyList<FileInfo> GetFiles(string baseName)
    {
        if (m_filesByBase.Value.TryGetValue(baseName, out var files))
            return files;

        return Array.Empty<FileInfo>();
    }

    protected static IEnumerable<TestCaseData> CreateCases(string baseName)
    {
        foreach (var file in GetFiles(baseName))
            yield return new TestCaseData(file).SetName(file.LeafName());
    }

    protected virtual Bus CreateBus() => new(0x1000000);

    private SingleStepTestFile GetDecodedFile(FileInfo sourceFile)
    {
        Assert.That(m_decodedLookup, Is.Not.Null);
        Assert.That(m_decodedLookup.ContainsKey(sourceFile.FullName), Is.True);

        return m_decodedLookup[sourceFile.FullName];
    }

    private static IReadOnlyDictionary<string, FileInfo[]> BuildFilesByBase()
    {
        SingleStepPaths.EnsureTestDataAvailable();

        var files = SingleStepPaths.TestDataRoot.GetFiles("*.json.bin", SearchOption.TopDirectoryOnly);
        return files
            .GroupBy(GetBaseName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string GetBaseName(FileInfo file)
    {
        const string suffix = ".json.bin";
        var name = file.Name;
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            name = name[..^suffix.Length];

        var dot = name.IndexOf('.', StringComparison.Ordinal);
        return dot >= 0 ? name[..dot] : name;
    }
}
