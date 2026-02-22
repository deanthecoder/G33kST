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
using DTC.Emulation.Devices;
using DTC.Emulation.Snapshot;
using DTC.M68000;

namespace DTC.AtariST;

/// <summary>
/// Wires together Atari ST-specific devices into a single emulated machine.
/// </summary>
public sealed class AtariST : IMachine, IMachineSnapshotter
{
    // Atari ST memory map:
    // $000000-$007FFF: ROM (first 32KB, typically for TOS)
    // $FC0000-$FEFFFF: ROM (192KB, main TOS ROM area)
    // $000000-$0FFFFF: RAM (1MB for ST 1040)
    // Note: ROM is typically mapped at $FC0000 but also appears at $000000 on reset

    internal const uint RomBaseAddress = 0xFC0000;
    private const int RomSize = 192 * 1024; // 192KB ROM
    private const int FullAddressSpaceSizeBytes = 0x1000000; // 16MB.
    private const uint BootRomOverlaySize = 8;
    private const uint VideoModeRegister = 0x00FF8260;
    private const uint BlitterRegisterFromAddress = 0x00FF8A00;
    private const uint BlitterRegisterToAddress = 0x00FF8A3F;
    private const uint IoRegionFromAddress = 0x00FF0000;
    private const uint IoRegionToAddress = 0x00FFFFFF;
    private const byte LowResolutionModeValue = 0x00;
    private const byte MediumResolutionModeValue = 0x01;
    private const byte HighResolutionModeValue = 0x02;
    private const int LowResolutionWidth = 320;
    private const int LowResolutionHeight = 200;
    private const int MediumResolutionWidth = 640;
    private const int MediumResolutionHeight = 200;
    private const int HighResolutionWidth = 640;
    private const int HighResolutionHeight = 400;
    private const int MouseDragActivationThresholdPixels = 2;
    private const int MaxQueuedMousePackets = 128;
    private const int IkbdMouseBackPressureThresholdBytes = 24;
    private const int MaxPendingInterrupts = 256;
    private const int MaxPendingAcknowledgePerLevel = 64;
    private const byte HostJoystickPortIndex = 1; // ST games typically use joystick port 1.
    private const byte HostJoystickPortZeroIndex = 0;
    private const uint RealTimeClockFromAddress = 0x00FFFC20;
    private const uint RealTimeClockToAddress = 0x00FFFC3F;
    private const byte SyntheticVblInterruptLevel = 4;
    private const long MfpInputClockHz = 2_457_600;
    private const double IkbdMousePacketHz = 200.0;
    private const double ReducedIkbdMousePacketHz = 100.0;
    private const double MouseInputSampleHz = 100.0;
    private const double FloppyTransferSpeedMultiplier = 1.5;
    private readonly Shifter m_video;
    private readonly AciaIkbdDevice m_aciaIkbd;
    private readonly ShifterRegistersDevice m_shifterRegisters;
    private readonly PsgDevice m_psg;
    private readonly FloppyDmaFdcDevice m_floppyController;
    private readonly SystemControlDevice m_systemControl;
    private readonly RtcDevice m_rtc;
    private readonly MfpDevice m_mfp;
    private readonly AtariSTDescriptor m_descriptor;
    private readonly Lock m_mouseStateSync = new();
    private readonly List<PendingInterrupt> m_pendingInterrupts = new(MaxPendingInterrupts);
    private readonly InterruptAcknowledgeResult[][] m_pendingAcknowledgeByLevel = CreatePendingAcknowledgeStorage();
    private readonly int[] m_pendingAcknowledgeReadByLevel = new int[8];
    private readonly int[] m_pendingAcknowledgeCountByLevel = new int[8];
    private readonly List<MousePacket> m_pendingMousePackets = [];
    private readonly bool m_isJoystickMirroredToPort0;
    private readonly double m_mouseInputTicksPerSample;
    private readonly long m_cpuClockHz;
    private byte m_latchedInterruptLevel;
    private InterruptAcknowledgeResult m_latchedInterruptAcknowledge;
    private bool m_hasLatchedInterrupt;
    private bool m_isInputActive = true;
    private double m_mousePacketTicksPerSample;
    private long m_mfpTickRemainder;
    private double m_mousePacketTickAccumulator;
    private double m_mouseInputTickAccumulator;
    private bool m_wasMouseActive;
    private int m_lastMouseX = -1;
    private int m_lastMouseY = -1;
    private int m_estimatedMouseX = -1;
    private int m_estimatedMouseY = -1;
    private bool m_isMouseDragPending;
    private int m_mouseDownX = -1;
    private int m_mouseDownY = -1;
    private bool m_isLeftMouseButtonPressed;
    private bool m_isRightMouseButtonPressed;
    private bool m_isMousePacketCoalescingEnabled = true;
    private bool m_isMousePacketRateLimitEnabled;
    private bool m_isMouseInputSamplingEnabled;
    private bool m_isIkbdMouseBackPressureEnabled;
    private bool m_hasPendingHostMouseState;
    private HostMouseState m_pendingHostMouseState;
    private long m_droppedMousePacketsDueToIkbdBackPressureCount;
    private JoystickState m_joystickState;
    private AtariMonitorType m_monitorType;
    private AtariVideoRegion m_videoRegion;
    public IMachineDescriptor Descriptor => m_descriptor;

    public string Name => Descriptor.Name;

    public long CpuTicks => Cpu.CyclesSinceCpuStart;

    public bool HasLoadedCartridge => Rom.Data.Any(b => b != 0);

    // Minimal Shifter-backed video path.
    public IVideoSource Video => m_video;

    public IAudioSource Audio => m_psg;

    public IMachineSnapshotter Snapshotter => this;

    public Cpu Cpu { get; }

    public Memory Ram { get; }

    public RomDevice Rom { get; }

    public RomMirrorDevice RomMirror { get; }
    
    /// <summary>
    /// Gets whether the current monitor mode is monochrome high-resolution.
    /// </summary>
    public bool IsHighResolutionMode => m_monitorType == AtariMonitorType.Monochrome;

    /// <summary>
    /// Gets the currently selected PAL/NTSC timing region.
    /// </summary>
    public AtariVideoRegion VideoRegion => m_videoRegion;

    public AtariST()
        : this(AtariSTOptions.Default)
    {
    }

    public AtariST(AtariSTOptions options, Action<double, double> audioSampleSink = null)
    {
        var options1 = options ?? AtariSTOptions.Default;
        ValidateOptions(options1);
        m_monitorType = options1.MonitorType;
        m_videoRegion = options1.VideoRegion;
        m_descriptor = new AtariSTDescriptor(m_videoRegion);
        m_isJoystickMirroredToPort0 = options1.MirrorJoystickToPort0;
        m_cpuClockHz = Math.Max(1, (long)Math.Round(Descriptor.CpuHz));
        m_mousePacketTicksPerSample = Math.Max(1.0, Descriptor.CpuHz / IkbdMousePacketHz);
        m_mouseInputTicksPerSample = Math.Max(1.0, Descriptor.CpuHz / MouseInputSampleHz);

        // Create main RAM and ROM
        Ram = new Memory(options1.RamSizeBytes);
        Rom = new RomDevice(RomSize, RomBaseAddress);

        // Create ROM mirror for boot-time reset vector access
        RomMirror = new RomMirrorDevice(Rom, BootRomOverlaySize);

        // Create bus with full 24-bit address space (16MB)
        // The 68000 has a 24-bit address bus, so create a backing device for full space.
        var fullAddressSpace = new Memory(FullAddressSpaceSizeBytes);
        var bus = new Bus(fullAddressSpace);
        AttachOpenBusGap(bus, options1.RamSizeBytes);
        bus.Attach(new OpenBusDevice(IoRegionFromAddress, IoRegionToAddress));
        bus.Attach(new BusErrorDevice(BlitterRegisterFromAddress, BlitterRegisterToAddress));

        // Attach RAM and ROM to the bus (they will override the full address space in their ranges)
        bus.Attach(Ram);
        bus.Attach(Rom);
        m_systemControl = new SystemControlDevice();
        bus.Attach(m_systemControl);
        m_aciaIkbd = new AciaIkbdDevice();
        m_aciaIkbd.KeyboardInterruptLineChanged += OnKeyboardInterruptLineChanged;
        bus.Attach(m_aciaIkbd);
        m_shifterRegisters = new ShifterRegistersDevice();
        bus.Attach(m_shifterRegisters);
        m_psg = new PsgDevice(audioSampleSink, (int)m_cpuClockHz, Descriptor.AudioSampleRateHz);
        m_psg.PortAChanged += OnPsgPortAChanged;
        bus.Attach(m_psg);
        var floppyTimingCpuHz = options1.AccelerateFloppyAccess ? 0 : m_cpuClockHz;
        m_floppyController = new FloppyDmaFdcDevice(
            driveAPresent: true,
            driveBPresent: false,
            cpuHz: floppyTimingCpuHz,
            transferSpeedMultiplier: FloppyTransferSpeedMultiplier);
        m_floppyController.ConfigureDmaAddressLimit(options1.RamSizeBytes);
        m_floppyController.DmaWrite8 = (address, value) => bus.Write8(address, value);
        m_floppyController.InterruptLineChanged += OnFloppyInterruptLineChanged;
        bus.Attach(m_floppyController);
        m_rtc = null;
        if (options1.HasRealTimeClock)
        {
            m_rtc = new RtcDevice();
            m_rtc.Reset();
            bus.Attach(m_rtc);
        }
        else
            bus.Attach(new OpenBusDevice(RealTimeClockFromAddress, RealTimeClockToAddress));

        m_mfp = new MfpDevice();
        m_mfp.SetMonitorType(m_monitorType);
        m_mfp.InterruptRequested += OnMfpInterruptRequested;
        bus.Attach(m_mfp);

        // Attach ROM mirror last so it takes priority over RAM at $000000-$000007
        bus.Attach(RomMirror);
        SetBootVideoMode(bus);

        // Create CPU
        Cpu = new Cpu(bus)
        {
            InterruptAcknowledge = ResolveInterruptAcknowledge
        };

        // Create minimal low-resolution video source.
        m_video = new Shifter(bus, Descriptor.CpuHz, Descriptor.VideoHz);
    }

    public void Reset()
    {
        m_video.Reset();
        m_systemControl.Reset();
        m_aciaIkbd.Reset();
        m_shifterRegisters.Reset();
        m_psg.Reset();
        m_floppyController.Reset();
        m_rtc?.Reset();
        m_mfp.Reset();
        m_mfp.SetMonitorType(m_monitorType);
        SetBootVideoMode(Cpu.Bus);
        m_pendingInterrupts.Clear();
        Array.Clear(m_pendingAcknowledgeReadByLevel, 0, m_pendingAcknowledgeReadByLevel.Length);
        Array.Clear(m_pendingAcknowledgeCountByLevel, 0, m_pendingAcknowledgeCountByLevel.Length);
        m_pendingMousePackets.Clear();
        m_mousePacketTickAccumulator = 0;
        m_mouseInputTickAccumulator = 0;
        m_hasPendingHostMouseState = false;
        m_droppedMousePacketsDueToIkbdBackPressureCount = 0;
        m_hasLatchedInterrupt = false;
        m_latchedInterruptLevel = 0;
        m_latchedInterruptAcknowledge = default;
        m_mfpTickRemainder = 0;
        lock (m_mouseStateSync)
        {
            m_isInputActive = true;
            m_wasMouseActive = false;
            m_lastMouseX = -1;
            m_lastMouseY = -1;
            m_estimatedMouseX = m_video.ActiveWidth / 2;
            m_estimatedMouseY = m_video.ActiveHeight / 2;
            m_isMouseDragPending = false;
            m_mouseDownX = -1;
            m_mouseDownY = -1;
            m_isLeftMouseButtonPressed = false;
            m_isRightMouseButtonPressed = false;
            m_joystickState = JoystickState.Neutral;
        }
        Cpu.Reset();
    }

    /// <summary>
    /// Switches PAL/NTSC video timing used for VBL pacing and shifter frame cadence.
    /// </summary>
    /// <remarks>
    /// Callers should reset the machine after changing this so software reinitializes timing assumptions.
    /// </remarks>
    public void SetVideoRegion(AtariVideoRegion videoRegion)
    {
        if (m_videoRegion == videoRegion)
            return;

        m_videoRegion = videoRegion;
        m_descriptor.SetVideoRegion(videoRegion);
        m_video.SetTiming(Descriptor.CpuHz, Descriptor.VideoHz);
    }

    public void LoadRom(byte[] romData, string romName)
    {
        if (romData == null || romData.Length == 0)
            throw new ArgumentException($"'{romName}' ROM data is empty.", nameof(romData));

        if (romData.Length > RomSize)
            throw new ArgumentException($"'{romName}'' ROM data is too large. Maximum size is {RomSize} bytes.", nameof(romData));

        // Copy ROM data into ROM memory
        Array.Copy(romData, 0, Rom.Data, 0, romData.Length);

        // Reset CPU to load vectors from ROM
        Reset();
    }

    public void StepCpu()
    {
        Cpu.Step();
    }

    public void AdvanceDevices(long deltaTicks)
    {
        m_aciaIkbd.Advance();
        m_psg.AdvanceCycles(deltaTicks);
        m_video.Advance(deltaTicks, OnHblank, OnVblank);
        m_floppyController.Advance(deltaTicks);
        SamplePendingHostMouseStateOnCadence(deltaTicks);
        FlushPendingMousePacketsOnCadence(deltaTicks);
        var mfpTicks = ScaleCpuTicksForMfp(deltaTicks);
        if (mfpTicks > 0)
            m_mfp.Advance(mfpTicks);
    }

    public bool TryConsumeInterrupt()
    {
        if (m_hasLatchedInterrupt)
            return true;
        if (m_pendingInterrupts.Count == 0)
            return false;

        var selectedIndex = 0;
        var pendingInterrupt = m_pendingInterrupts[0];
        for (var index = 1; index < m_pendingInterrupts.Count; index++)
        {
            var candidate = m_pendingInterrupts[index];
            if (candidate.Level <= pendingInterrupt.Level)
                continue;
            pendingInterrupt = candidate;
            selectedIndex = index;
        }

        m_pendingInterrupts.RemoveAt(selectedIndex);
        m_latchedInterruptLevel = pendingInterrupt.Level;
        m_latchedInterruptAcknowledge = pendingInterrupt.AcknowledgeResult;
        m_hasLatchedInterrupt = true;
        return true;
    }

    public void RequestInterrupt()
    {
        if (!m_hasLatchedInterrupt)
            return;

        Cpu.RequestInterrupt(m_latchedInterruptLevel);
        if (m_latchedInterruptLevel != 6)
            EnqueuePendingAcknowledge(m_latchedInterruptLevel, m_latchedInterruptAcknowledge);
        m_hasLatchedInterrupt = false;
        m_latchedInterruptLevel = 0;
        m_latchedInterruptAcknowledge = default;
    }

    public void SetInputActive(bool isActive)
    {
        lock (m_mouseStateSync)
        {
            m_isInputActive = isActive;
            if (!isActive)
            {
                m_hasPendingHostMouseState = false;
                m_mouseInputTickAccumulator = 0;
                ApplyHostMouseStateNoLock(0, 0, false, false, false);
            }
        }
        if (!isActive)
            UpdateJoystickState(JoystickState.Neutral);
    }

    /// <summary>
    /// Toggles between low-resolution color and high-resolution monochrome display modes.
    /// </summary>
    public void ToggleDisplayResolutionMode()
    {
        m_monitorType = m_monitorType == AtariMonitorType.Monochrome
            ? AtariMonitorType.Color
            : AtariMonitorType.Monochrome;
        ApplyMonitorAndVideoMode(Cpu.Bus);
    }

    /// <summary>
    /// Injects one raw keyboard scan code byte into the emulated keyboard controller receive stream.
    /// </summary>
    public void InjectKeyboardScanCode(byte scanCode) =>
        m_aciaIkbd.QueueKeyboardByte(scanCode);

    /// <summary>
    /// Clears pending IKBD receive bytes.
    /// </summary>
    public void ClearKeyboardInputQueue() =>
        m_aciaIkbd.ClearReceiveQueue();

    /// <summary>
    /// Clears queued host mouse packets awaiting IKBD cadence flush.
    /// </summary>
    private void ClearMouseInputQueue()
    {
        lock (m_mouseStateSync)
        {
            m_pendingMousePackets.Clear();
            m_hasPendingHostMouseState = false;
            m_mouseInputTickAccumulator = 0;
        }
    }

    /// <summary>
    /// Clears queued IKBD receive bytes and pending host mouse packets.
    /// </summary>
    public void ClearInputQueues()
    {
        ClearKeyboardInputQueue();
        ClearMouseInputQueue();
    }

    /// <summary>
    /// Gets the number of queued IKBD receive bytes.
    /// </summary>
    public int PendingKeyboardInputByteCount =>
        m_aciaIkbd.PendingReceiveQueueCount;
    
    /// <summary>
    /// Gets the number of queued IKBD bytes belonging to mouse packets.
    /// </summary>
    public int PendingIkbdMousePacketByteCount =>
        m_aciaIkbd.PendingMousePacketByteCount;

    /// <summary>
    /// Gets the number of queued host mouse packets awaiting cadence flush.
    /// </summary>
    public int PendingMousePacketCount
    {
        get
        {
            lock (m_mouseStateSync)
                return m_pendingMousePackets.Count;
        }
    }

    /// <summary>
    /// Gets the number of host mouse packets dropped due to IKBD mouse back-pressure.
    /// </summary>
    public long DroppedMousePacketsDueToIkbdBackPressureCount
    {
        get
        {
            lock (m_mouseStateSync)
                return m_droppedMousePacketsDueToIkbdBackPressureCount;
        }
    }
    
    /// <summary>
    /// Enables or disables host mouse-packet coalescing before IKBD cadence flush.
    /// </summary>
    public void SetMousePacketCoalescingEnabled(bool isEnabled)
    {
        lock (m_mouseStateSync)
            m_isMousePacketCoalescingEnabled = isEnabled;
    }

    /// <summary>
    /// Enables or disables reduced IKBD mouse output cadence (200Hz -> 100Hz).
    /// </summary>
    public void SetMousePacketRateLimitEnabled(bool isEnabled)
    {
        lock (m_mouseStateSync)
        {
            m_isMousePacketRateLimitEnabled = isEnabled;
            var packetHz = isEnabled ? ReducedIkbdMousePacketHz : IkbdMousePacketHz;
            m_mousePacketTicksPerSample = Math.Max(1.0, Descriptor.CpuHz / packetHz);
            if (m_mousePacketTickAccumulator >= m_mousePacketTicksPerSample)
                m_mousePacketTickAccumulator %= m_mousePacketTicksPerSample;
        }
    }

    /// <summary>
    /// Enables or disables host mouse-state sampling on emulated-time cadence before packet generation.
    /// </summary>
    public void SetMouseInputSamplingEnabled(bool isEnabled)
    {
        lock (m_mouseStateSync)
        {
            if (m_isMouseInputSamplingEnabled == isEnabled)
                return;

            m_isMouseInputSamplingEnabled = isEnabled;
            m_mouseInputTickAccumulator = 0;
            if (!isEnabled && m_hasPendingHostMouseState)
            {
                var pendingState = m_pendingHostMouseState;
                m_hasPendingHostMouseState = false;
                ApplyHostMouseStateNoLock(
                    pendingState.NormalizedX,
                    pendingState.NormalizedY,
                    pendingState.IsLeftButtonPressed,
                    pendingState.IsRightButtonPressed,
                    pendingState.IsPointerWithinDisplay);
            }
        }
    }

    /// <summary>
    /// Enables or disables IKBD mouse back-pressure that drops stale host packets while IKBD mouse bytes are already backlogged.
    /// </summary>
    public void SetIkbdMouseBackPressureEnabled(bool isEnabled)
    {
        lock (m_mouseStateSync)
            m_isIkbdMouseBackPressureEnabled = isEnabled;
    }

    /// <summary>
    /// Injects one keyboard key state transition as an IKBD make/break scan code.
    /// </summary>
    public void InjectKeyboardKeyState(byte scanCode, bool isPressed)
    {
        var keyCode = (byte)(scanCode & 0x7F);
        if (!isPressed)
            keyCode |= 0x80;
        m_aciaIkbd.QueueKeyboardByte(keyCode);
    }

    /// <summary>
    /// Updates the host joystick state and queues IKBD joystick packets for ST joystick port 1.
    /// </summary>
    public void UpdateJoystickState(JoystickState state)
    {
        lock (m_mouseStateSync)
        {
            if (!m_isInputActive)
            {
                ReleaseJoystickNoLock();
                return;
            }

            state = state.NormalizeOpposingDirections();
            if (state == m_joystickState)
                return;

            m_joystickState = state;
            m_aciaIkbd.QueueJoystickState(HostJoystickPortIndex, state);
            if (m_isJoystickMirroredToPort0)
                m_aciaIkbd.QueueJoystickState(HostJoystickPortZeroIndex, state);
        }
    }

    /// <summary>
    /// Tries to mount one floppy image into drive A: (0) or B: (1).
    /// A newly mounted image replaces any previous image in that drive.
    /// </summary>
    public bool TryMountFloppyImage(int driveIndex, byte[] imageData, string imageName) =>
        m_floppyController.TryMountImage(driveIndex, imageData, imageName);

    /// <summary>
    /// Unmounts the floppy image currently inserted in the selected drive.
    /// </summary>
    public void UnmountFloppyImage(int driveIndex) =>
        m_floppyController.UnmountImage(driveIndex);

    /// <summary>
    /// Reports whether the selected drive currently has a mounted image.
    /// </summary>
    public bool IsFloppyImageMounted(int driveIndex) =>
        m_floppyController.IsImageMounted(driveIndex);

    /// <summary>
    /// Returns the mounted image display name for the selected drive.
    /// </summary>
    public string GetMountedFloppyImageName(int driveIndex) =>
        m_floppyController.GetMountedImageName(driveIndex);

    /// <summary>
    /// Returns a snapshot of recent floppy activity counters for hang diagnostics.
    /// </summary>
    public FloppyDebugStats GetFloppyDebugStats() =>
        m_floppyController.GetDebugStats();

    /// <summary>
    /// Returns recent floppy trace lines, newest last, to help pinpoint freeze points.
    /// </summary>
    public IReadOnlyList<string> GetRecentFloppyTraceLines(int maxLines) =>
        m_floppyController.GetRecentTraceLines(maxLines);

    int IMachineSnapshotter.GetStateSize() =>
        GetSnapshotStateSize();

    void IMachineSnapshotter.Save(MachineState state, Span<byte> frameBuffer)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (state.Size != GetSnapshotStateSize())
            throw new InvalidOperationException($"State buffer size mismatch. Expected {GetSnapshotStateSize()} bytes.");

        var writer = state.CreateWriter();
        WriteSnapshotState(ref writer);
        if (writer.Offset != state.Size)
            throw new InvalidOperationException($"State buffer write size mismatch. Wrote {writer.Offset} bytes, expected {state.Size}.");

        m_video.CopyToFrameBuffer(frameBuffer);
    }

    void IMachineSnapshotter.Load(MachineState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var reader = state.CreateReader();
        ReadSnapshotState(ref reader);
        if (reader.Offset != state.Size)
            throw new InvalidOperationException("State buffer read size mismatch.");
    }

    private int GetSnapshotStateSize()
    {
        var size =
            sizeof(uint) + // magic
            sizeof(ushort) + // version
            sizeof(ushort) + // machine version
            1 + // rtc-present flag
            (Cpu?.GetStateSize() ?? 0) +
            Ram.GetStateSize() +
            m_systemControl.GetStateSize() +
            m_aciaIkbd.GetStateSize() +
            m_shifterRegisters.GetStateSize() +
            m_psg.GetStateSize() +
            m_floppyController.GetStateSize() +
            m_mfp.GetStateSize() +
            m_video.GetStateSize() +
            (m_rtc?.GetStateSize() ?? 0) +
            1114; // machine scalar state + pending-ack ring storage (excluding dynamic queues).

        size += m_pendingInterrupts.Count * 3;

        lock (m_mouseStateSync)
            size += 86 + m_pendingMousePackets.Count * 4;

        return size;
    }

    private void WriteSnapshotState(ref StateWriter writer)
    {
        const ushort snapshotVersion = 1;
        writer.WriteUInt32(MachineState.Magic);
        writer.WriteUInt16(MachineState.Version);
        writer.WriteUInt16(snapshotVersion);
        writer.WriteBool(m_rtc != null);

        // Machine-level orchestration state first to avoid lock-order inversions with device locks.
        writer.WriteByte(m_latchedInterruptLevel);
        WriteInterruptAcknowledge(ref writer, m_latchedInterruptAcknowledge);
        writer.WriteBool(m_hasLatchedInterrupt);
        writer.WriteDouble(m_mousePacketTicksPerSample);
        writer.WriteInt64(m_mfpTickRemainder);
        writer.WriteByte((byte)m_monitorType);
        writer.WriteByte((byte)m_videoRegion);

        writer.WriteInt32(m_pendingInterrupts.Count);
        foreach (var pending in m_pendingInterrupts)
        {
            writer.WriteByte(pending.Level);
            WriteInterruptAcknowledge(ref writer, pending.AcknowledgeResult);
        }

        for (var level = 0; level < m_pendingAcknowledgeByLevel.Length; level++)
        {
            writer.WriteInt32(m_pendingAcknowledgeReadByLevel[level]);
            writer.WriteInt32(m_pendingAcknowledgeCountByLevel[level]);
            var entries = m_pendingAcknowledgeByLevel[level];
            for (var i = 0; i < entries.Length; i++)
                WriteInterruptAcknowledge(ref writer, entries[i]);
        }

        lock (m_mouseStateSync)
        {
            writer.WriteBool(m_isInputActive);
            writer.WriteDouble(m_mousePacketTickAccumulator);
            writer.WriteDouble(m_mouseInputTickAccumulator);
            writer.WriteBool(m_wasMouseActive);
            writer.WriteInt32(m_lastMouseX);
            writer.WriteInt32(m_lastMouseY);
            writer.WriteInt32(m_estimatedMouseX);
            writer.WriteInt32(m_estimatedMouseY);
            writer.WriteBool(m_isMouseDragPending);
            writer.WriteInt32(m_mouseDownX);
            writer.WriteInt32(m_mouseDownY);
            writer.WriteBool(m_isLeftMouseButtonPressed);
            writer.WriteBool(m_isRightMouseButtonPressed);
            writer.WriteBool(m_isMousePacketCoalescingEnabled);
            writer.WriteBool(m_isMousePacketRateLimitEnabled);
            writer.WriteBool(m_isMouseInputSamplingEnabled);
            writer.WriteBool(m_isIkbdMouseBackPressureEnabled);
            writer.WriteBool(m_hasPendingHostMouseState);
            writer.WriteDouble(m_pendingHostMouseState.NormalizedX);
            writer.WriteDouble(m_pendingHostMouseState.NormalizedY);
            writer.WriteBool(m_pendingHostMouseState.IsLeftButtonPressed);
            writer.WriteBool(m_pendingHostMouseState.IsRightButtonPressed);
            writer.WriteBool(m_pendingHostMouseState.IsPointerWithinDisplay);
            writer.WriteInt64(m_droppedMousePacketsDueToIkbdBackPressureCount);
            WriteJoystickState(ref writer, m_joystickState);
            writer.WriteInt32(m_pendingMousePackets.Count);
            foreach (var packet in m_pendingMousePackets)
                WriteMousePacket(ref writer, packet);
        }

        // CPU state.
        var cpuState = new MachineState(Cpu.GetStateSize());
        Cpu.SaveState(cpuState);
        var cpuBytes = new byte[cpuState.Size];
        var cpuReader = cpuState.CreateReader();
        cpuReader.ReadBytes(cpuBytes);
        writer.WriteBytes(cpuBytes);

        // Device state.
        Ram.SaveState(ref writer);
        m_systemControl.SaveState(ref writer);
        m_aciaIkbd.SaveState(ref writer);
        m_shifterRegisters.SaveState(ref writer);
        m_psg.SaveState(ref writer);
        m_floppyController.SaveState(ref writer);
        m_mfp.SaveState(ref writer);
        m_video.SaveState(ref writer);
        if (m_rtc != null)
            m_rtc.SaveState(ref writer);
    }

    private void ReadSnapshotState(ref StateReader reader)
    {
        const ushort snapshotVersion = 1;
        var magic = reader.ReadUInt32();
        if (magic != MachineState.Magic)
            throw new InvalidOperationException("Invalid Atari ST state buffer (bad magic).");

        var version = reader.ReadUInt16();
        if (version != MachineState.Version)
            throw new InvalidOperationException($"Unsupported Atari ST state version {version}.");

        var machineVersion = reader.ReadUInt16();
        if (machineVersion != snapshotVersion)
            throw new InvalidOperationException($"Unsupported Atari ST snapshot layout version {machineVersion}.");

        var stateHasRtc = reader.ReadBool();
        if (stateHasRtc != (m_rtc != null))
            throw new InvalidOperationException("Snapshot RTC configuration does not match the current machine.");

        m_latchedInterruptLevel = reader.ReadByte();
        m_latchedInterruptAcknowledge = ReadInterruptAcknowledge(ref reader);
        m_hasLatchedInterrupt = reader.ReadBool();
        m_mousePacketTicksPerSample = reader.ReadDouble();
        m_mfpTickRemainder = reader.ReadInt64();
        m_monitorType = (AtariMonitorType)reader.ReadByte();
        m_videoRegion = (AtariVideoRegion)reader.ReadByte();
        m_descriptor.SetVideoRegion(m_videoRegion);

        var pendingInterruptCount = reader.ReadInt32();
        if (pendingInterruptCount < 0 || pendingInterruptCount > MaxPendingInterrupts)
            throw new InvalidOperationException("Snapshot pending interrupt queue is invalid.");
        m_pendingInterrupts.Clear();
        for (var i = 0; i < pendingInterruptCount; i++)
            m_pendingInterrupts.Add(new PendingInterrupt(reader.ReadByte(), ReadInterruptAcknowledge(ref reader)));

        for (var level = 0; level < m_pendingAcknowledgeByLevel.Length; level++)
        {
            m_pendingAcknowledgeReadByLevel[level] = reader.ReadInt32();
            m_pendingAcknowledgeCountByLevel[level] = reader.ReadInt32();
            var entries = m_pendingAcknowledgeByLevel[level];
            for (var i = 0; i < entries.Length; i++)
                entries[i] = ReadInterruptAcknowledge(ref reader);
        }

        lock (m_mouseStateSync)
        {
            m_isInputActive = reader.ReadBool();
            m_mousePacketTickAccumulator = reader.ReadDouble();
            m_mouseInputTickAccumulator = reader.ReadDouble();
            m_wasMouseActive = reader.ReadBool();
            m_lastMouseX = reader.ReadInt32();
            m_lastMouseY = reader.ReadInt32();
            m_estimatedMouseX = reader.ReadInt32();
            m_estimatedMouseY = reader.ReadInt32();
            m_isMouseDragPending = reader.ReadBool();
            m_mouseDownX = reader.ReadInt32();
            m_mouseDownY = reader.ReadInt32();
            m_isLeftMouseButtonPressed = reader.ReadBool();
            m_isRightMouseButtonPressed = reader.ReadBool();
            m_isMousePacketCoalescingEnabled = reader.ReadBool();
            m_isMousePacketRateLimitEnabled = reader.ReadBool();
            m_isMouseInputSamplingEnabled = reader.ReadBool();
            m_isIkbdMouseBackPressureEnabled = reader.ReadBool();
            m_hasPendingHostMouseState = reader.ReadBool();
            m_pendingHostMouseState = new HostMouseState(
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadBool(),
                reader.ReadBool(),
                reader.ReadBool());
            m_droppedMousePacketsDueToIkbdBackPressureCount = reader.ReadInt64();
            m_joystickState = ReadJoystickState(ref reader);
            var pendingMousePacketCount = reader.ReadInt32();
            if (pendingMousePacketCount < 0 || pendingMousePacketCount > MaxQueuedMousePackets)
                throw new InvalidOperationException("Snapshot mouse packet queue is invalid.");
            m_pendingMousePackets.Clear();
            for (var i = 0; i < pendingMousePacketCount; i++)
                m_pendingMousePackets.Add(ReadMousePacket(ref reader));
        }

        var cpuStateBytes = new byte[Cpu.GetStateSize()];
        reader.ReadBytes(cpuStateBytes);
        var cpuState = new MachineState(cpuStateBytes.Length);
        var cpuWriter = cpuState.CreateWriter();
        cpuWriter.WriteBytes(cpuStateBytes);
        Cpu.LoadState(cpuState);

        Ram.LoadState(ref reader);
        m_systemControl.LoadState(ref reader);
        m_aciaIkbd.LoadState(ref reader);
        m_shifterRegisters.LoadState(ref reader);
        m_psg.LoadState(ref reader);
        m_floppyController.LoadState(ref reader);
        m_mfp.LoadState(ref reader);
        m_video.LoadState(ref reader);
        if (m_rtc != null)
            m_rtc.LoadState(ref reader);
    }

    private static void WriteInterruptAcknowledge(ref StateWriter writer, InterruptAcknowledgeResult value)
    {
        writer.WriteByte((byte)value.Type);
        writer.WriteByte(value.VectorNumber);
    }

    private static InterruptAcknowledgeResult ReadInterruptAcknowledge(ref StateReader reader)
    {
        var type = (InterruptAcknowledgeType)reader.ReadByte();
        var vectorNumber = reader.ReadByte();
        return type switch
        {
            InterruptAcknowledgeType.Autovector => InterruptAcknowledgeResult.Autovector(),
            InterruptAcknowledgeType.Spurious => InterruptAcknowledgeResult.Spurious(),
            InterruptAcknowledgeType.VectorNumber => InterruptAcknowledgeResult.Vector(vectorNumber),
            _ => throw new InvalidOperationException($"Invalid interrupt acknowledge type {type}.")
        };
    }

    private static void WriteJoystickState(ref StateWriter writer, JoystickState state)
    {
        writer.WriteBool(state.IsUpPressed);
        writer.WriteBool(state.IsDownPressed);
        writer.WriteBool(state.IsLeftPressed);
        writer.WriteBool(state.IsRightPressed);
        writer.WriteBool(state.IsFirePressed);
    }

    private static JoystickState ReadJoystickState(ref StateReader reader) =>
        new(
            reader.ReadBool(),
            reader.ReadBool(),
            reader.ReadBool(),
            reader.ReadBool(),
            reader.ReadBool());

    private static void WriteMousePacket(ref StateWriter writer, MousePacket packet)
    {
        writer.WriteByte((byte)packet.DeltaX);
        writer.WriteByte((byte)packet.DeltaY);
        writer.WriteBool(packet.IsLeftButtonPressed);
        writer.WriteBool(packet.IsRightButtonPressed);
    }

    private static MousePacket ReadMousePacket(ref StateReader reader) =>
        new(
            unchecked((sbyte)reader.ReadByte()),
            unchecked((sbyte)reader.ReadByte()),
            reader.ReadBool(),
            reader.ReadBool());

    /// <summary>
    /// Updates host mouse state and translates it into IKBD relative mouse packets.
    /// </summary>
    public void UpdateMouseState(double normalizedX, double normalizedY, bool isLeftButtonPressed, bool isRightButtonPressed, bool isPointerWithinDisplay)
    {
        lock (m_mouseStateSync)
        {
            if (m_isMouseInputSamplingEnabled)
            {
                m_pendingHostMouseState = new HostMouseState(
                    normalizedX,
                    normalizedY,
                    isLeftButtonPressed,
                    isRightButtonPressed,
                    isPointerWithinDisplay);
                m_hasPendingHostMouseState = true;
                return;
            }

            ApplyHostMouseStateNoLock(
                normalizedX,
                normalizedY,
                isLeftButtonPressed,
                isRightButtonPressed,
                isPointerWithinDisplay);
        }
    }

    private void SamplePendingHostMouseStateOnCadence(long deltaTicks)
    {
        if (deltaTicks <= 0)
            return;

        lock (m_mouseStateSync)
        {
            if (!m_isMouseInputSamplingEnabled)
                return;

            m_mouseInputTickAccumulator += deltaTicks;
            var sampleCount = (int)(m_mouseInputTickAccumulator / m_mouseInputTicksPerSample);
            if (sampleCount <= 0)
                return;
            m_mouseInputTickAccumulator -= sampleCount * m_mouseInputTicksPerSample;
            if (!m_hasPendingHostMouseState)
                return;

            var pendingState = m_pendingHostMouseState;
            m_hasPendingHostMouseState = false;
            ApplyHostMouseStateNoLock(
                pendingState.NormalizedX,
                pendingState.NormalizedY,
                pendingState.IsLeftButtonPressed,
                pendingState.IsRightButtonPressed,
                pendingState.IsPointerWithinDisplay);
        }
    }

    private void ApplyHostMouseStateNoLock(double normalizedX, double normalizedY, bool isLeftButtonPressed, bool isRightButtonPressed, bool isPointerWithinDisplay)
    {
        var isMouseActive = m_isInputActive && isPointerWithinDisplay;
        if (!isMouseActive)
        {
            m_pendingMousePackets.Clear();
            ReleaseMouseButtonsNoLock();
            m_wasMouseActive = false;
            return;
        }

        normalizedX = Math.Clamp(normalizedX, 0.0, 1.0);
        normalizedY = Math.Clamp(normalizedY, 0.0, 1.0);

        var (activeWidth, activeHeight) = GetMouseCoordinateSpace();
        var mouseX = (int)Math.Round(normalizedX * (activeWidth - 1));
        var mouseY = (int)Math.Round(normalizedY * (activeHeight - 1));

        if (!m_wasMouseActive)
        {
            if (m_estimatedMouseX < 0 || m_estimatedMouseY < 0)
            {
                m_estimatedMouseX = activeWidth / 2;
                m_estimatedMouseY = activeHeight / 2;
            }

            var syncDeltaX = mouseX - m_estimatedMouseX;
            var syncDeltaY = mouseY - m_estimatedMouseY;
            if (syncDeltaX != 0 || syncDeltaY != 0)
            {
                QueueRelativeMouseDeltasNoLock(syncDeltaX, syncDeltaY, isLeftButtonPressed, isRightButtonPressed);
                m_estimatedMouseX = mouseX;
                m_estimatedMouseY = mouseY;
            }

            m_wasMouseActive = true;
            m_lastMouseX = mouseX;
            m_lastMouseY = mouseY;
            m_isLeftMouseButtonPressed = isLeftButtonPressed;
            m_isRightMouseButtonPressed = isRightButtonPressed;
            return;
        }

        var hasPreviousPosition = m_lastMouseX >= 0 && m_lastMouseY >= 0;
        var deltaX = hasPreviousPosition ? mouseX - m_lastMouseX : 0;
        var deltaY = hasPreviousPosition ? mouseY - m_lastMouseY : 0;
        var wasAnyButtonPressed = m_isLeftMouseButtonPressed || m_isRightMouseButtonPressed;
        var isAnyButtonPressed = isLeftButtonPressed || isRightButtonPressed;
        var buttonsChanged =
            isLeftButtonPressed != m_isLeftMouseButtonPressed ||
            isRightButtonPressed != m_isRightMouseButtonPressed;

        if (buttonsChanged && !wasAnyButtonPressed && isAnyButtonPressed)
        {
            m_isMouseDragPending = true;
            m_mouseDownX = m_lastMouseX >= 0 ? m_lastMouseX : mouseX;
            m_mouseDownY = m_lastMouseY >= 0 ? m_lastMouseY : mouseY;
        }

        if (!isAnyButtonPressed)
            m_isMouseDragPending = false;

        if (isAnyButtonPressed && m_isMouseDragPending)
        {
            var distanceX = Math.Abs(mouseX - m_mouseDownX);
            var distanceY = Math.Abs(mouseY - m_mouseDownY);
            if (distanceX <= MouseDragActivationThresholdPixels && distanceY <= MouseDragActivationThresholdPixels)
            {
                mouseX = m_mouseDownX;
                mouseY = m_mouseDownY;
                deltaX = hasPreviousPosition ? mouseX - m_lastMouseX : 0;
                deltaY = hasPreviousPosition ? mouseY - m_lastMouseY : 0;
            }
            else
                m_isMouseDragPending = false;
        }

        m_lastMouseX = mouseX;
        m_lastMouseY = mouseY;
        m_isLeftMouseButtonPressed = isLeftButtonPressed;
        m_isRightMouseButtonPressed = isRightButtonPressed;

        if (!hasPreviousPosition || deltaX == 0 && deltaY == 0)
        {
            if (buttonsChanged)
                EnqueueMousePacketNoLock(0, 0, isLeftButtonPressed, isRightButtonPressed);
            return;
        }

        QueueRelativeMouseDeltasNoLock(deltaX, deltaY, isLeftButtonPressed, isRightButtonPressed);
        m_estimatedMouseX = mouseX;
        m_estimatedMouseY = mouseY;
    }

    private void OnHblank()
    {
        m_mfp.NotifyHblank(m_video.IsDisplayEnableActive);
    }

    private (int Width, int Height) GetMouseCoordinateSpace()
    {
        // Medium-resolution output is vertically line-doubled for display, but IKBD mouse deltas
        // should remain in 640x200 logical coordinates so host pointer speed matches on-screen motion.
        return (Cpu.Bus.Read8(VideoModeRegister) & 0x03) switch
        {
            LowResolutionModeValue => (LowResolutionWidth, LowResolutionHeight),
            MediumResolutionModeValue => (MediumResolutionWidth, MediumResolutionHeight),
            HighResolutionModeValue => (HighResolutionWidth, HighResolutionHeight),
            _ => (Math.Max(1, m_video.ActiveWidth), Math.Max(1, m_video.ActiveHeight))
        };
    }

    private void OnVblank()
    {
        EnqueueInterrupt(SyntheticVblInterruptLevel, InterruptAcknowledgeResult.Autovector());
    }

    private void OnKeyboardInterruptLineChanged(bool isActiveLow) =>
        m_mfp.SetAciaInterruptLine(isActiveLow);

    private void OnFloppyInterruptLineChanged(bool isActiveLow) =>
        m_mfp.SetFloppyInterruptLine(isActiveLow);

    private void OnPsgPortAChanged(byte value) =>
        m_floppyController.ApplyPortA(value);

    /// <summary>
    /// Handles MFP interrupt requests.
    /// MFP is the ST's Multi-Function Peripheral chip that owns timer and peripheral interrupt sources.
    /// </summary>
    private void OnMfpInterruptRequested(byte level, byte vector) =>
        EnqueueInterrupt(level, InterruptAcknowledgeResult.Autovector());

    private void EnqueueInterrupt(byte level, InterruptAcknowledgeResult acknowledgeResult)
    {
        var allowDuplicate = level == 6 && acknowledgeResult.Type == InterruptAcknowledgeType.Autovector;
        if (!allowDuplicate && m_pendingInterrupts.Count > 0)
        {
            foreach (var pending in m_pendingInterrupts)
            {
                if (pending.Level != level || pending.AcknowledgeResult != acknowledgeResult)
                    continue;
                return;
            }
        }
        if (m_pendingInterrupts.Count >= MaxPendingInterrupts)
            return;
        m_pendingInterrupts.Add(new PendingInterrupt(level, acknowledgeResult));
    }

    private InterruptAcknowledgeResult ResolveInterruptAcknowledge(byte level)
    {
        if (level == 6)
        {
            if (!m_mfp.TryAcknowledgePendingInterrupt(out var vectorNumber))
                return InterruptAcknowledgeResult.Autovector();
            if (m_mfp.HasUnmaskedPendingInterrupt)
                EnqueueInterrupt(6, InterruptAcknowledgeResult.Autovector());
            return InterruptAcknowledgeResult.Vector(vectorNumber);
        }

        if (level >= m_pendingAcknowledgeByLevel.Length)
            return InterruptAcknowledgeResult.Autovector();
        if (m_pendingAcknowledgeCountByLevel[level] == 0)
            return InterruptAcknowledgeResult.Autovector();

        var readIndex = m_pendingAcknowledgeReadByLevel[level];
        var acknowledgeResult = m_pendingAcknowledgeByLevel[level][readIndex];
        var nextReadIndex = readIndex + 1;
        m_pendingAcknowledgeReadByLevel[level] = nextReadIndex == MaxPendingAcknowledgePerLevel ? 0 : nextReadIndex;
        m_pendingAcknowledgeCountByLevel[level]--;
        return acknowledgeResult;
    }

    private static void ValidateOptions(AtariSTOptions options)
    {
        if (options.RamSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RAM size must be greater than zero.");
        if (options.RamSizeBytes > RomBaseAddress)
            throw new ArgumentOutOfRangeException(nameof(options), $"RAM size must be <= ${RomBaseAddress:X6}.");
    }

    private static void AttachOpenBusGap(Bus bus, uint ramSizeBytes)
    {
        if (ramSizeBytes >= RomBaseAddress)
            return;

        const uint toAddress = RomBaseAddress - 1;
        bus.Attach(new OpenBusDevice(ramSizeBytes, toAddress));
    }

    private void SetBootVideoMode(Bus bus)
    {
        var mode = m_monitorType == AtariMonitorType.Monochrome
            ? HighResolutionModeValue
            : LowResolutionModeValue;
        bus.Write8(VideoModeRegister, mode);
    }

    private void ApplyMonitorAndVideoMode(Bus bus)
    {
        m_mfp.SetMonitorType(m_monitorType);
        SetBootVideoMode(bus);
    }

    private readonly record struct PendingInterrupt(byte Level, InterruptAcknowledgeResult AcknowledgeResult);

    private void EnqueuePendingAcknowledge(byte level, InterruptAcknowledgeResult acknowledgeResult)
    {
        if (level >= m_pendingAcknowledgeByLevel.Length)
            return;

        var count = m_pendingAcknowledgeCountByLevel[level];
        if (count >= MaxPendingAcknowledgePerLevel)
            return;

        var writeIndex = m_pendingAcknowledgeReadByLevel[level] + count;
        if (writeIndex >= MaxPendingAcknowledgePerLevel)
            writeIndex -= MaxPendingAcknowledgePerLevel;
        m_pendingAcknowledgeByLevel[level][writeIndex] = acknowledgeResult;
        m_pendingAcknowledgeCountByLevel[level] = count + 1;
    }

    private static InterruptAcknowledgeResult[][] CreatePendingAcknowledgeStorage() =>
    [
        new InterruptAcknowledgeResult[MaxPendingAcknowledgePerLevel],
        new InterruptAcknowledgeResult[MaxPendingAcknowledgePerLevel],
        new InterruptAcknowledgeResult[MaxPendingAcknowledgePerLevel],
        new InterruptAcknowledgeResult[MaxPendingAcknowledgePerLevel],
        new InterruptAcknowledgeResult[MaxPendingAcknowledgePerLevel],
        new InterruptAcknowledgeResult[MaxPendingAcknowledgePerLevel],
        new InterruptAcknowledgeResult[MaxPendingAcknowledgePerLevel],
        new InterruptAcknowledgeResult[MaxPendingAcknowledgePerLevel]
    ];

    private void ReleaseMouseButtonsNoLock()
    {
        if (!m_isLeftMouseButtonPressed && !m_isRightMouseButtonPressed)
            return;

        m_isLeftMouseButtonPressed = false;
        m_isRightMouseButtonPressed = false;
        m_isMouseDragPending = false;
        EnqueueMousePacketNoLock(0, 0, false, false);
    }

    private void QueueRelativeMouseDeltasNoLock(int deltaX, int deltaY, bool isLeftButtonPressed, bool isRightButtonPressed)
    {
        while (deltaX != 0 || deltaY != 0)
        {
            var stepX = Math.Clamp(deltaX, -127, 127);
            var stepY = Math.Clamp(deltaY, -127, 127);
            EnqueueMousePacketNoLock((sbyte)stepX, (sbyte)stepY, isLeftButtonPressed, isRightButtonPressed);
            deltaX -= stepX;
            deltaY -= stepY;
        }
    }

    private void EnqueueMousePacketNoLock(sbyte deltaX, sbyte deltaY, bool isLeftButtonPressed, bool isRightButtonPressed)
    {
        if (m_isMousePacketCoalescingEnabled)
        {
            if (deltaX == 0 && deltaY == 0 &&
                m_pendingMousePackets.Count > 0 &&
                m_pendingMousePackets[^1].DeltaX == 0 &&
                m_pendingMousePackets[^1].DeltaY == 0 &&
                m_pendingMousePackets[^1].IsLeftButtonPressed == isLeftButtonPressed &&
                m_pendingMousePackets[^1].IsRightButtonPressed == isRightButtonPressed)
                return;

            if (m_pendingMousePackets.Count > 0)
            {
                var tailIndex = m_pendingMousePackets.Count - 1;
                var tailPacket = m_pendingMousePackets[tailIndex];
                if (tailPacket.IsLeftButtonPressed == isLeftButtonPressed &&
                    tailPacket.IsRightButtonPressed == isRightButtonPressed)
                {
                    var combinedDeltaX = tailPacket.DeltaX + deltaX;
                    var combinedDeltaY = tailPacket.DeltaY + deltaY;
                    if (combinedDeltaX is >= sbyte.MinValue and <= sbyte.MaxValue &&
                        combinedDeltaY is >= sbyte.MinValue and <= sbyte.MaxValue)
                    {
                        m_pendingMousePackets[tailIndex] = new MousePacket((sbyte)combinedDeltaX, (sbyte)combinedDeltaY, isLeftButtonPressed, isRightButtonPressed);
                        return;
                    }
                }
            }
        }

        if (m_pendingMousePackets.Count >= MaxQueuedMousePackets)
            m_pendingMousePackets.RemoveAt(0);

        m_pendingMousePackets.Add(new MousePacket(deltaX, deltaY, isLeftButtonPressed, isRightButtonPressed));
    }

    private void FlushPendingMousePacketsOnCadence(long deltaTicks)
    {
        if (deltaTicks <= 0)
            return;

        lock (m_mouseStateSync)
        {
            m_mousePacketTickAccumulator += deltaTicks;
            var packetCount = (int)(m_mousePacketTickAccumulator / m_mousePacketTicksPerSample);
            if (packetCount <= 0)
                return;
            m_mousePacketTickAccumulator -= packetCount * m_mousePacketTicksPerSample;

            for (var i = 0; i < packetCount; i++)
            {
                if (m_pendingMousePackets.Count == 0)
                    return;
                if (m_isIkbdMouseBackPressureEnabled &&
                    ShouldApplyIkbdMouseBackPressureNoLock())
                {
                    DropStaleHostMousePacketsNoLock();
                    continue;
                }

                var packet = m_pendingMousePackets[0];
                m_pendingMousePackets.RemoveAt(0);
                m_aciaIkbd.QueueRelativeMousePacket(packet.DeltaX, packet.DeltaY, packet.IsLeftButtonPressed, packet.IsRightButtonPressed);
            }
        }
    }

    private bool ShouldApplyIkbdMouseBackPressureNoLock()
    {
        var queuedIkbdBytes = m_aciaIkbd.PendingReceiveQueueCount;
        if (queuedIkbdBytes < IkbdMouseBackPressureThresholdBytes)
            return false;

        var queuedIkbdMouseBytes = m_aciaIkbd.PendingMousePacketByteCount;
        return queuedIkbdMouseBytes == queuedIkbdBytes;
    }

    private void DropStaleHostMousePacketsNoLock()
    {
        if (m_pendingMousePackets.Count <= 1)
            return;

        var lastPacket = m_pendingMousePackets[^1];
        var droppedCount = m_pendingMousePackets.Count - 1;
        m_pendingMousePackets.Clear();
        m_pendingMousePackets.Add(lastPacket);
        m_droppedMousePacketsDueToIkbdBackPressureCount += droppedCount;
    }

    private void ReleaseJoystickNoLock()
    {
        if (m_joystickState == JoystickState.Neutral)
            return;

        m_joystickState = JoystickState.Neutral;
        m_aciaIkbd.QueueJoystickState(HostJoystickPortIndex, m_joystickState);
        if (m_isJoystickMirroredToPort0)
            m_aciaIkbd.QueueJoystickState(HostJoystickPortZeroIndex, m_joystickState);
    }

    /// <summary>
    /// Converts CPU ticks into MFP input-clock ticks.
    /// The ST MFP timers are clocked from ~2.4576 MHz, not directly from the 68000 clock.
    /// </summary>
    private int ScaleCpuTicksForMfp(long cpuTicks)
    {
        if (cpuTicks <= 0)
            return 0;

        var numerator = cpuTicks * MfpInputClockHz + m_mfpTickRemainder;
        var scaledTicks = numerator / m_cpuClockHz;
        m_mfpTickRemainder = numerator % m_cpuClockHz;
        return scaledTicks <= 0 ? 0 : (int)Math.Min(scaledTicks, int.MaxValue);
    }

    private readonly record struct HostMouseState(
        double NormalizedX,
        double NormalizedY,
        bool IsLeftButtonPressed,
        bool IsRightButtonPressed,
        bool IsPointerWithinDisplay);

    private readonly record struct MousePacket(sbyte DeltaX, sbyte DeltaY, bool IsLeftButtonPressed, bool IsRightButtonPressed);
}
