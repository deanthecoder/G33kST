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
/// Minimal Atari ST Shifter video source with basic low/medium/high mode rendering.
/// Output is exposed as a fixed RGB surface with a representative border area.
/// </summary>
public sealed class Shifter : IVideoSource
{
    private const int BytesPerPixel = 3;
    private const int ActiveOutputWidth = 640;
    private const int ActiveOutputHeight = 400;
    private const int BorderWidth = 32;
    private const int BorderHeight = 24;
    private const int OutputWidth = ActiveOutputWidth + (BorderWidth * 2);
    private const int OutputHeight = ActiveOutputHeight + (BorderHeight * 2);
    private const int LowResWidth = 320;
    private const int LowResHeight = 200;
    private const int LowResWordsPerLine = 20;
    private const int LowResBytesPerLine = LowResWordsPerLine * 8;
    private const int MediumResWidth = 640;
    private const int MediumResHeight = 200;
    private const int MediumResWordsPerLine = 40;
    private const int MediumResBytesPerLine = MediumResWordsPerLine * 4;
    private const int HighResWidth = 640;
    private const int HighResHeight = 400;
    private const int HighResWordsPerLine = 40;
    private const int HighResBytesPerLine = HighResWordsPerLine * 2;
    private const int VisibleRasterLines = 200;
    private const int TotalRasterLines = 262; // NTSC model for now.

    // ST Shifter/MMU registers (minimal subset for frame base, mode, and palette decode).
    private const uint VideoBaseHighRegister = 0x00FF8201;
    private const uint VideoBaseMidRegister = 0x00FF8203;
    private const uint VideoBaseLowRegister = 0x00FF820D; // STE extension; ignored on plain ST.
    private const uint VideoModeRegister = 0x00FF8260;
    private const uint PaletteBaseRegister = 0x00FF8240;

    private readonly Bus m_bus;
    private readonly double m_ticksPerLine;
    private readonly byte[] m_frameBuffer = new byte[OutputWidth * OutputHeight * BytesPerPixel];
    private readonly byte[] m_paletteR = new byte[16];
    private readonly byte[] m_paletteG = new byte[16];
    private readonly byte[] m_paletteB = new byte[16];
    private double m_lineTickAccumulator;
    private int m_currentRasterLine;
    private int m_activeWidth = LowResWidth;
    private int m_activeHeight = LowResHeight;
    private int m_activeOriginX = BorderWidth;
    private int m_activeOriginY = BorderHeight;

    /// <summary>
    /// Creates an Atari ST video source.
    /// </summary>
    public Shifter(Bus bus, double cpuHz, double videoHz)
    {
        m_bus = bus ?? throw new ArgumentNullException(nameof(bus));
        m_ticksPerLine = cpuHz / (videoHz * TotalRasterLines);
        if (m_ticksPerLine <= 0)
            throw new ArgumentOutOfRangeException(nameof(cpuHz), "Computed ticks-per-line must be positive.");

    }

    /// <inheritdoc />
    public int FrameWidth => OutputWidth;

    /// <inheritdoc />
    public int FrameHeight => OutputHeight;

    /// <inheritdoc />
    public int FrameBytesPerPixel => BytesPerPixel;

    /// <summary>
    /// Gets the active source width for the last rendered frame before scaling to output.
    /// </summary>
    public int ActiveWidth => m_activeWidth;

    /// <summary>
    /// Gets the active source height for the last rendered frame before scaling to output.
    /// </summary>
    public int ActiveHeight => m_activeHeight;

    /// <summary>
    /// Gets the X offset where the active picture starts in the output framebuffer.
    /// </summary>
    public int ActiveOriginX => m_activeOriginX;

    /// <summary>
    /// Gets the Y offset where the active picture starts in the output framebuffer.
    /// </summary>
    public int ActiveOriginY => m_activeOriginY;

    /// <summary>
    /// Gets whether the current raster line is in the display-enable range.
    /// </summary>
    public bool IsDisplayEnableActive => m_currentRasterLine < VisibleRasterLines;

    /// <inheritdoc />
    public event EventHandler<byte[]> FrameRendered;

    /// <summary>
    /// Resets internal raster timing state.
    /// </summary>
    public void Reset()
    {
        m_lineTickAccumulator = 0;
        m_currentRasterLine = 0;
        m_activeWidth = LowResWidth;
        m_activeHeight = LowResHeight;
        m_activeOriginX = BorderWidth;
        m_activeOriginY = BorderHeight;
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
            if (m_currentRasterLine == VisibleRasterLines)
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
                m_activeWidth = LowResWidth;
                m_activeHeight = LowResHeight;
                m_activeOriginX = BorderWidth;
                m_activeOriginY = BorderHeight;
                ClearToColor(m_paletteR[0], m_paletteG[0], m_paletteB[0]);
                RenderLowResolution(screenBaseAddress);
                break;

            case 1:
                m_activeWidth = MediumResWidth;
                m_activeHeight = MediumResHeight;
                m_activeOriginX = BorderWidth;
                m_activeOriginY = BorderHeight;
                ClearToColor(m_paletteR[0], m_paletteG[0], m_paletteB[0]);
                RenderMediumResolution(screenBaseAddress);
                break;

            case 2:
                m_activeWidth = HighResWidth;
                m_activeHeight = HighResHeight;
                m_activeOriginX = BorderWidth;
                m_activeOriginY = BorderHeight;
                ClearToColor(255, 255, 255);
                RenderHighResolutionMonochrome(screenBaseAddress);
                break;

            default:
                m_activeWidth = 0;
                m_activeHeight = 0;
                m_activeOriginX = 0;
                m_activeOriginY = 0;
                ClearToColor(0, 0, 0);
                break;
        }

        FrameRendered?.Invoke(this, m_frameBuffer);
    }

    private void RenderLowResolution(uint screenBaseAddress)
    {
        for (var y = 0; y < LowResHeight; y++)
        {
            var lineAddress = unchecked(screenBaseAddress + (uint)(y * LowResBytesPerLine));
            for (var chunk = 0; chunk < LowResWordsPerLine; chunk++)
            {
                var chunkAddress = unchecked(lineAddress + (uint)(chunk * 8));
                var plane0 = m_bus.Read16BigEndian(chunkAddress);
                var plane1 = m_bus.Read16BigEndian(chunkAddress + 2);
                var plane2 = m_bus.Read16BigEndian(chunkAddress + 4);
                var plane3 = m_bus.Read16BigEndian(chunkAddress + 6);
                for (var bit = 15; bit >= 0; bit--)
                {
                    var x = (chunk * 16) + (15 - bit);
                    var pixelIndex =
                        ((plane0 >> bit) & 1) |
                        (((plane1 >> bit) & 1) << 1) |
                        (((plane2 >> bit) & 1) << 2) |
                        (((plane3 >> bit) & 1) << 3);
                    WriteScaled2X2(x, y, m_paletteR[pixelIndex], m_paletteG[pixelIndex], m_paletteB[pixelIndex]);
                }
            }
        }
    }

    private void RenderMediumResolution(uint screenBaseAddress)
    {
        for (var y = 0; y < MediumResHeight; y++)
        {
            var lineAddress = unchecked(screenBaseAddress + (uint)(y * MediumResBytesPerLine));
            for (var chunk = 0; chunk < MediumResWordsPerLine; chunk++)
            {
                var chunkAddress = unchecked(lineAddress + (uint)(chunk * 4));
                var plane0 = m_bus.Read16BigEndian(chunkAddress);
                var plane1 = m_bus.Read16BigEndian(chunkAddress + 2);
                for (var bit = 15; bit >= 0; bit--)
                {
                    var x = m_activeOriginX + (chunk * 16) + (15 - bit);
                    var pixelIndex =
                        ((plane0 >> bit) & 1) |
                        (((plane1 >> bit) & 1) << 1);
                    WriteRgbAt(x, m_activeOriginY + (y * 2), m_paletteR[pixelIndex], m_paletteG[pixelIndex], m_paletteB[pixelIndex]);
                    WriteRgbAt(x, m_activeOriginY + (y * 2) + 1, m_paletteR[pixelIndex], m_paletteG[pixelIndex], m_paletteB[pixelIndex]);
                }
            }
        }
    }

    private void RenderHighResolutionMonochrome(uint screenBaseAddress)
    {
        for (var y = 0; y < HighResHeight; y++)
        {
            var lineAddress = unchecked(screenBaseAddress + (uint)(y * HighResBytesPerLine));
            for (var chunk = 0; chunk < HighResWordsPerLine; chunk++)
            {
                var word = m_bus.Read16BigEndian(unchecked(lineAddress + (uint)(chunk * 2)));
                for (var bit = 15; bit >= 0; bit--)
                {
                    var x = m_activeOriginX + (chunk * 16) + (15 - bit);
                    var on = ((word >> bit) & 1) != 0;
                    var value = on ? (byte)0 : (byte)255;
                    WriteRgbAt(x, m_activeOriginY + y, value, value, value);
                }
            }
        }
    }

    private void WriteScaled2X2(int sourceX, int sourceY, byte red, byte green, byte blue)
    {
        var outX = m_activeOriginX + (sourceX * 2);
        var outY = m_activeOriginY + (sourceY * 2);
        WriteRgbAt(outX, outY, red, green, blue);
        WriteRgbAt(outX + 1, outY, red, green, blue);
        WriteRgbAt(outX, outY + 1, red, green, blue);
        WriteRgbAt(outX + 1, outY + 1, red, green, blue);
    }

    private void WriteRgbAt(int x, int y, byte red, byte green, byte blue)
    {
        var outputIndex = ((y * OutputWidth) + x) * BytesPerPixel;
        m_frameBuffer[outputIndex] = red;
        m_frameBuffer[outputIndex + 1] = green;
        m_frameBuffer[outputIndex + 2] = blue;
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

    private void ClearToColor(byte red, byte green, byte blue)
    {
        for (var i = 0; i < m_frameBuffer.Length; i += BytesPerPixel)
        {
            m_frameBuffer[i] = red;
            m_frameBuffer[i + 1] = green;
            m_frameBuffer[i + 2] = blue;
        }
    }

    private static byte ScaleThreeBitToEightBit(int value) =>
        (byte)(value * 255 / 7);
}
