// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace UnitTests;

/// <summary>
/// Base class for all test fixtures, providing common utilities like project directory access.
/// </summary>
public abstract class TestsBase
{
    private static DirectoryInfo s_projectDir;

    /// <summary>
    /// Gets the project directory for the G33kST solution.
    /// This can be used to locate test files, ROMs, and other resources.
    /// </summary>
    protected static DirectoryInfo ProjectDir => s_projectDir ??= GetProjectDir();

    /// <summary>
    /// Gets the project directory for the G33kST solution.
    /// This can be used by static helper classes.
    /// </summary>
    public static DirectoryInfo GetProjectDir()
    {
        if (s_projectDir != null)
            return s_projectDir;

        // Start from the test assembly location and walk up to find the solution root
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "G33kST.sln")))
        {
            dir = dir.Parent;
        }

        return s_projectDir = dir ?? throw new InvalidOperationException("Could not find project root directory (G33kST.sln)");
    }
}
