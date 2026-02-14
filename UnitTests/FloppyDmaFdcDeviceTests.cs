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
    private const ushort FdcStatusAccessControl = 0x0080;
    private const byte RestoreCommand = 0x00;
    private const byte TrackZeroMask = 0x04;

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
