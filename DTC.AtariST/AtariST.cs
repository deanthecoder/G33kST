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
using DTC.M68000;

namespace DTC.AtariST;

/// <summary>
/// Wires together Atari ST-specific devices into a single emulated machine.
/// </summary>
public sealed class AtariST : IMachine
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
    private const byte HighResolutionModeValue = 0x02;
    private const int MouseDragActivationThresholdPixels = 2;
    private const int MaxQueuedMousePackets = 128;
    private const uint RealTimeClockFromAddress = 0x00FFFC20;
    private const uint RealTimeClockToAddress = 0x00FFFC3F;
    private const byte SyntheticVblInterruptLevel = 4;
    private const long MfpInputClockHz = 2_457_600;
    private const double IkbdMousePacketHz = 200.0;
    private readonly Shifter m_video;
    private readonly AciaIkbdDevice m_aciaIkbd;
    private readonly ShifterRegistersDevice m_shifterRegisters;
    private readonly PsgDevice m_psg;
    private readonly FloppyDmaFdcDevice m_floppyController;
    private readonly SystemControlDevice m_systemControl;
    private readonly RtcDevice m_rtc;
    private readonly MfpDevice m_mfp;
    private readonly Lock m_mouseStateSync = new();
    private readonly List<PendingInterrupt> m_pendingInterrupts = [];
    private readonly Queue<InterruptAcknowledgeResult>[] m_pendingAcknowledgeByLevel = CreateAcknowledgeQueues();
    private byte m_latchedInterruptLevel;
    private InterruptAcknowledgeResult m_latchedInterruptAcknowledge;
    private bool m_hasLatchedInterrupt;
    private bool m_isInputActive = true;
    private readonly long m_cpuClockHz;
    private readonly double m_mousePacketTicksPerSample;
    private long m_mfpTickRemainder;
    private double m_mousePacketTickAccumulator;
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
    private readonly Queue<MousePacket> m_pendingMousePackets = [];
    private JoystickState m_joystickState;
    private AtariMonitorType m_monitorType;

    public IMachineDescriptor Descriptor { get; } = new AtariSTDescriptor();

    public string Name => Descriptor.Name;

    public long CpuTicks => Cpu.CyclesSinceCpuStart;

    public bool HasLoadedCartridge => Rom.Data.Any(b => b != 0);

    // Minimal Shifter-backed video path.
    public IVideoSource Video => m_video;

    public IAudioSource Audio => m_psg;

    public IMachineSnapshotter Snapshotter => null;

    public Cpu Cpu { get; }

    public Memory Ram { get; }

    public RomDevice Rom { get; }

    public RomMirrorDevice RomMirror { get; }
    
    /// <summary>
    /// Gets whether the current monitor mode is monochrome high-resolution.
    /// </summary>
    public bool IsHighResolutionMode => m_monitorType == AtariMonitorType.Monochrome;

    public AtariST()
        : this(AtariSTOptions.Default)
    {
    }

    public AtariST(AtariSTOptions options, Action<double, double> audioSampleSink = null)
    {
        var options1 = options ?? AtariSTOptions.Default;
        ValidateOptions(options1);
        m_monitorType = options1.MonitorType;
        m_cpuClockHz = Math.Max(1, (long)Math.Round(Descriptor.CpuHz));
        m_mousePacketTicksPerSample = Math.Max(1.0, Descriptor.CpuHz / IkbdMousePacketHz);

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
        m_floppyController = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
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
        foreach (var queue in m_pendingAcknowledgeByLevel)
            queue.Clear();
        m_pendingMousePackets.Clear();
        m_mousePacketTickAccumulator = 0;
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
        m_psg.AdvanceCycles(deltaTicks);
        m_video.Advance(deltaTicks, OnHblank, OnVblank);
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
        m_pendingAcknowledgeByLevel[m_latchedInterruptLevel].Enqueue(m_latchedInterruptAcknowledge);
        m_hasLatchedInterrupt = false;
        m_latchedInterruptLevel = 0;
        m_latchedInterruptAcknowledge = default;
    }

    public void SetInputActive(bool isActive)
    {
        lock (m_mouseStateSync)
            m_isInputActive = isActive;
        if (isActive)
            return;

        UpdateMouseState(0, 0, false, false, false);
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
    /// Updates joystick 0 state and queues IKBD joystick event packets.
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
            m_aciaIkbd.QueueJoystickState(0, state);
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

    /// <summary>
    /// Updates host mouse state and translates it into IKBD relative mouse packets.
    /// </summary>
    public void UpdateMouseState(double normalizedX, double normalizedY, bool isLeftButtonPressed, bool isRightButtonPressed, bool isPointerWithinDisplay)
    {
        lock (m_mouseStateSync)
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

            var activeWidth = Math.Max(1, m_video.ActiveWidth);
            var activeHeight = Math.Max(1, m_video.ActiveHeight);
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
    }

    private void OnHblank()
    {
        m_mfp.NotifyHblank(m_video.IsDisplayEnableActive);
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
        EnqueueInterrupt(level, InterruptAcknowledgeResult.Vector(vector));

    private void EnqueueInterrupt(byte level, InterruptAcknowledgeResult acknowledgeResult)
    {
        if (m_pendingInterrupts.Count >= int.MaxValue)
            return;
        m_pendingInterrupts.Add(new PendingInterrupt(level, acknowledgeResult));
    }

    private InterruptAcknowledgeResult ResolveInterruptAcknowledge(byte level)
    {
        if (level >= m_pendingAcknowledgeByLevel.Length)
            return InterruptAcknowledgeResult.Autovector();

        var acknowledgeQueue = m_pendingAcknowledgeByLevel[level];
        return acknowledgeQueue.Count > 0 ? acknowledgeQueue.Dequeue() : InterruptAcknowledgeResult.Autovector();
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

    private static Queue<InterruptAcknowledgeResult>[] CreateAcknowledgeQueues() =>
    [
        new Queue<InterruptAcknowledgeResult>(),
        new Queue<InterruptAcknowledgeResult>(),
        new Queue<InterruptAcknowledgeResult>(),
        new Queue<InterruptAcknowledgeResult>(),
        new Queue<InterruptAcknowledgeResult>(),
        new Queue<InterruptAcknowledgeResult>(),
        new Queue<InterruptAcknowledgeResult>(),
        new Queue<InterruptAcknowledgeResult>()
    ];

    private readonly record struct PendingInterrupt(byte Level, InterruptAcknowledgeResult AcknowledgeResult);

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
        if (m_pendingMousePackets.Count >= MaxQueuedMousePackets)
            _ = m_pendingMousePackets.Dequeue();

        m_pendingMousePackets.Enqueue(new MousePacket(deltaX, deltaY, isLeftButtonPressed, isRightButtonPressed));
    }

    private void FlushPendingMousePacketsOnCadence(long deltaTicks)
    {
        if (deltaTicks <= 0)
            return;

        m_mousePacketTickAccumulator += deltaTicks;
        var packetCount = (int)(m_mousePacketTickAccumulator / m_mousePacketTicksPerSample);
        if (packetCount <= 0)
            return;
        m_mousePacketTickAccumulator -= packetCount * m_mousePacketTicksPerSample;

        lock (m_mouseStateSync)
        {
            for (var i = 0; i < packetCount; i++)
            {
                if (m_pendingMousePackets.Count == 0)
                    return;

                var packet = m_pendingMousePackets.Dequeue();
                m_aciaIkbd.QueueRelativeMousePacket(packet.DeltaX, packet.DeltaY, packet.IsLeftButtonPressed, packet.IsRightButtonPressed);
            }
        }
    }

    private void ReleaseJoystickNoLock()
    {
        if (m_joystickState == JoystickState.Neutral)
            return;

        m_joystickState = JoystickState.Neutral;
        m_aciaIkbd.QueueJoystickState(0, m_joystickState);
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

    private readonly record struct MousePacket(sbyte DeltaX, sbyte DeltaY, bool IsLeftButtonPressed, bool IsRightButtonPressed);
}
