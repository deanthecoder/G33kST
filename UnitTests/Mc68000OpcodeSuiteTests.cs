// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation;
using DTC.M68000;
using System.Text.RegularExpressions;
using DTC.Core.Extensions;
using UnitTests.SingleStep;

namespace UnitTests;

/// <summary>
/// Integration-style runner for the external MicroCore Labs MC68000 opcode suite.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class Mc68000OpcodeSuiteTests
{
    private const int MaxStepsFullSuite = 2_000_000;
    
    private static readonly Regex LabelRegex = new(
        @"^(?<address>[0-9A-Fa-f]{8})\s+.*?\b(?<label>[A-Za-z_][A-Za-z0-9_]*):",
        RegexOptions.Compiled);
    private static readonly Regex JsrEntryRegex = new(
        @"^(?<address>[0-9A-Fa-f]{8})\s+.*?\bjsr\s+(?<label>op_[A-Za-z0-9_]+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Lazy<SuiteAssets> m_suiteAssets = new(LoadSuiteAssets);

    public static IEnumerable<TestCaseData> OpcodeSections =>
        m_suiteAssets.Value.Listing.TestEntries
            .Select(o => new TestCaseData(o.Name).SetName($"OpcodeSuiteSection_{o.Name}"));

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        var suiteAssets = m_suiteAssets.Value;
        TestContext.Progress.WriteLine($"Loaded external MC68000 opcode suite metadata ({suiteAssets.Listing.TestEntries.Count} sections).");
    }

    [Test]
    public void RunsMicroCoreLabsOpcodeSuiteFull()
    {
        var runResult = RunSuite(targetSectionName: string.Empty, MaxStepsFullSuite, stopOnError: false);
        var summary = BuildSummary(runResult, targetSectionName: string.Empty);
        TestContext.Progress.WriteLine(summary);
        AssertRunSucceeded(runResult, targetSectionName: string.Empty, MaxStepsFullSuite, summary);
    }

    [TestCaseSource(nameof(OpcodeSections))]
    public void RunsMicroCoreLabsOpcodeSuiteSection(string sectionName)
    {
        var runResult = RunSuite(sectionName, MaxStepsFullSuite, stopOnError: true);
        var summary = BuildSummary(runResult, sectionName);
        TestContext.Progress.WriteLine(summary);
        AssertRunSucceeded(runResult, sectionName, MaxStepsFullSuite, summary);
    }

    private static SuiteRunResult RunSuite(string targetSectionName, int maxSteps, bool stopOnError)
    {
        var suiteAssets = m_suiteAssets.Value;
        var listing = suiteAssets.Listing;

        var bus = new Bus(0x1000000);
        LoadBinary(bus, suiteAssets.BinaryFile);
        var cpu = new Cpu(bus);
        cpu.Reset();

        var isSingleSectionMode = targetSectionName.Length > 0;
        uint? sectionCompleteAddress = null;
        var currentTest = string.Empty;
        if (isSingleSectionMode)
        {
            var targetEntry = ResolveTargetEntry(listing, targetSectionName);
            cpu.Registers.ProgramCounter = targetEntry.CallAddress;
            currentTest = targetEntry.Name;
            sectionCompleteAddress = ResolveSectionCompleteAddress(listing, targetEntry.Name);
        }

        var steps = 0;
        var reachedAllDone = false;
        var reachedSectionBoundary = false;
        var failedLabel = string.Empty;
        Exception failureException = null;
        var previousPc = uint.MaxValue;
        while (steps < maxSteps && (!stopOnError || string.IsNullOrEmpty(failedLabel)))
        {
            var pc = cpu.Registers.ProgramCounter & 0x00FF_FFFF;
            if (listing.AddressToTestName.TryGetValue(pc, out var enteredTest))
                currentTest = enteredTest;

            if (listing.FailLabelsByAddress.TryGetValue(pc, out var failLabel))
            {
                failedLabel = failLabel;
                break;
            }

            if (sectionCompleteAddress.HasValue && sectionCompleteAddress.Value == pc)
            {
                reachedSectionBoundary = true;
                break;
            }

            if (listing.AllDoneAddress == pc)
            {
                reachedAllDone = true;
                break;
            }

            try
            {
                cpu.Step();
            }
            catch (Exception ex)
            {
                failureException = ex;
                break;
            }

            steps++;

            // Detect infinite loop (branch-to-self indicating test failure)
            var newPc = cpu.Registers.ProgramCounter & 0x00FF_FFFF;
            if (newPc == previousPc)
            {
                failedLabel = $"STUCK_AT_0x{newPc:X6}";
                break;
            }
            previousPc = newPc;
        }

        return new SuiteRunResult(
            steps,
            cpu.Registers.ProgramCounter & 0x00FF_FFFF,
            currentTest,
            failedLabel,
            failureException,
            reachedAllDone,
            reachedSectionBoundary);
    }

    private static string BuildSummary(SuiteRunResult runResult, string targetSectionName)
    {
        var listing = m_suiteAssets.Value.Listing;
        var failedTest = ResolveFailedTestName(listing, runResult.CurrentTest, runResult.FailedLabel);
        var fullTestOrder = listing.TestEntries.Select(o => o.Name).ToArray();

        if (targetSectionName.Length != 0)
        {
            return
                $"MC68000 suite steps executed: {runResult.Steps:N0}.{Environment.NewLine}" +
                $"Current PC: 0x{runResult.ProgramCounter:X6}.";
        }

        var passedTests = ResolvePassedTests(fullTestOrder, failedTest);
        var notRunTests = ResolveNotRunTests(fullTestOrder, passedTests, failedTest);
        return
            $"MC68000 suite steps executed: {runResult.Steps:N0}.{Environment.NewLine}" +
            $"Current PC: 0x{runResult.ProgramCounter:X6}.{Environment.NewLine}" +
            $"Passed tests ({passedTests.Count}): {JoinOrNone(passedTests)}{Environment.NewLine}" +
            $"Failed tests ({(failedTest.Length == 0 ? 0 : 1)}): {JoinOrNone([failedTest])}{Environment.NewLine}" +
            $"Did not run ({notRunTests.Count}): {JoinOrNone(notRunTests)}";
    }

    private static void AssertRunSucceeded(SuiteRunResult runResult, string targetSectionName, int maxSteps, string summary)
    {
        if (IsRunSuccessful(runResult, targetSectionName))
            return;

        var failureReason = runResult.FailureException != null
            ? $"Exception during execution: {runResult.FailureException.GetType().Name}: {runResult.FailureException.Message}"
            : runResult.FailedLabel.Length > 0
                ? $"Reached: {runResult.FailedLabel}."
                : runResult.Steps >= maxSteps
                    ? $"Exceeded max steps ({maxSteps}) without reaching the completion condition."
                    : targetSectionName.Length == 0
                        ? "Did not reach ALL_DONE."
                        : $"Did not complete section '{targetSectionName}'.";

        Assert.Fail($"{failureReason}{Environment.NewLine}{summary}");
    }

    private static bool IsRunSuccessful(SuiteRunResult runResult, string targetSectionName)
    {
        if (runResult.FailureException != null || runResult.FailedLabel.Length > 0)
            return false;

        return targetSectionName.Length == 0
            ? runResult.ReachedAllDone
            : runResult.ReachedSectionBoundary || runResult.ReachedAllDone;
    }

    private static SuiteAssets LoadSuiteAssets()
    {
        var suiteDirectory = ResolveSuiteDirectory();
        var suiteBinaryFile = new FileInfo(Path.Combine(suiteDirectory.FullName, "Test_Suite.bin"));
        var listingFile = new FileInfo(Path.Combine(suiteDirectory.FullName, "MC68000_test_all_opcodes.L68"));
        if (!suiteBinaryFile.Exists)
            throw new FileNotFoundException($"Missing suite binary: {suiteBinaryFile.FullName}", suiteBinaryFile.FullName);
        if (!listingFile.Exists)
            throw new FileNotFoundException($"Missing suite listing: {listingFile.FullName}", listingFile.FullName);

        var listing = ParseListing(listingFile);
        if (listing.AllDoneAddress is null)
            throw new InvalidOperationException("Could not find ALL_DONE label in listing.");
        if (listing.TestEntries.Count == 0)
            throw new InvalidOperationException("Could not find jsr op_* test entry sequence in listing.");

        return new SuiteAssets(suiteBinaryFile, listing);
    }

    private static DirectoryInfo ResolveSuiteDirectory()
    {
        return new DirectoryInfo(Path.Combine(SingleStepPaths.ExternalRoot.FullName, "MC68000_Test_Code"));
    }

    private static void LoadBinary(Bus bus, FileInfo binaryFile)
    {
        var bytes = File.ReadAllBytes(binaryFile.FullName);
        var memory = bus.MainMemory.Data;
        Assert.That(bytes.Length, Is.LessThanOrEqualTo(memory.Length), "Suite binary does not fit in bus memory.");
        Array.Clear(memory, 0, memory.Length);
        Array.Copy(bytes, memory, bytes.Length);
    }

    private static SuiteListing ParseListing(FileInfo listingFile)
    {
        var labelToAddress = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var testCallSites = new List<SuiteCallSite>();
        var seenCallSiteLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(listingFile.FullName))
        {
            var labelMatch = LabelRegex.Match(line);
            if (labelMatch.Success)
            {
                var label = labelMatch.Groups["label"].Value;
                if (!labelToAddress.ContainsKey(label))
                    labelToAddress[label] = Convert.ToUInt32(labelMatch.Groups["address"].Value, 16);
            }

            var jsrMatch = JsrEntryRegex.Match(line);
            if (!jsrMatch.Success)
                continue;

            var testLabel = jsrMatch.Groups["label"].Value;
            if (!seenCallSiteLabels.Add(testLabel))
                continue;

            var callAddress = Convert.ToUInt32(jsrMatch.Groups["address"].Value, 16);
            testCallSites.Add(new SuiteCallSite(testLabel, callAddress));
        }

        var testEntries = new List<SuiteTestEntry>();
        var addressToTestName = new Dictionary<uint, string>();
        foreach (var callSite in testCallSites)
        {
            if (!labelToAddress.TryGetValue(callSite.Label, out var entryAddress))
                continue;

            testEntries.Add(new SuiteTestEntry(callSite.Label, callSite.Address, entryAddress));
            if (!addressToTestName.ContainsKey(entryAddress))
                addressToTestName[entryAddress] = callSite.Label;
        }

        var failLabelsByAddress = new Dictionary<uint, string>();
        var failLabelToTestName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var knownTests = new HashSet<string>(testEntries.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in labelToAddress)
        {
            if (!pair.Key.EndsWith("_FAIL", StringComparison.OrdinalIgnoreCase))
                continue;

            failLabelsByAddress[pair.Value] = pair.Key;
            var candidate = $"op_{pair.Key[..^5]}";
            if (knownTests.Contains(candidate))
                failLabelToTestName[pair.Key] = candidate;
        }

        labelToAddress.TryGetValue("ALL_DONE", out var allDoneAddress);
        return new SuiteListing(
            allDoneAddress == 0 ? null : allDoneAddress,
            testEntries,
            addressToTestName,
            failLabelsByAddress,
            failLabelToTestName);
    }

    private static SuiteTestEntry ResolveTargetEntry(SuiteListing listing, string targetSectionName)
    {
        foreach (var entry in listing.TestEntries)
        {
            if (string.Equals(entry.Name, targetSectionName, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        throw new ArgumentException($"Unknown section '{targetSectionName}'.", nameof(targetSectionName));
    }

    private static uint? ResolveSectionCompleteAddress(SuiteListing listing, string targetSectionName)
    {
        for (var i = 0; i < listing.TestEntries.Count; i++)
        {
            var entry = listing.TestEntries[i];
            if (!string.Equals(entry.Name, targetSectionName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 >= listing.TestEntries.Count)
                return null;

            return listing.TestEntries[i + 1].CallAddress;
        }

        return null;
    }

    private static string ResolveFailedTestName(SuiteListing listing, string currentTest, string failedLabel)
    {
        if (failedLabel.Length > 0 && listing.FailLabelToTestName.TryGetValue(failedLabel, out var mapped))
            return mapped;

        return currentTest;
    }

    private static IReadOnlyList<string> ResolvePassedTests(IReadOnlyList<string> orderedTests, string failedTest)
    {
        if (failedTest.Length == 0)
            return orderedTests;

        var passed = new List<string>();
        foreach (var test in orderedTests)
        {
            if (string.Equals(test, failedTest, StringComparison.OrdinalIgnoreCase))
                break;

            passed.Add(test);
        }

        return passed;
    }

    private static IReadOnlyList<string> ResolveNotRunTests(IReadOnlyList<string> orderedTests, IReadOnlyList<string> passedTests, string failedTest)
    {
        var notRun = new List<string>();
        foreach (var test in orderedTests)
        {
            if (passedTests.Contains(test, StringComparer.OrdinalIgnoreCase))
                continue;
            if (failedTest.Length > 0 && string.Equals(test, failedTest, StringComparison.OrdinalIgnoreCase))
                continue;

            notRun.Add(test);
        }

        return notRun;
    }

    private static string JoinOrNone(IEnumerable<string> values)
    {
        var items = values.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
        return items.Length == 0 ? "<none>" : items.ToCsv(addSpace: true);
    }

    private sealed record SuiteAssets(
        FileInfo BinaryFile,
        SuiteListing Listing);

    private sealed record SuiteListing(
        uint? AllDoneAddress,
        IReadOnlyList<SuiteTestEntry> TestEntries,
        IReadOnlyDictionary<uint, string> AddressToTestName,
        IReadOnlyDictionary<uint, string> FailLabelsByAddress,
        IReadOnlyDictionary<string, string> FailLabelToTestName);

    private sealed record SuiteTestEntry(
        string Name,
        uint CallAddress,
        uint EntryAddress);

    private sealed record SuiteCallSite(
        string Label,
        uint Address);

    private sealed record SuiteRunResult(
        int Steps,
        uint ProgramCounter,
        string CurrentTest,
        string FailedLabel,
        Exception FailureException,
        bool ReachedAllDone,
        bool ReachedSectionBoundary);
}
