// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace UnitTests.SingleStep;

/// <summary>
/// Represents a decoded single-step test case.
/// </summary>
public sealed class SingleStepTestCase
{
    public string Name { get; init; } = string.Empty;
    public SingleStepCpuState Initial { get; init; } = new();
    public SingleStepCpuState Final { get; init; } = new();
    public List<SingleStepTransaction> Transactions { get; init; } = new();
    public uint Length { get; init; }
}

/// <summary>
/// Captures CPU state used by a single-step test case.
/// </summary>
public sealed class SingleStepCpuState
{
    public uint[] D { get; init; } = new uint[8];
    public uint[] A { get; init; } = new uint[7];
    public uint Usp { get; init; }
    public uint Ssp { get; init; }
    public uint Sr { get; init; }
    public uint Pc { get; init; }
    public uint[] Prefetch { get; init; } = new uint[2];
    public List<SingleStepRamByte> Ram { get; init; } = new();
}

/// <summary>
/// Represents a RAM byte entry in a test case.
/// </summary>
public sealed class SingleStepRamByte
{
    public uint Address { get; init; }
    public byte Value { get; init; }
}

/// <summary>
/// Describes a single bus transaction captured by the test.
/// </summary>
public sealed class SingleStepTransaction
{
    public string Kind { get; init; } = string.Empty;
    public uint Cycles { get; init; }
    public uint Fc { get; init; }
    public uint Address { get; init; }
    public string Size { get; init; } = string.Empty;
    public uint Data { get; init; }
    public uint Uds { get; init; }
    public uint Lds { get; init; }
}

/// <summary>
/// Tracks a decoded test file and its source.
/// </summary>
public sealed class SingleStepTestFile
{
    public FileInfo SourceFile { get; }
    public FileInfo DecodedFile { get; }
    public int TestCount { get; }

    public SingleStepTestFile(FileInfo sourceFile, FileInfo decodedFile, int testCount)
    {
        SourceFile = sourceFile;
        DecodedFile = decodedFile;
        TestCount = testCount;
    }
}

/// <summary>
/// Provides a decode summary for a single instruction group.
/// </summary>
public sealed class SingleStepDecodeResult
{
    public string GroupName { get; }
    public IReadOnlyList<SingleStepTestFile> Files { get; }
    public DirectoryInfo DecodedDirectory { get; }

    public SingleStepDecodeResult(
        string groupName,
        IReadOnlyList<SingleStepTestFile> files,
        DirectoryInfo decodedDirectory)
    {
        GroupName = groupName;
        Files = files;
        DecodedDirectory = decodedDirectory;
    }
}
