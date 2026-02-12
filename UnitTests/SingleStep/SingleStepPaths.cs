// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Reflection;
using DTC.Core.Extensions;

namespace UnitTests.SingleStep;

/// <summary>
/// Provides path helpers for the single-step test data.
/// </summary>
public static class SingleStepPaths
{
    private static DirectoryInfo m_repoRoot;

    private static DirectoryInfo RepoRoot => m_repoRoot ??= FindRepoRoot();
    public static DirectoryInfo ExternalRoot => RepoRoot.GetDir("external");
    private static DirectoryInfo SingleStepRoot => ExternalRoot.GetDir("m68000");

    public static DirectoryInfo TestDataRoot => SingleStepRoot.GetDir("v1");
    public static DirectoryInfo DecodedRoot => SingleStepRoot.GetDir("decoded");

    public static void EnsureTestDataAvailable()
    {
        if (!TestDataRoot.Exists())
            Assert.Fail($"Missing test data folder: {TestDataRoot.FullName}. Did you init the m68000 submodule?");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = Assembly.GetExecutingAssembly().GetDirectory();
        while (dir != null)
        {
            var sln = dir.GetFiles("G33kST.sln", SearchOption.TopDirectoryOnly);
            if (sln.Length > 0)
                return dir;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate G33kST.sln. Ensure tests are run from within the repo.");
    }
}
