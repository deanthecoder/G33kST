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
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using DTC.AtariST;
using DTC.Core;
using DTC.Core.Commands;
using DTC.Core.Extensions;
using DTC.Core.Image;
using DTC.Core.UI;
using DTC.Core.ViewModels;
using DTC.Emulation;
using DTC.Emulation.Recording;

namespace G33kST.ViewModels;

/// <summary>
/// Hosts the Atari ST machine loop and exposes desktop UI actions.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const string AppTitle = "G33kST";
    private const int DriveAIndex = 0;
    private readonly Lock m_frameUpdateLock = new();
    private readonly AtariST m_machine;
    private readonly MachineRunner m_runner;
    private readonly LcdScreen m_screen;
    private readonly NullAudioOutputDevice m_audioDevice;
    private readonly DisplayRecorder m_recorder;
    private byte[] m_backFrameBuffer;
    private byte[] m_frontFrameBuffer;
    private int m_isUiFrameUpdateQueued;
    private static readonly string[] FloppyFileExtensions = [".st", ".zip"];

    public MainWindowViewModel()
    {
        m_machine = new AtariST(AtariSTOptions.Default);
        m_runner = new MachineRunner(m_machine, () => m_machine.Descriptor.CpuHz, OnMachineRunnerError);
        m_screen = new LcdScreen(m_machine.Video.FrameWidth, m_machine.Video.FrameHeight);
        m_audioDevice = new NullAudioOutputDevice(m_machine.Descriptor.AudioSampleRateHz);
        m_recorder = new DisplayRecorder();
        var frameBufferSize = m_machine.Video.FrameWidth * m_machine.Video.FrameHeight * m_machine.Video.FrameBytesPerPixel;
        m_backFrameBuffer = new byte[frameBufferSize];
        m_frontFrameBuffer = new byte[frameBufferSize];

        m_machine.Video.FrameRendered += OnFrameRendered;
        m_recorder.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsRecordingIndicatorOn));
        };

        Settings.PropertyChanged += OnSettingsPropertyChanged;
        FloppyMru = new MruFiles().InitFromString(Settings.FloppyImageMru);
        FloppyMru.OpenRequested += (_, file) => MountFloppyImageFromFile(file, addToMru: false);
        ApplySettingState();
        AboutInfo = AboutInfoProvider.Info;
        LoadRomInternal(FindBundledRom());
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

    public bool IsSoundEnabled => Settings.IsSoundEnabled;

    public bool IsHighResolutionMode => m_machine.IsHighResolutionMode;

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

    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        m_machine.Video.FrameRendered -= OnFrameRendered;
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
        OnPropertyChanged(nameof(IsHighResolutionMode));
        Logger.Instance.Info("Machine reset.");
    }

    public void HardResetMachine()
    {
        if (m_machine.IsFloppyImageMounted(DriveAIndex))
            m_machine.UnmountFloppyImage(DriveAIndex);

        m_runner.Reset();
        OnPropertyChanged(nameof(IsHighResolutionMode));
        Logger.Instance.Info("Machine hard reset.");
    }

    public void SaveScreenshotCommand()
    {
        var command = new FileSaveCommand(
            "Save Screenshot",
            "TGA Files",
            ["*.tga"],
            $"{AppTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.tga");
        command.FileSelected += (_, file) =>
        {
            var frameCopy = new byte[m_frontFrameBuffer.Length];
            lock (m_frameUpdateLock)
                Buffer.BlockCopy(m_frontFrameBuffer, 0, frameCopy, 0, frameCopy.Length);
            TgaWriter.Write(file, frameCopy, m_machine.Video.FrameWidth, m_machine.Video.FrameHeight, m_machine.Video.FrameBytesPerPixel);
            Logger.Instance.Info($"Screenshot saved: {file.FullName}");
        };
        command.Execute(null);
    }

    public void StartRecordingCommand()
    {
        if (IsRecording)
            return;

        m_recorder.Start(m_screen.Display, m_machine.Descriptor.VideoHz, m_audioDevice, () => AppTitle);
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

    public void MountFloppyImage()
    {
        var command = new FileOpenCommand("Mount Floppy Image", "Atari ST Floppy Images", ["*.st", "*.zip"]);
        command.FileSelected += (_, info) => MountFloppyImageFromFile(info, addToMru: true);
        command.Execute(null);
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

    public void TrackCpuHistory() =>
        Settings.IsCpuHistoryTracked = !Settings.IsCpuHistoryTracked;

    public void DumpCpuHistory() =>
        Console.WriteLine("CPU history tracking is not wired yet for the Atari ST core.");

    public void ReportCpuClockTicks() =>
        Console.WriteLine($"CPU clock ticks: {m_machine.CpuTicks}");

    public void CloseCommand() =>
        Application.Current?.GetMainWindow()?.Close();

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

        var (imageName, imageData) = FloppyImageLoader.ReadImageData(imageFile);
        if (imageData == null || imageData.Length == 0)
        {
            Logger.Instance.Warn($"Unable to mount floppy image '{imageFile.FullName}': No supported floppy image data found.");
            if (imageFile.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                DialogService.Instance.ShowMessage(
                    "Unable to mount floppy image.",
                    $"The zip '{imageFile.Name}' does not contain a supported .st disk image.");
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
        Settings.LastFloppyImagePath = imageFile.FullName;

        Logger.Instance.Info(wasMounted ? $"Drive A: floppy image replaced with '{imageFile.Name}'." : $"Drive A: mounted floppy image '{imageFile.Name}'.");
        Logger.Instance.Info($"Drive A image details: {imageSummary}.");
        return true;
    }

    private void LoadRomInternal(FileInfo romFile)
    {
        if (romFile?.Exists() != true)
            return;

        try
        {
            var romData = romFile.ReadAllBytes();
            if (romData == null || romData.Length == 0)
            {
                Logger.Instance.Warn($"ROM '{romFile.FullName}' is empty.");
                return;
            }

            m_machine.LoadRom(romData, romFile.Name);
            Logger.Instance.Info($"ROM loaded: {romFile.Name} ({romData.Length / 1024.0:F1} KB).");
        }
        catch (Exception e)
        {
            Logger.Instance.Exception($"Failed to load ROM '{romFile.FullName}'.", e);
        }
    }

    private void OnFrameRendered(object sender, byte[] frameBuffer)
    {
        if (frameBuffer == null || frameBuffer.Length == 0)
            return;

        CopyToBackFrame(frameBuffer);
        m_recorder.CaptureFrame();

        if (Interlocked.Exchange(ref m_isUiFrameUpdateQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var frameToPresent = SwapFrameBuffers();
                m_screen.Update(frameToPresent);
                DisplayUpdated?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                Interlocked.Exchange(ref m_isUiFrameUpdateQueued, 0);
            }
        }, DispatcherPriority.Normal);
    }

    private void CopyToBackFrame(byte[] frameBuffer)
    {
        lock (m_frameUpdateLock)
        {
            var bytesToCopy = Math.Min(frameBuffer.Length, m_backFrameBuffer.Length);
            var source = frameBuffer.AsSpan(0, bytesToCopy);
            source.CopyTo(m_backFrameBuffer.AsSpan(0, bytesToCopy));
        }
    }

    private byte[] SwapFrameBuffers()
    {
        lock (m_frameUpdateLock)
        {
            (m_backFrameBuffer, m_frontFrameBuffer) = (m_frontFrameBuffer, m_backFrameBuffer);
            return m_frontFrameBuffer;
        }
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
        }
    }

    private void ApplySettingState()
    {
        m_audioDevice.SetEnabled(Settings.IsSoundEnabled);
        m_screen.FrameBuffer.IsCrt = Settings.IsCrtEmulationEnabled;
    }

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
        }

        return null;
    }
}
