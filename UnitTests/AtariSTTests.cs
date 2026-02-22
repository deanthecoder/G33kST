// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.AtariST;
using DTC.Emulation;
using DTC.M68000;

namespace UnitTests;

/// <summary>
/// Tests for the Atari ST emulator machine.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class AtariSTTests : TestsBase
{
    private const uint RealTimeClockRegister = 0x00FFFC21;
    private const uint RealTimeClockAlarmMinuteLowRegister = 0x00FFFC27;
    private const uint UnmappedIoHoleAddress = 0x00FF9001;
    private const uint BlitterConfigRegister = 0x00FF8A3C;
    private const uint VideoModeRegister = 0x00FF8260;

    [Test]
    public void ConstructorShouldInitializeMachineWithDefaultDescriptor()
    {
        var atariST = new AtariST();

        Assert.That(atariST, Is.Not.Null);
        Assert.That(atariST.Name, Is.EqualTo("Atari ST 1040 STFM"));
        Assert.That(atariST.Descriptor, Is.Not.Null);
        Assert.That(atariST.Descriptor.CpuHz, Is.EqualTo(8_000_000.0));
        Assert.That(atariST.Descriptor.VideoHz, Is.EqualTo(60.0));
        Assert.That(atariST.Descriptor.FrameWidth, Is.EqualTo(384));
        Assert.That(atariST.Descriptor.FrameHeight, Is.EqualTo(248));
        Assert.That(atariST.Video, Is.Not.Null);
        Assert.That(atariST.Video.FrameWidth, Is.EqualTo(384));
        Assert.That(atariST.Video.FrameHeight, Is.EqualTo(248));
    }

    [Test]
    public void VideoFrameSizeShouldTrackVideoModeWithMargins()
    {
        var atariST = new AtariST();
        var shifter = (Shifter)atariST.Video;
        var bus = atariST.Cpu.Bus;

        bus.Write8(VideoModeRegister, 0x00);
        shifter.Reset();
        Assert.That((atariST.Video.FrameWidth, atariST.Video.FrameHeight), Is.EqualTo((384, 248)));

        bus.Write8(VideoModeRegister, 0x01);
        shifter.Reset();
        Assert.That((atariST.Video.FrameWidth, atariST.Video.FrameHeight), Is.EqualTo((704, 448)));

        bus.Write8(VideoModeRegister, 0x02);
        shifter.Reset();
        Assert.That((atariST.Video.FrameWidth, atariST.Video.FrameHeight), Is.EqualTo((704, 448)));
    }

    [Test]
    public void ConstructorShouldInitializeCpuAndMemory()
    {
        var atariST = new AtariST();

        Assert.That(atariST.Cpu, Is.Not.Null);
        Assert.That(atariST.Ram, Is.Not.Null);
        Assert.That(atariST.Rom, Is.Not.Null);
        Assert.That(atariST.Ram.Data.Length, Is.EqualTo(1024 * 1024)); // 1MB RAM
        Assert.That(atariST.Rom.Data.Length, Is.EqualTo(192 * 1024)); // 192KB ROM
    }

    [Test]
    public void ConstructorShouldExposeAudioSource()
    {
        var atariST = new AtariST();

        Assert.Multiple(() =>
        {
            Assert.That(atariST.Audio, Is.Not.Null);
            Assert.That(atariST.Audio.ChannelCount, Is.EqualTo(3));
            Assert.That(atariST.Audio.SampleRateHz, Is.EqualTo(atariST.Descriptor.AudioSampleRateHz));
        });
    }

    [Test]
    public void ConstructorShouldUseAtariStOptionsRamSize()
    {
        var options = new AtariSTOptions
        {
            RamSizeBytes = 512 * 1024
        };
        var atariST = new AtariST(options);

        Assert.That(atariST.Ram.Data.Length, Is.EqualTo(512 * 1024));
    }

    [Test]
    public void ConstructorDefaultConfigShouldNotExposeRealTimeClockWindow()
    {
        var atariST = new AtariST();

        var value = atariST.Cpu.Bus.Read8(RealTimeClockRegister);

        Assert.That(value, Is.EqualTo(0xFF));
    }

    [Test]
    public void ConstructorConfigWithRealTimeClockShouldExposeClockWindow()
    {
        var options = new AtariSTOptions
        {
            HasRealTimeClock = true
        };
        var atariST = new AtariST(options);

        var value = atariST.Cpu.Bus.Read8(RealTimeClockRegister);

        Assert.That(value, Is.Not.EqualTo(0xFF));
    }

    [Test]
    public void ConstructorDefaultConfigShouldNotLatchRealTimeClockWrites()
    {
        var atariST = new AtariST();
        atariST.Cpu.Bus.Write8(RealTimeClockAlarmMinuteLowRegister, 0x0A);

        var value = atariST.Cpu.Bus.Read8(RealTimeClockAlarmMinuteLowRegister);

        Assert.That(value, Is.EqualTo(0xFF));
    }

    [Test]
    public void ConstructorShouldTreatUnmappedIoAsOpenBus()
    {
        var atariST = new AtariST();
        var bus = atariST.Cpu.Bus;

        bus.Write8(UnmappedIoHoleAddress, 0x34);
        var value = bus.Read8(UnmappedIoHoleAddress);

        Assert.That(value, Is.EqualTo(0xFF));
    }

    [Test]
    public void ConstructorShouldRaiseBusErrorWhenReadingBlitterRegisterOnStfm()
    {
        var atariST = new AtariST();

        Assert.That(
            () => atariST.Cpu.Read8(BlitterConfigRegister),
            Throws.TypeOf<BusErrorException>());
    }

    [Test]
    public void ConstructorDefaultColorMonitorShouldBootInLowResolutionMode()
    {
        var atariST = new AtariST();
        var shifter = (Shifter)atariST.Video;
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);

        atariST.AdvanceDevices(oneFrameTicks);

        Assert.Multiple(() =>
        {
            Assert.That(atariST.Cpu.Bus.Read8(VideoModeRegister) & 0x03, Is.Zero);
            Assert.That(shifter.ActiveWidth, Is.EqualTo(320));
            Assert.That(shifter.ActiveHeight, Is.EqualTo(200));
        });
    }

    [Test]
    public void ConstructorMonochromeMonitorShouldBootInHighResolutionMode()
    {
        var options = new AtariSTOptions
        {
            MonitorType = AtariMonitorType.Monochrome
        };
        var atariST = new AtariST(options);
        var shifter = (Shifter)atariST.Video;
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);

        atariST.AdvanceDevices(oneFrameTicks);

        Assert.Multiple(() =>
        {
            Assert.That(atariST.Cpu.Bus.Read8(VideoModeRegister) & 0x03, Is.EqualTo(2));
            Assert.That(shifter.ActiveWidth, Is.EqualTo(640));
            Assert.That(shifter.ActiveHeight, Is.EqualTo(400));
        });
    }

    [Test]
    public void MemoryWritesAboveConfiguredRamShouldReadBackAsOpenBus()
    {
        var atariST = new AtariST();
        var bus = atariST.Cpu.Bus;
        var mappedAddresses = new uint[]
        {
            0x00000010,
            0x00080000,
            0x000FFFFF
        };
        var unmappedAddresses = new uint[]
        {
            0x00100000,
            0x00180000,
            0x00200000
        };

        foreach (var address in mappedAddresses)
            bus.Write8(address, 0x5A);
        foreach (var address in unmappedAddresses)
            bus.Write8(address, 0x5A);

        Assert.Multiple(() =>
        {
            foreach (var address in mappedAddresses)
                Assert.That(bus.Read8(address), Is.EqualTo(0x5A), $"RAM write should stick at ${address:X6}.");
            foreach (var address in unmappedAddresses)
                Assert.That(bus.Read8(address), Is.EqualTo(0xFF), $"Open-bus range should read as 0xFF at ${address:X6}.");
        });
    }

    [Test]
    public void ConstructorShouldInitializeWithZeroCycles()
    {
        var atariST = new AtariST();

        Assert.That(atariST.CpuTicks, Is.Zero);
    }

    [Test]
    public void HasLoadedCartridgeShouldBeFalseInitially()
    {
        var atariST = new AtariST();

        Assert.That(atariST.HasLoadedCartridge, Is.False);
    }

    [Test]
    public void LoadRomShouldThrowWhenRomDataIsNull()
    {
        var atariST = new AtariST();

        Assert.That(() => atariST.LoadRom(null, "test.rom"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void LoadRomShouldThrowWhenRomDataIsEmpty()
    {
        var atariST = new AtariST();

        Assert.That(() => atariST.LoadRom([], "test.rom"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void LoadRomShouldThrowWhenRomDataIsTooLarge()
    {
        var atariST = new AtariST();
        var tooLargeRom = new byte[200 * 1024]; // 200KB, larger than 192KB max

        Assert.That(() => atariST.LoadRom(tooLargeRom, "test.rom"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void LoadRomShouldLoadValidRomData()
    {
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();

        atariST.LoadRom(romData, "tos.img");

        Assert.That(atariST.HasLoadedCartridge, Is.True);

        // Verify ROM data was copied
        for (var i = 0; i < romData.Length; i++)
            Assert.That(atariST.Rom.Data[i], Is.EqualTo(romData[i]), $"ROM byte at offset {i} should match");
    }

    [Test]
    public void ResetShouldResetCpuState()
    {
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");

        // Step CPU to change state
        atariST.StepCpu();

        atariST.Reset();

        Assert.That(atariST.CpuTicks, Is.Zero);
        Assert.That(atariST.Cpu.Registers.ProgramCounter, Is.Not.EqualTo(0xFC0000)); // Should have loaded from reset vector
    }

    [Test]
    public void RomDeviceShouldBeReadOnly()
    {
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");

        var originalValue = atariST.Rom.Data[0];
        atariST.Rom.Write8(0xFC0000, 0xFF);

        Assert.That(atariST.Rom.Data[0], Is.EqualTo(originalValue));
    }

    [Test]
    public void RomDeviceShouldBeAccessibleAtCorrectAddress()
    {
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");

        var value = atariST.Rom.Read8(0xFC0000);

        Assert.That(value, Is.EqualTo(romData[0]));
    }

    [Test]
    public void StepCpuShouldExecuteInstruction()
    {
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");

        var initialTicks = atariST.CpuTicks;

        atariST.StepCpu();

        Assert.That(atariST.CpuTicks, Is.GreaterThan(initialTicks), "CPU should have advanced cycles");
    }

    [Test]
    public void KeyboardAciaStatusShouldReportTransmitReady()
    {
        var atariST = new AtariST();

        var status = atariST.Cpu.Bus.Read8(0x00FFFC00);

        Assert.That(status & 0x02, Is.Not.Zero);
    }

    [Test]
    public void KeyboardAciaResetCommandShouldQueueAcknowledgeByte()
    {
        var atariST = new AtariST();

        atariST.Cpu.Bus.Write8(0x00FFFC02, 0x80);
        atariST.Cpu.Bus.Write8(0x00FFFC02, 0x01);
        var status = atariST.Cpu.Bus.Read8(0x00FFFC00);
        var data = atariST.Cpu.Bus.Read8(0x00FFFC02);

        Assert.Multiple(() =>
        {
            Assert.That(status & 0x01, Is.Not.Zero, "Receive-ready should be set after IKBD reset command.");
            Assert.That(data, Is.EqualTo(0xF1), "IKBD reset complete code should be returned.");
        });
    }

    [Test]
    public void TryMountFloppyImageShouldMountInDriveA()
    {
        var atariST = new AtariST();
        var mounted = atariST.TryMountFloppyImage(0, [0x01, 0x02], "disk-a");

        Assert.Multiple(() =>
        {
            Assert.That(mounted, Is.True);
            Assert.That(atariST.IsFloppyImageMounted(0), Is.True);
            Assert.That(atariST.GetMountedFloppyImageName(0), Is.EqualTo("disk-a"));
        });
    }

    [Test]
    public void UnmountFloppyImageShouldClearDriveAImage()
    {
        var atariST = new AtariST();
        _ = atariST.TryMountFloppyImage(0, [0x01, 0x02], "disk-a");

        atariST.UnmountFloppyImage(0);

        Assert.Multiple(() =>
        {
            Assert.That(atariST.IsFloppyImageMounted(0), Is.False);
            Assert.That(atariST.GetMountedFloppyImageName(0), Is.Null);
        });
    }

    [Test]
    public void ResetShouldKeepMountedDriveAFloppyImage()
    {
        var atariST = new AtariST();
        _ = atariST.TryMountFloppyImage(0, [0x01, 0x02], "disk-a");

        atariST.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(atariST.IsFloppyImageMounted(0), Is.True);
            Assert.That(atariST.GetMountedFloppyImageName(0), Is.EqualTo("disk-a"));
        });
    }

    [Test]
    public void InjectKeyboardScanCodeShouldQueueLevel6Interrupt()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint interruptEnableBRegister = 0x00FFFA09;
        const uint interruptMaskBRegister = 0x00FFFA15;
        const uint keyboardAciaControlRegister = 0x00FFFC00;
        bus.Write8(interruptEnableBRegister, 0x40);
        bus.Write8(interruptMaskBRegister, 0x40);
        bus.Write8(keyboardAciaControlRegister, 0x80);

        atariST.InjectKeyboardScanCode(0x1C);

        var hasInterrupt = atariST.TryConsumeInterrupt();

        Assert.That(hasInterrupt, Is.True);
    }

    [Test]
    public void InjectKeyboardScanCodeShouldNotQueueDuplicateInterruptsBeforeConsumption()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint interruptEnableBRegister = 0x00FFFA09;
        const uint interruptMaskBRegister = 0x00FFFA15;
        const uint keyboardAciaControlRegister = 0x00FFFC00;
        bus.Write8(interruptEnableBRegister, 0x40);
        bus.Write8(interruptMaskBRegister, 0x40);
        bus.Write8(keyboardAciaControlRegister, 0x80);

        atariST.InjectKeyboardScanCode(0x1C);
        atariST.InjectKeyboardScanCode(0x1D);

        var interruptCount = 0;
        for (var i = 0; i < 4; i++)
        {
            if (!atariST.TryConsumeInterrupt())
                break;
            interruptCount++;
            atariST.RequestInterrupt();
        }

        Assert.That(interruptCount, Is.EqualTo(1));
    }

    [Test]
    public void InterruptAcknowledgeShouldPreserveMultiplePendingLevel6VectorsInOrder()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint interruptEnableBRegister = 0x00FFFA09;
        const uint interruptMaskBRegister = 0x00FFFA15;
        const uint vectorRegister = 0x00FFFA17;
        const uint timerCdControlRegister = 0x00FFFA1D;
        const uint timerDDataRegister = 0x00FFFA25;
        const uint keyboardAciaControlRegister = 0x00FFFC00;

        bus.Write8(vectorRegister, 0x50);
        bus.Write8(interruptEnableBRegister, 0x50); // GPIP4 + Timer D.
        bus.Write8(interruptMaskBRegister, 0x50);
        bus.Write8(keyboardAciaControlRegister, 0x80);
        bus.Write8(timerDDataRegister, 0x01);
        bus.Write8(timerCdControlRegister, 0x01);

        atariST.InjectKeyboardScanCode(0x1C); // GPIP4 -> vector 0x56.
        var cpuTicksForOneTimerDStep = (long)Math.Ceiling(4.0 * atariST.Descriptor.CpuHz / 2_457_600.0);
        atariST.AdvanceDevices(cpuTicksForOneTimerDStep); // Timer D -> vector 0x54.

        Assert.That(atariST.TryConsumeInterrupt(), Is.True);
        atariST.RequestInterrupt();
        Assert.That(atariST.TryConsumeInterrupt(), Is.True);
        atariST.RequestInterrupt();

        var firstAcknowledge = atariST.Cpu.InterruptAcknowledge?.Invoke(6) ?? InterruptAcknowledgeResult.Autovector();
        var secondAcknowledge = atariST.Cpu.InterruptAcknowledge?.Invoke(6) ?? InterruptAcknowledgeResult.Autovector();

        Assert.Multiple(() =>
        {
            Assert.That(firstAcknowledge.Type, Is.EqualTo(InterruptAcknowledgeType.VectorNumber));
            Assert.That(firstAcknowledge.VectorNumber, Is.EqualTo(0x56));
            Assert.That(secondAcknowledge.Type, Is.EqualTo(InterruptAcknowledgeType.VectorNumber));
            Assert.That(secondAcknowledge.VectorNumber, Is.EqualTo(0x54));
        });
    }

    [Test]
    public void InjectKeyboardKeyStateShouldQueueMakeAndBreakCodes()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.InjectKeyboardKeyState(0x1E, isPressed: true);
        atariST.InjectKeyboardKeyState(0x1E, isPressed: false);
        var make = bus.Read8(keyboardDataAddress);
        var breakCode = bus.Read8(keyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(make, Is.EqualTo(0x1E));
            Assert.That(breakCode, Is.EqualTo(0x9E));
        });
    }

    [Test]
    public void UpdateMouseStateShouldQueueRelativeIkbdMousePacket()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateMouseState(0.50, 0.50, isLeftButtonPressed: false, isRightButtonPressed: false, isPointerWithinDisplay: true);
        atariST.UpdateMouseState(0.60, 0.50, isLeftButtonPressed: true, isRightButtonPressed: false, isPointerWithinDisplay: true);
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));

        var header = bus.Read8(keyboardDataAddress);
        var deltaX = bus.Read8(keyboardDataAddress);
        var deltaY = bus.Read8(keyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFA), "Expected relative mouse packet with left-button bit set.");
            Assert.That((sbyte)deltaX, Is.Not.Zero, "Expected horizontal mouse delta.");
            Assert.That((sbyte)deltaY, Is.Zero, "Expected no vertical mouse delta.");
        });
    }

    [Test]
    public void UpdateMouseStateShouldUseLogicalMediumResolutionHeightForRelativeDeltas()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        const uint videoModeRegister = 0x00FF8260;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        bus.Write8(videoModeRegister, 0x01);

        // Start at low-res center equivalent in medium-res coordinates to avoid an entry sync packet.
        atariST.UpdateMouseState(0.25, 0.50, false, false, true);
        atariST.UpdateMouseState(0.25, 0.60, false, false, true);
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));

        var header = bus.Read8(keyboardDataAddress);
        var deltaX = bus.Read8(keyboardDataAddress);
        var deltaY = bus.Read8(keyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xF8), "Expected relative mouse packet with no button bits set.");
            Assert.That((sbyte)deltaX, Is.Zero, "Expected no horizontal movement.");
            Assert.That((sbyte)deltaY, Is.EqualTo(19), "Expected medium-res Y delta in 200-line logical coordinates.");
        });
    }

    [Test]
    public void UpdateJoystickStateShouldQueueIkbdJoystickPacket()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateJoystickState(new JoystickState(
            IsUpPressed: true,
            IsDownPressed: false,
            IsLeftPressed: false,
            IsRightPressed: true,
            IsFirePressed: true));
        var header = bus.Read8(keyboardDataAddress);
        var state = bus.Read8(keyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFF));
            Assert.That(state, Is.EqualTo(0x89));
        });
    }

    [Test]
    public void UpdateJoystickStateShouldNotQueueDuplicatePacketsForUnchangedState()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateJoystickState(new JoystickState(
            IsUpPressed: false,
            IsDownPressed: true,
            IsLeftPressed: true,
            IsRightPressed: false,
            IsFirePressed: false));
        atariST.UpdateJoystickState(new JoystickState(
            IsUpPressed: false,
            IsDownPressed: true,
            IsLeftPressed: true,
            IsRightPressed: false,
            IsFirePressed: false));
        _ = bus.Read8(keyboardDataAddress);
        _ = bus.Read8(keyboardDataAddress);
        var statusAfterFirstPacket = bus.Read8(keyboardStatusAddress);

        Assert.That(statusAfterFirstPacket & 0x01, Is.Zero, "Unchanged joystick state should not queue another packet.");
    }

    [Test]
    public void UpdateMouseStateShouldQueueDeltaOnFirstReEntryMove()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateMouseState(0.45, 0.45, false, false, true);
        atariST.UpdateMouseState(0.50, 0.45, false, false, true);
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST) * 2);
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);
        atariST.UpdateMouseState(0.00, 0.00, false, false, false);
        atariST.UpdateMouseState(0.70, 0.45, false, false, true);
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));

        var status = bus.Read8(keyboardStatusAddress);
        Assert.That(status & 0x01, Is.Not.Zero, "Expected a relative mouse packet on first move after pointer re-entry.");
    }

    [Test]
    public void UpdateMouseStateShouldResyncToEntryPositionFromBoot()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateMouseState(0.90, 0.90, false, false, true);
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));

        var status = bus.Read8(keyboardStatusAddress);
        Assert.That(status & 0x01, Is.Not.Zero, "Expected initial entry to queue a sync delta from boot cursor position.");
    }

    [Test]
    public void UpdateMouseStateShouldQueuePacketsOnFixedCadence()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateMouseState(0.50, 0.50, false, false, true);
        atariST.UpdateMouseState(0.70, 0.50, false, false, true);
        var statusBeforeHblank = bus.Read8(keyboardStatusAddress);
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));
        var statusAfterCadenceTick = bus.Read8(keyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(statusBeforeHblank & 0x01, Is.Zero, "Packets should not be queued immediately from host mouse events.");
            Assert.That(statusAfterCadenceTick & 0x01, Is.Not.Zero, "Expected one queued packet to flush on fixed mouse cadence.");
        });
    }

    [Test]
    public void UpdateMouseStateShouldCoalescePendingDeltasBeforeCadence()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateMouseState(0.50, 0.50, false, false, true);
        atariST.UpdateMouseState(0.60, 0.50, false, false, true);
        atariST.UpdateMouseState(0.70, 0.50, false, false, true);

        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));
        var header = bus.Read8(keyboardDataAddress);
        var deltaX = bus.Read8(keyboardDataAddress);
        _ = bus.Read8(keyboardDataAddress);
        var statusAfterFirstCadence = bus.Read8(keyboardStatusAddress);

        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));
        var statusAfterSecondCadence = bus.Read8(keyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xF8), "Expected relative mouse packet header with no button bits set.");
            Assert.That((sbyte)deltaX, Is.Not.Zero, "Expected coalesced horizontal movement.");
            Assert.That(statusAfterFirstCadence & 0x01, Is.Zero, "Expected receive queue to be empty after reading first cadence packet.");
            Assert.That(statusAfterSecondCadence & 0x01, Is.Zero, "Expected no stale packet on the next cadence tick.");
        });
    }

    [Test]
    public void UpdateMouseStateShouldAllowBacklogWhenMousePacketCoalescingIsDisabled()
    {
        var atariST = new AtariST();
        atariST.Reset();
        atariST.SetMousePacketCoalescingEnabled(false);

        atariST.UpdateMouseState(0.50, 0.50, false, false, true);
        atariST.UpdateMouseState(0.60, 0.50, false, false, true);
        atariST.UpdateMouseState(0.70, 0.50, false, false, true);

        Assert.That(atariST.PendingMousePacketCount, Is.EqualTo(2), "Expected two pending packets without coalescing.");
    }

    [Test]
    public void MousePacketRateLimitShouldRequireTwoCadenceTicksBeforeFirstFlush()
    {
        var atariST = new AtariST();
        atariST.Reset();
        atariST.SetMousePacketCoalescingEnabled(false);
        atariST.SetMousePacketRateLimitEnabled(true);
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateMouseState(0.50, 0.50, false, false, true);
        atariST.UpdateMouseState(0.60, 0.50, false, false, true);
        atariST.UpdateMouseState(0.70, 0.50, false, false, true);

        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));
        var statusAfterOneTick = bus.Read8(keyboardStatusAddress);
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));
        var statusAfterTwoTicks = bus.Read8(keyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(statusAfterOneTick & 0x01, Is.Zero, "Expected no flush after one 200Hz cadence tick when rate-limited to 100Hz.");
            Assert.That(statusAfterTwoTicks & 0x01, Is.Not.Zero, "Expected one flush after two 200Hz cadence ticks when rate-limited.");
        });
    }

    [Test]
    public void MouseInputSamplingShouldDeferHostMouseProcessingUntilSamplingCadence()
    {
        var atariST = new AtariST();
        atariST.Reset();
        atariST.SetMousePacketCoalescingEnabled(false);
        atariST.SetMouseInputSamplingEnabled(true);
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateMouseState(0.50, 0.50, false, false, true);
        atariST.UpdateMouseState(0.70, 0.50, false, false, true);
        Assert.That(atariST.PendingMousePacketCount, Is.Zero, "Expected no pending host packets before the first sample tick.");

        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));
        var statusAfterOneTick = bus.Read8(keyboardStatusAddress);
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));
        var statusAfterTwoTicks = bus.Read8(keyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(statusAfterOneTick & 0x01, Is.Zero, "Expected no IKBD packet before 100Hz sample cadence elapsed.");
            Assert.That(statusAfterTwoTicks & 0x01, Is.Not.Zero, "Expected IKBD packet once sample cadence elapsed.");
        });
    }

    [Test]
    public void IkbdMouseBackPressureShouldDropStaleHostPacketsWhenIkbdMouseBytesBackUp()
    {
        var atariST = new AtariST();
        atariST.Reset();
        atariST.SetMousePacketCoalescingEnabled(false);
        atariST.SetIkbdMouseBackPressureEnabled(true);
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.UpdateMouseState(0.10, 0.50, false, false, true);
        for (var i = 1; i <= 80; i++)
            atariST.UpdateMouseState(0.10 + (i * 0.01), 0.50, false, false, true);

        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST) * 80);

        Assert.Multiple(() =>
        {
            Assert.That(atariST.DroppedMousePacketsDueToIkbdBackPressureCount, Is.GreaterThan(0), "Expected stale host packets to be dropped under IKBD back-pressure.");
            Assert.That(atariST.PendingKeyboardInputByteCount, Is.LessThanOrEqualTo(30), "Expected IKBD mouse queue depth to be bounded by back-pressure.");
            Assert.That(atariST.PendingIkbdMousePacketByteCount, Is.EqualTo(atariST.PendingKeyboardInputByteCount), "Expected queued IKBD bytes to be entirely mouse bytes in this scenario.");
        });
    }

    [Test]
    public void ClearInputQueuesShouldDropPendingKeyboardAndMouseInput()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var bus = atariST.Cpu.Bus;
        const uint keyboardStatusAddress = 0x00FFFC00;
        const uint keyboardDataAddress = 0x00FFFC02;
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);

        atariST.InjectKeyboardKeyState(0x1E, isPressed: true);
        atariST.UpdateMouseState(0.50, 0.50, false, false, true);
        atariST.UpdateMouseState(0.60, 0.50, false, false, true);

        Assert.That(atariST.PendingKeyboardInputByteCount, Is.GreaterThan(0));
        Assert.That(atariST.PendingMousePacketCount, Is.GreaterThan(0));

        atariST.ClearInputQueues();
        atariST.AdvanceDevices(CpuTicksForOneMousePacket(atariST));

        var status = bus.Read8(keyboardStatusAddress);
        Assert.Multiple(() =>
        {
            Assert.That(atariST.PendingKeyboardInputByteCount, Is.Zero);
            Assert.That(atariST.PendingMousePacketCount, Is.Zero);
            Assert.That(status & 0x01, Is.Zero);
        });
    }

    [Test]
    public void MfpTimerInterruptShouldQueueLevel6Interrupt()
    {
        var atariST = new AtariST();
        atariST.Reset();
        const uint interruptEnableBRegister = 0x00FFFA09;
        const uint interruptMaskBRegister = 0x00FFFA15;
        const uint vectorRegister = 0x00FFFA17;
        const uint timerCdControlRegister = 0x00FFFA1D;
        const uint timerDDataRegister = 0x00FFFA25;
        var bus = atariST.Cpu.Bus;

        bus.Write8(vectorRegister, 0x50);
        bus.Write8(interruptEnableBRegister, 0x10);
        bus.Write8(interruptMaskBRegister, 0x10);
        bus.Write8(timerDDataRegister, 0x01);
        bus.Write8(timerCdControlRegister, 0x01);
        var cpuTicksForOneTimerDStep = (long)Math.Ceiling(4.0 * atariST.Descriptor.CpuHz / 2_457_600.0);
        atariST.AdvanceDevices(cpuTicksForOneTimerDStep);

        Assert.That(atariST.TryConsumeInterrupt(), Is.True);
    }

    [Test]
    public void RomMirrorShouldBeAttachedToBus()
    {
        var atariST = new AtariST();

        Assert.That(atariST.RomMirror, Is.Not.Null);
    }

    [Test]
    public void WritingMemoryConfigRegisterShouldNotChangeResetVectorMirror()
    {
        var atariST = new AtariST();
        var romData = CreateMinimalTosRom();
        atariST.LoadRom(romData, "tos.img");
        var before = atariST.Cpu.Bus.Read8(0x000000);
        atariST.Cpu.Bus.Write8(0x00FF8001, 0x01);
        atariST.Ram.Write8(0x000000, 0xFF);
        var after = atariST.Cpu.Bus.Read8(0x000000);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(romData[0]));
            Assert.That(after, Is.EqualTo(romData[0]));
        });
    }

    [Test]
    public void AdvanceDevicesShouldScheduleVblankInterrupt()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);

        atariST.AdvanceDevices(oneFrameTicks);

        Assert.That(atariST.TryConsumeInterrupt(), Is.True);
    }

    [Test]
    public void VideoShouldRenderLowResolutionBitplanesToFrameBuffer()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var shifter = (Shifter)atariST.Video;
        var bus = atariST.Cpu.Bus;
        const uint screenBaseAddress = 0x00000100;
        const uint videoBaseHighRegister = 0x00FF8201;
        const uint videoBaseMidRegister = 0x00FF8203;
        const uint videoModeRegister = 0x00FF8260;
        const uint paletteBaseRegister = 0x00FF8240;
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);
        var frameRendered = false;
        var bytesPerPixel = atariST.Video.FrameBytesPerPixel;
        atariST.Video.FrameRendered += (_, _) => frameRendered = true;
        bus.Write8(0x00FF8001, 0x01);

        // Low-res mode.
        bus.Write8(videoModeRegister, 0x00);

        // Screen base = $000100.
        bus.Write8(videoBaseHighRegister, 0x00);
        bus.Write8(videoBaseMidRegister, 0x01);

        // Palette index 1 -> full red (3-bit channel max).
        bus.Write16BigEndian(paletteBaseRegister + 2, 0x0700);

        // First 16-pixel chunk: pixel 0 has index 1, the rest use index 0.
        bus.Write16BigEndian(screenBaseAddress, 0x8000); // Plane 0
        bus.Write16BigEndian(screenBaseAddress + 2, 0x0000); // Plane 1
        bus.Write16BigEndian(screenBaseAddress + 4, 0x0000); // Plane 2
        bus.Write16BigEndian(screenBaseAddress + 6, 0x0000); // Plane 3

        atariST.AdvanceDevices(oneFrameTicks);
        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * bytesPerPixel];
        atariST.Video.CopyToFrameBuffer(frameBuffer);
        var firstPixelOffset = (shifter.ActiveOriginY * atariST.Video.FrameWidth + shifter.ActiveOriginX) * bytesPerPixel;

        Assert.Multiple(() =>
        {
            Assert.That(frameRendered, Is.True);

            // Border should use color 0 (black by default).
            Assert.That(frameBuffer[0], Is.Zero);
            Assert.That(frameBuffer[1], Is.Zero);
            Assert.That(frameBuffer[2], Is.Zero);

            // Pixel 0: red.
            Assert.That(frameBuffer[firstPixelOffset], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 1], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + 2], Is.Zero);

            // Pixel 1: black (next source bit is clear).
            Assert.That(frameBuffer[firstPixelOffset + 3], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + 4], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + 5], Is.Zero);
        });
    }

    [Test]
    public void VideoShouldRenderHighResolutionMonochromeWithZeroAsWhite()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var shifter = (Shifter)atariST.Video;
        var bus = atariST.Cpu.Bus;
        const uint screenBaseAddress = 0x00000100;
        const uint videoBaseHighRegister = 0x00FF8201;
        const uint videoBaseMidRegister = 0x00FF8203;
        const uint videoModeRegister = 0x00FF8260;
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);
        var bytesPerPixel = atariST.Video.FrameBytesPerPixel;
        bus.Write8(0x00FF8001, 0x01);

        // High-res mono mode.
        bus.Write8(videoModeRegister, 0x02);

        // Screen base = $000100.
        bus.Write8(videoBaseHighRegister, 0x00);
        bus.Write8(videoBaseMidRegister, 0x01);

        // Bit 15 set (pixel 0 "ink"), remaining bits clear (background).
        bus.Write16BigEndian(screenBaseAddress, 0x8000);

        atariST.AdvanceDevices(oneFrameTicks);
        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * bytesPerPixel];
        atariST.Video.CopyToFrameBuffer(frameBuffer);
        var firstPixelOffset = (shifter.ActiveOriginY * atariST.Video.FrameWidth + shifter.ActiveOriginX) * bytesPerPixel;

        Assert.Multiple(() =>
        {
            // Mono border defaults to white.
            Assert.That(frameBuffer[0], Is.EqualTo(255));
            Assert.That(frameBuffer[1], Is.EqualTo(255));
            Assert.That(frameBuffer[2], Is.EqualTo(255));

            // Pixel 0 should be black (set bit).
            Assert.That(frameBuffer[firstPixelOffset], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + 1], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + 2], Is.Zero);

            // Pixel 1 should be white (clear bit).
            Assert.That(frameBuffer[firstPixelOffset + 3], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 4], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 5], Is.EqualTo(255));
        });
    }

    [Test]
    public void VideoShouldRenderMediumResolutionWithVerticalLineDoubling()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var shifter = (Shifter)atariST.Video;
        var bus = atariST.Cpu.Bus;
        const uint screenBaseAddress = 0x00000100;
        const uint videoBaseHighRegister = 0x00FF8201;
        const uint videoBaseMidRegister = 0x00FF8203;
        const uint videoModeRegister = 0x00FF8260;
        const uint paletteBaseRegister = 0x00FF8240;
        var oneFrameTicks = (long)Math.Ceiling(atariST.Descriptor.CpuHz / atariST.Descriptor.VideoHz);
        var bytesPerPixel = atariST.Video.FrameBytesPerPixel;
        bus.Write8(0x00FF8001, 0x01);

        // Medium-res mode.
        bus.Write8(videoModeRegister, 0x01);

        // Screen base = $000100.
        bus.Write8(videoBaseHighRegister, 0x00);
        bus.Write8(videoBaseMidRegister, 0x01);

        // Palette index 2 -> full green.
        bus.Write16BigEndian(paletteBaseRegister + 4, 0x0070);

        // First 16-pixel chunk: pixel 0 has index 2, others index 0.
        bus.Write16BigEndian(screenBaseAddress, 0x0000); // Plane 0
        bus.Write16BigEndian(screenBaseAddress + 2, 0x8000); // Plane 1

        atariST.AdvanceDevices(oneFrameTicks);
        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * bytesPerPixel];
        atariST.Video.CopyToFrameBuffer(frameBuffer);
        var firstPixelOffset = (shifter.ActiveOriginY * atariST.Video.FrameWidth + shifter.ActiveOriginX) * bytesPerPixel;

        Assert.Multiple(() =>
        {
            // Pixel (0,0): green.
            Assert.That(frameBuffer[firstPixelOffset], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + 1], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 2], Is.Zero);

            // Pixel (0,1): green (same source line is duplicated vertically).
            var secondLineOffset = atariST.Video.FrameWidth * bytesPerPixel;
            Assert.That(frameBuffer[firstPixelOffset + secondLineOffset], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + secondLineOffset + 1], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + secondLineOffset + 2], Is.Zero);

            // Pixel (1,0): black (next source bit is clear).
            Assert.That(frameBuffer[firstPixelOffset + 3], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + 4], Is.Zero);
            Assert.That(frameBuffer[firstPixelOffset + 5], Is.Zero);
        });
    }

    [Test]
    public void VideoShouldApplyPaletteChangesPerScanlineInLowResolutionMode()
    {
        var atariST = new AtariST();
        atariST.Reset();
        var shifter = (Shifter)atariST.Video;
        var bus = atariST.Cpu.Bus;
        const uint screenBaseAddress = 0x00000100;
        const uint videoBaseHighRegister = 0x00FF8201;
        const uint videoBaseMidRegister = 0x00FF8203;
        const uint videoModeRegister = 0x00FF8260;
        const uint paletteBaseRegister = 0x00FF8240;
        const int lowResBytesPerLine = 160;
        var ticksPerLine = (long)Math.Ceiling(atariST.Descriptor.CpuHz / (atariST.Descriptor.VideoHz * 262.0));
        var bytesPerPixel = atariST.Video.FrameBytesPerPixel;
        bus.Write8(0x00FF8001, 0x01);

        bus.Write8(videoModeRegister, 0x00);
        bus.Write8(videoBaseHighRegister, 0x00);
        bus.Write8(videoBaseMidRegister, 0x01);

        bus.Write16BigEndian(paletteBaseRegister + 2, 0x0700); // Palette index 1 = red.
        bus.Write16BigEndian(screenBaseAddress, 0x8000);
        bus.Write16BigEndian(screenBaseAddress + 2, 0x0000);
        bus.Write16BigEndian(screenBaseAddress + 4, 0x0000);
        bus.Write16BigEndian(screenBaseAddress + 6, 0x0000);

        const uint line1Address = screenBaseAddress + lowResBytesPerLine;
        bus.Write16BigEndian(line1Address, 0x8000);
        bus.Write16BigEndian(line1Address + 2, 0x0000);
        bus.Write16BigEndian(line1Address + 4, 0x0000);
        bus.Write16BigEndian(line1Address + 6, 0x0000);

        // Re-evaluate line 0 with the configured registers and palette.
        shifter.Reset();

        var hasUpdatedPaletteForNextLine = false;
        shifter.Advance(
            ticksPerLine,
            () =>
            {
                if (hasUpdatedPaletteForNextLine)
                    return;

                hasUpdatedPaletteForNextLine = true;
                bus.Write16BigEndian(paletteBaseRegister + 2, 0x0070); // Palette index 1 = green.
            },
            null);

        var frameBuffer = new byte[atariST.Video.FrameWidth * atariST.Video.FrameHeight * bytesPerPixel];
        atariST.Video.CopyToFrameBuffer(frameBuffer);
        var line0PixelOffset = (shifter.ActiveOriginY * atariST.Video.FrameWidth + shifter.ActiveOriginX) * bytesPerPixel;
        var line1OutputY = shifter.ActiveOriginY + 1;
        var line1PixelOffset = (line1OutputY * atariST.Video.FrameWidth + shifter.ActiveOriginX) * bytesPerPixel;

        Assert.Multiple(() =>
        {
            Assert.That(frameBuffer[line0PixelOffset], Is.EqualTo(255));
            Assert.That(frameBuffer[line0PixelOffset + 1], Is.Zero);
            Assert.That(frameBuffer[line0PixelOffset + 2], Is.Zero);

            Assert.That(frameBuffer[line1PixelOffset], Is.Zero);
            Assert.That(frameBuffer[line1PixelOffset + 1], Is.EqualTo(255));
            Assert.That(frameBuffer[line1PixelOffset + 2], Is.Zero);
        });
    }

    /// <summary>
    /// Creates a minimal TOS ROM image with valid reset vectors.
    /// The 68000 expects the initial SSP at $000000 and initial PC at $000004.
    /// For the Atari ST, ROM appears at $FC0000, so we create vectors there.
    /// </summary>
    private static byte[] CreateMinimalTosRom()
    {
        var rom = new byte[192 * 1024]; // 192KB

        // Set initial SSP (supervisor stack pointer) at $FC0000: $00001000
        rom[0] = 0x00;
        rom[1] = 0x00;
        rom[2] = 0x10;
        rom[3] = 0x00;

        // Set initial PC (program counter) at $FC0004: $00FC0100 (start of code in ROM)
        rom[4] = 0x00;
        rom[5] = 0xFC;
        rom[6] = 0x01;
        rom[7] = 0x00;

        // At $FC0100 (offset $100 in ROM), place a simple infinite loop: 0x4E71 (NOP)
        rom[0x100] = 0x4E;
        rom[0x101] = 0x71;

        return rom;
    }

    private static void DrainKeyboardReceiveQueue(Bus bus, uint statusAddress, uint dataAddress)
    {
        while ((bus.Read8(statusAddress) & 0x01) != 0)
            _ = bus.Read8(dataAddress);
    }

    private static long CpuTicksForOneMousePacket(AtariST atariST) =>
        (long)Math.Ceiling(atariST.Descriptor.CpuHz / 200.0);
}
