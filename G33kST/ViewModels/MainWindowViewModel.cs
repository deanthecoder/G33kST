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
    private readonly Lock m_frameUpdateLock = new();
    private readonly AtariST m_machine;
    private readonly MachineRunner m_runner;
    private readonly LcdScreen m_screen;
    private readonly NullAudioOutputDevice m_audioDevice;
    private readonly DisplayRecorder m_recorder;
    private readonly byte[] m_latestFrameBuffer;
    private readonly byte[] m_uiFrameBuffer;
    private int m_isUiFrameUpdateQueued;
    private uint m_lastFrameChecksum;
    private long m_lastFrameTicks;
    private bool m_hasFrameChecksum;

    public MainWindowViewModel()
    {
        m_machine = new AtariST(AtariSTOptions.Default);
        m_runner = new MachineRunner(m_machine, () => m_machine.Descriptor.CpuHz, e => Logger.Instance.Exception("Machine runner error.", e));
        m_screen = new LcdScreen(m_machine.Video.FrameWidth, m_machine.Video.FrameHeight);
        m_audioDevice = new NullAudioOutputDevice(m_machine.Descriptor.AudioSampleRateHz);
        m_recorder = new DisplayRecorder();
        m_latestFrameBuffer = new byte[m_machine.Video.FrameWidth * m_machine.Video.FrameHeight * 4];
        m_uiFrameBuffer = new byte[m_machine.Video.FrameWidth * m_machine.Video.FrameHeight * 4];

        m_machine.Video.FrameRendered += OnFrameRendered;
        m_recorder.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsRecordingIndicatorOn));
        };

        Settings.PropertyChanged += OnSettingsPropertyChanged;
        ApplySettingState();
        AboutInfo = AboutInfoProvider.Info;
        LoadRomInternal(FindBundledRom());
    }

    public Settings Settings => Settings.Instance;

    public IImage Display => m_screen.Display;

    public AboutInfo AboutInfo { get; }

    public bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    public bool IsRecording => m_recorder.IsRecording;

    public bool IsRecordingIndicatorOn => m_recorder.IsIndicatorOn;

    public bool IsSoundEnabled => Settings.IsSoundEnabled;

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

    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        m_machine.Video.FrameRendered -= OnFrameRendered;
        m_recorder.Dispose();
        m_runner.Dispose();
        m_audioDevice.Dispose();
        m_screen.Dispose();
        Settings.Dispose();
    }

    public void ResetMachine()
    {
        m_runner.Reset();
        Logger.Instance.Info("Machine reset.");
    }

    public void SaveScreenshotCommand()
    {
        var command = new DTC.Core.Commands.FileSaveCommand(
            "Save Screenshot",
            "TGA Files",
            ["*.tga"],
            $"{AppTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.tga");
        command.FileSelected += (_, file) =>
        {
            lock (m_frameUpdateLock)
                Buffer.BlockCopy(m_latestFrameBuffer, 0, m_uiFrameBuffer, 0, m_uiFrameBuffer.Length);
            TgaWriter.Write(file, m_uiFrameBuffer, m_machine.Video.FrameWidth, m_machine.Video.FrameHeight, 4);
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

    public void ToggleCrtEmulation() =>
        Settings.IsCrtEmulationEnabled = !Settings.IsCrtEmulationEnabled;

    public static void OpenLog()
    {
        var logFile = Logger.Instance.File;
        if (logFile.Exists)
            logFile.OpenWithDefaultViewer();
        else
            logFile.Directory?.Explore();
    }

    public static void OpenProjectPage() =>
        new Uri("https://github.com/deanthecoder/G33kST").Open();

    public void TrackCpuHistory() =>
        Settings.IsCpuHistoryTracked = !Settings.IsCpuHistoryTracked;

    public static void DumpCpuHistory() =>
        Console.WriteLine("CPU history tracking is not wired yet for the Atari ST core.");

    public void ReportCpuClockTicks() =>
        Console.WriteLine($"CPU clock ticks: {m_machine.CpuTicks}");

    public void ReportFrameChecksum()
    {
        if (!m_hasFrameChecksum)
        {
            Console.WriteLine($"Frame checksum: n/a (no frame rendered yet). CPU ticks: {m_machine.CpuTicks}");
            return;
        }

        Console.WriteLine($"Frame checksum: 0x{m_lastFrameChecksum:X8} @ CPU ticks {m_lastFrameTicks} (current {m_machine.CpuTicks}).");
    }

    public static void CloseCommand() =>
        Application.Current?.GetMainWindow()?.Close();

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

        lock (m_frameUpdateLock)
            Buffer.BlockCopy(frameBuffer, 0, m_latestFrameBuffer, 0, Math.Min(frameBuffer.Length, m_latestFrameBuffer.Length));

        m_lastFrameChecksum = ComputeFrameChecksum(frameBuffer);
        m_lastFrameTicks = m_machine.CpuTicks;
        m_hasFrameChecksum = true;

        m_recorder.CaptureFrame();

        if (Interlocked.Exchange(ref m_isUiFrameUpdateQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                lock (m_frameUpdateLock)
                    Buffer.BlockCopy(m_latestFrameBuffer, 0, m_uiFrameBuffer, 0, m_uiFrameBuffer.Length);
                m_screen.Update(m_uiFrameBuffer);
                DisplayUpdated?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                Interlocked.Exchange(ref m_isUiFrameUpdateQueued, 0);
            }
        }, DispatcherPriority.Render);
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
}
