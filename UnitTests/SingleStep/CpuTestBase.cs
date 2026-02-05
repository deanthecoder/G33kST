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

namespace UnitTests.SingleStep;

/// <summary>
/// Shared helpers for single-step CPU test fixtures.
/// </summary>
public abstract class CpuTestBase
{
    private static readonly Lazy<IReadOnlyDictionary<string, FileInfo[]>> m_filesByBase = new(BuildFilesByBase);
    private IReadOnlyDictionary<string, SingleStepTestFile> m_decodedLookup;

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

    protected void AssertFileDecoded(FileInfo sourceFile)
    {
        Assert.That(m_decodedLookup, Is.Not.Null);
        Assert.That(m_decodedLookup.ContainsKey(sourceFile.FullName), Is.True);

        var decoded = m_decodedLookup[sourceFile.FullName];
        Assert.That(decoded.DecodedFile.Exists(), Is.True);
        Assert.That(decoded.TestCount, Is.GreaterThan(0));
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
