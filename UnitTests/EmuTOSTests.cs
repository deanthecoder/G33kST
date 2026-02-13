// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Diagnostics;
using DTC.AtariST;
using DTC.Core.Extensions;
using DTC.Core.Image;
using DTC.Emulation.Debuggers;

namespace UnitTests;

/// <summary>
/// Tests that run the actual EmuTOS ROM to verify CPU and machine functionality.
/// These tests are marked Explicit as they require the ROM file and take longer to run.
/// </summary>
[TestFixture]
public sealed class EmuTOSTests : TestsBase
{
    private const int StallTraceLineCount = 40;
    private const uint VideoModeRegister = 0x00FF8260;
    private const bool SaveFrameSamplesToDesktop = false;
    private const bool SaveOnlyChangedFrameSamples = true;

    [Test]
    [Explicit("Requires EmuTOS ROM file and runs for extended duration")]
    public void CheckEmuTosRomRunsWithoutDeadlock()
    {
        // Arrange - Find and load EmuTOS ROM
        var romFile = FindEmuTosRom();
        Assert.That(romFile, Is.Not.Null, "Could not find EmuTOS ROM file");
        Assert.That(romFile.Exists, Is.True, $"EmuTOS ROM file not found: {romFile.FullName}");

        var romData = romFile.ReadAllBytes();
        Assert.That(romData, Is.Not.Null);
        Assert.That(romData.Length, Is.GreaterThan(0));

        // Create machine and load ROM
        var atariST = new AtariST();
        atariST.LoadRom(romData, romFile.Name);
        TestContext.Out.WriteLine($"Reset SSP: ${atariST.Cpu.Registers.SupervisorStackPointer:X8}");
        TestContext.Out.WriteLine($"Reset PC : ${atariST.Cpu.Registers.ProgramCounter:X8}");
        Assert.That(
            atariST.Cpu.Registers.ProgramCounter,
            Is.InRange(0x00FC0000u, 0x00FEFFFFu),
            "Reset PC should point into the ROM range.");
        var instructionTrace = new InstructionTraceDebugger(1024);
        atariST.Cpu.AddDebugger(instructionTrace);

        var executionHealth = new ExecutionHealthTracker(atariST.Descriptor.CpuHz, atariST.Descriptor.VideoHz, stallFrameCount: 2);

        // Act - Run for approximately 10 seconds of emulated time
        var targetCycles = (long)(atariST.Descriptor.CpuHz * 10.0);
        var stopwatch = Stopwatch.StartNew();

        TestContext.Out.WriteLine($"Running EmuTOS for ~10 emulated seconds ({targetCycles:N0} cycles)...");
        TestContext.Out.WriteLine($"ROM: {romFile.Name}");
        TestContext.Out.WriteLine($"CPU: {atariST.Descriptor.CpuHz / 1_000_000.0:F1} MHz");
        TestContext.Out.WriteLine();

        var exceptionCount = 0;
        var lastProgressUpdate = 0L;
        var progressInterval = targetCycles / 10; // Update every 10%
        var instructionCount = 0L;
        var loggedLikelyStall = false;
        var stallReason = string.Empty;
        var lastCpuTicks = atariST.CpuTicks;
        var frameSampleIntervalCycles = (long)Math.Round(atariST.Descriptor.CpuHz);
        var nextFrameSampleCycle = frameSampleIntervalCycles;
        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * 4];
        var blankScreenChecksum = ComputeBlankScreenChecksum(atariST.Video.FrameWidth, atariST.Video.FrameHeight);
        var sawNonBlankFrame = false;
        var firstNonBlankSeenAtCycles = 0L;
        var sampledFrames = 0;
        var savedFrames = 0;
        var hasLastSavedChecksum = false;
        var lastSavedChecksum = 0u;
        var frameCaptureDir = SaveFrameSamplesToDesktop ? CreateDesktopFrameOutputDir() : null;
        var sampledModeCounts = new Dictionary<int, int>();
        var firstSampledMode = -1;
        var firstSampledModeAtCycles = 0L;

        if (frameCaptureDir != null)
            TestContext.Out.WriteLine($"Frame capture enabled: {frameCaptureDir.FullName}");

        while (atariST.CpuTicks < targetCycles)
        {
            try
            {
                var pcBeforeStep = atariST.Cpu.Registers.ProgramCounter & 0x00FF_FFFF;
                atariST.StepCpu();
                var currentCpuTicks = atariST.CpuTicks;
                var deltaTicks = currentCpuTicks - lastCpuTicks;
                executionHealth.Observe(pcBeforeStep, deltaTicks);
                if (deltaTicks > 0)
                    atariST.AdvanceDevices(deltaTicks);
                lastCpuTicks = currentCpuTicks;
                if (atariST.TryConsumeInterrupt())
                    atariST.RequestInterrupt();
                instructionCount++;

                while (atariST.CpuTicks >= nextFrameSampleCycle)
                {
                    sampledFrames++;
                    atariST.Video.CopyToFrameBuffer(frameBuffer);
                    var checksum = ComputeFrameChecksum(frameBuffer);
                    var mode = atariST.Cpu.Bus.Read8(VideoModeRegister) & 0x03;
                    if (sampledModeCounts.TryGetValue(mode, out var modeCount))
                        sampledModeCounts[mode] = modeCount + 1;
                    else
                        sampledModeCounts[mode] = 1;
                    if (firstSampledMode < 0)
                    {
                        firstSampledMode = mode;
                        firstSampledModeAtCycles = nextFrameSampleCycle;
                    }

                    if (!sawNonBlankFrame && checksum != blankScreenChecksum)
                    {
                        sawNonBlankFrame = true;
                        firstNonBlankSeenAtCycles = nextFrameSampleCycle;
                    }

                    if (frameCaptureDir != null)
                    {
                        var frameChanged = !hasLastSavedChecksum || checksum != lastSavedChecksum;
                        if (!SaveOnlyChangedFrameSamples || frameChanged)
                        {
                            var elapsedSeconds = nextFrameSampleCycle / atariST.Descriptor.CpuHz;
                            var fileName = $"emutos_{elapsedSeconds:0000.000}s.tga";
                            var tgaFile = frameCaptureDir.GetFile(fileName);
                            TgaWriter.Write(tgaFile, frameBuffer, atariST.Video.FrameWidth, atariST.Video.FrameHeight, 4);
                            savedFrames++;
                            lastSavedChecksum = checksum;
                            hasLastSavedChecksum = true;
                        }
                    }

                    nextFrameSampleCycle += frameSampleIntervalCycles;
                }

                // Progress reporting
                if (atariST.CpuTicks - lastProgressUpdate >= progressInterval)
                {
                    var progress = atariST.CpuTicks * 100.0 / targetCycles;
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    var cyclesPerSecond = atariST.CpuTicks / elapsed;
                    TestContext.Out.WriteLine($"Progress: {progress:F1}% | {atariST.CpuTicks:N0} cycles | " +
                                        $"{cyclesPerSecond / 1_000_000.0:F2} MHz real | " +
                                        $"{instructionCount:N0} steps");

                    if (!loggedLikelyStall && executionHealth.TryGetLikelyStallReason(out var reason))
                    {
                        loggedLikelyStall = true;
                        stallReason = reason;
                        TestContext.Out.WriteLine($"[Health] Likely stall detected: {reason}");
                        var recentTrace = instructionTrace.GetRecentLines(StallTraceLineCount);
                        if (recentTrace.Count > 0)
                        {
                            TestContext.Out.WriteLine("[Health] Recent instruction trace:");
                            foreach (var traceLine in recentTrace)
                                TestContext.Out.WriteLine($"  {traceLine}");
                        }
                    }

                    lastProgressUpdate = atariST.CpuTicks;
                }
            }
            catch (Exception ex)
            {
                exceptionCount++;
                TestContext.Out.WriteLine($"Exception at PC=${atariST.Cpu.Registers.ProgramCounter:X8}, " +
                                    $"Cycles={atariST.CpuTicks:N0}: {ex.Message}");
                var recentTrace = instructionTrace.GetRecentLines(StallTraceLineCount);
                if (recentTrace.Count > 0)
                {
                    TestContext.Out.WriteLine("Recent instruction trace before exception:");
                    foreach (var traceLine in recentTrace)
                        TestContext.Out.WriteLine($"  {traceLine}");
                }

                // Stop if too many exceptions
                if (exceptionCount > 100)
                {
                    TestContext.Out.WriteLine("Too many exceptions, stopping execution.");
                    break;
                }
            }
        }

        stopwatch.Stop();

        // Assert - Report results
        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine("=== Execution Summary ===");
        TestContext.Out.WriteLine($"Total cycles: {atariST.CpuTicks:N0}");
        TestContext.Out.WriteLine($"Real time: {stopwatch.Elapsed.TotalSeconds:F2}s");
        TestContext.Out.WriteLine($"Average speed: {atariST.CpuTicks / stopwatch.Elapsed.TotalSeconds / 1_000_000.0:F2} MHz");
        TestContext.Out.WriteLine($"Instruction steps: {instructionCount:N0}");
        TestContext.Out.WriteLine($"Longest same-PC run: {executionHealth.LongestSamePcRunLength:N0} steps / {executionHealth.LongestSamePcRunCycles:N0} cycles ({executionHealth.LongestSamePcRunMilliseconds:F2} ms) at ${executionHealth.LongestSamePcRunPc:X6}");
        TestContext.Out.WriteLine($"Exceptions: {exceptionCount}");
        TestContext.Out.WriteLine($"Top PCs: {executionHealth.FormatTopPcs(8)}");
        TestContext.Out.WriteLine($"Blank checksum: 0x{blankScreenChecksum:X8}");
        TestContext.Out.WriteLine($"Frame samples: {sampledFrames} (saved: {savedFrames})");
        TestContext.Out.WriteLine($"Saw non-blank frame: {sawNonBlankFrame} at {firstNonBlankSeenAtCycles:N0} cycles");
        TestContext.Out.WriteLine($"Video mode samples: {FormatVideoModeSamples(sampledModeCounts)}");
        TestContext.Out.WriteLine($"First sampled video mode: {DescribeVideoMode(firstSampledMode)} at {firstSampledModeAtCycles:N0} cycles");
        TestContext.Out.WriteLine();

        // Basic sanity checks
        Assert.That(atariST.CpuTicks, Is.GreaterThan(0), "CPU should have executed some cycles");
        Assert.That(atariST.CpuTicks, Is.GreaterThanOrEqualTo(targetCycles), "Execution ended before reaching target cycles.");
        Assert.That(exceptionCount, Is.EqualTo(0), "Unexpected exceptions were raised while running the ROM.");
        Assert.That(
            loggedLikelyStall,
            Is.False,
            $"Likely deadlock detected: {stallReason}");
        Assert.That(sampledFrames, Is.GreaterThan(0), "No frame samples were captured.");
        Assert.That(sawNonBlankFrame, Is.True, "Did not observe a non-blank frame during boot.");
        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"Test completed. Check output above for EmuTOS behavior.");
    }

    private DirectoryInfo CreateDesktopFrameOutputDir()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var outputDirName = $"G33kST_EmuTOSFrames_{DateTime.Now:yyyyMMdd_HHmmss}";
        var outputDir = new DirectoryInfo(Path.Combine(desktopPath, outputDirName));
        if (!outputDir.Exists)
            outputDir.Create();

        return outputDir;
    }

    private FileInfo FindEmuTosRom()
    {
        // Use ProjectDir to locate the TOS directory
        var tosDir = new DirectoryInfo(Path.Combine(ProjectDir.FullName, "DTC.AtariST", "TOS"));

        if (!tosDir.Exists)
            return null;

        // Look for any .img or .rom file
        var romFiles = tosDir.GetFiles("*.img", SearchOption.TopDirectoryOnly)
            .Concat(tosDir.GetFiles("*.rom", SearchOption.TopDirectoryOnly))
            .ToArray();

        return romFiles.Length > 0 ? romFiles[0] : null;
    }

    private static uint ComputeBlankScreenChecksum(int width, int height)
    {
        var blankFrame = new byte[width * height * 4];
        for (var i = 3; i < blankFrame.Length; i += 4)
            blankFrame[i] = 255;
        return ComputeFrameChecksum(blankFrame);
    }

    private static uint ComputeFrameChecksum(byte[] frameBuffer)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var b in frameBuffer)
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    private static string FormatVideoModeSamples(Dictionary<int, int> sampledModeCounts)
    {
        if (sampledModeCounts.Count == 0)
            return "none";

        return string.Join(", ", sampledModeCounts
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{DescribeVideoMode(kv.Key)}={kv.Value}"));
    }

    private static string DescribeVideoMode(int mode) =>
        mode switch
        {
            0 => "0 (low 320x200x16)",
            1 => "1 (medium 640x200x4)",
            2 => "2 (high mono 640x400)",
            _ => $"{mode} (unknown)"
        };

    private sealed class ExecutionHealthTracker
    {
        private readonly double m_cpuHz;
        private readonly long m_samePcStallCycleThreshold;
        private readonly Dictionary<uint, long> m_pcHitCounts = [];
        private uint m_lastPc = uint.MaxValue;
        private long m_currentSamePcRunLength;
        private long m_currentSamePcRunCycles;

        public long LongestSamePcRunLength { get; private set; }
        public long LongestSamePcRunCycles { get; private set; }
        public double LongestSamePcRunMilliseconds => LongestSamePcRunCycles * 1000.0 / m_cpuHz;
        public uint LongestSamePcRunPc { get; private set; }

        public ExecutionHealthTracker(double cpuHz, double videoHz, int stallFrameCount)
        {
            m_cpuHz = cpuHz;
            var ticksPerFrame = (long)Math.Round(cpuHz / videoHz);
            m_samePcStallCycleThreshold = ticksPerFrame * stallFrameCount;
        }

        public void Observe(uint pc, long deltaCycles)
        {
            deltaCycles = Math.Max(deltaCycles, 0);

            if (m_pcHitCounts.TryGetValue(pc, out var currentHits))
                m_pcHitCounts[pc] = currentHits + 1;
            else
                m_pcHitCounts[pc] = 1;

            if (pc == m_lastPc)
            {
                m_currentSamePcRunLength++;
                m_currentSamePcRunCycles += deltaCycles;
            }
            else
            {
                m_lastPc = pc;
                m_currentSamePcRunLength = 1;
                m_currentSamePcRunCycles = deltaCycles;
            }

            if (m_currentSamePcRunCycles > LongestSamePcRunCycles)
            {
                LongestSamePcRunLength = m_currentSamePcRunLength;
                LongestSamePcRunCycles = m_currentSamePcRunCycles;
                LongestSamePcRunPc = pc;
            }
        }

        public bool TryGetLikelyStallReason(out string reason)
        {
            if (m_currentSamePcRunCycles >= m_samePcStallCycleThreshold)
            {
                var currentRunMilliseconds = m_currentSamePcRunCycles * 1000.0 / m_cpuHz;
                reason = $"PC ${m_lastPc:X6} repeated for {m_currentSamePcRunCycles:N0} cycles ({currentRunMilliseconds:F2} ms).";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        public string FormatTopPcs(int take)
        {
            var topPcs = m_pcHitCounts
                .OrderByDescending(o => o.Value)
                .Take(take)
                .Select(o => $"${o.Key:X6}={o.Value:N0}")
                .ToArray();
            return topPcs.Length == 0 ? "<none>" : string.Join(", ", topPcs);
        }
    }
}
