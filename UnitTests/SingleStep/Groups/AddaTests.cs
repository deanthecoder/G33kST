// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace UnitTests.SingleStep.Groups;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public sealed class AddaTests : CpuTestBase
{
    protected override string GroupName => "ADDA";
    protected override IReadOnlyList<FileInfo> SourceFiles => GetFiles("ADDA");

    public static IEnumerable<TestCaseData> TestFiles => CreateCases("ADDA");

    [TestCaseSource(nameof(TestFiles))]
    public void Run(FileInfo sourceFile)
    {
        RunJsonTests(sourceFile);
    }
}
