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
public sealed class PsgDeviceTests
{
    private const uint RegisterSelectAddress = 0x00FF8800;
    private const uint DataAddress = 0x00FF8802;
    private const byte PortARegister = 0x0E;

    [Test]
    public void ResetShouldDefaultPortAToDeselectFloppyDrives()
    {
        var device = new PsgDevice();

        device.Reset();
        device.Write8(RegisterSelectAddress, PortARegister);
        var portA = device.Read8(RegisterSelectAddress);

        Assert.That(portA & 0x07, Is.EqualTo(0x07));
    }

    [Test]
    public void WritingPortAShouldRaisePortAChangedEvent()
    {
        var device = new PsgDevice();
        var eventRaised = false;
        var observedValue = (byte)0;
        device.PortAChanged += value =>
        {
            eventRaised = true;
            observedValue = value;
        };

        device.Write8(RegisterSelectAddress, PortARegister);
        device.Write8(DataAddress, 0x05);

        Assert.Multiple(() =>
        {
            Assert.That(eventRaised, Is.True);
            Assert.That(observedValue, Is.EqualTo(0x05));
        });
    }
}
