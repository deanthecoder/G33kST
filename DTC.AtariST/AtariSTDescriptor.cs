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
/// Provides machine characteristics for the Atari ST 1040 (NTSC).
/// </summary>
public sealed class AtariSTDescriptor : IMachineDescriptor
{
    // NTSC Atari ST runs at 8MHz (PAL runs at ~7.99MHz)
    private const double NtscCpuHz = 8_000_000.0;

    // 60Hz NTSC vertical refresh
    private const double NtscVideoHz = 60.0;

    // Standard audio sample rate
    private const int AudioSampleRate = 44100;

    // Expose a fixed output surface including a representative border area.
    private const int OutputWidth = 704;
    private const int OutputHeight = 448;

    public string Name => "Atari ST 1040 STFM";

    public double CpuHz => NtscCpuHz;

    public double VideoHz => NtscVideoHz;

    public int AudioSampleRateHz => AudioSampleRate;

    public int FrameWidth => OutputWidth;

    public int FrameHeight => OutputHeight;
}
