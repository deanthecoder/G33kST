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
using DTC.M68000;

namespace UnitTests;

[TestFixture]
public sealed class BusErrorDeviceTests
{
    [Test]
    public void Read8ShouldThrowBusErrorException()
    {
        var device = new BusErrorDevice(0x00FF8A00, 0x00FF8A3F);

        Assert.That(
            () => device.Read8(0x00FF8A3C),
            Throws.TypeOf<BusErrorException>());
    }

    [Test]
    public void Write8ShouldThrowBusErrorException()
    {
        var device = new BusErrorDevice(0x00FF8A00, 0x00FF8A3F);

        Assert.That(
            () => device.Write8(0x00FF8A3C, 0x12),
            Throws.TypeOf<BusErrorException>());
    }
}
