// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation.Image;

namespace UnitTests;

/// <summary>
/// Tests CRT framebuffer output behavior.
/// </summary>
[TestFixture]
public sealed class CrtFrameBufferTests
{
    [Test]
    public void ApplyInCrtModeShouldLiftBlackPixelsAboveZero()
    {
        var crt = new CrtFrameBuffer(1, 1)
        {
            IsCrt = true
        };
        var source = new byte[] { 0, 0, 0 };

        var output = crt.Apply(source);
        var rgbBytes = output.Where((_, index) => index % CrtFrameBuffer.BytesPerPixel != 3);

        Assert.That(rgbBytes.Any(channel => channel > 0), Is.True, "CRT mode should produce a faint glow for black input.");
    }

    [Test]
    public void ApplyInPlainModeShouldKeepBlackPixelsAtZero()
    {
        var crt = new CrtFrameBuffer(1, 1)
        {
            IsCrt = false
        };
        var source = new byte[] { 0, 0, 0 };

        var output = crt.Apply(source);
        var rgbBytes = output.Where((_, index) => index % CrtFrameBuffer.BytesPerPixel != 3);

        Assert.That(rgbBytes.All(channel => channel == 0), Is.True, "Plain mode should not apply CRT black-floor lifting.");
    }

    [Test]
    public void ApplyInCrtModeShouldApplyRetroScanlineSweepWhenEnabled()
    {
        var crt = new CrtFrameBuffer(4, 40)
        {
            IsCrt = true,
            IsRetroScanlineEffectEnabled = false
        };
        var source = Enumerable.Repeat((byte)32, 4 * 40 * 3).ToArray();

        var withoutSweep = crt.Apply(source).ToArray();
        crt.IsRetroScanlineEffectEnabled = true;
        var withSweep = crt.Apply(source).ToArray();

        Assert.That(withSweep, Is.Not.EqualTo(withoutSweep), "Retro scanline sweep should change CRT output when enabled.");
    }

    [Test]
    public void ApplyInCrtModeShouldLetRetroScanlineSweepBrightenBlackPixels()
    {
        var crt = new CrtFrameBuffer(4, 80)
        {
            IsCrt = true,
            IsRetroScanlineEffectEnabled = false
        };
        var source = new byte[4 * 80 * 3];

        var withoutSweep = crt.Apply(source).ToArray();
        crt.IsRetroScanlineEffectEnabled = true;
        var withSweep = crt.Apply(source).ToArray();

        var maxWithoutSweep = withoutSweep.Where((_, index) => index % CrtFrameBuffer.BytesPerPixel != 3).Max();
        var maxWithSweep = withSweep.Where((_, index) => index % CrtFrameBuffer.BytesPerPixel != 3).Max();

        Assert.That(maxWithSweep, Is.GreaterThan(maxWithoutSweep), "Sweep should add a little emissive brightness even over black source pixels.");
    }
}
