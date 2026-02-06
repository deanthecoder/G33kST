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
    private Bus m_bus;
    private Cpu m_cpu;

    protected abstract string GroupName { get; }
    protected abstract IReadOnlyList<FileInfo> SourceFiles { get; }
    protected virtual bool ExecuteStep => false;

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

        Assert.Multiple(() =>
        {
            for (var i = 0; i < state.D.Length; i++)
                Assert.That(m_cpu.Registers.GetDataRegister(i), Is.EqualTo(state.D[i]), $"D{i} mismatch.");

            for (var i = 0; i < state.A.Length; i++)
                Assert.That(m_cpu.Registers.GetAddressRegister(i), Is.EqualTo(state.A[i]), $"A{i} mismatch.");

            Assert.That(m_cpu.Registers.StatusRegister, Is.EqualTo(expectedStatus), "SR mismatch.");
            Assert.That(m_cpu.Registers.ProgramCounter, Is.EqualTo(state.Pc), "PC mismatch.");
            Assert.That(m_cpu.Registers.UserStackPointer, Is.EqualTo(state.Usp), "USP mismatch.");
            Assert.That(m_cpu.Registers.SupervisorStackPointer, Is.EqualTo(state.Ssp), "SSP mismatch.");
            Assert.That(m_cpu.Registers.StackPointer, Is.EqualTo(expectedStackPointer), "A7/active stack pointer mismatch.");

            foreach (var entry in state.Ram)
            {
                if (entry.Address >= (uint)ram.Length)
                    throw new ArgumentOutOfRangeException(nameof(testCase), $"Final RAM address 0x{entry.Address:X} is outside bus space (0x{ram.Length:X}).");

                Assert.That(ram[(int)entry.Address], Is.EqualTo(entry.Value), $"RAM mismatch at 0x{entry.Address:X6}.");
            }
        });
    }

    protected void RunJsonTests(FileInfo sourceFile)
    {
        AssertFileDecoded(sourceFile);

        var testCases = LoadTests(sourceFile);
        Assert.Multiple(() =>
        {
            foreach (var testCase in testCases)
                RunJsonTestCase(testCase);
        });
    }

    private void RunJsonTestCase(SingleStepTestCase testCase)
    {
        ApplyInitialRamState(testCase);
        ApplyInitialRegisterState(testCase);
        if (!ExecuteStep)
            return;

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

    protected static IReadOnlyList<FileInfo> GetFiles(string baseName) =>
        m_filesByBase.Value.TryGetValue(baseName, out var files) ? files : [];

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
