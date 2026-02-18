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
    private const int OutputWidth = ActiveOutputWidth + BorderWidth * 2;
    private const int OutputHeight = ActiveOutputHeight + BorderHeight * 2;
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
    private byte m_lastClearRed;
    private byte m_lastClearGreen;
    private byte m_lastClearBlue;
    private bool m_hasLastClearColor;
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
        Reset();
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
        m_hasLastClearColor = false;
        BeginFrame();
        if (GetVideoMode() == 0)
            RenderLowResolutionScanline(0);
        else
            RenderFrameForNonLowResolutionModes(GetVideoMode());
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
            var mode = GetVideoMode();
            if (mode == 0 && m_currentRasterLine < VisibleRasterLines)
                RenderLowResolutionScanline(m_currentRasterLine);

            if (m_currentRasterLine == VisibleRasterLines)
            {
                if (mode != 0)
                    RenderFrameForNonLowResolutionModes(mode);
                FrameRendered?.Invoke(this, m_frameBuffer);
                vblankCallback?.Invoke();
            }

            if (m_currentRasterLine >= TotalRasterLines)
            {
                m_currentRasterLine = 0;
                BeginFrame();
                if (GetVideoMode() == 0)
                    RenderLowResolutionScanline(0);
            }
        }
    }

    /// <inheritdoc />
    public void CopyToFrameBuffer(Span<byte> frameBuffer)
    {
        if (frameBuffer.Length < m_frameBuffer.Length)
            throw new ArgumentException($"Frame buffer too small; expected at least {m_frameBuffer.Length} bytes.", nameof(frameBuffer));

        m_frameBuffer.CopyTo(frameBuffer);
    }

    private void BeginFrame()
    {
        var mode = m_bus.Read8(VideoModeRegister) & 0x03;
        switch (mode)
        {
            case 0:
                m_activeWidth = LowResWidth;
                m_activeHeight = LowResHeight;
                m_activeOriginX = BorderWidth;
                m_activeOriginY = BorderHeight;
                RefreshPalette();
                ClearToColorIfNeeded(m_paletteR[0], m_paletteG[0], m_paletteB[0]);
                break;

            case 1:
                m_activeWidth = MediumResWidth;
                m_activeHeight = MediumResHeight;
                m_activeOriginX = BorderWidth;
                m_activeOriginY = BorderHeight;
                RefreshPalette();
                ClearToColorIfNeeded(m_paletteR[0], m_paletteG[0], m_paletteB[0]);
                break;

            case 2:
                m_activeWidth = HighResWidth;
                m_activeHeight = HighResHeight;
                m_activeOriginX = BorderWidth;
                m_activeOriginY = BorderHeight;
                ClearToColorIfNeeded(255, 255, 255);
                break;

            default:
                m_activeWidth = 0;
                m_activeHeight = 0;
                m_activeOriginX = 0;
                m_activeOriginY = 0;
                ClearToColorIfNeeded(0, 0, 0);
                break;
        }
    }

    private int GetVideoMode() =>
        m_bus.Read8(VideoModeRegister) & 0x03;

    private void RenderFrameForNonLowResolutionModes(int mode)
    {
        switch (mode)
        {
            case 1:
                RefreshPalette();
                ClearToColorIfNeeded(m_paletteR[0], m_paletteG[0], m_paletteB[0]);
                RenderMediumResolutionFrame(ReadScreenBaseAddress());
                return;
            case 2:
                ClearToColorIfNeeded(255, 255, 255);
                RenderHighResolutionMonochromeFrame(ReadScreenBaseAddress());
                return;
            default:
                return;
        }
    }

    private void RenderLowResolutionScanline(int sourceLine)
    {
        if (sourceLine < 0 || sourceLine >= LowResHeight)
            return;

        RefreshPalette();
        var screenBaseAddress = ReadScreenBaseAddress();
        var borderRed = m_paletteR[0];
        var borderGreen = m_paletteG[0];
        var borderBlue = m_paletteB[0];
        var topOutputY = m_activeOriginY + sourceLine * 2;
        var bottomOutputY = topOutputY + 1;
        FillOutputRow(topOutputY, borderRed, borderGreen, borderBlue);
        FillOutputRow(bottomOutputY, borderRed, borderGreen, borderBlue);

        var frameBuffer = m_frameBuffer;
        var paletteR = m_paletteR;
        var paletteG = m_paletteG;
        var paletteB = m_paletteB;
        const int outputStride = OutputWidth * BytesPerPixel;
        var activeOriginXBytes = m_activeOriginX * BytesPerPixel;
        var lineAddress = unchecked(screenBaseAddress + (uint)(sourceLine * LowResBytesPerLine));
        var outputTopRowIndex = topOutputY * outputStride + activeOriginXBytes;
        var outputBottomRowIndex = bottomOutputY * outputStride + activeOriginXBytes;
        for (var chunk = 0; chunk < LowResWordsPerLine; chunk++)
        {
            var chunkAddress = unchecked(lineAddress + (uint)(chunk * 8));
            var plane0 = m_bus.Read16BigEndian(chunkAddress);
            var plane1 = m_bus.Read16BigEndian(chunkAddress + 2);
            var plane2 = m_bus.Read16BigEndian(chunkAddress + 4);
            var plane3 = m_bus.Read16BigEndian(chunkAddress + 6);
            var topIndex = outputTopRowIndex + chunk * 16 * 2 * BytesPerPixel;
            var bottomIndex = outputBottomRowIndex + chunk * 16 * 2 * BytesPerPixel;
            for (var bit = 15; bit >= 0; bit--)
            {
                var pixelIndex =
                    ((plane0 >> bit) & 1) |
                    (((plane1 >> bit) & 1) << 1) |
                    (((plane2 >> bit) & 1) << 2) |
                    (((plane3 >> bit) & 1) << 3);
                var red = paletteR[pixelIndex];
                var green = paletteG[pixelIndex];
                var blue = paletteB[pixelIndex];

                frameBuffer[topIndex] = red;
                frameBuffer[topIndex + 1] = green;
                frameBuffer[topIndex + 2] = blue;
                frameBuffer[topIndex + 3] = red;
                frameBuffer[topIndex + 4] = green;
                frameBuffer[topIndex + 5] = blue;

                frameBuffer[bottomIndex] = red;
                frameBuffer[bottomIndex + 1] = green;
                frameBuffer[bottomIndex + 2] = blue;
                frameBuffer[bottomIndex + 3] = red;
                frameBuffer[bottomIndex + 4] = green;
                frameBuffer[bottomIndex + 5] = blue;

                topIndex += 2 * BytesPerPixel;
                bottomIndex += 2 * BytesPerPixel;
            }
        }
    }

    private void RenderMediumResolutionFrame(uint screenBaseAddress)
    {
        for (var sourceLine = 0; sourceLine < MediumResHeight; sourceLine++)
            RenderMediumResolutionLine(screenBaseAddress, sourceLine);
    }

    private void RenderMediumResolutionLine(uint screenBaseAddress, int sourceLine)
    {
        if (sourceLine < 0 || sourceLine >= MediumResHeight)
            return;

        var borderRed = m_paletteR[0];
        var borderGreen = m_paletteG[0];
        var borderBlue = m_paletteB[0];
        var topOutputY = m_activeOriginY + sourceLine * 2;
        var bottomOutputY = topOutputY + 1;
        FillOutputRow(topOutputY, borderRed, borderGreen, borderBlue);
        FillOutputRow(bottomOutputY, borderRed, borderGreen, borderBlue);

        var lineAddress = unchecked(screenBaseAddress + (uint)(sourceLine * MediumResBytesPerLine));
        for (var chunk = 0; chunk < MediumResWordsPerLine; chunk++)
        {
            var chunkAddress = unchecked(lineAddress + (uint)(chunk * 4));
            var plane0 = m_bus.Read16BigEndian(chunkAddress);
            var plane1 = m_bus.Read16BigEndian(chunkAddress + 2);
            for (var bit = 15; bit >= 0; bit--)
            {
                var x = m_activeOriginX + chunk * 16 + (15 - bit);
                var pixelIndex =
                    ((plane0 >> bit) & 1) |
                    (((plane1 >> bit) & 1) << 1);
                WriteRgbAt(x, topOutputY, m_paletteR[pixelIndex], m_paletteG[pixelIndex], m_paletteB[pixelIndex]);
                WriteRgbAt(x, bottomOutputY, m_paletteR[pixelIndex], m_paletteG[pixelIndex], m_paletteB[pixelIndex]);
            }
        }
    }

    private void RenderHighResolutionMonochromeFrame(uint screenBaseAddress)
    {
        for (var rasterLine = 0; rasterLine < VisibleRasterLines; rasterLine++)
            RenderHighResolutionMonochromePair(screenBaseAddress, rasterLine);
    }

    private void RenderHighResolutionMonochromePair(uint screenBaseAddress, int rasterLine)
    {
        var sourceTopLine = rasterLine * 2;
        var sourceBottomLine = sourceTopLine + 1;
        if (sourceTopLine >= HighResHeight)
            return;

        var outputTopY = m_activeOriginY + sourceTopLine;
        var outputBottomY = m_activeOriginY + sourceBottomLine;
        FillOutputRow(outputTopY, 255, 255, 255);
        if (sourceBottomLine < HighResHeight)
            FillOutputRow(outputBottomY, 255, 255, 255);

        RenderHighResolutionMonochromeLine(screenBaseAddress, sourceTopLine, outputTopY);
        if (sourceBottomLine < HighResHeight)
            RenderHighResolutionMonochromeLine(screenBaseAddress, sourceBottomLine, outputBottomY);
    }

    private void RenderHighResolutionMonochromeLine(uint screenBaseAddress, int sourceLine, int outputY)
    {
        var lineAddress = unchecked(screenBaseAddress + (uint)(sourceLine * HighResBytesPerLine));
        for (var chunk = 0; chunk < HighResWordsPerLine; chunk++)
        {
            var word = m_bus.Read16BigEndian(unchecked(lineAddress + (uint)(chunk * 2)));
            for (var bit = 15; bit >= 0; bit--)
            {
                var x = m_activeOriginX + chunk * 16 + (15 - bit);
                var on = ((word >> bit) & 1) != 0;
                var value = on ? (byte)0 : (byte)255;
                WriteRgbAt(x, outputY, value, value, value);
            }
        }
    }

    private void FillOutputRow(int y, byte red, byte green, byte blue)
    {
        if (y < 0 || y >= OutputHeight)
            return;

        var rowStart = y * OutputWidth * BytesPerPixel;
        for (var x = 0; x < OutputWidth; x++)
        {
            var pixelIndex = rowStart + x * BytesPerPixel;
            m_frameBuffer[pixelIndex] = red;
            m_frameBuffer[pixelIndex + 1] = green;
            m_frameBuffer[pixelIndex + 2] = blue;
        }
    }

    private void WriteRgbAt(int x, int y, byte red, byte green, byte blue)
    {
        var outputIndex = (y * OutputWidth + x) * BytesPerPixel;
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

    private void ClearToColorIfNeeded(byte red, byte green, byte blue)
    {
        if (m_hasLastClearColor &&
            m_lastClearRed == red &&
            m_lastClearGreen == green &&
            m_lastClearBlue == blue)
            return;

        ClearToColor(red, green, blue);
        m_lastClearRed = red;
        m_lastClearGreen = green;
        m_lastClearBlue = blue;
        m_hasLastClearColor = true;
    }

    private static byte ScaleThreeBitToEightBit(int value) =>
        (byte)(value * 255 / 7);
}
