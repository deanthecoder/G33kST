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
using System.Text;

namespace UnitTests.SingleStep;

/// <summary>
/// Shared helpers for single-step CPU test fixtures.
/// </summary>
public abstract class CpuTestBase
{
    private static readonly Lazy<IReadOnlyDictionary<string, FileInfo[]>> FilesByBase = new(BuildFilesByBase);
    private IReadOnlyDictionary<string, SingleStepTestFile> m_decodedLookup;
    private Bus m_bus;
    private Cpu m_cpu;

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
        m_bus = new Bus(0x1000000);
        m_cpu = new Cpu(m_bus);
    }

    private void AssertFileDecoded(FileInfo sourceFile)
    {
        var decoded = GetDecodedFile(sourceFile);
        Assert.That(decoded.DecodedFile, Does.Exist);
        Assert.That(decoded.TestCount, Is.GreaterThan(0));
    }

    private IReadOnlyList<SingleStepTestCase> LoadTests(FileInfo sourceFile)
    {
        var decoded = GetDecodedFile(sourceFile);
        return SingleStepDecoder.LoadDecodedTests(decoded.DecodedFile);
    }

    private void ApplyInitialRamState(SingleStepTestCase testCase)
    {
        if (m_bus == null)
            throw new InvalidOperationException("Bus has not been created for this test.");

        var data = m_bus.MainMemory.Data;
        Array.Clear(data, 0, data.Length);

        foreach (var entry in testCase.Initial.Ram)
        {
            if (entry.Address >= (uint)data.Length)
                throw new ArgumentOutOfRangeException(nameof(testCase), $"RAM address 0x{entry.Address:X} is outside bus space (0x{data.Length:X}).");
            data[(int)entry.Address] = entry.Value;
        }
    }

    private void ApplyInitialRegisterState(SingleStepTestCase testCase)
    {
        if (m_cpu == null)
            throw new InvalidOperationException("CPU has not been created for this test.");

        var state = testCase.Initial;
        ValidateStateShape(state, nameof(testCase.Initial));

        for (var i = 0; i < state.D.Length; i++)
            m_cpu.Registers.SetDataRegister(i, state.D[i]);

        for (var i = 0; i < state.A.Length; i++)
            m_cpu.Registers.SetAddressRegister(i, state.A[i]);

        m_cpu.Registers.StatusRegister = (ushort)state.Sr;
        m_cpu.Registers.ProgramCounter = state.Pc;
        m_cpu.Registers.UserStackPointer = state.Usp;
        m_cpu.Registers.SupervisorStackPointer = state.Ssp;
        m_cpu.Registers.StackPointer = m_cpu.Registers.IsSupervisor ? state.Ssp : state.Usp;
        m_cpu.SeedPrefetch((ushort)state.Prefetch[0], (ushort)state.Prefetch[1]);
    }

    private void AssertFinalCpuState(SingleStepTestCase testCase)
    {
        if (m_cpu == null)
            throw new InvalidOperationException("CPU has not been created for this test.");
        if (m_bus == null)
            throw new InvalidOperationException("Bus has not been created for this test.");

        var state = testCase.Final;
        ValidateStateShape(state, nameof(testCase.Final));

        var expectedStatus = (ushort)state.Sr;
        var expectedStackPointer = (expectedStatus & 0x2000) != 0 ? state.Ssp : state.Usp;
        var ram = m_bus.MainMemory.Data;
        var failures = new List<string>();
        const int maxFailures = 20;

        void RecordFailure(string message)
        {
            if (failures.Count >= maxFailures)
                return;

            failures.Add(message);
            if (failures.Count == maxFailures)
                failures.Add("Additional mismatches omitted.");
        }

        for (var i = 0; i < state.D.Length; i++)
        {
            var actual = m_cpu.Registers.GetDataRegister(i);
            var expected = state.D[i];
            if (actual != expected)
                RecordFailure($"D{i} mismatch. Expected 0x{expected:X8}, got 0x{actual:X8}.");
        }

        for (var i = 0; i < state.A.Length; i++)
        {
            var actual = m_cpu.Registers.GetAddressRegister(i);
            var expected = state.A[i];
            if (actual != expected)
                RecordFailure($"A{i} mismatch. Expected 0x{expected:X8}, got 0x{actual:X8}.");
        }

        if (m_cpu.Registers.StatusRegister != expectedStatus)
            RecordFailure($"SR mismatch. Expected 0x{expectedStatus:X4}, got 0x{m_cpu.Registers.StatusRegister:X4}.");
        if (m_cpu.Registers.ProgramCounter != state.Pc)
            RecordFailure($"PC mismatch. Expected 0x{state.Pc:X6}, got 0x{m_cpu.Registers.ProgramCounter:X6}.");
        if (m_cpu.Registers.UserStackPointer != state.Usp)
            RecordFailure($"USP mismatch. Expected 0x{state.Usp:X6}, got 0x{m_cpu.Registers.UserStackPointer:X6}.");
        if (m_cpu.Registers.SupervisorStackPointer != state.Ssp)
            RecordFailure($"SSP mismatch. Expected 0x{state.Ssp:X6}, got 0x{m_cpu.Registers.SupervisorStackPointer:X6}.");
        if (m_cpu.Registers.StackPointer != expectedStackPointer)
            RecordFailure($"A7/active stack pointer mismatch. Expected 0x{expectedStackPointer:X6}, got 0x{m_cpu.Registers.StackPointer:X6}.");

        foreach (var entry in state.Ram)
        {
            if (entry.Address >= (uint)ram.Length)
                throw new ArgumentOutOfRangeException(nameof(testCase), $"Final RAM address 0x{entry.Address:X} is outside bus space (0x{ram.Length:X}).");

            var actual = ram[(int)entry.Address];
            if (actual != entry.Value)
                RecordFailure($"RAM mismatch at 0x{entry.Address:X6}. Expected 0x{entry.Value:X2}, got 0x{actual:X2}.");
        }

        if (failures.Count > 0)
            throw new AssertionException(string.Join(" | ", failures));
    }

    protected void RunJsonTests(FileInfo sourceFile)
    {
        AssertFileDecoded(sourceFile);

        var testCases = LoadTests(sourceFile);
        var passed = 0;
        var failures = new List<string>();
        var opcodeFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var testCase in testCases)
        {
            try
            {
                RunJsonTestCase(testCase);
                passed++;
            }
            catch (Exception ex)
            {
                failures.Add(FormatFailure(testCase, ex));
                var opcode = TryExtractOpcode(ex);
                if (opcode != null)
                    opcodeFailures[opcode] = opcodeFailures.TryGetValue(opcode, out var count) ? count + 1 : 1;
            }
        }

        var failed = testCases.Count - passed;
        if (failed == 0)
        {
            TestContext.Progress.WriteLine($"Single-step cases passed: {passed}.");
            return;
        }

        Assert.Fail(BuildFailureSummary(testCases.Count, passed, failures, opcodeFailures));
    }

    private void RunJsonTestCase(SingleStepTestCase testCase)
    {
        ApplyInitialRamState(testCase);
        ApplyInitialRegisterState(testCase);
        m_cpu.Step();
        AssertFinalCpuState(testCase);
    }

    private static void ValidateStateShape(SingleStepCpuState state, string stateName)
    {
        if (state.D.Length != 8)
            throw new InvalidOperationException($"{stateName}: expected 8 data registers, got {state.D.Length}.");
        if (state.A.Length != 7)
            throw new InvalidOperationException($"{stateName}: expected 7 address registers, got {state.A.Length}.");
        if (state.Sr > ushort.MaxValue)
            throw new InvalidOperationException($"{stateName}: status register value 0x{state.Sr:X} exceeds 16 bits.");
        if (state.Prefetch.Length != 2)
            throw new InvalidOperationException($"{stateName}: expected 2 prefetch words, got {state.Prefetch.Length}.");
        if (state.Prefetch.Any(o => o > ushort.MaxValue))
            throw new InvalidOperationException($"{stateName}: prefetch values must fit in 16 bits.");
    }

    private static string FormatFailure(SingleStepTestCase testCase, Exception exception)
    {
        var name = string.IsNullOrWhiteSpace(testCase.Name) ? string.Empty : $"{testCase.Name}: ";
        var message = NormalizeFailureMessage(exception.Message);
        return $"{name}{exception.GetType().Name} - {message}";
    }

    private static string TryExtractOpcode(Exception exception)
    {
        var message = exception.Message;
        const string token = "Opcode ";
        var index = message.IndexOf(token, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var start = index + token.Length;
        var end = message.IndexOf(' ', start);
        var opcode = end > start ? message[start..end] : message[start..];
        return string.IsNullOrWhiteSpace(opcode) ? null : opcode.Trim();
    }

    private static string BuildFailureSummary(
        int total,
        int passed,
        IReadOnlyList<string> failures,
        IReadOnlyDictionary<string, int> opcodeFailures)
    {
        const int maxLines = 10;
        var failed = total - passed;
        var builder = new StringBuilder();
        builder.AppendLine($"Failed {failed} of {total} cases. Passed {passed}.");

        if (opcodeFailures.Count > 0)
        {
            builder.AppendLine("Missing opcode counts:");
            foreach (var entry in opcodeFailures
                .OrderByDescending(o => o.Value)
                .ThenBy(o => o.Key, StringComparer.OrdinalIgnoreCase)
                .Take(maxLines))
            {
                builder.AppendLine($"  {entry.Key}: {entry.Value}");
            }
        }

        builder.AppendLine("Sample failures:");
        foreach (var line in failures.Take(maxLines))
            builder.AppendLine($"  {line}");

        return builder.ToString();
    }

    private static string NormalizeFailureMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        const string marker = "Multiple failures or warnings in test:";
        var lines = message
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Replace(marker, string.Empty, StringComparison.Ordinal).Trim())
            .Where(line => line.Length > 0)
            .ToList();

        return lines.Count == 0 ? string.Empty : string.Join(" | ", lines);
    }

    protected static IReadOnlyList<FileInfo> GetFiles(string baseName) =>
        FilesByBase.Value.TryGetValue(baseName, out var files) ? files : [];

    protected static IEnumerable<TestCaseData> CreateCases(string baseName)
    {
        foreach (var file in GetFiles(baseName))
            yield return new TestCaseData(file).SetName(file.LeafName().Replace(".json", string.Empty));
    }

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
