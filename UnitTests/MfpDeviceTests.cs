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
public sealed class MfpDeviceTests
{
    private const uint BaseAddress = 0x00FFFA00;
    private const uint InterruptEnableBRegister = BaseAddress + 0x09;
    private const uint InterruptPendingBRegister = BaseAddress + 0x0D;
    private const uint InterruptMaskBRegister = BaseAddress + 0x15;
    private const uint VectorRegister = BaseAddress + 0x17;
    private const uint TimerCdControlRegister = BaseAddress + 0x1D;
    private const uint TimerCDataRegister = BaseAddress + 0x23;
    private const uint TimerDDataRegister = BaseAddress + 0x25;

    [Test]
    public void ResetShouldSetDefaultVectorBase()
    {
        var mfp = new MfpDevice();

        mfp.Reset();
        var vectorRegisterValue = mfp.Read8(VectorRegister);

        Assert.That(vectorRegisterValue, Is.EqualTo(0x40));
    }

    [Test]
    public void ReadsFromEvenRegisterOffsetsShouldReturnFf()
    {
        var mfp = new MfpDevice();

        mfp.Reset();
        var evenOffsetValue = mfp.Read8(BaseAddress + 0x08);

        Assert.That(evenOffsetValue, Is.EqualTo(0xFF));
    }

    [Test]
    public void TimerDShouldRaiseLevel6InterruptWhenEnabledAndUnmasked()
    {
        var mfp = new MfpDevice();
        mfp.Reset();
        var interruptCount = 0;
        var interruptLevel = (byte)0;
        var interruptVector = (byte)0;
        mfp.InterruptRequested += (level, vector) =>
        {
            interruptCount++;
            interruptLevel = level;
            interruptVector = vector;
        };

        mfp.Write8(VectorRegister, 0x50);
        mfp.Write8(InterruptEnableBRegister, 0x10);
        mfp.Write8(InterruptMaskBRegister, 0x10);
        mfp.Write8(TimerDDataRegister, 0x01);
        mfp.Write8(TimerCdControlRegister, 0x01);
        mfp.Advance(4);
        var interruptPendingValue = mfp.Read8(InterruptPendingBRegister);

        Assert.Multiple(() =>
        {
            Assert.That(interruptCount, Is.EqualTo(1));
            Assert.That(interruptLevel, Is.EqualTo(6));
            Assert.That(interruptVector, Is.EqualTo(0x54));
            Assert.That(interruptPendingValue & 0x10, Is.Not.Zero);
        });
    }

    [Test]
    public void TimerCShouldNotRaiseInterruptWhenMasked()
    {
        var mfp = new MfpDevice();
        mfp.Reset();
        var interruptCount = 0;
        mfp.InterruptRequested += (_, _) => interruptCount++;

        mfp.Write8(InterruptEnableBRegister, 0x20);
        mfp.Write8(InterruptMaskBRegister, 0x00);
        mfp.Write8(TimerCDataRegister, 0x01);
        mfp.Write8(TimerCdControlRegister, 0x10);
        mfp.Advance(4);

        Assert.That(interruptCount, Is.EqualTo(0));
    }

    [Test]
    public void RaiseGpip4InterruptShouldRaiseVectorWhenEnabledAndUnmasked()
    {
        var mfp = new MfpDevice();
        mfp.Reset();
        var interruptCount = 0;
        var interruptVector = (byte)0;
        mfp.InterruptRequested += (_, vector) =>
        {
            interruptCount++;
            interruptVector = vector;
        };

        mfp.Write8(VectorRegister, 0x50);
        mfp.Write8(InterruptEnableBRegister, 0x40);
        mfp.Write8(InterruptMaskBRegister, 0x40);
        var raised = mfp.RaiseGpip4Interrupt();

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.True);
            Assert.That(interruptCount, Is.EqualTo(1));
            Assert.That(interruptVector, Is.EqualTo(0x56));
        });
    }

    [Test]
    public void SetAciaInterruptLineShouldRaiseVectorWhenEnabledAndUnmasked()
    {
        var mfp = new MfpDevice();
        mfp.Reset();
        var interruptCount = 0;
        var interruptVector = (byte)0;
        mfp.InterruptRequested += (_, vector) =>
        {
            interruptCount++;
            interruptVector = vector;
        };

        mfp.Write8(VectorRegister, 0x50);
        mfp.Write8(InterruptEnableBRegister, 0x40);
        mfp.Write8(InterruptMaskBRegister, 0x40);
        mfp.SetAciaInterruptLine(isActiveLow: true);

        Assert.Multiple(() =>
        {
            Assert.That(interruptCount, Is.EqualTo(1));
            Assert.That(interruptVector, Is.EqualTo(0x56));
        });
    }

    [Test]
    public void SetFloppyInterruptLineShouldRaiseVectorWhenEnabledAndUnmasked()
    {
        var mfp = new MfpDevice();
        mfp.Reset();
        var interruptCount = 0;
        var interruptVector = (byte)0;
        mfp.InterruptRequested += (_, vector) =>
        {
            interruptCount++;
            interruptVector = vector;
        };

        mfp.Write8(VectorRegister, 0x50);
        mfp.Write8(InterruptEnableBRegister, 0x80);
        mfp.Write8(InterruptMaskBRegister, 0x80);
        mfp.SetFloppyInterruptLine(isActiveLow: true);

        Assert.Multiple(() =>
        {
            Assert.That(interruptCount, Is.EqualTo(1));
            Assert.That(interruptVector, Is.EqualTo(0x57));
        });
    }

    [Test]
    public void EnablingFloppyInterruptShouldRaiseWhenLineIsAlreadyActive()
    {
        var mfp = new MfpDevice();
        mfp.Reset();
        var interruptCount = 0;
        var interruptVector = (byte)0;
        mfp.InterruptRequested += (_, vector) =>
        {
            interruptCount++;
            interruptVector = vector;
        };

        mfp.Write8(VectorRegister, 0x50);
        mfp.SetFloppyInterruptLine(isActiveLow: true);
        mfp.Write8(InterruptEnableBRegister, 0x80);
        mfp.Write8(InterruptMaskBRegister, 0x80);

        Assert.Multiple(() =>
        {
            Assert.That(interruptCount, Is.EqualTo(1));
            Assert.That(interruptVector, Is.EqualTo(0x57));
        });
    }

}
