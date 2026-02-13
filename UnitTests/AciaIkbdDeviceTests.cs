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
public sealed class AciaIkbdDeviceTests
{
    private const uint KeyboardStatusAddress = 0x00FFFC00;
    private const uint KeyboardDataAddress = 0x00FFFC02;
    private const uint MidiStatusAddress = 0x00FFFC04;
    private const uint MidiDataAddress = 0x00FFFC06;

    [Test]
    public void KeyboardStatusShouldReportTransmitReadyWhenQueueIsEmpty()
    {
        var device = new AciaIkbdDevice();

        var status = device.Read8(KeyboardStatusAddress);

        Assert.That(status, Is.EqualTo(0x02));
    }

    [Test]
    public void KeyboardResetCommandShouldQueueResetAcknowledge()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x80);
        device.Write8(KeyboardDataAddress, 0x01);
        var status = device.Read8(KeyboardStatusAddress);
        var acknowledge = device.Read8(KeyboardDataAddress);
        var statusAfterRead = device.Read8(KeyboardStatusAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status & 0x01, Is.Not.Zero);
            Assert.That(acknowledge, Is.EqualTo(0xF1));
            Assert.That(statusAfterRead & 0x01, Is.Zero);
        });
    }

    [Test]
    public void UnknownKeyboardCommandShouldNotQueueResponse()
    {
        var device = new AciaIkbdDevice();

        device.Write8(KeyboardDataAddress, 0x01);
        var status = device.Read8(KeyboardStatusAddress);

        Assert.That(status & 0x01, Is.Zero);
    }

    [Test]
    public void KeyboardControlMasterResetShouldClearReceiveQueue()
    {
        var device = new AciaIkbdDevice();
        device.Write8(KeyboardDataAddress, 0x80);
        device.Write8(KeyboardDataAddress, 0x01);
        Assert.That(device.Read8(KeyboardStatusAddress) & 0x01, Is.Not.Zero);

        device.Write8(KeyboardStatusAddress, 0x03);
        var statusAfterReset = device.Read8(KeyboardStatusAddress);

        Assert.That(statusAfterReset & 0x01, Is.Zero);
    }

    [Test]
    public void MidiReadsShouldReturnDefaultValues()
    {
        var device = new AciaIkbdDevice();

        var midiStatus = device.Read8(MidiStatusAddress);
        var midiData = device.Read8(MidiDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(midiStatus, Is.EqualTo(0x02));
            Assert.That(midiData, Is.EqualTo(0x00));
        });
    }

    [Test]
    public void UnsupportedAddressShouldReturnOpenBusValue()
    {
        var device = new AciaIkbdDevice();

        var value = device.Read8(0x00FFFC40);

        Assert.That(value, Is.EqualTo(0xFF));
    }

    [Test]
    public void QueueKeyboardByteShouldRaiseDataReadyEventForEachQueuedByte()
    {
        var device = new AciaIkbdDevice();
        var eventCount = 0;
        device.KeyboardDataReady += (_, _) => eventCount++;

        device.QueueKeyboardByte(0x1C);
        device.QueueKeyboardByte(0x9C);

        Assert.That(eventCount, Is.EqualTo(2));
    }

    [Test]
    public void QueueKeyboardByteShouldExposeQueuedValueThroughKeyboardDataRegister()
    {
        var device = new AciaIkbdDevice();
        device.QueueKeyboardByte(0x1C);

        var status = device.Read8(KeyboardStatusAddress);
        var value = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status & 0x01, Is.Not.Zero);
            Assert.That(value, Is.EqualTo(0x1C));
        });
    }

    [Test]
    public void KeyboardStatusShouldSetInterruptFlagWhenReceiveDataIsPending()
    {
        var device = new AciaIkbdDevice();
        device.Write8(KeyboardStatusAddress, 0x80);
        device.QueueKeyboardByte(0x1C);

        var status = device.Read8(KeyboardStatusAddress);

        Assert.That(status & 0x80, Is.Not.Zero);
    }

    [Test]
    public void ResetShouldQueueIkbdResetCompleteCode()
    {
        var device = new AciaIkbdDevice();

        device.Reset();
        var status = device.Read8(KeyboardStatusAddress);
        var value = device.Read8(KeyboardDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status & 0x01, Is.Not.Zero);
            Assert.That(value, Is.EqualTo(0xF1));
        });
    }

    [Test]
    public void ClockRegisterWindowShouldNotAliasToKeyboardAcia()
    {
        var device = new AciaIkbdDevice();
        const uint clockWindowStatusAddress = 0x00FFFC3B;
        const uint clockWindowDataAddress = 0x00FFFC3D;

        var status = device.Read8(clockWindowStatusAddress);
        var data = device.Read8(clockWindowDataAddress);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(0xFF));
            Assert.That(data, Is.EqualTo(0xFF));
        });
    }
}
