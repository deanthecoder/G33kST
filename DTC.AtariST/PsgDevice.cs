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
using DTC.Emulation.Snapshot;

namespace DTC.AtariST;

/// <summary>
/// Emulates the Atari ST Programmable Sound Generator (YM2149-compatible).
/// Exposes the PSG register window at $FF8800-$FF8803 and produces mixed mono
/// audio samples (duplicated to L/R by the caller).
/// </summary>
public sealed class PsgDevice : IMemDevice, IAudioSource
{
    private const uint BaseAddress = 0x00FF8800;
    private const uint MirroredWindowSize = 0x0100;
    private const int RegisterCount = 16;
    private const int VoiceCount = 3;
    private const int EnvelopeLength = 32 * 3;
    private const int DefaultCpuClockHz = 8_000_000;
    private const int DefaultSampleRateHz = 44_100;
    private const byte MixerRegister = 0x07;
    private const byte VolumeARegister = 0x08;
    private const byte EnvelopeFineRegister = 0x0B;
    private const byte EnvelopeCoarseRegister = 0x0C;
    private const byte EnvelopeShapeRegister = 0x0D;
    private const byte PortARegister = 0x0E;
    private const uint NoiseShiftRegisterSeed = 0x1FFFF;

    // Maps YM fixed 4-bit volume values to the 5-bit internal DAC step index.
    private static readonly byte[] Volume4To5 =
        [0, 1, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31];

    // Relative YM2149 DAC curve for 32 output steps (0..31), scaled to 0..65535.
    private static readonly ushort[] Dac5BitLevel =
    [
        0, 369, 438, 521, 619, 735, 874, 1039,
        1234, 1467, 1744, 2072, 2463, 2927, 3479, 4135,
        4914, 5841, 6942, 8250, 9806, 11654, 13851, 16462,
        19565, 23253, 27636, 32845, 39037, 46395, 55141, 65535
    ];

    // Prebuilt 16 envelope shapes, each expanded to three 32-step blocks.
    private static readonly byte[,] EnvelopeWaves = BuildEnvelopeWaves();
    private readonly byte[] m_registers = new byte[RegisterCount];
    private readonly bool[] m_channelEnabled = [true, true, true];
    private readonly bool[] m_toneOutputHigh = [true, true, true];
    private readonly double[] m_toneCounterTicks = new double[VoiceCount];

    // Fraction of the current output sample where each tone channel is logically "high".
    // Used to smooth single-edge transitions and reduce aliasing at high frequencies.
    private readonly double[] m_toneHighFraction = [1.0, 1.0, 1.0];
    private readonly Action<double, double> m_sampleSink;
    private readonly int m_sampleRateHz;
    private readonly double m_ticksPerSample;
    private double m_ticksUntilNextSample;
    private double m_noiseCounterTicks;
    private uint m_noiseShiftRegister;
    private double m_envelopeCounterTicks;
    private int m_envelopePosition;
    private byte m_selectedRegister;

    public PsgDevice()
        : this(null, DefaultCpuClockHz, DefaultSampleRateHz)
    {
    }

    /// <summary>
    /// Creates a PSG device connected to an optional audio sink.
    /// </summary>
    /// <param name="sampleSink">Receives generated samples as left/right pairs. Can be null for register-only use.</param>
    /// <param name="cpuClockHz">CPU clock driving PSG timing.</param>
    /// <param name="sampleRateHz">Audio output sample rate.</param>
    public PsgDevice(Action<double, double> sampleSink, int cpuClockHz, int sampleRateHz)
    {
        if (cpuClockHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(cpuClockHz));
        if (sampleRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));

        m_sampleSink = sampleSink;
        var cpuClockHz1 = cpuClockHz;
        m_sampleRateHz = sampleRateHz;
        m_ticksPerSample = (double)cpuClockHz1 / m_sampleRateHz;
        Reset();
    }

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + MirroredWindowSize - 1;

    int IAudioSource.ChannelCount => VoiceCount;

    int IAudioSource.SampleRateHz => m_sampleRateHz;

    /// <summary>
    /// Raised when PSG port-A output value changes.
    /// </summary>
    public event Action<byte> PortAChanged;

    /// <summary>
    /// Resets PSG registers and synthesis state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(m_registers, 0, m_registers.Length);
        Array.Clear(m_toneCounterTicks, 0, m_toneCounterTicks.Length);
        for (var i = 0; i < m_toneOutputHigh.Length; i++)
            m_toneOutputHigh[i] = true;
        for (var i = 0; i < m_toneHighFraction.Length; i++)
            m_toneHighFraction[i] = 1.0;
        m_selectedRegister = 0;
        m_registers[PortARegister] = 0x07;
        m_noiseShiftRegister = NoiseShiftRegisterSeed;
        m_noiseCounterTicks = 0;
        m_envelopeCounterTicks = 0;
        m_envelopePosition = 0;
        m_ticksUntilNextSample = m_ticksPerSample;
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        if (address < BaseAddress || address > ToAddr)
            return 0xFF;

        // YM2149 decoding on ST effectively mirrors every 4 bytes across FF8800-FF88FF.
        // Many real programs use MOVEP forms that target odd/shadow addresses.
        var offset = (int)((address - BaseAddress) & 0x03);
        if (offset is 0 or 2)
            return m_registers[m_selectedRegister];
        return 0xFF;
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        if (address < BaseAddress || address > ToAddr)
            return;

        var offset = (int)((address - BaseAddress) & 0x03);
        if (offset is 0 or 1)
        {
            m_selectedRegister = (byte)(value & 0x0F);
            return;
        }
        if (offset is not (2 or 3))
            return;

        WriteRegister(m_selectedRegister, value);
    }

    /// <summary>
    /// Advances audio generation by CPU clock ticks.
    /// </summary>
    public void AdvanceCycles(long cpuTicks)
    {
        if (cpuTicks <= 0 || m_sampleSink == null)
            return;

        m_ticksUntilNextSample -= cpuTicks;
        while (m_ticksUntilNextSample <= 0.0)
        {
            GenerateSample();
            m_ticksUntilNextSample += m_ticksPerSample;
        }
    }

    /// <inheritdoc />
    public void SetChannelEnabled(int channel, bool isEnabled)
    {
        if (channel is < 1 or > VoiceCount)
            return;
        m_channelEnabled[channel - 1] = isEnabled;
    }

    private static byte[,] BuildEnvelopeWaves()
    {
        const byte goDown = 0;
        const byte goUp = 1;
        const byte down = 2;
        const byte up = 3;
        var shapeBlocks = new[,]
        {
            { goDown, down, down }, { goDown, down, down }, { goDown, down, down }, { goDown, down, down },
            { goUp, down, down }, { goUp, down, down }, { goUp, down, down }, { goUp, down, down },
            { goDown, goDown, goDown }, { goDown, down, down }, { goDown, goUp, goDown }, { goDown, up, up },
            { goUp, goUp, goUp }, { goUp, up, up }, { goUp, goDown, goUp }, { goUp, down, down }
        };

        var waves = new byte[16, EnvelopeLength];
        for (var shape = 0; shape < 16; shape++)
        {
            for (var block = 0; block < 3; block++)
            {
                for (var i = 0; i < 32; i++)
                {
                    var value = shapeBlocks[shape, block] switch
                    {
                        0 => 31 - i,
                        1 => i,
                        2 => 0,
                        _ => 31
                    };
                    waves[shape, block * 32 + i] = (byte)value;
                }
            }
        }

        return waves;
    }

    private void WriteRegister(byte register, byte value)
    {
        var maskedValue = MaskRegisterValue(register, value);
        var previous = m_registers[register];
        m_registers[register] = maskedValue;

        // On YM2149, any write to shape register retriggers the envelope generator,
        // even when the value is unchanged.
        if (register == EnvelopeShapeRegister)
        {
            m_envelopePosition = 0;
            m_envelopeCounterTicks = 0;
        }

        if (register == PortARegister && previous != maskedValue)
            PortAChanged?.Invoke(maskedValue);
    }

    private static byte MaskRegisterValue(byte register, byte value) =>
        register switch
        {
            0x01 or 0x03 or 0x05 => (byte)(value & 0x0F),
            0x06 => (byte)(value & 0x1F),
            0x08 or 0x09 or 0x0A => (byte)(value & 0x1F),
            0x0D => (byte)(value & 0x0F),
            _ => value
        };

    private void GenerateSample()
    {
        var sampleTicks = m_ticksPerSample;
        for (var channel = 0; channel < VoiceCount; channel++)
            StepTone(channel, sampleTicks);
        StepNoise(sampleTicks);
        StepEnvelope(sampleTicks);

        var sample = MixVoices();
        m_sampleSink(sample, sample);
    }

    private void StepTone(int channel, double sampleTicks)
    {
        var halfPeriodTicks = GetToneHalfPeriodTicks(channel);
        if (halfPeriodTicks <= sampleTicks)
        {
            // Above Nyquist: treat rapid toggling as a DC average to avoid harsh aliasing bursts.
            m_toneHighFraction[channel] = 0.5;
            return;
        }

        var previousHigh = m_toneOutputHigh[channel];
        var previousCounterTicks = m_toneCounterTicks[channel];
        m_toneCounterTicks[channel] += sampleTicks;
        var transitionCount = 0;
        var firstTransitionTicks = 0.0;
        while (m_toneCounterTicks[channel] >= halfPeriodTicks)
        {
            if (transitionCount == 0)
                firstTransitionTicks = halfPeriodTicks - previousCounterTicks;
            m_toneCounterTicks[channel] -= halfPeriodTicks;
            m_toneOutputHigh[channel] = !m_toneOutputHigh[channel];
            transitionCount++;
        }

        if (transitionCount == 0)
        {
            m_toneHighFraction[channel] = previousHigh ? 1.0 : 0.0;
            return;
        }

        if (transitionCount > 1)
        {
            // Multiple edges in one sample are folded to their average duty cycle.
            m_toneHighFraction[channel] = 0.5;
            return;
        }

        var transitionPosition = Math.Clamp(firstTransitionTicks / sampleTicks, 0.0, 1.0);
        m_toneHighFraction[channel] = previousHigh
            ? transitionPosition
            : 1.0 - transitionPosition;
    }

    private void StepNoise(double sampleTicks)
    {
        var noiseStepTicks = GetNoiseStepTicks();
        m_noiseCounterTicks += sampleTicks;
        while (m_noiseCounterTicks >= noiseStepTicks)
        {
            m_noiseCounterTicks -= noiseStepTicks;
            StepNoiseShiftRegister();
        }
    }

    private void StepEnvelope(double sampleTicks)
    {
        var envelopeStepTicks = GetEnvelopeStepTicks();
        m_envelopeCounterTicks += sampleTicks;
        while (m_envelopeCounterTicks >= envelopeStepTicks)
        {
            m_envelopeCounterTicks -= envelopeStepTicks;
            m_envelopePosition++;
            if (m_envelopePosition >= EnvelopeLength)
                m_envelopePosition -= 64;
        }
    }

    private double MixVoices()
    {
        var mixed = 0.0;
        var mixer = m_registers[MixerRegister];
        var noiseHigh = (m_noiseShiftRegister & 1) != 0;
        for (var channel = 0; channel < VoiceCount; channel++)
        {
            if (!m_channelEnabled[channel])
                continue;

            var toneDisabled = (mixer & (1 << channel)) != 0;
            var noiseDisabled = (mixer & (1 << (channel + 3))) != 0;
            var toneHighFraction = toneDisabled ? 1.0 : m_toneHighFraction[channel];
            var noisePasses = noiseDisabled || noiseHigh;
            var voiceHighFraction = noisePasses ? toneHighFraction : 0.0;
            var level = ResolveVoiceLevel(channel);
            mixed += voiceHighFraction * level;
        }

        return Math.Clamp(mixed / VoiceCount, 0.0, 1.0);
    }

    private double ResolveVoiceLevel(int channel)
    {
        var volumeRegister = m_registers[VolumeARegister + channel];
        var level5Bit = (volumeRegister & 0x10) != 0
            ? EnvelopeWaves[m_registers[EnvelopeShapeRegister] & 0x0F, m_envelopePosition]
            : Volume4To5[volumeRegister & 0x0F];
        return Dac5BitLevel[level5Bit] / 65535.0;
    }

    private int GetTonePeriod(int channel)
    {
        var low = m_registers[channel * 2];
        var high = m_registers[channel * 2 + 1] & 0x0F;
        var period = (high << 8) | low;
        return period <= 0 ? 1 : period;
    }

    private double GetToneHalfPeriodTicks(int channel) =>
        32.0 * GetTonePeriod(channel);

    private int GetNoisePeriod()
    {
        var period = m_registers[0x06] & 0x1F;
        return period == 0 ? 1 : period;
    }

    private double GetNoiseStepTicks() =>
        // YM2149 noise counter advances at half the master 250 kHz tick used by tone/envelope logic.
        64.0 * GetNoisePeriod();

    private int GetEnvelopePeriod()
    {
        var period = (m_registers[EnvelopeCoarseRegister] << 8) | m_registers[EnvelopeFineRegister];
        return period <= 0 ? 1 : period;
    }

    private double GetEnvelopeStepTicks() =>
        // YM envelope counter advances at the same 250 kHz base tick as tone counters.
        // With an 8 MHz CPU clock, that is one YM tick every 32 CPU cycles.
        32.0 * GetEnvelopePeriod();

    private void StepNoiseShiftRegister()
    {
        var feedback = (m_noiseShiftRegister ^ (m_noiseShiftRegister >> 3)) & 1;
        m_noiseShiftRegister = (m_noiseShiftRegister >> 1) | (feedback << 16);
        if (m_noiseShiftRegister == 0)
            m_noiseShiftRegister = NoiseShiftRegisterSeed;
    }

    internal int GetStateSize() =>
        m_registers.Length +
        m_channelEnabled.Length +
        m_toneOutputHigh.Length +
        sizeof(double) * (m_toneCounterTicks.Length + m_toneHighFraction.Length) +
        sizeof(double) * 3 +
        sizeof(uint) +
        sizeof(int) +
        sizeof(byte);

    internal void SaveState(ref StateWriter writer)
    {
        writer.WriteBytes(m_registers);
        for (var i = 0; i < m_channelEnabled.Length; i++)
            writer.WriteBool(m_channelEnabled[i]);
        for (var i = 0; i < m_toneOutputHigh.Length; i++)
            writer.WriteBool(m_toneOutputHigh[i]);
        for (var i = 0; i < m_toneCounterTicks.Length; i++)
            writer.WriteDouble(m_toneCounterTicks[i]);
        for (var i = 0; i < m_toneHighFraction.Length; i++)
            writer.WriteDouble(m_toneHighFraction[i]);
        writer.WriteDouble(m_ticksUntilNextSample);
        writer.WriteDouble(m_noiseCounterTicks);
        writer.WriteUInt32(m_noiseShiftRegister);
        writer.WriteDouble(m_envelopeCounterTicks);
        writer.WriteInt32(m_envelopePosition);
        writer.WriteByte(m_selectedRegister);
    }

    internal void LoadState(ref StateReader reader)
    {
        reader.ReadBytes(m_registers);
        for (var i = 0; i < m_channelEnabled.Length; i++)
            m_channelEnabled[i] = reader.ReadBool();
        for (var i = 0; i < m_toneOutputHigh.Length; i++)
            m_toneOutputHigh[i] = reader.ReadBool();
        for (var i = 0; i < m_toneCounterTicks.Length; i++)
            m_toneCounterTicks[i] = reader.ReadDouble();
        for (var i = 0; i < m_toneHighFraction.Length; i++)
            m_toneHighFraction[i] = reader.ReadDouble();
        m_ticksUntilNextSample = reader.ReadDouble();
        m_noiseCounterTicks = reader.ReadDouble();
        m_noiseShiftRegister = reader.ReadUInt32();
        m_envelopeCounterTicks = reader.ReadDouble();
        m_envelopePosition = reader.ReadInt32();
        m_selectedRegister = reader.ReadByte();
    }
}
