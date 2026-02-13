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
public sealed class SystemControlDeviceTests
{
    [Test]
    public void WriteMemoryConfigurationRegisterShouldRaiseEvent()
    {
        var device = new SystemControlDevice();
        var called = false;
        var writtenValue = (byte)0;
        device.MemoryConfigurationWritten += value =>
        {
            called = true;
            writtenValue = value;
        };

        device.Write8(0x00FF8001, 0xA5);

        Assert.Multiple(() =>
        {
            Assert.That(called, Is.True);
            Assert.That(writtenValue, Is.EqualTo(0xA5));
            Assert.That(device.Read8(0x00FF8001), Is.EqualTo(0xA5));
        });
    }

    [Test]
    public void OutOfRangeReadShouldReturnOpenBus()
    {
        var device = new SystemControlDevice();

        var value = device.Read8(0x00FF7FFF);

        Assert.That(value, Is.EqualTo(0xFF));
    }
}
