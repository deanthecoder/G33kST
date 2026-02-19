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

namespace UnitTests;

[TestFixture]
public sealed class FloppyDmaFdcDeviceTests
{
    private const uint DataRegisterAddress = 0x00FF8604;
    private const uint ControlRegisterAddress = 0x00FF8606;
    private const uint DmaAddressHighRegisterAddress = 0x00FF8609;
    private const uint DmaAddressMidRegisterAddress = 0x00FF860B;
    private const uint DmaAddressLowRegisterAddress = 0x00FF860D;
    private const ushort FdcStatusAccessControl = 0x0080;
    private const ushort FdcTrackAccessControl = 0x0082;
    private const ushort FdcSectorAccessControl = 0x0084;
    private const ushort FdcDataAccessControl = 0x0086;
    private const ushort DmaSectorCountAccessControl = 0x0090;
    private const byte RestoreCommand = 0x00;
    private const byte ReadSectorCommand = 0x80;
    private const byte WriteSectorCommand = 0xA0;
    private const byte ReadAddressCommand = 0xC0;
    private const byte TrackZeroMask = 0x04;
    private const byte RecordNotFoundMask = 0x10;
    private const byte WriteProtectMask = 0x40;

    [Test]
    public void RestoreCommandShouldReportTrackZeroWhenDriveAIsSelected()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        device.ApplyPortA(0x05);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        var status = ReadWord(device, DataRegisterAddress);

        Assert.That(status & TrackZeroMask, Is.Not.Zero);
    }

    [Test]
    public void RestoreCommandShouldNotReportTrackZeroWhenDriveBIsMissing()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        device.ApplyPortA(0x03);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        var status = ReadWord(device, DataRegisterAddress);

        Assert.That(status & TrackZeroMask, Is.Zero);
    }

    [Test]
    public void CommandAndStatusReadShouldToggleInterruptLine()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        device.ApplyPortA(0x05);
        var transitions = new List<bool>();
        device.InterruptLineChanged += isActiveLow => transitions.Add(isActiveLow);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        _ = ReadWord(device, DataRegisterAddress);

        Assert.That(transitions, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public void StartingNewCommandShouldClearThenReassertInterruptLine()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        device.ApplyPortA(0x05);
        var transitions = new List<bool>();
        device.InterruptLineChanged += isActiveLow => transitions.Add(isActiveLow);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);

        Assert.That(transitions, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void TimedModeShouldDelayInterruptAssertionUntilCyclesAdvance()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false, cpuHz: 8_000_000, transferSpeedMultiplier: 1.5);
        device.ApplyPortA(0x05);
        var transitions = new List<bool>();
        device.InterruptLineChanged += isActiveLow => transitions.Add(isActiveLow);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);

        device.Advance(5_000);
        Assert.That(transitions, Is.Empty, "Completion IRQ should not assert before command latency elapses.");

        device.Advance(50_000);
        Assert.That(transitions, Is.EqualTo(new[] { true }));

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        _ = ReadWord(device, DataRegisterAddress);
        Assert.That(transitions, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public void StatusReadAfterCommandShouldPreserveTrackZeroBit()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        device.ApplyPortA(0x05);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        var status = ReadWord(device, DataRegisterAddress);

        Assert.That(status & TrackZeroMask, Is.Not.Zero);
    }

    [Test]
    public void TypeIStatusShouldReportWriteProtectWhenNoDiskIsMounted()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        device.ApplyPortA(0x05);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);

        var status = ReadWord(device, DataRegisterAddress);

        Assert.That(status & WriteProtectMask, Is.Not.Zero);
    }

    [Test]
    public void TypeIStatusShouldReportWriteProtectForMountedReadOnlyDisk()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        Assert.That(device.TryMountImage(0, new byte[80 * 2 * 9 * 512], "disk"), Is.True);
        device.ApplyPortA(0x05);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);

        var status = ReadWord(device, DataRegisterAddress);

        Assert.That(status & WriteProtectMask, Is.Not.Zero);
    }

    [Test]
    public void MountImageShouldReplaceExistingImageInTheSameDrive()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        var firstData = new byte[] { 0x01, 0x02 };
        var secondData = new byte[] { 0x03, 0x04 };

        var firstMounted = device.TryMountImage(0, firstData, "first");
        var secondMounted = device.TryMountImage(0, secondData, "second");

        Assert.Multiple(() =>
        {
            Assert.That(firstMounted, Is.True);
            Assert.That(secondMounted, Is.True);
            Assert.That(device.IsImageMounted(0), Is.True);
            Assert.That(device.GetMountedImageName(0), Is.EqualTo("second"));
        });
    }

    [Test]
    public void MountImageShouldFailOnMissingDrive()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);

        var mounted = device.TryMountImage(1, [0x01], "drive-b");

        Assert.Multiple(() =>
        {
            Assert.That(mounted, Is.False);
            Assert.That(device.IsImageMounted(1), Is.False);
        });
    }

    [Test]
    public void ReadSectorCommandShouldTransferSectorToDmaAddress()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        var transferred = new Dictionary<uint, byte>();
        device.DmaWrite8 = (address, value) => transferred[address] = value;

        var image = new byte[80 * 2 * 9 * 512];
        image[0] = 0x12;
        image[1] = 0x34;
        image[2] = 0x56;
        Assert.That(device.TryMountImage(0, image, "disk"), Is.True);

        device.ApplyPortA(0x05);
        device.Write8(DmaAddressHighRegisterAddress, 0x00);
        device.Write8(DmaAddressMidRegisterAddress, 0x10);
        device.Write8(DmaAddressLowRegisterAddress, 0x00);

        WriteWord(device, ControlRegisterAddress, FdcTrackAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0000);
        WriteWord(device, ControlRegisterAddress, FdcSectorAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);
        WriteWord(device, ControlRegisterAddress, DmaSectorCountAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, ReadSectorCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        var status = ReadWord(device, DataRegisterAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status & RecordNotFoundMask, Is.Zero);
            Assert.That(transferred[0x001000], Is.EqualTo(0x12));
            Assert.That(transferred[0x001001], Is.EqualTo(0x34));
            Assert.That(transferred[0x001002], Is.EqualTo(0x56));
        });
    }

    [Test]
    public void ReadSectorStatusShouldNotSetLostDataBitOnSuccessfulTransfer()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false)
        {
            DmaWrite8 = (_, _) => { }
        };
        Assert.That(device.TryMountImage(0, new byte[80 * 2 * 9 * 512], "disk"), Is.True);
        device.ApplyPortA(0x05);
        WriteWord(device, ControlRegisterAddress, FdcTrackAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0000);
        WriteWord(device, ControlRegisterAddress, FdcSectorAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);
        WriteWord(device, ControlRegisterAddress, DmaSectorCountAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, ReadSectorCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);

        var status = ReadWord(device, DataRegisterAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status & RecordNotFoundMask, Is.Zero);
            Assert.That(status & TrackZeroMask, Is.Zero, "Type-II status bit2 is LOST_DATA and should be clear on success.");
            Assert.That(status & WriteProtectMask, Is.Zero, "Type-II read status bit6 should stay clear for normal sector data.");
        });
    }

    [Test]
    public void WriteSectorCommandShouldReportWriteProtected()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        Assert.That(device.TryMountImage(0, new byte[80 * 2 * 9 * 512], "disk"), Is.True);
        device.ApplyPortA(0x05);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, WriteSectorCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        var status = ReadWord(device, DataRegisterAddress);

        Assert.That(status & WriteProtectMask, Is.Not.Zero);
    }

    [Test]
    public void SeekCommandShouldUpdateTrackRegisterFromDataRegister()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        device.ApplyPortA(0x05);

        WriteWord(device, ControlRegisterAddress, FdcTrackAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0005);
        WriteWord(device, ControlRegisterAddress, FdcDataAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0007);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0010); // Seek.
        WriteWord(device, ControlRegisterAddress, FdcTrackAccessControl);
        var track = ReadWord(device, DataRegisterAddress);

        Assert.That(track & 0x00FF, Is.EqualTo(7));
    }

    [Test]
    public void DmaStatusShouldExposeSectorCountZeroBit()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);

        WriteWord(device, ControlRegisterAddress, DmaSectorCountAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);
        var statusWithCount = ReadWord(device, ControlRegisterAddress);

        WriteWord(device, ControlRegisterAddress, DmaSectorCountAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0000);
        var statusWithZero = ReadWord(device, ControlRegisterAddress);

        Assert.Multiple(() =>
        {
            Assert.That(statusWithCount & 0x0002, Is.Not.Zero);
            Assert.That(statusWithZero & 0x0002, Is.Zero);
        });
    }

    [Test]
    public void TogglingDmaWriteBitShouldClearSectorCount()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        WriteWord(device, ControlRegisterAddress, DmaSectorCountAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);

        var statusBeforeToggle = ReadWord(device, ControlRegisterAddress);
        WriteWord(device, ControlRegisterAddress, 0x0180);
        var statusAfterToggle = ReadWord(device, ControlRegisterAddress);

        Assert.Multiple(() =>
        {
            Assert.That(statusBeforeToggle & 0x0002, Is.Not.Zero);
            Assert.That(statusAfterToggle & 0x0002, Is.Zero);
        });
    }

    [Test]
    public void ReadSectorWithZeroSectorCountShouldSetDmaErrorAndNotWriteMemory()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        var writeCount = 0;
        device.DmaWrite8 = (_, _) => writeCount++;
        Assert.That(device.TryMountImage(0, new byte[80 * 2 * 9 * 512], "disk"), Is.True);
        device.ApplyPortA(0x05);
        WriteWord(device, ControlRegisterAddress, FdcTrackAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0000);
        WriteWord(device, ControlRegisterAddress, FdcSectorAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);
        WriteWord(device, ControlRegisterAddress, DmaSectorCountAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0000);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, ReadSectorCommand);

        var dmaStatus = ReadWord(device, ControlRegisterAddress);
        Assert.Multiple(() =>
        {
            Assert.That(writeCount, Is.Zero);
            Assert.That(dmaStatus & 0x0001, Is.Zero);
        });
    }

    [Test]
    public void ReadAddressCommandShouldWriteIdTupleToDma()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        var transferred = new Dictionary<uint, byte>();
        device.DmaWrite8 = (address, value) => transferred[address] = value;
        Assert.That(device.TryMountImage(0, new byte[80 * 2 * 9 * 512], "disk"), Is.True);
        device.ApplyPortA(0x05);
        device.Write8(DmaAddressHighRegisterAddress, 0x00);
        device.Write8(DmaAddressMidRegisterAddress, 0x20);
        device.Write8(DmaAddressLowRegisterAddress, 0x00);
        WriteWord(device, ControlRegisterAddress, FdcTrackAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0003);
        WriteWord(device, ControlRegisterAddress, FdcSectorAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0005);
        WriteWord(device, ControlRegisterAddress, DmaSectorCountAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, ReadAddressCommand);

        Assert.Multiple(() =>
        {
            Assert.That(transferred[0x002000], Is.EqualTo(0x03));
            Assert.That(transferred[0x002001], Is.EqualTo(0x00));
            Assert.That(transferred[0x002002], Is.EqualTo(0x05));
            Assert.That(transferred[0x002003], Is.EqualTo(0x02));
        });
    }

    [Test]
    public void DmaAddressWritesShouldMaskToStAddressSpaceAndEvenAddress()
    {
        var device = new FloppyDmaFdcDevice();
        device.Write8(DmaAddressHighRegisterAddress, 0xFF);
        device.Write8(DmaAddressMidRegisterAddress, 0xFF);
        device.Write8(DmaAddressLowRegisterAddress, 0xFF);

        var high = device.Read8(DmaAddressHighRegisterAddress);
        var mid = device.Read8(DmaAddressMidRegisterAddress);
        var low = device.Read8(DmaAddressLowRegisterAddress);

        Assert.Multiple(() =>
        {
            Assert.That(high, Is.EqualTo(0x3F));
            Assert.That(mid, Is.EqualTo(0xFF));
            Assert.That(low & 0x01, Is.Zero);
        });
    }

    [Test]
    public void DmaAddressWritesShouldPreserveMidByteWhenHighByteWrittenAfterMidByte()
    {
        var device = new FloppyDmaFdcDevice();
        device.Write8(DmaAddressMidRegisterAddress, 0x10);
        device.Write8(DmaAddressHighRegisterAddress, 0x00);
        device.Write8(DmaAddressLowRegisterAddress, 0x04);

        var high = device.Read8(DmaAddressHighRegisterAddress);
        var mid = device.Read8(DmaAddressMidRegisterAddress);
        var low = device.Read8(DmaAddressLowRegisterAddress);

        Assert.Multiple(() =>
        {
            Assert.That(high, Is.EqualTo(0x00));
            Assert.That(mid, Is.EqualTo(0x10));
            Assert.That(low, Is.EqualTo(0x04));
        });
    }

    [Test]
    public void ReadSectorShouldFailWhenDmaTargetIsOutsideConfiguredRamRange()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        var writeCount = 0;
        device.DmaWrite8 = (_, _) => writeCount++;
        device.ConfigureDmaAddressLimit(0x00010000);
        Assert.That(device.TryMountImage(0, new byte[80 * 2 * 9 * 512], "disk"), Is.True);
        device.ApplyPortA(0x05);
        device.Write8(DmaAddressHighRegisterAddress, 0x00);
        device.Write8(DmaAddressMidRegisterAddress, 0xFF);
        device.Write8(DmaAddressLowRegisterAddress, 0x00);
        WriteWord(device, ControlRegisterAddress, FdcTrackAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0000);
        WriteWord(device, ControlRegisterAddress, FdcSectorAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);
        WriteWord(device, ControlRegisterAddress, DmaSectorCountAccessControl);
        WriteWord(device, DataRegisterAddress, 0x0001);

        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, ReadSectorCommand);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        var status = ReadWord(device, DataRegisterAddress);

        Assert.Multiple(() =>
        {
            Assert.That(writeCount, Is.Zero);
            Assert.That(status & RecordNotFoundMask, Is.Not.Zero);
        });
    }

    [Test]
    public void FloppyTraceShouldCaptureRecentCommandFlow()
    {
        var device = new FloppyDmaFdcDevice(driveAPresent: true, driveBPresent: false);
        device.SetTraceEnabled(true);
        
        device.ApplyPortA(0x05);
        WriteWord(device, ControlRegisterAddress, FdcStatusAccessControl);
        WriteWord(device, DataRegisterAddress, RestoreCommand);

        var stats = device.GetDebugStats();
        var traceLines = device.GetRecentTraceLines(16);

        Assert.Multiple(() =>
        {
            Assert.That(stats.CommandCount, Is.EqualTo(1));
            Assert.That(stats.LastCommand, Is.EqualTo(RestoreCommand));
            Assert.That(traceLines, Is.Not.Empty);
            Assert.That(traceLines.Any(line => line.Contains("RESTORE", StringComparison.Ordinal)), Is.True);
        });
    }

    private static void WriteWord(FloppyDmaFdcDevice device, uint address, ushort value)
    {
        device.Write8(address, (byte)(value >> 8));
        device.Write8(address + 1, (byte)(value & 0xFF));
    }

    private static ushort ReadWord(FloppyDmaFdcDevice device, uint address)
    {
        var high = device.Read8(address);
        var low = device.Read8(address + 1);
        return (ushort)((high << 8) | low);
    }
}
