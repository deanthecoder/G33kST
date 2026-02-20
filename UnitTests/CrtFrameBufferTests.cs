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
}
