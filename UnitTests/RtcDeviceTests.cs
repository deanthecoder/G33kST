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
public sealed class RtcDeviceTests
{
    private const uint BaseAddress = 0x00FFFC20;

    [Test]
    public void EvenAddressReadShouldReturnOpenBus()
    {
        var rtc = new RtcDevice();
        rtc.Reset();

        var value = rtc.Read8(BaseAddress);

        Assert.That(value, Is.EqualTo(0xFF));
    }

    [Test]
    public void OddAddressWriteReadShouldRoundTripNibble()
    {
        var rtc = new RtcDevice();
        rtc.Reset();
        const uint minuteUnitsAddress = BaseAddress + 0x05;

        rtc.Write8(minuteUnitsAddress, 0xAB);
        var value = rtc.Read8(minuteUnitsAddress);

        Assert.That(value, Is.EqualTo(0x0B));
    }

    [Test]
    public void OutOfRangeReadShouldReturnOpenBus()
    {
        var rtc = new RtcDevice();
        rtc.Reset();

        var value = rtc.Read8(BaseAddress - 1);

        Assert.That(value, Is.EqualTo(0xFF));
    }
}
