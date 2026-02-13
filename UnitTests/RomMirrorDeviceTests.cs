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
public sealed class RomMirrorDeviceTests : TestsBase
{
    [Test]
    public void CheckConstructorInitializesAddressRange()
    {
        var rom = new RomDevice(192 * 1024, 0xFC0000);
        var mirror = new RomMirrorDevice(rom);

        Assert.That(mirror.FromAddr, Is.EqualTo(0x000000));
        Assert.That(mirror.ToAddr, Is.EqualTo(0x000007));
    }

    [Test]
    public void GivenNullRomCheckConstructionThrows()
    {
        Assert.That(() => new RomMirrorDevice(null), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void CheckReadsFromMirrorReturnRomContent()
    {
        var rom = new RomDevice(192 * 1024, 0xFC0000)
        {
            Data =
            {
                [0] = 0x12,
                [1] = 0x34,
                [7] = 0xAB
            }
        };

        var mirror = new RomMirrorDevice(rom);

        Assert.That(mirror.Read8(0x000000), Is.EqualTo(0x12));
        Assert.That(mirror.Read8(0x000001), Is.EqualTo(0x34));
        Assert.That(mirror.Read8(0x000007), Is.EqualTo(0xAB));
    }

    [Test]
    public void CheckWritesToMirrorAreIgnored()
    {
        var rom = new RomDevice(192 * 1024, 0xFC0000)
        {
            Data =
            {
                [0] = 0x12
            }
        };

        var mirror = new RomMirrorDevice(rom);
        mirror.Write8(0x000000, 0xFF);

        Assert.That(rom.Data[0], Is.EqualTo(0x12));
        Assert.That(mirror.Read8(0x000000), Is.EqualTo(0x12));
    }
}
