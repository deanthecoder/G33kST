// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.AtariST;

namespace UnitTests;

[TestFixture]
public sealed class PsgDeviceTests
{
    private const uint RegisterSelectAddress = 0x00FF8800;
    private const uint DataAddress = 0x00FF8802;
    private const byte PortARegister = 0x0E;
    private const byte MixerRegister = 0x07;
    private const byte ToneAFineRegister = 0x00;
    private const byte ToneACoarseRegister = 0x01;
    private const byte VolumeARegister = 0x08;

    [Test]
    public void ResetShouldDefaultPortAToDeselectFloppyDrives()
    {
        var device = new PsgDevice();

        device.Reset();
        device.Write8(RegisterSelectAddress, PortARegister);
        var portA = device.Read8(RegisterSelectAddress);

        Assert.That(portA & 0x07, Is.EqualTo(0x07));
    }

    [Test]
    public void WritingPortAShouldRaisePortAChangedEvent()
    {
        var device = new PsgDevice();
        var eventRaised = false;
        var observedValue = (byte)0;
        device.PortAChanged += value =>
        {
            eventRaised = true;
            observedValue = value;
        };

        device.Write8(RegisterSelectAddress, PortARegister);
        device.Write8(DataAddress, 0x05);

        Assert.Multiple(() =>
        {
            Assert.That(eventRaised, Is.True);
            Assert.That(observedValue, Is.EqualTo(0x05));
        });
    }

    [Test]
    public void AdvanceCyclesShouldEmitVaryingSamplesForActiveToneChannel()
    {
        var samples = new List<double>();
        var device = new PsgDevice((left, _) => samples.Add(left), cpuClockHz: 8_000_000, sampleRateHz: 44_100);
        WriteRegister(device, ToneAFineRegister, 0x20);
        WriteRegister(device, ToneACoarseRegister, 0x00);
        WriteRegister(device, MixerRegister, 0x3E); // Tone A only.
        WriteRegister(device, VolumeARegister, 0x0F); // Loud fixed volume.

        device.AdvanceCycles(200_000);

        Assert.Multiple(() =>
        {
            Assert.That(samples.Count, Is.GreaterThan(0));
            Assert.That(samples.Max() - samples.Min(), Is.GreaterThan(0.05));
        });
    }

    [Test]
    public void DisableChannelShouldMuteThatVoiceInMixedOutput()
    {
        var samples = new List<double>();
        var device = new PsgDevice((left, _) => samples.Add(left), cpuClockHz: 8_000_000, sampleRateHz: 44_100);
        WriteRegister(device, ToneAFineRegister, 0x20);
        WriteRegister(device, ToneACoarseRegister, 0x00);
        WriteRegister(device, MixerRegister, 0x3E); // Tone A only.
        WriteRegister(device, VolumeARegister, 0x0F);
        device.SetChannelEnabled(channel: 1, isEnabled: false);

        device.AdvanceCycles(200_000);

        Assert.That(samples.All(sample => Math.Abs(sample) < 0.0001), Is.True);
    }

    [Test]
    public void AdvanceCyclesShouldAverageToneAboveNyquistToNearSilence()
    {
        var samples = new List<double>();
        var device = new PsgDevice((left, _) => samples.Add(left), cpuClockHz: 8_000_000, sampleRateHz: 44_100);
        WriteRegister(device, ToneAFineRegister, 0x01);
        WriteRegister(device, ToneACoarseRegister, 0x00);
        WriteRegister(device, MixerRegister, 0x3E); // Tone A only.
        WriteRegister(device, VolumeARegister, 0x0F);

        device.AdvanceCycles(200_000);

        Assert.Multiple(() =>
        {
            Assert.That(samples.Count, Is.GreaterThan(0));
            Assert.That(samples.All(sample => Math.Abs(sample) < 0.0001), Is.True);
        });
    }

    private static void WriteRegister(PsgDevice device, byte register, byte value)
    {
        device.Write8(RegisterSelectAddress, register);
        device.Write8(DataAddress, value);
    }
}
