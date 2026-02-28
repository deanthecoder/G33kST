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
using DTC.Core.Image;
using DTC.Emulation.Snapshot;

namespace DTC.AtariST;

/// <summary>
/// Minimal Atari ST Shifter video source with basic low/medium/high mode rendering.
/// Output is exposed as RGB using mode-native active resolution plus configurable margins.
/// </summary>
public sealed class Shifter : IVideoSource
{
    private const int BytesPerPixel = 3;
    private const int BorderWidth = 32;
    private const int BorderHeight = 24;
    private const int LowResWidth = 320;
    private const int LowResHeight = 200;
    private const int LowResWordsPerLine = 20;
    private const int LowResBytesPerLine = LowResWordsPerLine * 8;
    private const int MediumResWidth = 640;
    private const int MediumResHeight = 200;
    private const int MediumResOutputHeight = MediumResHeight * 2;
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
    private const uint VideoCounterHighRegister = 0x00FF8205;
    private const uint VideoCounterMidRegister = 0x00FF8207;
    private const uint VideoBaseLowRegister = 0x00FF820D; // STE extension; ignored on plain ST.
    private const uint VideoModeRegister = 0x00FF8260;
    private const uint PaletteBaseRegister = 0x00FF8240;

    private readonly Bus m_bus;
    private double m_ticksPerLine;
    private readonly byte[] m_paletteR = new byte[16];
    private readonly byte[] m_paletteG = new byte[16];
    private readonly byte[] m_paletteB = new byte[16];
    private readonly FrameBuffer m_frameBuffer;
    private byte m_lastClearRed;
    private byte m_lastClearGreen;
    private byte m_lastClearBlue;
    private bool m_hasLastClearColor;
    private double m_lineTickAccumulator;
    private int m_currentRasterLine;
    private uint m_latchedScreenBaseAddress;

    /// <summary>
    /// Default frame width used at startup (low-resolution mode plus margins).
    /// </summary>
    public const int DefaultFrameWidth = LowResWidth + BorderWidth * 2;

    /// <summary>
    /// Default frame height used at startup (low-resolution mode plus margins).
    /// </summary>
    public const int DefaultFrameHeight = LowResHeight + BorderHeight * 2;

    /// <summary>
    /// Creates an Atari ST video source.
    /// </summary>
    public Shifter(Bus bus, double cpuHz, double videoHz)
    {
        m_bus = bus ?? throw new ArgumentNullException(nameof(bus));
        SetTiming(cpuHz, videoHz);
        m_frameBuffer = new FrameBuffer(DefaultFrameWidth, DefaultFrameHeight, BytesPerPixel);
        Reset();
    }

    /// <inheritdoc />
    public int FrameWidth => m_frameBuffer.Width;

    /// <inheritdoc />
    public int FrameHeight => m_frameBuffer.Height;

    /// <inheritdoc />
    public int FrameBytesPerPixel => BytesPerPixel;

    /// <summary>
    /// Gets the active picture width for the current mode.
    /// </summary>
    public int ActiveWidth { get; private set; } = LowResWidth;

    /// <summary>
    /// Gets the active picture height for the current mode.
    /// </summary>
    public int ActiveHeight { get; private set; } = LowResHeight;

    /// <summary>
    /// Gets the X offset where the active picture starts in the output framebuffer.
    /// </summary>
    public int ActiveOriginX { get; private set; } = BorderWidth;

    /// <summary>
    /// Gets the Y offset where the active picture starts in the output framebuffer.
    /// </summary>
    public int ActiveOriginY { get; private set; } = BorderHeight;

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
        ActiveWidth = LowResWidth;
        ActiveHeight = LowResHeight;
        ActiveOriginX = BorderWidth;
        ActiveOriginY = BorderHeight;
        m_hasLastClearColor = false;
        BeginFrame();
        if (GetVideoMode() == 0)
            RenderLowResolutionScanline(0);
        else
            RenderFrameForNonLowResolutionModes(GetVideoMode());
    }

    /// <summary>
    /// Updates raster timing so frame cadence follows the selected machine video region.
    /// </summary>
    /// <remarks>
    /// This adjusts line timing only; raster-line totals remain the current pragmatic model.
    /// </remarks>
    public void SetTiming(double cpuHz, double videoHz)
    {
        m_ticksPerLine = cpuHz / (videoHz * TotalRasterLines);
        if (m_ticksPerLine <= 0)
            throw new ArgumentOutOfRangeException(nameof(cpuHz), "Computed ticks-per-line must be positive.");
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
            {
                ConfigureGeometryForMode(mode);
                RenderLowResolutionScanline(m_currentRasterLine);
            }

            if (m_currentRasterLine == VisibleRasterLines)
            {
                if (mode != 0)
                    RenderFrameForNonLowResolutionModes(mode);
                FrameRendered?.Invoke(this, m_frameBuffer.Data);
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
        if (frameBuffer.Length < m_frameBuffer.ByteLength)
            throw new ArgumentException($"Frame buffer too small; expected at least {m_frameBuffer.ByteLength} bytes.", nameof(frameBuffer));

        m_frameBuffer.Data.CopyTo(frameBuffer);
    }

    internal byte? TryReadDynamicRegister(uint address)
    {
        if (address != VideoCounterHighRegister && address != VideoCounterMidRegister)
            return null;

        var currentVideoCounter = GetCurrentVideoAddressCounter();
        if (address == VideoCounterHighRegister)
            return (byte)((currentVideoCounter >> 16) & 0xFF);
        return (byte)((currentVideoCounter >> 8) & 0xFF);
    }

    private void BeginFrame()
    {
        var mode = m_bus.Read8(VideoModeRegister) & 0x03;
        m_latchedScreenBaseAddress = ReadScreenBaseAddress();
        ConfigureGeometryForMode(mode);
        switch (mode)
        {
            case 0:
                RefreshPalette();
                ClearToColorIfNeeded(m_paletteR[0], m_paletteG[0], m_paletteB[0]);
                break;

            case 1:
                RefreshPalette();
                ClearToColorIfNeeded(m_paletteR[0], m_paletteG[0], m_paletteB[0]);
                break;

            case 2:
                ClearToColorIfNeeded(255, 255, 255);
                break;

            default:
                ClearToColorIfNeeded(0, 0, 0);
                break;
        }
    }

    private int GetVideoMode() =>
        m_bus.Read8(VideoModeRegister) & 0x03;

    private void RenderFrameForNonLowResolutionModes(int mode)
    {
        ConfigureGeometryForMode(mode);
        switch (mode)
        {
            case 1:
                RefreshPalette();
                ClearToColorIfNeeded(m_paletteR[0], m_paletteG[0], m_paletteB[0]);
                RenderMediumResolutionFrame(m_latchedScreenBaseAddress);
                return;
            case 2:
                ClearToColorIfNeeded(255, 255, 255);
                RenderHighResolutionMonochromeFrame(m_latchedScreenBaseAddress);
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
        var screenBaseAddress = m_latchedScreenBaseAddress;
        var borderRed = m_paletteR[0];
        var borderGreen = m_paletteG[0];
        var borderBlue = m_paletteB[0];
        var outputY = ActiveOriginY + sourceLine;
        FillOutputRow(outputY, borderRed, borderGreen, borderBlue);

        var frameBuffer = m_frameBuffer.Data;
        var paletteR = m_paletteR;
        var paletteG = m_paletteG;
        var paletteB = m_paletteB;
        var outputStride = m_frameBuffer.Width * BytesPerPixel;
        var activeOriginXBytes = ActiveOriginX * BytesPerPixel;
        var lineAddress = unchecked(screenBaseAddress + (uint)(sourceLine * LowResBytesPerLine));
        var outputRowIndex = outputY * outputStride + activeOriginXBytes;
        for (var chunk = 0; chunk < LowResWordsPerLine; chunk++)
        {
            var chunkAddress = unchecked(lineAddress + (uint)(chunk * 8));
            var plane0 = m_bus.Read16BigEndian(chunkAddress);
            var plane1 = m_bus.Read16BigEndian(chunkAddress + 2);
            var plane2 = m_bus.Read16BigEndian(chunkAddress + 4);
            var plane3 = m_bus.Read16BigEndian(chunkAddress + 6);
            var outputIndex = outputRowIndex + chunk * 16 * BytesPerPixel;
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

                frameBuffer[outputIndex] = red;
                frameBuffer[outputIndex + 1] = green;
                frameBuffer[outputIndex + 2] = blue;

                outputIndex += BytesPerPixel;
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
        var topOutputY = ActiveOriginY + sourceLine * 2;
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
                var x = ActiveOriginX + chunk * 16 + (15 - bit);
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

        var outputTopY = ActiveOriginY + sourceTopLine;
        var outputBottomY = ActiveOriginY + sourceBottomLine;
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
                var x = ActiveOriginX + chunk * 16 + (15 - bit);
                var on = ((word >> bit) & 1) != 0;
                var value = on ? (byte)0 : (byte)255;
                WriteRgbAt(x, outputY, value, value, value);
            }
        }
    }

    private void FillOutputRow(int y, byte red, byte green, byte blue)
    {
        if (y < 0 || y >= m_frameBuffer.Height)
            return;

        var frameBuffer = m_frameBuffer.Data;
        var rowStart = y * m_frameBuffer.Width * BytesPerPixel;
        for (var x = 0; x < m_frameBuffer.Width; x++)
        {
            var pixelIndex = rowStart + x * BytesPerPixel;
            frameBuffer[pixelIndex] = red;
            frameBuffer[pixelIndex + 1] = green;
            frameBuffer[pixelIndex + 2] = blue;
        }
    }

    private void WriteRgbAt(int x, int y, byte red, byte green, byte blue)
    {
        var frameBuffer = m_frameBuffer.Data;
        var outputIndex = (y * m_frameBuffer.Width + x) * BytesPerPixel;
        frameBuffer[outputIndex] = red;
        frameBuffer[outputIndex + 1] = green;
        frameBuffer[outputIndex + 2] = blue;
    }

    private uint ReadScreenBaseAddress()
    {
        var high = m_bus.Read8(VideoBaseHighRegister);
        var mid = m_bus.Read8(VideoBaseMidRegister);
        _ = m_bus.Read8(VideoBaseLowRegister);

        // $FF820D is an STE-only extension. For the current STF/STFM target, the screen
        // base uses only the high and middle bytes and remains 256-byte aligned.
        var address = (uint)((high << 16) | (mid << 8));
        return address & 0x00FF_FF00;
    }

    private uint GetCurrentVideoAddressCounter()
    {
        var mode = GetVideoMode();
        var bytesPerVisibleLine = mode switch
        {
            0 => LowResBytesPerLine,
            1 => MediumResBytesPerLine,
            2 => HighResBytesPerLine,
            _ => 0
        };

        if (bytesPerVisibleLine <= 0)
            return m_latchedScreenBaseAddress & 0x00FF_FFFF;

        var completedVisibleLines = Math.Clamp(m_currentRasterLine, 0, VisibleRasterLines);
        var currentAddress = unchecked(m_latchedScreenBaseAddress + (uint)(completedVisibleLines * bytesPerVisibleLine));

        if (m_currentRasterLine >= 0 && m_currentRasterLine < VisibleRasterLines && m_ticksPerLine > 0)
        {
            var lineProgress = Math.Clamp(m_lineTickAccumulator / m_ticksPerLine, 0.0, 0.999999);
            var bytesIntoLine = (int)Math.Floor(lineProgress * bytesPerVisibleLine);
            currentAddress = unchecked(currentAddress + (uint)bytesIntoLine);
        }

        return currentAddress & 0x00FF_FFFF;
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
        var frameBuffer = m_frameBuffer.Data;
        for (var i = 0; i < frameBuffer.Length; i += BytesPerPixel)
        {
            frameBuffer[i] = red;
            frameBuffer[i + 1] = green;
            frameBuffer[i + 2] = blue;
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

    private void ConfigureGeometryForMode(int mode)
    {
        var (activeWidth, activeHeight, originX, originY) = mode switch
        {
            0 => (LowResWidth, LowResHeight, BorderWidth, BorderHeight),
            1 => (MediumResWidth, MediumResOutputHeight, BorderWidth, BorderHeight),
            2 => (HighResWidth, HighResHeight, BorderWidth, BorderHeight),
            _ => (0, 0, 0, 0)
        };

        var frameWidth = Math.Max(1, activeWidth + originX * 2);
        var frameHeight = Math.Max(1, activeHeight + originY * 2);
        var geometryChanged =
            ActiveWidth != activeWidth ||
            ActiveHeight != activeHeight ||
            ActiveOriginX != originX ||
            ActiveOriginY != originY ||
            m_frameBuffer.Width != frameWidth ||
            m_frameBuffer.Height != frameHeight;

        ActiveWidth = activeWidth;
        ActiveHeight = activeHeight;
        ActiveOriginX = originX;
        ActiveOriginY = originY;
        m_frameBuffer.Resize(frameWidth, frameHeight, BytesPerPixel);

        if (geometryChanged)
        {
            // New frame storage must be cleared even if the color itself is unchanged.
            m_hasLastClearColor = false;
        }
    }

    private static byte ScaleThreeBitToEightBit(int value) =>
        (byte)(value * 255 / 7);

    internal int GetStateSize() =>
        sizeof(double) * 2 +
        m_paletteR.Length + m_paletteG.Length + m_paletteB.Length +
        3 + // last clear rgb
        1 + // has last clear color
        sizeof(int) * 5 + // raster line + geometry
        m_frameBuffer.ByteLength;

    internal void SaveState(ref StateWriter writer)
    {
        writer.WriteDouble(m_ticksPerLine);
        writer.WriteDouble(m_lineTickAccumulator);
        writer.WriteBytes(m_paletteR);
        writer.WriteBytes(m_paletteG);
        writer.WriteBytes(m_paletteB);
        writer.WriteByte(m_lastClearRed);
        writer.WriteByte(m_lastClearGreen);
        writer.WriteByte(m_lastClearBlue);
        writer.WriteBool(m_hasLastClearColor);
        writer.WriteInt32(m_currentRasterLine);
        writer.WriteInt32(ActiveWidth);
        writer.WriteInt32(ActiveHeight);
        writer.WriteInt32(ActiveOriginX);
        writer.WriteInt32(ActiveOriginY);
        writer.WriteBytes(m_frameBuffer.Data);
    }

    internal void LoadState(ref StateReader reader)
    {
        m_ticksPerLine = reader.ReadDouble();
        m_lineTickAccumulator = reader.ReadDouble();
        reader.ReadBytes(m_paletteR);
        reader.ReadBytes(m_paletteG);
        reader.ReadBytes(m_paletteB);
        m_lastClearRed = reader.ReadByte();
        m_lastClearGreen = reader.ReadByte();
        m_lastClearBlue = reader.ReadByte();
        m_hasLastClearColor = reader.ReadBool();
        m_currentRasterLine = reader.ReadInt32();
        var activeWidth = reader.ReadInt32();
        var activeHeight = reader.ReadInt32();
        var activeOriginX = reader.ReadInt32();
        var activeOriginY = reader.ReadInt32();
        var frameWidth = Math.Max(1, activeWidth + activeOriginX * 2);
        var frameHeight = Math.Max(1, activeHeight + activeOriginY * 2);
        m_frameBuffer.Resize(frameWidth, frameHeight, BytesPerPixel);
        ActiveWidth = activeWidth;
        ActiveHeight = activeHeight;
        ActiveOriginX = activeOriginX;
        ActiveOriginY = activeOriginY;
        reader.ReadBytes(m_frameBuffer.Data);
        m_latchedScreenBaseAddress = ReadScreenBaseAddress();
    }
}
