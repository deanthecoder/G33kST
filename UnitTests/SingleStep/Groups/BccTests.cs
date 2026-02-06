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
[Parallelizable(ParallelScope.All)]
public sealed class BccTests : CpuTestBase
{
    protected override string GroupName => "Bcc";
    protected override IReadOnlyList<FileInfo> SourceFiles => GetFiles("Bcc");

    public static IEnumerable<TestCaseData> TestFiles => CreateCases("Bcc");

    [TestCaseSource(nameof(TestFiles))]
    public void FileDecodesAndSeedsRam(FileInfo sourceFile)
    {
        RunJsonTests(sourceFile);
    }
}
