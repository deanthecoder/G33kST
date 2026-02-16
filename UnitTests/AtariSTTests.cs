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
        Assert.That(atariST.Descriptor.FrameWidth, Is.EqualTo(704));
        Assert.That(atariST.Descriptor.FrameHeight, Is.EqualTo(448));
        Assert.That(atariST.Video, Is.Not.Null);
        Assert.That(atariST.Video.FrameWidth, Is.EqualTo(704));
        Assert.That(atariST.Video.FrameHeight, Is.EqualTo(448));
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
            Assert.That(atariST.Cpu.Bus.Read8(VideoModeRegister) & 0x03, Is.EqualTo(0));
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

        Assert.That(atariST.CpuTicks, Is.EqualTo(0));
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

        Assert.That(() => atariST.LoadRom(Array.Empty<byte>(), "test.rom"), Throws.TypeOf<ArgumentException>());
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
        for (int i = 0; i < romData.Length; i++)
        {
            Assert.That(atariST.Rom.Data[i], Is.EqualTo(romData[i]), $"ROM byte at offset {i} should match");
        }
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

        Assert.That(atariST.CpuTicks, Is.EqualTo(0));
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
        var mounted = atariST.TryMountFloppyImage(0, new byte[] { 0x01, 0x02 }, "disk-a");

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
        _ = atariST.TryMountFloppyImage(0, new byte[] { 0x01, 0x02 }, "disk-a");

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
        _ = atariST.TryMountFloppyImage(0, new byte[] { 0x01, 0x02 }, "disk-a");

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

        var header = bus.Read8(keyboardDataAddress);
        var deltaX = bus.Read8(keyboardDataAddress);
        var deltaY = bus.Read8(keyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(header, Is.EqualTo(0xFA), "Expected relative mouse packet with left-button bit set.");
            Assert.That((sbyte)deltaX, Is.Not.EqualTo(0), "Expected horizontal mouse delta.");
            Assert.That((sbyte)deltaY, Is.EqualTo(0), "Expected no vertical mouse delta.");
        });
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
        DrainKeyboardReceiveQueue(bus, keyboardStatusAddress, keyboardDataAddress);
        atariST.UpdateMouseState(0.00, 0.00, false, false, false);
        atariST.UpdateMouseState(0.70, 0.45, false, false, true);

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

        var status = bus.Read8(keyboardStatusAddress);
        Assert.That(status & 0x01, Is.Not.Zero, "Expected initial entry to queue a sync delta from boot cursor position.");
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
        var firstPixelOffset = ((shifter.ActiveOriginY * atariST.Video.FrameWidth) + shifter.ActiveOriginX) * bytesPerPixel;

        Assert.Multiple(() =>
        {
            Assert.That(frameRendered, Is.True);

            // Border should use color 0 (black by default).
            Assert.That(frameBuffer[0], Is.EqualTo(0));
            Assert.That(frameBuffer[1], Is.EqualTo(0));
            Assert.That(frameBuffer[2], Is.EqualTo(0));

            // Pixel 0: red.
            Assert.That(frameBuffer[firstPixelOffset], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 1], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 2], Is.EqualTo(0));

            // Pixel 1: duplicated low-res pixel 0 (also red due 2x horizontal scaling).
            Assert.That(frameBuffer[firstPixelOffset + 3], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 4], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 5], Is.EqualTo(0));

            // Pixel 2: first black pixel after 2x horizontal scaling.
            Assert.That(frameBuffer[firstPixelOffset + 6], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 7], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 8], Is.EqualTo(0));
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
        var firstPixelOffset = ((shifter.ActiveOriginY * atariST.Video.FrameWidth) + shifter.ActiveOriginX) * bytesPerPixel;

        Assert.Multiple(() =>
        {
            // Mono border defaults to white.
            Assert.That(frameBuffer[0], Is.EqualTo(255));
            Assert.That(frameBuffer[1], Is.EqualTo(255));
            Assert.That(frameBuffer[2], Is.EqualTo(255));

            // Pixel 0 should be black (set bit).
            Assert.That(frameBuffer[firstPixelOffset], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 1], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 2], Is.EqualTo(0));

            // Pixel 1 should be white (clear bit).
            Assert.That(frameBuffer[firstPixelOffset + 3], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 4], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 5], Is.EqualTo(255));
        });
    }

    [Test]
    public void VideoShouldRenderMediumResolutionWithVerticalScaling()
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
        var firstPixelOffset = ((shifter.ActiveOriginY * atariST.Video.FrameWidth) + shifter.ActiveOriginX) * bytesPerPixel;

        Assert.Multiple(() =>
        {
            // Pixel (0,0): green.
            Assert.That(frameBuffer[firstPixelOffset], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 1], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + 2], Is.EqualTo(0));

            // Pixel (0,1): also green (2x vertical scaling in medium mode).
            var secondLineOffset = atariST.Video.FrameWidth * bytesPerPixel;
            Assert.That(frameBuffer[firstPixelOffset + secondLineOffset], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + secondLineOffset + 1], Is.EqualTo(255));
            Assert.That(frameBuffer[firstPixelOffset + secondLineOffset + 2], Is.EqualTo(0));

            // Pixel (1,0): black (next source bit is clear).
            Assert.That(frameBuffer[firstPixelOffset + 3], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 4], Is.EqualTo(0));
            Assert.That(frameBuffer[firstPixelOffset + 5], Is.EqualTo(0));
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
}
