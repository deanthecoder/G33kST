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
    private static bool SaveFrameSamplesToDesktop => false;
    private const int StallTraceLineCount = 40;
    private const uint VideoModeRegister = 0x00FF8260;
    private const uint KeyboardAciaStatusRegister = 0x00FFFC00;
    private const uint KeyboardAciaDataRegister = 0x00FFFC02;
    private const uint NumberOfFloppyDrivesVariableAddress = 0x000004A6;
    private const uint DriveMapVariableAddress = 0x000004C2;
    private const uint PhysicalTopOfRamVariableAddress = 0x0000042E;
    private const uint LowMemoryProbeFromAddress = 0x000008;
    private const uint LowMemoryProbeToAddress = 0x0002FFFF;
    private const double FrameSampleIntervalSeconds = 0.1;
    private const int MenuBarScanTop = 2;
    private const int MenuBarScanHeight = 10;
    private const int MenuBarScanMarginX = 8;
    private const int MenuBarInkThreshold = 96;
    private const int FloppyTraceLineCount = 80;
    private static readonly uint[] PanicCandidateProgramCounters = [0x00FC5104, 0x00FCD042];

    [Test]
    public void CheckEmuTosInitialScreenModeIsLowResolutionColor()
    {
        var romFile = FindEmuTosRom();
        if (romFile == null || !romFile.Exists)
            Assert.Ignore("EmuTOS ROM not found.");

        var romData = romFile.ReadAllBytes();
        var atariST = new AtariST();
        atariST.LoadRom(romData, romFile.Name);
        var shifter = (Shifter)atariST.Video;

        var targetCycles = (long)(atariST.Descriptor.CpuHz * 1.0);
        var sampleIntervalCycles = (long)Math.Round(atariST.Descriptor.CpuHz * 0.05);
        var nextSampleCycle = sampleIntervalCycles;
        var lastCpuTicks = atariST.CpuTicks;
        var firstSampledMode = -1;
        var firstSampledWidth = -1;
        var firstSampledHeight = -1;
        var sampleCount = 0;

        while (atariST.CpuTicks < targetCycles)
        {
            atariST.StepCpu();
            var currentCpuTicks = atariST.CpuTicks;
            var deltaTicks = currentCpuTicks - lastCpuTicks;
            if (deltaTicks > 0)
                atariST.AdvanceDevices(deltaTicks);
            lastCpuTicks = currentCpuTicks;
            if (atariST.TryConsumeInterrupt())
                atariST.RequestInterrupt();

            while (atariST.CpuTicks >= nextSampleCycle)
            {
                sampleCount++;
                var sampledMode = atariST.Cpu.Bus.Read8(VideoModeRegister) & 0x03;
                if (firstSampledMode < 0)
                {
                    firstSampledMode = sampledMode;
                    firstSampledWidth = shifter.ActiveWidth;
                    firstSampledHeight = shifter.ActiveHeight;
                }

                nextSampleCycle += sampleIntervalCycles;
            }
        }

        var detectedStramTop = atariST.Cpu.Bus.Read32BigEndian(PhysicalTopOfRamVariableAddress);
        var detectedFloppyCount = atariST.Cpu.Bus.Read16BigEndian(NumberOfFloppyDrivesVariableAddress);
        var detectedDriveMap = atariST.Cpu.Bus.Read32BigEndian(DriveMapVariableAddress);

        Assert.Multiple(() =>
        {
            Assert.That(sampleCount, Is.GreaterThan(0), "Expected at least one screen-mode sample.");
            Assert.That(firstSampledMode, Is.EqualTo(0), "Expected first sampled video mode to be low-resolution color.");
            Assert.That(firstSampledWidth, Is.EqualTo(320), "Expected initial active screen width to be 320.");
            Assert.That(firstSampledHeight, Is.EqualTo(200), "Expected initial active screen height to be 200.");
            Assert.That(detectedStramTop, Is.EqualTo(0x00100000), "Expected EmuTOS to detect 1MB ST-RAM.");
            Assert.That(detectedFloppyCount, Is.GreaterThanOrEqualTo(1), "Expected EmuTOS to detect at least one floppy drive.");
            Assert.That((detectedDriveMap & 0x00000001) != 0, Is.True, "Expected drive map to include floppy A:.");
        });
    }

    [Test]
    [Explicit("Requires EmuTOS ROM file and validates floppy BIOS read-sector flow.")]
    public void CheckEmuTosMountedFloppyTriggersBiosReadSectorFlow()
    {
        var romFile = FindEmuTosRom();
        if (romFile == null || !romFile.Exists)
            Assert.Ignore("EmuTOS ROM not found.");

        var atariST = new AtariST();
        atariST.LoadRom(romFile.ReadAllBytes(), romFile.Name);
        var mounted = atariST.TryMountFloppyImage(0, CreateTestFloppyImage(), "integration");
        Assert.That(mounted, Is.True, "Expected drive A image to mount.");

        try
        {
            RunMachine(atariST, durationSeconds: 6.0);
        }
        catch (Exception ex)
        {
            DumpFloppyTrace(atariST, "Exception while running mounted-floppy integration.");
            Assert.Fail($"Execution failed: {ex.Message}");
        }

        var stats = atariST.GetFloppyDebugStats();
        if (stats.ReadSectorCommandCount == 0 || stats.SuccessfulReadSectorCommandCount == 0)
            DumpFloppyTrace(atariST, "No successful BIOS floppy read observed.");

        Assert.Multiple(() =>
        {
            Assert.That(stats.CommandCount, Is.GreaterThan(0), "Expected at least one floppy command.");
            Assert.That(stats.ReadSectorCommandCount, Is.GreaterThan(0), "Expected EmuTOS to issue at least one read-sector command.");
            Assert.That(stats.SuccessfulReadSectorCommandCount, Is.GreaterThan(0), "Expected at least one read-sector command to complete.");
            Assert.That(stats.DmaBytesWritten, Is.GreaterThan(0), "Expected floppy DMA to write bytes into ST-RAM.");
        });
    }

    [Test]
    [Explicit("Requires EmuTOS ROM file and runs for extended duration")]
    public void CheckEmuTosRomRunsWithoutDeadlock()
    {
        var romFile = FindEmuTosRom();
        Assert.That(romFile, Is.Not.Null, "Could not find EmuTOS ROM file");
        Assert.That(romFile.Exists, Is.True, $"EmuTOS ROM file not found: {romFile.FullName}");

        var romData = romFile.ReadAllBytes();
        Assert.That(romData, Is.Not.Null);
        Assert.That(romData, Is.Not.Empty);

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
        var panicDetector = new PanicDetector(PanicCandidateProgramCounters);
        var panicFrameDetector = new PanicFrameSignatureDetector(atariST.Video.FrameWidth, atariST.Video.FrameHeight, atariST.Video.FrameBytesPerPixel);
        var ioAccess = new IoAccessTracker(0x00FF8000, 0x00FFFFFF);
        var lowMemoryReads = new RangeAccessTracker(LowMemoryProbeFromAddress, LowMemoryProbeToAddress);
        atariST.Cpu.AddDebugger(ioAccess);
        atariST.Cpu.AddDebugger(lowMemoryReads);
        var keyboardStatusReads = 0L;
        var keyboardDataReads = 0L;
        var keyboardDataWrites = 0L;
        atariST.Cpu.AddDebugger(new MemoryReadDebugger(KeyboardAciaStatusRegister, value =>
        {
            keyboardStatusReads++;
            if (keyboardStatusReads % 50_000 != 0)
                return;
            TestContext.Out.WriteLine($"IKBD poll: status reads={keyboardStatusReads:N0}, last=0x{value:X2}");
        }));
        atariST.Cpu.AddDebugger(new MemoryReadDebugger(KeyboardAciaDataRegister, value =>
        {
            keyboardDataReads++;
            TestContext.Out.WriteLine($"IKBD rx: 0x{value:X2}");
        }));
        atariST.Cpu.AddDebugger(new MemoryWriteDebugger(KeyboardAciaDataRegister, value =>
        {
            keyboardDataWrites++;
            TestContext.Out.WriteLine($"IKBD tx: 0x{value:X2}");
        }));

        const double durationSecs = 8.0;
        var targetCycles = (long)(atariST.Descriptor.CpuHz * durationSecs);
        var stopwatch = Stopwatch.StartNew();

        TestContext.Out.WriteLine($"Running EmuTOS for ~{durationSecs} emulated seconds ({targetCycles:N0} cycles)...");
        TestContext.Out.WriteLine($"ROM: {romFile.Name}");
        TestContext.Out.WriteLine($"CPU: {atariST.Descriptor.CpuHz / 1_000_000.0:F1} MHz");
        TestContext.Out.WriteLine();

        var exceptionCount = 0;
        var lastProgressUpdate = 0L;
        var progressInterval = targetCycles / 10; // Update every 10%
        var loggedLikelyStall = false;
        var stallReason = string.Empty;
        var lastCpuTicks = atariST.CpuTicks;
        var frameSampleIntervalCycles = (long)Math.Round(atariST.Descriptor.CpuHz * FrameSampleIntervalSeconds);
        var nextFrameSampleCycle = frameSampleIntervalCycles;
        var bytesPerPixel = atariST.Video.FrameBytesPerPixel;
        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * bytesPerPixel];
        var blankScreenChecksum = ComputeBlankScreenChecksum(atariST.Video.FrameWidth, atariST.Video.FrameHeight, bytesPerPixel);
        var sawNonBlankFrame = false;
        var firstNonBlankSeenAtCycles = 0L;
        var sawMenuBarInk = false;
        var firstMenuBarInkSeenAtCycles = 0L;
        var sampledFrames = 0;
        var savedFrames = 0;
        var hasLastSampledChecksum = false;
        var lastSampledChecksum = 0u;
        var frameCaptureDir = SaveFrameSamplesToDesktop ? CreateDesktopFrameOutputDir() : null;

        var sampledModeCounts = new Dictionary<int, int>();
        var panicDetected = false;
        var panicProgramCounter = 0u;
        var panicDetectedAtCycles = 0L;
        var panicReason = string.Empty;
        var panicCandidateLogged = false;

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
                panicDetector.Observe(pcBeforeStep, currentCpuTicks);
                if (panicDetector.IsDetected && !panicCandidateLogged)
                {
                    panicCandidateLogged = true;
                    var panicProgress = panicDetector.DetectedAtCycles * 100.0 / targetCycles;
                    TestContext.Out.WriteLine($"[PanicCandidate] PC=${panicDetector.DetectedProgramCounter:X8} at {panicProgress:F2}% ({panicDetector.DetectedAtCycles:N0} cycles).");
                }

                if (deltaTicks > 0)
                    atariST.AdvanceDevices(deltaTicks);
                lastCpuTicks = currentCpuTicks;
                if (atariST.TryConsumeInterrupt())
                    atariST.RequestInterrupt();

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

                    if (!sawNonBlankFrame && checksum != blankScreenChecksum)
                    {
                        sawNonBlankFrame = true;
                        firstNonBlankSeenAtCycles = nextFrameSampleCycle;
                    }
                    if (!sawMenuBarInk && HasMenuBarInk(frameBuffer, atariST.Video.FrameWidth, atariST.Video.FrameHeight))
                    {
                        sawMenuBarInk = true;
                        firstMenuBarInkSeenAtCycles = nextFrameSampleCycle;
                    }

                    panicFrameDetector.Observe(frameBuffer, checksum, mode, nextFrameSampleCycle);
                    if (panicFrameDetector.IsDetected)
                    {
                        panicDetected = true;
                        panicProgramCounter = atariST.Cpu.Registers.ProgramCounter & 0x00FF_FFFF;
                        panicDetectedAtCycles = nextFrameSampleCycle;
                        panicReason = $"panic-frame signature ({panicFrameDetector.DetectionDetails})";
                        TestContext.Out.WriteLine($"[Panic] Frame signature detected at {panicDetectedAtCycles:N0} cycles: {panicFrameDetector.DetectionDetails}.");
                        break;
                    }

                    var frameChanged = !hasLastSampledChecksum || checksum != lastSampledChecksum;
                    if (frameChanged)
                    {
                        hasLastSampledChecksum = true;
                        lastSampledChecksum = checksum;
                        var frameProgress = atariST.CpuTicks * 100.0 / targetCycles;
                        var frameElapsed = stopwatch.Elapsed.TotalSeconds;
                        var frameCyclesPerSecond = frameElapsed > 0 ? atariST.CpuTicks / frameElapsed : 0;
                        TestContext.Out.WriteLine($"Frame changed: {frameProgress:F1}% | {frameCyclesPerSecond / 1_000_000.0:F2} MHz real | {DescribeVideoMode(mode)} | {atariST.Video.FrameWidth}x{atariST.Video.FrameHeight}");
                    }

                    if (frameCaptureDir != null && frameChanged)
                    {
                        var elapsedSeconds = nextFrameSampleCycle / atariST.Descriptor.CpuHz;
                        var fileName = $"emutos_{elapsedSeconds:0000.000}s.tga";
                        var tgaFile = frameCaptureDir.GetFile(fileName);
                        TgaWriter.Write(tgaFile, frameBuffer, atariST.Video.FrameWidth, atariST.Video.FrameHeight, bytesPerPixel);
                        savedFrames++;
                    }

                    nextFrameSampleCycle += frameSampleIntervalCycles;
                }

                if (panicDetected)
                    break;

                // Progress reporting
                if (atariST.CpuTicks - lastProgressUpdate < progressInterval)
                    continue; // Not time to update yet.
                
                var progress = atariST.CpuTicks * 100.0 / targetCycles;
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var cyclesPerSecond = elapsed > 0 ? atariST.CpuTicks / elapsed : 0;
                TestContext.Out.WriteLine($"Progress: {progress:F1}% | {cyclesPerSecond / 1_000_000.0:F2} MHz real");

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

        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine("=== Execution Summary ===");
        TestContext.Out.WriteLine($"Total cycles: {atariST.CpuTicks:N0}");
        TestContext.Out.WriteLine($"Real time: {stopwatch.Elapsed.TotalSeconds:F2}s");
        TestContext.Out.WriteLine($"Average speed: {atariST.CpuTicks / stopwatch.Elapsed.TotalSeconds / 1_000_000.0:F2} MHz");
        TestContext.Out.WriteLine($"Longest same-PC run: {executionHealth.LongestSamePcRunLength:N0} steps / {executionHealth.LongestSamePcRunCycles:N0} cycles ({executionHealth.LongestSamePcRunMilliseconds:F2} ms) at ${executionHealth.LongestSamePcRunPc:X6}");
        TestContext.Out.WriteLine($"Exceptions: {exceptionCount}");
        TestContext.Out.WriteLine($"Frame samples: {sampledFrames} (saved: {savedFrames})");
        TestContext.Out.WriteLine($"Saw non-blank frame: {sawNonBlankFrame} at {firstNonBlankSeenAtCycles:N0} cycles");
        TestContext.Out.WriteLine($"Saw menu bar ink: {sawMenuBarInk} at {firstMenuBarInkSeenAtCycles:N0} cycles");
        TestContext.Out.WriteLine($"Video mode samples: {FormatVideoModeSamples(sampledModeCounts)}");
        TestContext.Out.WriteLine($"IKBD access: status reads={keyboardStatusReads:N0}, data reads={keyboardDataReads:N0}, data writes={keyboardDataWrites:N0}");
        TestContext.Out.WriteLine($"Top I/O reads: {ioAccess.FormatTopReads(8)}");
        TestContext.Out.WriteLine($"Top I/O writes: {ioAccess.FormatTopWrites(8)}");
        TestContext.Out.WriteLine($"Low memory reads ${LowMemoryProbeFromAddress:X6}-${LowMemoryProbeToAddress:X6}: {lowMemoryReads.TotalReads:N0} (top: {lowMemoryReads.FormatTopReads(8)})");
        TestContext.Out.WriteLine($"Panic candidate PC hits: {panicDetector.HitCount}");
        TestContext.Out.WriteLine();

        if (panicDetected)
            Assert.Fail($"Detected EmuTOS panic ({panicReason}) at PC=${panicProgramCounter:X8} after {panicDetectedAtCycles:N0} cycles.");

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
        Assert.That(sawMenuBarInk, Is.True, "Did not observe non-background menu bar text pixels.");

        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine("Test completed. Check output above for EmuTOS behavior.");
    }
    
    private static DirectoryInfo CreateDesktopFrameOutputDir()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var outputDirName = $"G33kST_EmuTOSFrames_{DateTime.Now:yyyyMMdd_HHmmss}";
        var outputDir = new DirectoryInfo(Path.Combine(desktopPath, outputDirName));
        if (!outputDir.Exists)
            outputDir.Create();

        return outputDir;
    }

    private static FileInfo FindEmuTosRom()
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

    private static byte[] CreateTestFloppyImage()
    {
        const int sectorsPerTrack = 9;
        const int sideCount = 2;
        const int trackCount = 80;
        const int sectorSize = 512;
        var image = new byte[trackCount * sideCount * sectorsPerTrack * sectorSize];

        // Simple, deterministic boot sector with valid 0x55AA signature.
        image[0] = 0xEB;
        image[1] = 0x3C;
        image[2] = 0x90;
        var oemName = "G33KST  ";
        for (var i = 0; i < oemName.Length; i++)
            image[3 + i] = (byte)oemName[i];
        image[510] = 0x55;
        image[511] = 0xAA;

        return image;
    }

    private static void RunMachine(AtariST atariST, double durationSeconds)
    {
        var targetCycles = (long)(atariST.Descriptor.CpuHz * durationSeconds);
        var lastCpuTicks = atariST.CpuTicks;
        while (atariST.CpuTicks < targetCycles)
        {
            atariST.StepCpu();
            var currentCpuTicks = atariST.CpuTicks;
            var deltaTicks = currentCpuTicks - lastCpuTicks;
            if (deltaTicks > 0)
                atariST.AdvanceDevices(deltaTicks);
            lastCpuTicks = currentCpuTicks;
            if (!atariST.TryConsumeInterrupt())
                continue;
            atariST.RequestInterrupt();
        }
    }

    private static void DumpFloppyTrace(AtariST atariST, string reason)
    {
        var stats = atariST.GetFloppyDebugStats();
        TestContext.Out.WriteLine(reason);
        TestContext.Out.WriteLine($"Floppy stats: commands={stats.CommandCount}, reads={stats.ReadSectorCommandCount}, readsOk={stats.SuccessfulReadSectorCommandCount}, dmaBytes={stats.DmaBytesWritten}, lastCmd=0x{stats.LastCommand:X2}, lastStatus=0x{stats.LastStatus:X2}, lastDma=0x{stats.LastDmaStatusWord:X4}.");
        var traceLines = atariST.GetRecentFloppyTraceLines(FloppyTraceLineCount);
        if (traceLines.Count == 0)
            return;
        TestContext.Out.WriteLine("Recent floppy trace:");
        foreach (var line in traceLines)
            TestContext.Out.WriteLine($"  {line}");
    }

    private static uint ComputeBlankScreenChecksum(int width, int height, int bytesPerPixel)
    {
        var blankFrame = new byte[width * height * bytesPerPixel];
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

    private static bool HasMenuBarInk(byte[] frameBuffer, int width, int height)
    {
        if (frameBuffer == null)
            return false;
        var bytesPerPixel = frameBuffer.Length / Math.Max(1, width * height);
        if (bytesPerPixel < 3 || frameBuffer.Length < width * height * bytesPerPixel)
            return false;
        if (width <= (MenuBarScanMarginX * 2) || height <= MenuBarScanTop)
            return false;

        var startX = MenuBarScanMarginX;
        var endX = width - MenuBarScanMarginX;
        var startY = MenuBarScanTop;
        var endY = Math.Min(height, startY + MenuBarScanHeight);
        var inkPixels = 0;

        for (var y = startY; y < endY; y++)
        {
            var rowOffset = y * width * bytesPerPixel;
            for (var x = startX; x < endX; x++)
            {
                var pixelOffset = rowOffset + (x * bytesPerPixel);
                var red = frameBuffer[pixelOffset];
                var green = frameBuffer[pixelOffset + 1];
                var blue = frameBuffer[pixelOffset + 2];
                if (red >= 224 && green >= 224 && blue >= 224)
                    continue;
                inkPixels++;
                if (inkPixels >= MenuBarInkThreshold)
                    return true;
            }
        }

        return false;
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

    private sealed class PanicDetector
    {
        private readonly HashSet<uint> m_knownPanicProgramCounters;

        public PanicDetector(IEnumerable<uint> knownPanicProgramCounters) =>
            m_knownPanicProgramCounters = [..knownPanicProgramCounters];

        public bool IsDetected { get; private set; }

        public uint DetectedProgramCounter { get; private set; }

        public long DetectedAtCycles { get; private set; }

        public long HitCount { get; private set; }

        public void Observe(uint programCounter, long cpuCycles)
        {
            if (!m_knownPanicProgramCounters.Contains(programCounter))
                return;

            HitCount++;
            if (IsDetected)
                return;

            IsDetected = true;
            DetectedProgramCounter = programCounter;
            DetectedAtCycles = cpuCycles;
        }
    }

    private sealed class PanicFrameSignatureDetector
    {
        private const int MinBrightPixelCount = 4_000;
        private const int MaxBrightPixelCount = 60_000;
        private const int MinActiveTextRows = 24;
        private const int MaxActiveTextRows = 220;
        private const int MinStableSamples = 4;
        private readonly int m_width;
        private readonly int m_height;
        private readonly int m_bytesPerPixel;
        private uint m_lastChecksum;
        private int m_stableSampleCount;

        public PanicFrameSignatureDetector(int width, int height, int bytesPerPixel)
        {
            m_width = width;
            m_height = height;
            m_bytesPerPixel = bytesPerPixel;
        }

        public bool IsDetected { get; private set; }

        public string DetectionDetails { get; private set; } = string.Empty;

        public void Observe(byte[] frameBuffer, uint checksum, int videoMode, long cycle)
        {
            if (IsDetected || videoMode != 2)
                return;

            var rowStride = m_width * m_bytesPerPixel;
            var brightPixelCount = 0;
            var activeTextRows = 0;
            for (var y = 0; y < m_height; y++)
            {
                var rowBrightPixels = 0;
                var rowStart = y * rowStride;
                for (var x = 0; x < m_width; x++)
                {
                    var pixelOffset = rowStart + (x * m_bytesPerPixel);
                    if (frameBuffer[pixelOffset] <= 192)
                        continue;
                    rowBrightPixels++;
                    brightPixelCount++;
                }

                if (rowBrightPixels >= 24)
                    activeTextRows++;
            }

            if (brightPixelCount < MinBrightPixelCount ||
                brightPixelCount > MaxBrightPixelCount ||
                activeTextRows < MinActiveTextRows ||
                activeTextRows > MaxActiveTextRows)
            {
                m_stableSampleCount = 0;
                m_lastChecksum = 0;
                return;
            }

            if (checksum == m_lastChecksum)
                m_stableSampleCount++;
            else
            {
                m_lastChecksum = checksum;
                m_stableSampleCount = 1;
            }

            if (m_stableSampleCount < MinStableSamples)
                return;

            IsDetected = true;
            DetectionDetails = $"mode=mono stableSamples={m_stableSampleCount} brightPixels={brightPixelCount} activeRows={activeTextRows} checksum=0x{checksum:X8} cycle={cycle:N0}";
        }
    }

    private sealed class IoAccessTracker : CpuDebuggerBase
    {
        private readonly uint m_fromAddress;
        private readonly uint m_toAddress;
        private readonly Dictionary<uint, long> m_reads = [];
        private readonly Dictionary<uint, long> m_writes = [];

        public IoAccessTracker(uint fromAddress, uint toAddress)
        {
            m_fromAddress = fromAddress;
            m_toAddress = toAddress;
        }

        public override void OnMemoryRead(DTC.Emulation.CpuBase cpu, uint address, byte value)
        {
            if (address < m_fromAddress || address > m_toAddress)
                return;
            IncrementCount(m_reads, address);
        }

        public override void OnMemoryWrite(DTC.Emulation.CpuBase cpu, uint address, byte value)
        {
            if (address < m_fromAddress || address > m_toAddress)
                return;
            IncrementCount(m_writes, address);
        }

        public string FormatTopReads(int count) =>
            FormatTop(m_reads, count);

        public string FormatTopWrites(int count) =>
            FormatTop(m_writes, count);

        private static void IncrementCount(Dictionary<uint, long> counts, uint address)
        {
            if (counts.TryGetValue(address, out var current))
                counts[address] = current + 1;
            else
                counts[address] = 1;
        }

        private static string FormatTop(Dictionary<uint, long> counts, int count)
        {
            var top = counts
                .OrderByDescending(o => o.Value)
                .Take(count)
                .Select(o => $"${o.Key:X6}={o.Value:N0}")
                .ToArray();
            return top.Length == 0 ? "<none>" : string.Join(", ", top);
        }
    }

    private sealed class RangeAccessTracker : CpuDebuggerBase
    {
        private readonly uint m_fromAddress;
        private readonly uint m_toAddress;
        private readonly Dictionary<uint, long> m_reads = [];

        public RangeAccessTracker(uint fromAddress, uint toAddress)
        {
            m_fromAddress = fromAddress;
            m_toAddress = toAddress;
        }

        public long TotalReads { get; private set; }

        public override void OnMemoryRead(DTC.Emulation.CpuBase cpu, uint address, byte value)
        {
            if (address < m_fromAddress || address > m_toAddress)
                return;

            TotalReads++;
            if (m_reads.TryGetValue(address, out var readCount))
                m_reads[address] = readCount + 1;
            else
                m_reads[address] = 1;
        }

        public string FormatTopReads(int count)
        {
            var top = m_reads
                .OrderByDescending(o => o.Value)
                .Take(count)
                .Select(o => $"${o.Key:X6}={o.Value:N0}")
                .ToArray();
            return top.Length == 0 ? "<none>" : string.Join(", ", top);
        }
    }

    private sealed class ExecutionHealthTracker
    {
        private readonly double m_cpuHz;
        private readonly long m_samePcStallCycleThreshold;
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
    }
}
