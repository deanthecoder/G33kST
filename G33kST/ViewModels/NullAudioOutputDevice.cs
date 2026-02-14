// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation.Audio;

namespace G33kST.ViewModels;

/// <summary>
/// Minimal audio output implementation used by the UI shell until PSG audio is wired.
/// </summary>
public sealed class NullAudioOutputDevice : IAudioOutputDevice
{
    public NullAudioOutputDevice(int sampleRateHz)
    {
        if (sampleRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));

        SampleRateHz = sampleRateHz;
    }

    public int SampleRateHz { get; }

    public void Start()
    {
    }

    public void SetEnabled(bool isSoundEnabled)
    {
    }

    public void SetLowPassFilterEnabled(bool isEnabled)
    {
    }

    public void SetCaptureSink(IAudioSampleSink value)
    {
    }

    public void FlushCapture()
    {
    }

    public void Dispose()
    {
    }
}
