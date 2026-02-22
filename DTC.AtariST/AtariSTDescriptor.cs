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
/// Provides machine characteristics for the Atari ST 1040 STFM.
/// </summary>
public sealed class AtariSTDescriptor : IMachineDescriptor
{
    // Kept at 8 MHz for now. PAL/NTSC selection currently focuses on vertical timing.
    private const double CpuClockHz = 8_000_000.0;

    // 60Hz NTSC vertical refresh
    private const double NtscVideoHz = 60.0;
    private const double PalVideoHz = 50.0;

    // Standard audio sample rate
    private const int AudioSampleRate = 44100;

    private AtariVideoRegion m_videoRegion;

    public AtariSTDescriptor()
        : this(AtariVideoRegion.Ntsc)
    {
    }

    public AtariSTDescriptor(AtariVideoRegion videoRegion)
    {
        m_videoRegion = videoRegion;
    }

    public string Name => "Atari ST 1040 STFM";

    public double CpuHz => CpuClockHz;

    public double VideoHz => m_videoRegion == AtariVideoRegion.Pal ? PalVideoHz : NtscVideoHz;

    public int AudioSampleRateHz => AudioSampleRate;

    public int FrameWidth => Shifter.DefaultFrameWidth;

    public int FrameHeight => Shifter.DefaultFrameHeight;

    /// <summary>
    /// Updates the video timing region used by the descriptor.
    /// </summary>
    public void SetVideoRegion(AtariVideoRegion videoRegion) =>
        m_videoRegion = videoRegion;
}
