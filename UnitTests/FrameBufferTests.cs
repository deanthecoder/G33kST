// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Image;

namespace UnitTests;

/// <summary>
/// Tests for framebuffer helper operations shared by UI and emulation glue code.
/// </summary>
[TestFixture]
public sealed class FrameBufferTests
{
    [Test]
    public void FillBlackShouldKeepRgbaAlphaOpaque()
    {
        var frameBuffer = new FrameBuffer(2, 1, 4);

        frameBuffer.FillBlack();

        Assert.Multiple(() =>
        {
            Assert.That(frameBuffer.Data[0], Is.EqualTo(0));
            Assert.That(frameBuffer.Data[1], Is.EqualTo(0));
            Assert.That(frameBuffer.Data[2], Is.EqualTo(0));
            Assert.That(frameBuffer.Data[3], Is.EqualTo(255));
            Assert.That(frameBuffer.Data[4], Is.EqualTo(0));
            Assert.That(frameBuffer.Data[5], Is.EqualTo(0));
            Assert.That(frameBuffer.Data[6], Is.EqualTo(0));
            Assert.That(frameBuffer.Data[7], Is.EqualTo(255));
        });
    }

    [Test]
    public void CopyFromShouldAllowShortSourceWhenRequestedAndClearRemainder()
    {
        var frameBuffer = new FrameBuffer(4, 1, 1);
        frameBuffer.Data.AsSpan().Fill(0xFF);
        var source = new byte[] { 0x11, 0x22 };

        frameBuffer.CopyFrom(source, clearRemainderWhenShort: true);

        Assert.That(frameBuffer.Data, Is.EqualTo(new byte[] { 0x11, 0x22, 0x00, 0x00 }));
    }

    [Test]
    public void BlendWithPreviousShouldApplyConfiguredWeights()
    {
        var frameBuffer = new FrameBuffer(1, 1, 4);
        frameBuffer.CopyFrom(new byte[] { 100, 120, 140, 160 });
        var current = new byte[] { 200, 220, 240, 255 };

        frameBuffer.BlendWithPrevious(current, previousWeight: 3, currentWeight: 1);

        Assert.That(frameBuffer.Data, Is.EqualTo(new byte[] { 125, 145, 165, 183 }));
    }

    [Test]
    public void CloneShouldCopyGeometryAndPixelData()
    {
        var frameBuffer = new FrameBuffer(2, 1, 3);
        frameBuffer.CopyFrom(new byte[] { 1, 2, 3, 4, 5, 6 });

        var clone = frameBuffer.Clone();
        frameBuffer.Data[0] = 9;

        Assert.Multiple(() =>
        {
            Assert.That(clone.Width, Is.EqualTo(2));
            Assert.That(clone.Height, Is.EqualTo(1));
            Assert.That(clone.BytesPerPixel, Is.EqualTo(3));
            Assert.That(clone.Data, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
        });
    }
}
