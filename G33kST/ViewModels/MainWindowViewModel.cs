// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using DTC.AtariST;
using DTC.Core;
using DTC.Core.Commands;
using DTC.Core.Extensions;
using DTC.Core.Image;
using DTC.Core.Recording;
using DTC.Core.UI;
using DTC.Core.ViewModels;
using DTC.Emulation;
using DTC.Emulation.Audio;
using DTC.Emulation.Debuggers;
using DTC.Emulation.Recording;
using DTC.Emulation.Rom;
using DTC.M68000;
using G33kST.Snapshot;

namespace G33kST.ViewModels;

/// <summary>
/// Hosts the Atari ST machine loop and exposes desktop UI actions.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const string AppTitle = "G33kST";
    private const int DriveAIndex = 0;
    private const int FloppyLedHoldMs = 130;
    private const int CpuHistoryCapacity = 8192;
    private const byte CrtPreviousFrameBlendWeight = 2;
    private const byte CrtCurrentFrameBlendWeight = 3;
    private const double SpeedIndicatorSmoothingFactor = 0.18;
    private const double SpeedIndicatorSampleIntervalSeconds = 0.20;
    private const double MaxSpeedIndicatorPercent = 250.0;
    private const string SnapshotFileExtension = ".stsnap";
    private readonly Lock m_frameUpdateLock = new();
    private readonly Stopwatch m_speedStopwatch = Stopwatch.StartNew();
    private readonly AtariST m_machine;
    private readonly MachineRunner m_runner;
    private readonly IAudioOutputDevice m_audioDevice;
    private readonly DisplayRecorder m_recorder;
    private readonly InstructionTraceDebugger m_cpuHistoryTrace;
    private readonly DirectoryInfo m_romStoreDir;
    private readonly string m_recordingAvailabilityHint;
    private FileInfo m_loadedRomFile;
    private FileInfo m_mountedFloppyAFile;
    private LcdScreen m_screen;
    private FrameBuffer m_backFrameBuffer;
    private FrameBuffer m_frontFrameBuffer;
    private int m_screenInputWidth;
    private int m_screenInputHeight;
    private int m_isUiFrameUpdateQueued;
    private bool m_isFloppyActivityIndicatorOn;
    private volatile bool m_isSpeedIndicatorDirty;
    private long m_lastFloppyCommandCount;
    private long m_floppyActivityVisibleUntilMs;
    private long m_lastSpeedSampleCpuTicks;
    private long m_lastSpeedSampleStopwatchTicks;
    private double m_smoothedSpeedPercent = 100.0;
    private string m_speedIndicatorText = "100%";
    private static readonly string[] FloppyFileExtensions = [".st", ".stx", ".zip"];
    private static readonly string[] RomFileExtensions = [".img", ".rom", ".bin", ".zip"];

    public MainWindowViewModel()
    {
        var sampleRateHz = new AtariSTDescriptor().AudioSampleRateHz;
        m_audioDevice = CreateAudioOutputDevice(sampleRateHz, out var audioSampleSink);
        var machineOptions = new AtariSTOptions
        {
            RamSizeBytes = AtariSTOptions.Default.RamSizeBytes,
            MonitorType = AtariSTOptions.Default.MonitorType,
            VideoRegion = Settings.IsPalVideoRegion ? AtariVideoRegion.Pal : AtariVideoRegion.Ntsc,
            HasRealTimeClock = AtariSTOptions.Default.HasRealTimeClock,
            AccelerateFloppyAccess = AtariSTOptions.Default.AccelerateFloppyAccess,
            MirrorJoystickToPort0 = AtariSTOptions.Default.MirrorJoystickToPort0
        };
        m_machine = new AtariST(machineOptions, audioSampleSink);
        m_machine.SetMouseInputSamplingEnabled(true);
        m_cpuHistoryTrace = new InstructionTraceDebugger(CpuHistoryCapacity, FormatCpuHistoryLine)
        {
            IsEnabled = Settings.IsCpuHistoryTracked
        };
        m_machine.Cpu.AddDebugger(m_cpuHistoryTrace);
        m_machine.Cpu.ExceptionRaised += OnCpuExceptionRaised;
        m_runner = new MachineRunner(m_machine, () => m_machine.Descriptor.CpuHz, OnMachineRunnerError);
        m_screenInputWidth = m_machine.Video.FrameWidth;
        m_screenInputHeight = m_machine.Video.FrameHeight;
        m_screen = new LcdScreen(m_screenInputWidth, m_screenInputHeight);
        m_screen.CrtBlendWeights = new CrtBlendWeights(CrtPreviousFrameBlendWeight, CrtCurrentFrameBlendWeight);
        m_recorder = new DisplayRecorder();
        IsRecordingAvailable = RecordingSession.IsFfmpegAvailable(out var ffmpegReason);
        m_recordingAvailabilityHint = IsRecordingAvailable
            ? string.Empty
            : string.IsNullOrWhiteSpace(ffmpegReason)
                ? "FFmpeg was not found on this system."
                : ffmpegReason;
        if (!IsRecordingAvailable)
            Logger.Instance.Warn($"Recording disabled: {m_recordingAvailabilityHint}");
        m_backFrameBuffer = new FrameBuffer(m_screenInputWidth, m_screenInputHeight, m_machine.Video.FrameBytesPerPixel);
        m_frontFrameBuffer = new FrameBuffer(m_screenInputWidth, m_screenInputHeight, m_machine.Video.FrameBytesPerPixel);

        m_machine.Video.FrameRendered += OnFrameRendered;
        m_recorder.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsRecordingIndicatorOn));
        };

        Settings.PropertyChanged += OnSettingsPropertyChanged;
        FloppyMru = new MruFiles().InitFromString(Settings.FloppyImageMru);
        FloppyMru.OpenRequested += (_, file) => MountFloppyImageFromFile(file, addToMru: false);
        m_romStoreDir = GetRomStoreDirectory();
        ApplySettingState();
        ResetSpeedIndicatorSampler();
        AboutInfo = AboutInfoProvider.Info;
        LoadInitialRom();
        LoadLastFloppyImage();
    }

    public Settings Settings => Settings.Instance;

    public IImage Display => m_screen.Display;

    public AboutInfo AboutInfo { get; }

    public MruFiles FloppyMru { get; }

    public bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    public bool IsRecording => m_recorder.IsRecording;

    public bool IsRecordingIndicatorOn => m_recorder.IsIndicatorOn;

    public bool IsRecordingAvailable { get; }

    public string FloppyIndicatorState =>
        !m_machine.IsFloppyImageMounted(DriveAIndex)
            ? "NoDisk"
            : m_isFloppyActivityIndicatorOn
                ? "Active"
                : "Idle";

    public string FloppyIndicatorTooltip
    {
        get
        {
            var mountedImageName = m_machine.GetMountedFloppyImageName(DriveAIndex);
            if (string.IsNullOrWhiteSpace(mountedImageName))
                return "Drive A: no disk mounted";
            return m_isFloppyActivityIndicatorOn ? $"Drive A: activity ({mountedImageName})" : $"Drive A: mounted ({mountedImageName})";
        }
    }

    public bool IsSoundEnabled => Settings.IsSoundEnabled;

    public bool IsSpeedIndicatorVisible => Settings.IsSpeedIndicatorVisible;

    public string SpeedIndicatorText => m_speedIndicatorText;

    public bool IsHighResolutionMode => m_machine.IsHighResolutionMode;

    public bool IsPalVideoRegion => m_machine.VideoRegion == AtariVideoRegion.Pal;

    public bool IsJoystickInputEnabled { get; private set; }

    public string WindowTitle => $"{AppTitle} - {m_machine.Descriptor.Name}";

    public event EventHandler DisplayUpdated;

    /// <summary>
    /// Starts background emulation once the window is ready.
    /// </summary>
    public void Start()
    {
        if (m_runner.IsRunning)
            return;
        m_audioDevice.Start();
        m_runner.Start();
    }

    /// <summary>
    /// Passes focus state into machine input hooks.
    /// </summary>
    public void SetInputActive(bool isActive) =>
        m_machine.SetInputActive(isActive);

    /// <summary>
    /// Forwards host pointer state to the machine so IKBD mouse packets can be generated.
    /// </summary>
    public void UpdateMouseState(double normalizedX, double normalizedY, bool isLeftButtonPressed, bool isRightButtonPressed, bool isPointerWithinDisplay) =>
        m_machine.UpdateMouseState(normalizedX, normalizedY, isLeftButtonPressed, isRightButtonPressed, isPointerWithinDisplay);

    /// <summary>
    /// Forwards one keyboard key transition into the IKBD stream.
    /// </summary>
    public void UpdateKeyboardState(byte scanCode, bool isPressed) =>
        m_machine.InjectKeyboardKeyState(scanCode, isPressed);

    /// <summary>
    /// Clears any queued IKBD keyboard/controller output bytes.
    /// </summary>
    public void ClearKeyboardInputQueue() =>
        m_machine.ClearKeyboardInputQueue();

    /// <summary>
    /// Forwards host joystick state to IKBD joystick port 0.
    /// </summary>
    public void UpdateJoystickState(JoystickState state) =>
        m_machine.UpdateJoystickState(state);

    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        m_machine.Video.FrameRendered -= OnFrameRendered;
        m_machine.Cpu.ExceptionRaised -= OnCpuExceptionRaised;
        Settings.FloppyImageMru = FloppyMru.AsString();
        m_recorder.Dispose();
        m_runner.Dispose();
        m_audioDevice.Dispose();
        m_screen.Dispose();
        Settings.Dispose();
    }

    public void ResetMachine()
    {
        m_runner.Reset();
        ResetSpeedIndicatorSampler();
        OnPropertyChanged(nameof(IsHighResolutionMode));
        Logger.Instance.Info("Machine reset.");
    }

    public void HardResetMachine()
    {
        if (m_machine.IsFloppyImageMounted(DriveAIndex))
        {
            m_machine.UnmountFloppyImage(DriveAIndex);
            m_mountedFloppyAFile = null;
        }

        m_runner.Reset();
        ResetSpeedIndicatorSampler();
        OnPropertyChanged(nameof(IsHighResolutionMode));
        NotifyFloppyIndicatorChanged();
        Logger.Instance.Info("Machine hard reset.");
    }

    public void SaveScreenshotCommand()
    {
        var command = new FileSaveCommand(
            "Save Screenshot",
            "TGA Files",
            ["*.tga"],
            $"{GetCaptureFilePrefix()}_{DateTime.Now:yyyyMMdd_HHmmss}.tga");
        command.FileSelected += (_, file) =>
        {
            FrameBuffer frameCopy;
            lock (m_frameUpdateLock)
                frameCopy = m_frontFrameBuffer.Clone();

            TgaWriter.Write(file, frameCopy.Data, frameCopy.Width, frameCopy.Height, frameCopy.BytesPerPixel);
            Logger.Instance.Info($"Screenshot saved: {file.FullName}");
        };
        command.Execute(null);
    }

    public void StartRecordingCommand()
    {
        if (!IsRecordingAvailable)
        {
            DialogService.Instance.ShowMessage("Recording unavailable", m_recordingAvailabilityHint);
            return;
        }
        if (IsRecording)
            return;

        m_recorder.Start(m_screen.Display, m_machine.Descriptor.VideoHz, m_audioDevice, GetCaptureTitle);
    }

    public void ToggleRecordingCommand()
    {
        if (IsRecording)
            StopRecordingCommand();
        else
            StartRecordingCommand();
    }

    public void StopRecordingCommand()
    {
        if (IsRecording)
            m_recorder.Stop();
    }

    public void ToggleSound()
    {
        Settings.IsSoundEnabled = !Settings.IsSoundEnabled;
        OnPropertyChanged(nameof(IsSoundEnabled));
    }

    public void ToggleAmbientBlur() =>
        Settings.IsAmbientBlurred = !Settings.IsAmbientBlurred;

    public void ToggleSpeedIndicator() =>
        Settings.IsSpeedIndicatorVisible = !Settings.IsSpeedIndicatorVisible;

    public void MountFloppyImage()
    {
        var command = new FileOpenCommand("Mount Floppy Image", "Atari ST Floppy Images", ["*.st", "*.stx", "*.zip"]);
        command.FileSelected += (_, info) => MountFloppyImageFromFile(info, addToMru: true);
        command.Execute(null);
    }

    public void OpenFile()
    {
        var command = new FileOpenCommand(
            "Open ROM or Snapshot",
            "Atari ST ROM Images / G33kST Snapshots",
            ["*.img", "*.rom", "*.bin", "*.zip", "*.stsnap"]);
        command.FileSelected += (_, file) =>
        {
            if (IsSnapshotFile(file))
                RestoreSnapshotFromFile(file);
            else
                SwitchRomImage(file);
        };
        command.Execute(null);
    }

    public void OpenSnapshot()
    {
        var command = new FileOpenCommand(
            "Open Snapshot",
            "G33kST Snapshots",
            ["*.stsnap"]);
        command.FileSelected += (_, file) => RestoreSnapshotFromFile(file);
        command.Execute(null);
    }

    public void SelectRomImage()
    {
        var command = new FileOpenCommand("Select ROM Image", "Atari ST ROM Images", ["*.img", "*.rom", "*.bin", "*.zip"]);
        command.FileSelected += (_, romFile) => SwitchRomImage(romFile);
        command.Execute(null);
    }

    public void SaveSnapshot()
    {
        if (!m_machine.HasLoadedCartridge)
        {
            Logger.Instance.Warn("Unable to save snapshot: No ROM loaded.");
            return;
        }

        var romPath = m_loadedRomFile?.FullName ?? Settings.SelectedRomPath;
        if (string.IsNullOrWhiteSpace(romPath))
        {
            Logger.Instance.Warn("Unable to save snapshot: ROM path is unknown.");
            return;
        }

        var command = new FileSaveCommand(
            "Save Snapshot",
            "G33kST Snapshots",
            ["*.stsnap"],
            $"{GetCaptureFilePrefix()}_{DateTime.Now:yyyyMMdd_HHmmss}.stsnap");
        command.FileSelected += (_, file) =>
        {
            try
            {
                var state = m_runner.CaptureState();
                SnapshotFile.Save(file, state, romPath, m_mountedFloppyAFile?.FullName);
                Logger.Instance.Info($"Snapshot saved: {file.FullName}");
            }
            catch (Exception e)
            {
                Logger.Instance.Exception($"Failed to save snapshot '{file.FullName}'.", e);
                DialogService.Instance.ShowMessage("Unable to save snapshot.", e.Message);
            }
        };
        command.Execute(null);
    }

    public void UseBundledEmuTosRom()
    {
        var bundledRom = FindBundledRom();
        if (bundledRom?.Exists() != true)
        {
            DialogService.Instance.ShowMessage(
                "Bundled ROM not found.",
                "Could not locate the bundled EmuTOS ROM.");
            return;
        }

        var storedRom = TryCopyRomIntoStore(bundledRom, showDialogOnFailure: false);
        if (storedRom == null)
            return;
        if (!TryLoadRom(storedRom, shouldHardReset: true, updateSelection: true))
            return;

        Logger.Instance.Info($"ROM switched to bundled EmuTOS '{storedRom.Name}'.");
    }

    public void ToggleCrtEmulation() =>
        Settings.IsCrtEmulationEnabled = !Settings.IsCrtEmulationEnabled;

    public void ToggleDisplayResolutionMode()
    {
        var targetMode = m_machine.IsHighResolutionMode
            ? "low-resolution color"
            : "high-resolution monochrome";
        DialogService.Instance.Warn(
            "Switch display mode?",
            $"Switching to {targetMode} performs a hard reset.",
            "Cancel",
            "Switch + Reset",
            isConfirmed =>
            {
                if (!isConfirmed)
                    return;

                m_machine.ToggleDisplayResolutionMode();
                HardResetMachine();
                Logger.Instance.Info($"Display mode switched to {targetMode}.");
            });
    }

    public void SetNtscVideoRegion() =>
        SetVideoRegion(AtariVideoRegion.Ntsc);

    public void SetPalVideoRegion() =>
        SetVideoRegion(AtariVideoRegion.Pal);

    public void ToggleJoystickInput()
    {
        IsJoystickInputEnabled = !IsJoystickInputEnabled;
        if (!IsJoystickInputEnabled)
            UpdateJoystickState(JoystickState.Neutral);
        OnPropertyChanged(nameof(IsJoystickInputEnabled));
    }
    
    private void SetVideoRegion(AtariVideoRegion targetRegion)
    {
        if (m_machine.VideoRegion == targetRegion)
            return;

        m_machine.SetVideoRegion(targetRegion);
        Settings.IsPalVideoRegion = targetRegion == AtariVideoRegion.Pal;
        ResetMachine();
        OnPropertyChanged(nameof(IsPalVideoRegion));
        Logger.Instance.Info($"Video timing switched to {targetRegion} ({m_machine.Descriptor.VideoHz:0} Hz).");
    }

    public void OpenLog()
    {
        var logFile = Logger.Instance.File;
        if (logFile.Exists)
            logFile.OpenWithDefaultViewer();
        else
            logFile.Directory?.Explore();
    }

    public void OpenProjectPage() =>
        new Uri("https://github.com/deanthecoder/G33kST").Open();

    public void TrackCpuHistory()
    {
        Settings.IsCpuHistoryTracked = !Settings.IsCpuHistoryTracked;
        m_cpuHistoryTrace.IsEnabled = Settings.IsCpuHistoryTracked;
        if (Settings.IsCpuHistoryTracked)
        {
            m_cpuHistoryTrace.Clear();
            Logger.Instance.Info("CPU history tracking enabled.");
            return;
        }

        Logger.Instance.Info("CPU history tracking disabled.");
    }

    public void ToggleCpuExceptionCapture()
    {
        Settings.IsCpuExceptionCaptureEnabled = !Settings.IsCpuExceptionCaptureEnabled;
        if (IsCpuExceptionCaptureSupported && Settings.IsCpuExceptionCaptureEnabled)
        {
            Logger.Instance.Info("CPU exception capture enabled.");
            return;
        }

        Logger.Instance.Info("CPU exception capture disabled.");
    }

    public void DumpCpuHistory()
    {
        var lines = m_cpuHistoryTrace.GetRecentLines();
        if (lines.Count == 0)
        {
            Logger.Instance.Info("CPU history is empty.");
            return;
        }

        var logDir = Logger.Instance.File.Directory ?? new DirectoryInfo(AppContext.BaseDirectory);
        var dumpFile = logDir.GetFile($"cpu_history_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        dumpFile.WriteAllText(string.Join(Environment.NewLine, lines));
        Logger.Instance.Info($"CPU history dumped ({lines.Count:N0} lines): {dumpFile.FullName}");
    }

    public void ReportCpuClockTicks() =>
        Console.WriteLine($"CPU clock ticks: {m_machine.CpuTicks}");

    public void CloseCommand() =>
        Application.Current?.GetMainWindow()?.Close();

    private void RestoreSnapshotFromFile(FileInfo snapshotFile)
    {
        if (snapshotFile == null)
            return;
        if (!snapshotFile.Exists())
        {
            Logger.Instance.Warn($"Unable to load snapshot '{snapshotFile.FullName}': File not found.");
            return;
        }

        try
        {
            var state = SnapshotFile.Load(snapshotFile, out var romPath, out var floppyAPath, out _);
            if (string.IsNullOrWhiteSpace(romPath))
            {
                DialogService.Instance.ShowMessage("Unable to restore snapshot.", "The snapshot does not contain a ROM path.");
                return;
            }

            var romFile = new FileInfo(romPath);
            if (!romFile.Exists())
            {
                DialogService.Instance.ShowMessage(
                    "Unable to restore snapshot.",
                    $"The ROM file was not found:\n{romFile.FullName}");
                return;
            }

            FileInfo floppyAFile = null;
            if (!string.IsNullOrWhiteSpace(floppyAPath))
            {
                floppyAFile = new FileInfo(floppyAPath);
                if (!floppyAFile.Exists())
                {
                    DialogService.Instance.ShowMessage(
                        "Unable to restore snapshot.",
                        $"The floppy image file was not found:\n{floppyAFile.FullName}");
                    return;
                }
            }

            var wasRunning = m_runner.IsRunning;
            if (wasRunning)
                m_runner.Stop();

            try
            {
                if (!TryLoadRom(romFile, shouldHardReset: false, updateSelection: true))
                    return;

                if (floppyAFile != null)
                {
                    if (!MountFloppyImageFromFile(floppyAFile, addToMru: false))
                    {
                        DialogService.Instance.ShowMessage(
                            "Unable to restore snapshot.",
                            $"Failed to mount floppy image '{floppyAFile.FullName}'.");
                        return;
                    }
                }
                else if (m_machine.IsFloppyImageMounted(DriveAIndex))
                {
                    m_machine.UnmountFloppyImage(DriveAIndex);
                    m_mountedFloppyAFile = null;
                    NotifyFloppyIndicatorChanged();
                }

                m_runner.LoadState(state);
                ResetSpeedIndicatorSampler();
                OnPropertyChanged(nameof(IsHighResolutionMode));
                OnPropertyChanged(nameof(IsPalVideoRegion));
                Logger.Instance.Info($"Snapshot restored: {snapshotFile.FullName}");
            }
            finally
            {
                if (wasRunning)
                    m_runner.StartFromCurrentState();
            }
        }
        catch (Exception e)
        {
            Logger.Instance.Exception($"Failed to restore snapshot '{snapshotFile.FullName}'.", e);
            DialogService.Instance.ShowMessage("Unable to restore snapshot.", e.Message);
        }
    }

    internal bool MountFloppyImageFromFile(FileInfo imageFile, bool addToMru)
    {
        if (imageFile == null)
            return false;
        if (!imageFile.Exists())
        {
            Logger.Instance.Warn($"Unable to mount floppy image '{imageFile.FullName}': File not found.");
            return false;
        }

        var extension = imageFile.Extension;
        if (!FloppyFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            Logger.Instance.Warn($"Unable to mount floppy image '{imageFile.FullName}': Unsupported extension.");
            return false;
        }

        var loadedImage = FloppyImageLoader.ReadImage(imageFile);
        var imageName = loadedImage.ImageName;
        var imageData = loadedImage.ImageData;
        if (imageData == null || imageData.Length == 0)
        {
            Logger.Instance.Warn($"Unable to mount floppy image '{imageFile.FullName}': No supported floppy image data found.");
            if (imageFile.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                DialogService.Instance.ShowMessage(
                    "Unable to mount floppy image.",
                    $"The zip '{imageFile.Name}' does not contain a supported .st or .stx disk image.");
            }
            return false;
        }
        var imageSummary = FloppyImageLoader.DescribeImage(imageData);

        var wasMounted = m_machine.IsFloppyImageMounted(DriveAIndex);
        if (!m_machine.TryMountFloppyImage(DriveAIndex, imageData, imageName))
        {
            Logger.Instance.Warn($"Unable to mount floppy image '{imageFile.FullName}' into drive A.");
            return false;
        }

        if (addToMru)
            FloppyMru.Add(imageFile);
        m_mountedFloppyAFile = imageFile;
        Settings.LastFloppyImagePath = imageFile.FullName;
        NotifyFloppyIndicatorChanged();

        Logger.Instance.Info(wasMounted
            ? $"Drive A: floppy image replaced with '{imageFile.Name}' ({loadedImage.FormatLabel})."
            : $"Drive A: mounted floppy image '{imageFile.Name}' ({loadedImage.FormatLabel}).");
        Logger.Instance.Info($"Drive A image details ({loadedImage.FormatLabel}): {imageSummary}.");
        return true;
    }

    private void LoadInitialRom()
    {
        var selectedRomFile = Settings.SelectedRomPath?.ToFile();
        if (selectedRomFile?.Exists() == true && TryLoadRom(selectedRomFile, shouldHardReset: false, updateSelection: false))
            return;

        var bundledRom = FindBundledRom();
        if (bundledRom?.Exists() != true)
        {
            Logger.Instance.Warn("No Atari ST ROM was found. Use File -> Select ROM Image... to choose one.");
            return;
        }

        var storedRom = TryCopyRomIntoStore(bundledRom, showDialogOnFailure: false);
        if (storedRom == null)
            return;

        _ = TryLoadRom(storedRom, shouldHardReset: false, updateSelection: true);
    }

    private void LoadLastFloppyImage()
    {
        var lastFloppyPath = Settings.LastFloppyImagePath;
        if (string.IsNullOrWhiteSpace(lastFloppyPath))
            return;

        var floppyFile = new FileInfo(lastFloppyPath);
        if (!floppyFile.Exists())
        {
            Settings.LastFloppyImagePath = null;
            return;
        }

        _ = MountFloppyImageFromFile(floppyFile, addToMru: false);
    }

    private void SwitchRomImage(FileInfo selectedRomFile)
    {
        var storedRom = TryCopyRomIntoStore(selectedRomFile, showDialogOnFailure: true);
        if (storedRom == null)
            return;

        if (!TryLoadRom(storedRom, shouldHardReset: true, updateSelection: true))
            return;

        Logger.Instance.Info($"ROM switched to '{storedRom.Name}'.");
    }

    private FileInfo TryCopyRomIntoStore(FileInfo sourceRomFile, bool showDialogOnFailure)
    {
        if (sourceRomFile == null || !sourceRomFile.Exists())
        {
            Logger.Instance.Warn($"Unable to load ROM '{sourceRomFile?.FullName}': File not found.");
            return null;
        }

        if (!RomFileExtensions.Contains(sourceRomFile.Extension, StringComparer.OrdinalIgnoreCase))
        {
            Logger.Instance.Warn($"Unable to load ROM '{sourceRomFile.FullName}': Unsupported extension.");
            return null;
        }

        var (romFileName, romData) = RomImageLoader.ReadRomData(sourceRomFile);
        if (romData == null || romData.Length == 0)
        {
            Logger.Instance.Warn($"Unable to load ROM '{sourceRomFile.FullName}': No supported ROM data found.");
            if (showDialogOnFailure && sourceRomFile.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                DialogService.Instance.ShowMessage(
                    "Unable to load ROM image.",
                    $"The zip '{sourceRomFile.Name}' does not contain a supported .img/.rom/.bin ROM.");
            }
            return null;
        }

        var storedFileName = GetStoredRomFileName(romFileName);
        var storedRomFile = m_romStoreDir.GetFile(storedFileName);
        storedRomFile.WriteAllBytes(romData);
        return storedRomFile;
    }

    private bool TryLoadRom(FileInfo romFile, bool shouldHardReset, bool updateSelection)
    {
        if (romFile?.Exists() != true)
            return false;

        var wasRunning = m_runner.IsRunning;
        if (wasRunning)
            m_runner.Stop();

        try
        {
            var romData = romFile.ReadAllBytes();
            if (romData == null || romData.Length == 0)
            {
                Logger.Instance.Warn($"ROM '{romFile.FullName}' is empty.");
                return false;
            }

            if (shouldHardReset && m_machine.IsFloppyImageMounted(DriveAIndex))
            {
                m_machine.UnmountFloppyImage(DriveAIndex);
                m_mountedFloppyAFile = null;
                NotifyFloppyIndicatorChanged();
            }

            m_machine.LoadRom(romData, romFile.Name);
            m_loadedRomFile = romFile;
            ResetSpeedIndicatorSampler();
            if (updateSelection)
                Settings.SelectedRomPath = romFile.FullName;
            OnPropertyChanged(nameof(IsHighResolutionMode));
            Logger.Instance.Info($"ROM loaded: {romFile.Name} ({romData.Length / 1024.0:F1} KB).");
            return true;
        }
        catch (Exception e)
        {
            Logger.Instance.Exception($"Failed to load ROM '{romFile.FullName}'.", e);
            return false;
        }
        finally
        {
            if (wasRunning)
                m_runner.Start();
        }
    }

    private void OnFrameRendered(object sender, byte[] frameBuffer)
    {
        if (frameBuffer == null || frameBuffer.Length == 0)
            return;

        var currentCpuTicks = m_machine.CpuTicks;
        var frameWidth = m_machine.Video.FrameWidth;
        var frameHeight = m_machine.Video.FrameHeight;
        var bytesPerPixel = m_machine.Video.FrameBytesPerPixel;
        UpdateSpeedIndicator(currentCpuTicks);
        UpdateFloppyActivityWindow();
        CopyToBackFrame(frameBuffer, frameWidth, frameHeight, bytesPerPixel);
        m_recorder.CaptureFrame();

        if (Interlocked.Exchange(ref m_isUiFrameUpdateQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var frameToPresent = SwapFrameBuffers();
                EnsureScreenGeometry(frameToPresent.Frame.Width, frameToPresent.Frame.Height);
                m_screen.Update(frameToPresent.Frame.Data);
                if (m_isSpeedIndicatorDirty)
                {
                    m_isSpeedIndicatorDirty = false;
                    OnPropertyChanged(nameof(SpeedIndicatorText));
                }
                UpdateFloppyActivityIndicatorState();
                DisplayUpdated?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                Interlocked.Exchange(ref m_isUiFrameUpdateQueued, 0);
            }
        }, DispatcherPriority.Normal);
    }

    /// <summary>
    /// Extends the on-screen floppy LED window when new FDC commands are observed.
    /// </summary>
    private void UpdateFloppyActivityWindow()
    {
        var commandCount = m_machine.GetFloppyDebugStats().CommandCount;
        if (commandCount == m_lastFloppyCommandCount)
            return;

        m_lastFloppyCommandCount = commandCount;
        Volatile.Write(ref m_floppyActivityVisibleUntilMs, Environment.TickCount64 + FloppyLedHoldMs);
    }

    /// <summary>
    /// Updates the LED binding so the UI can show short floppy access flashes like original ST hardware.
    /// </summary>
    private void UpdateFloppyActivityIndicatorState()
    {
        var shouldBeOn = Environment.TickCount64 <= Volatile.Read(ref m_floppyActivityVisibleUntilMs);
        if (shouldBeOn == m_isFloppyActivityIndicatorOn)
            return;

        m_isFloppyActivityIndicatorOn = shouldBeOn;
        NotifyFloppyIndicatorChanged();
    }

    private void NotifyFloppyIndicatorChanged()
    {
        OnPropertyChanged(nameof(FloppyIndicatorState));
        OnPropertyChanged(nameof(FloppyIndicatorTooltip));
    }

    private void ResetSpeedIndicatorSampler()
    {
        m_lastSpeedSampleCpuTicks = m_machine.CpuTicks;
        m_lastSpeedSampleStopwatchTicks = m_speedStopwatch.ElapsedTicks;
        m_smoothedSpeedPercent = 100.0;
        SetSpeedIndicatorText(m_smoothedSpeedPercent);
    }

    private void UpdateSpeedIndicator(long currentCpuTicks)
    {
        if (!Settings.IsSpeedIndicatorVisible)
            return;

        var elapsedStopwatchTicks = m_speedStopwatch.ElapsedTicks - m_lastSpeedSampleStopwatchTicks;
        if (elapsedStopwatchTicks <= 0)
            return;

        var elapsedSeconds = elapsedStopwatchTicks / (double)Stopwatch.Frequency;
        if (elapsedSeconds < SpeedIndicatorSampleIntervalSeconds)
            return;

        m_lastSpeedSampleStopwatchTicks += elapsedStopwatchTicks;
        var cpuTicksDelta = currentCpuTicks - m_lastSpeedSampleCpuTicks;
        m_lastSpeedSampleCpuTicks = currentCpuTicks;
        if (cpuTicksDelta < 0)
        {
            m_smoothedSpeedPercent = 100.0;
            SetSpeedIndicatorText(m_smoothedSpeedPercent);
            return;
        }

        var expectedTicks = m_machine.Descriptor.CpuHz * elapsedSeconds;
        if (expectedTicks <= 0)
            return;

        var instantPercent = Math.Clamp(cpuTicksDelta * 100.0 / expectedTicks, 0.0, MaxSpeedIndicatorPercent);
        m_smoothedSpeedPercent += (instantPercent - m_smoothedSpeedPercent) * SpeedIndicatorSmoothingFactor;
        SetSpeedIndicatorText(m_smoothedSpeedPercent);
    }

    private void SetSpeedIndicatorText(double speedPercent)
    {
        var text = $"{speedPercent:0}%";
        if (text == m_speedIndicatorText)
            return;

        m_speedIndicatorText = text;
        m_isSpeedIndicatorDirty = true;
    }

    private void CopyToBackFrame(byte[] frameBuffer, int frameWidth, int frameHeight, int bytesPerPixel)
    {
        lock (m_frameUpdateLock)
        {
            EnsureFrameBuffers(frameWidth, frameHeight, bytesPerPixel);
            m_backFrameBuffer.CopyFrom(frameBuffer, clearRemainderWhenShort: true);
        }
    }

    private FrameToPresent SwapFrameBuffers()
    {
        lock (m_frameUpdateLock)
        {
            (m_backFrameBuffer, m_frontFrameBuffer) = (m_frontFrameBuffer, m_backFrameBuffer);
            return new FrameToPresent(m_frontFrameBuffer);
        }
    }

    private void EnsureFrameBuffers(int frameWidth, int frameHeight, int bytesPerPixel)
    {
        var hasMatchingGeometry =
            m_backFrameBuffer.Width == frameWidth &&
            m_backFrameBuffer.Height == frameHeight &&
            m_backFrameBuffer.BytesPerPixel == bytesPerPixel &&
            m_frontFrameBuffer.Width == frameWidth &&
            m_frontFrameBuffer.Height == frameHeight &&
            m_frontFrameBuffer.BytesPerPixel == bytesPerPixel;
        if (hasMatchingGeometry)
            return;

        m_backFrameBuffer = new FrameBuffer(frameWidth, frameHeight, bytesPerPixel);
        m_frontFrameBuffer = new FrameBuffer(frameWidth, frameHeight, bytesPerPixel);
    }

    private void EnsureScreenGeometry(int width, int height)
    {
        if (width == m_screenInputWidth && height == m_screenInputHeight)
            return;

        if (IsRecording)
        {
            m_recorder.Stop();
            Logger.Instance.Warn("Recording stopped because display size changed.");
        }

        var oldScreen = m_screen;
        m_screen = new LcdScreen(width, height);
        m_screen.CrtBlendWeights = new CrtBlendWeights(CrtPreviousFrameBlendWeight, CrtCurrentFrameBlendWeight);
        m_screen.FrameBuffer.IsCrt = Settings.IsCrtEmulationEnabled;
        oldScreen.Dispose();
        m_screenInputWidth = width;
        m_screenInputHeight = height;
        OnPropertyChanged(nameof(Display));
    }

    private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Settings.IsSoundEnabled):
                m_audioDevice.SetEnabled(Settings.IsSoundEnabled);
                OnPropertyChanged(nameof(IsSoundEnabled));
                return;
            case nameof(Settings.IsCrtEmulationEnabled):
                m_screen.FrameBuffer.IsCrt = Settings.IsCrtEmulationEnabled;
                return;
            case nameof(Settings.IsSpeedIndicatorVisible):
                if (Settings.IsSpeedIndicatorVisible)
                    ResetSpeedIndicatorSampler();
                OnPropertyChanged(nameof(IsSpeedIndicatorVisible));
                OnPropertyChanged(nameof(SpeedIndicatorText));
                return;
            case nameof(Settings.IsCpuHistoryTracked):
                m_cpuHistoryTrace.IsEnabled = Settings.IsCpuHistoryTracked;
                return;
        }
    }

    private void ApplySettingState()
    {
        m_audioDevice.SetEnabled(Settings.IsSoundEnabled);
        m_screen.FrameBuffer.IsCrt = Settings.IsCrtEmulationEnabled;
    }

    private string GetCaptureTitle()
    {
        var mountedDisk = m_mountedFloppyAFile?.Name;
        if (!string.IsNullOrWhiteSpace(mountedDisk))
            return mountedDisk;

        var romPath = m_loadedRomFile?.Name ?? Settings.SelectedRomPath;
        if (!string.IsNullOrWhiteSpace(romPath))
            return romPath;

        return AppTitle;
    }

    private string GetCaptureFilePrefix() =>
        RomNameHelper.GetSafeFileBaseName(GetCaptureTitle(), AppTitle);

    private void OnMachineRunnerError(Exception exception)
    {
        Logger.Instance.Exception("Machine runner error.", exception);
        var stats = m_machine.GetFloppyDebugStats();
        Logger.Instance.Info($"Floppy stats: commands={stats.CommandCount}, reads={stats.ReadSectorCommandCount}, readsOk={stats.SuccessfulReadSectorCommandCount}, dmaBytes={stats.DmaBytesWritten}, lastCmd=0x{stats.LastCommand:X2}, lastStatus=0x{stats.LastStatus:X2}, lastDma=0x{stats.LastDmaStatusWord:X4}.");
        var traceLines = m_machine.GetRecentFloppyTraceLines(40);
        if (traceLines.Count == 0)
            return;

        Logger.Instance.Info("Recent floppy trace:");
        foreach (var line in traceLines)
            Logger.Instance.Info($"  {line}");
    }

    private void OnCpuExceptionRaised(CpuExceptionInfo exceptionInfo)
    {
        if (!IsCpuExceptionCaptureSupported || !Settings.IsCpuExceptionCaptureEnabled)
            return;

        var accessType = exceptionInfo.IsRead ? "read" : "write";
        var accessSpace = exceptionInfo.IsProgramAccess ? "program" : "data";
        Logger.Instance.Info(
            $"CPU {exceptionInfo.Kind}: vector=0x{exceptionInfo.VectorAddress:X6}, sr=0x{exceptionInfo.StatusRegister:X4}, " +
            $"framePc=0x{exceptionInfo.FrameProgramCounter:X8}, ir=0x{exceptionInfo.InstructionRegister:X4}, " +
            $"fault=0x{exceptionInfo.FaultAddress:X8}, ssw=0x{exceptionInfo.SpecialStatusWord:X4}, " +
            $"access={accessType}/{accessSpace}.");
    }

    private static bool IsCpuExceptionCaptureSupported
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    private static IAudioOutputDevice CreateAudioOutputDevice(int sampleRateHz, out Action<double, double> audioSampleSink)
    {
        try
        {
            var soundDevice = new SoundDevice(sampleRateHz);
            audioSampleSink = soundDevice.AddSample;
            return soundDevice;
        }
        catch (Exception exception)
        {
            audioSampleSink = null;
            Logger.Instance.Warn($"Audio output unavailable, continuing in silent mode: {exception.Message}");
            return new NullAudioOutputDevice(sampleRateHz);
        }
    }

    private static string FormatCpuHistoryLine(CpuBase cpu, uint opcodeAddress, ushort opcode, string instructionText)
    {
        var suffix = string.IsNullOrWhiteSpace(instructionText)
            ? string.Empty
            : $" {instructionText}";
        return $"{opcodeAddress:X6}: {opcode:X4}{suffix}";
    }

    private static FileInfo FindBundledRom()
    {
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        var candidateDirs = new[]
        {
            baseDir.GetDir("TOS"),
            baseDir.GetDir("DTC.AtariST").GetDir("TOS"),
            new DirectoryInfo(Path.GetFullPath(Path.Combine(baseDir.FullName, "..", "..", "..", "..", "DTC.AtariST", "TOS")))
        };

        foreach (var dir in candidateDirs)
        {
            if (!dir.Exists)
                continue;

            var rom = dir.GetFiles("*.img", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (rom != null)
                return rom;
            rom = dir.GetFiles("*.rom", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (rom != null)
                return rom;
            rom = dir.GetFiles("*.bin", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (rom != null)
                return rom;
            rom = dir.GetFiles("*.zip", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (rom != null)
                return rom;
        }

        return null;
    }

    private static DirectoryInfo GetRomStoreDirectory()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var romStore = assembly.GetAppSettingsPath().GetDir("Roms");
        if (!romStore.Exists())
            romStore.Create();
        return romStore;
    }

    private static string GetStoredRomFileName(string romFileName)
    {
        var safeName = Path.GetFileName(romFileName ?? string.Empty).ToSafeFileName();
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = $"rom_{DateTime.Now:yyyyMMdd_HHmmss}.img";
        return Path.HasExtension(safeName) ? safeName : $"{safeName}.img";
    }

    private static bool IsSnapshotFile(FileInfo file) =>
        file != null &&
        file.Extension.Equals(SnapshotFileExtension, StringComparison.OrdinalIgnoreCase);

    private readonly record struct FrameToPresent(FrameBuffer Frame);
}
