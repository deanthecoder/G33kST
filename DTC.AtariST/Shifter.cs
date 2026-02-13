// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation;

namespace DTC.AtariST;

/// <summary>
/// Minimal Atari ST Shifter video source for low-resolution (320x200, 4 bitplanes) output.
/// </summary>
public sealed class Shifter : IVideoSource
{
    private const int BytesPerPixel = 4;
    private const int LowResWidth = 320;
    private const int LowResHeight = 200;
    private const int LowResWordsPerLine = 20;
    private const int LowResBytesPerLine = LowResWordsPerLine * 8;
    private const int TotalRasterLines = 262; // NTSC model for now.

    // ST Shifter/MMU registers (minimal subset for low-res fetch and palette decode).
    private const uint VideoBaseHighRegister = 0x00FF8201;
    private const uint VideoBaseMidRegister = 0x00FF8203;
    private const uint VideoBaseLowRegister = 0x00FF820D; // STE extension; ignored on plain ST.
    private const uint VideoModeRegister = 0x00FF8260;
    private const uint PaletteBaseRegister = 0x00FF8240;

    private readonly Bus m_bus;
    private readonly double m_ticksPerLine;
    private readonly byte[] m_frameBuffer = new byte[LowResWidth * LowResHeight * BytesPerPixel];
    private readonly byte[] m_paletteR = new byte[16];
    private readonly byte[] m_paletteG = new byte[16];
    private readonly byte[] m_paletteB = new byte[16];
    private double m_lineTickAccumulator;
    private int m_currentRasterLine;

    /// <summary>
    /// Creates a low-resolution Atari ST video source.
    /// </summary>
    public Shifter(Bus bus, double cpuHz, double videoHz)
    {
        m_bus = bus ?? throw new ArgumentNullException(nameof(bus));
        m_ticksPerLine = cpuHz / (videoHz * TotalRasterLines);
        if (m_ticksPerLine <= 0)
            throw new ArgumentOutOfRangeException(nameof(cpuHz), "Computed ticks-per-line must be positive.");

        // Ensure alpha is always opaque.
        for (var i = 3; i < m_frameBuffer.Length; i += BytesPerPixel)
            m_frameBuffer[i] = 255;
    }

    /// <inheritdoc />
    public int FrameWidth => LowResWidth;

    /// <inheritdoc />
    public int FrameHeight => LowResHeight;

    /// <inheritdoc />
    public event EventHandler<byte[]> FrameRendered;

    /// <summary>
    /// Resets internal raster timing state.
    /// </summary>
    public void Reset()
    {
        m_lineTickAccumulator = 0;
        m_currentRasterLine = 0;
    }

    /// <summary>
    /// Advances raster timing and raises callbacks on HBlank and VBlank boundaries.
    /// </summary>
    public void Advance(long deltaTicks, Action hblankCallback, Action vblankCallback)
    {
        if (deltaTicks <= 0)
            return;

        m_lineTickAccumulator += deltaTicks;
        while (m_lineTickAccumulator >= m_ticksPerLine)
        {
            m_lineTickAccumulator -= m_ticksPerLine;
            hblankCallback?.Invoke();

            m_currentRasterLine++;
            if (m_currentRasterLine == LowResHeight)
            {
                RenderFrame();
                vblankCallback?.Invoke();
            }

            if (m_currentRasterLine >= TotalRasterLines)
                m_currentRasterLine = 0;
        }
    }

    /// <inheritdoc />
    public void CopyToFrameBuffer(Span<byte> frameBuffer)
    {
        if (frameBuffer.Length < m_frameBuffer.Length)
            throw new ArgumentException($"Frame buffer too small; expected at least {m_frameBuffer.Length} bytes.", nameof(frameBuffer));

        m_frameBuffer.CopyTo(frameBuffer);
    }

    private void RenderFrame()
    {
        RefreshPalette();
        var screenBaseAddress = ReadScreenBaseAddress();

        var mode = m_bus.Read8(VideoModeRegister) & 0x03;
        switch (mode)
        {
            case 0:
                RenderLowResolution(screenBaseAddress);
                break;

            case 1:
                RenderMediumResolution(screenBaseAddress);
                break;

            case 2:
                RenderHighResolutionMonochrome(screenBaseAddress);
                break;

            default:
                ClearToBlack();
                break;
        }

        FrameRendered?.Invoke(this, m_frameBuffer);
    }

    private void RenderLowResolution(uint screenBaseAddress)
    {
        for (var y = 0; y < LowResHeight; y++)
        {
            var lineAddress = unchecked(screenBaseAddress + (uint)(y * LowResBytesPerLine));
            var outIndex = y * LowResWidth * BytesPerPixel;
            for (var chunk = 0; chunk < LowResWordsPerLine; chunk++)
            {
                var chunkAddress = unchecked(lineAddress + (uint)(chunk * 8));
                var plane0 = m_bus.Read16BigEndian(chunkAddress);
                var plane1 = m_bus.Read16BigEndian(chunkAddress + 2);
                var plane2 = m_bus.Read16BigEndian(chunkAddress + 4);
                var plane3 = m_bus.Read16BigEndian(chunkAddress + 6);
                for (var bit = 15; bit >= 0; bit--)
                {
                    var pixelIndex =
                        ((plane0 >> bit) & 1) |
                        (((plane1 >> bit) & 1) << 1) |
                        (((plane2 >> bit) & 1) << 2) |
                        (((plane3 >> bit) & 1) << 3);
                    WriteRgbAt(outIndex, m_paletteR[pixelIndex], m_paletteG[pixelIndex], m_paletteB[pixelIndex]);
                    outIndex += BytesPerPixel;
                }
            }
        }
    }

    private void RenderMediumResolution(uint screenBaseAddress)
    {
        const int wordsPerLine = 40; // 640 / 16
        const int bytesPerLine = wordsPerLine * 4; // 2 planes

        for (var y = 0; y < LowResHeight; y++)
        {
            var lineAddress = unchecked(screenBaseAddress + (uint)(y * bytesPerLine));
            var outIndex = y * LowResWidth * BytesPerPixel;
            for (var chunk = 0; chunk < wordsPerLine; chunk++)
            {
                var chunkAddress = unchecked(lineAddress + (uint)(chunk * 4));
                var plane0 = m_bus.Read16BigEndian(chunkAddress);
                var plane1 = m_bus.Read16BigEndian(chunkAddress + 2);
                for (var outPixel = 0; outPixel < 8; outPixel++)
                {
                    var bit = 15 - (outPixel * 2);
                    var pixelIndex =
                        ((plane0 >> bit) & 1) |
                        (((plane1 >> bit) & 1) << 1);
                    WriteRgbAt(outIndex, m_paletteR[pixelIndex], m_paletteG[pixelIndex], m_paletteB[pixelIndex]);
                    outIndex += BytesPerPixel;
                }
            }
        }
    }

    private void RenderHighResolutionMonochrome(uint screenBaseAddress)
    {
        const int wordsPerLine = 40; // 640 / 16
        const int bytesPerLine = wordsPerLine * 2; // 1 plane

        for (var y = 0; y < LowResHeight; y++)
        {
            var sourceY = y * 2;
            var lineAddress = unchecked(screenBaseAddress + (uint)(sourceY * bytesPerLine));
            var outIndex = y * LowResWidth * BytesPerPixel;
            for (var chunk = 0; chunk < wordsPerLine; chunk++)
            {
                var word = m_bus.Read16BigEndian(unchecked(lineAddress + (uint)(chunk * 2)));
                for (var outPixel = 0; outPixel < 8; outPixel++)
                {
                    var bit = 15 - (outPixel * 2);
                    var on = ((word >> bit) & 1) != 0;
                    var value = on ? (byte)255 : (byte)0;
                    WriteRgbAt(outIndex, value, value, value);
                    outIndex += BytesPerPixel;
                }
            }
        }
    }

    private void WriteRgbAt(int outputIndex, byte red, byte green, byte blue)
    {
        m_frameBuffer[outputIndex] = red;
        m_frameBuffer[outputIndex + 1] = green;
        m_frameBuffer[outputIndex + 2] = blue;
        m_frameBuffer[outputIndex + 3] = 255;
    }

    private uint ReadScreenBaseAddress()
    {
        var high = m_bus.Read8(VideoBaseHighRegister);
        var mid = m_bus.Read8(VideoBaseMidRegister);
        var low = m_bus.Read8(VideoBaseLowRegister);
        var address = (uint)((high << 16) | (mid << 8) | low);
        return address & 0x00FF_FFFE;
    }

    private void RefreshPalette()
    {
        for (var i = 0; i < 16; i++)
        {
            var registerValue = m_bus.Read16BigEndian(PaletteBaseRegister + (uint)(i * 2));
            var r3 = (registerValue >> 8) & 0x7;
            var g3 = (registerValue >> 4) & 0x7;
            var b3 = registerValue & 0x7;
            m_paletteR[i] = ScaleThreeBitToEightBit(r3);
            m_paletteG[i] = ScaleThreeBitToEightBit(g3);
            m_paletteB[i] = ScaleThreeBitToEightBit(b3);
        }
    }

    private void ClearToBlack()
    {
        for (var i = 0; i < m_frameBuffer.Length; i += BytesPerPixel)
            WriteRgbAt(i, 0, 0, 0);
    }

    private static byte ScaleThreeBitToEightBit(int value) =>
        (byte)(value * 255 / 7);
}
