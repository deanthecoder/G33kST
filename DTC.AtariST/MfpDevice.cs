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
/// Minimal Atari ST MFP (Multi-Function Peripheral) implementation.
/// </summary>
/// <remarks>
/// The MFP is the Atari ST's "housekeeping" chip: it provides timers plus interrupt lines for
/// peripherals like keyboard/serial handshakes, and normally signals CPU interrupt level 6.
/// This phase models enough register behavior and timer C/D interrupts for early TOS bring-up.
/// </remarks>
public sealed class MfpDevice : IMemDevice
{
    private const uint BaseAddress = 0x00FFFA00;
    private const int RegisterSpace = 0x40;
    private const byte InterruptLevel = 6;
    private const byte DefaultVectorBase = 0x40;
    private const byte TimerCInterruptMask = 0x20;
    private const byte TimerDInterruptMask = 0x10;
    private const byte TimerCSourceNumber = 5;
    private const byte TimerDSourceNumber = 4;
    private const byte TimerAInterruptMask = 0x20;
    private const byte TimerASourceNumber = 13;
    private const byte Gpip5InterruptMask = 0x80;
    private const byte Gpip5SourceNumber = 7;
    private const byte Gpip4InterruptMask = 0x40;
    private const byte Gpip4SourceNumber = 6;
    private const byte MonitorDetectInputMask = 0x80;
    private const byte FloppyInputMask = 0x20;
    private const byte AciaInputMask = 0x10;

    private const int GpipRegister = 0x01;
    private const int DataDirectionRegister = 0x05;
    private const int InterruptEnableB = 0x09;
    private const int InterruptPendingB = 0x0D;
    private const int InterruptInServiceB = 0x11;
    private const int InterruptMaskB = 0x15;
    private const int InterruptEnableA = 0x07;
    private const int InterruptPendingA = 0x0B;
    private const int InterruptMaskA = 0x13;
    private const int VectorRegister = 0x17;
    private const int TimerAControl = 0x19;
    private const int TimerBControl = 0x1B;
    private const int TimerCdControl = 0x1D;
    private const int TimerAData = 0x1F;
    private const int TimerBData = 0x21;
    private const int TimerCData = 0x23;
    private const int TimerDData = 0x25;
    private const byte TimerBInterruptMask = 0x01;
    private const byte TimerBSourceNumber = 8;

    private static readonly int[] TimerPrescaleDivisors =
    [
        0,   // stopped
        4,
        10,
        16,
        50,
        64,
        100,
        200
    ];

    private readonly byte[] m_registers = new byte[RegisterSpace];
    private int m_timerCCurrent;
    private int m_timerDCurrent;
    private int m_timerACurrent;
    private int m_timerBCurrent;
    private int m_timerAAccumulator;
    private int m_timerCAccumulator;
    private int m_timerDAccumulator;
    private int m_timerBAccumulator;
    private bool m_aciaInterruptLineActiveLow;
    private bool m_floppyInterruptLineActiveLow;
    private byte m_gpipInputState = 0xFF;
    private byte m_gpipOutputLatch;

    /// <inheritdoc />
    public uint FromAddr => BaseAddress;

    /// <inheritdoc />
    public uint ToAddr => BaseAddress + RegisterSpace - 1;

    /// <summary>
    /// Raised when the MFP requests an interrupt.
    /// </summary>
    public event Action<byte, byte> InterruptRequested;

    /// <summary>
    /// Gets whether any enabled, unmasked MFP source is currently pending.
    /// </summary>
    public bool HasUnmaskedPendingInterrupt =>
        ((m_registers[InterruptPendingA] & m_registers[InterruptEnableA] & m_registers[InterruptMaskA]) |
         (m_registers[InterruptPendingB] & m_registers[InterruptEnableB] & m_registers[InterruptMaskB])) != 0;

    /// <summary>
    /// Resets internal MFP state and register defaults.
    /// </summary>
    public void Reset()
    {
        Array.Clear(m_registers, 0, m_registers.Length);
        m_gpipInputState = 0xFF;
        m_gpipOutputLatch = 0;
        m_registers[VectorRegister] = DefaultVectorBase;
        m_timerCCurrent = 0;
        m_timerDCurrent = 0;
        m_timerACurrent = 0;
        m_timerBCurrent = 0;
        m_timerAAccumulator = 0;
        m_timerCAccumulator = 0;
        m_timerDAccumulator = 0;
        m_timerBAccumulator = 0;
        m_aciaInterruptLineActiveLow = false;
        m_floppyInterruptLineActiveLow = false;
    }

    /// <summary>
    /// Advances timer state by CPU ticks.
    /// </summary>
    public void Advance(long deltaTicks)
    {
        if (deltaTicks <= 0)
            return;

        AdvanceTimerB((int)Math.Min(deltaTicks, int.MaxValue));
        AdvanceTimerA((int)Math.Min(deltaTicks, int.MaxValue));
        AdvanceTimerC((int)Math.Min(deltaTicks, int.MaxValue));
        AdvanceTimerD((int)Math.Min(deltaTicks, int.MaxValue));
    }

    /// <summary>
    /// Advances Timer B by one display-enable edge in event-count mode.
    /// On real ST hardware this input is tied to the Shifter DE signal,
    /// so there are no Timer B event pulses during vertical blank.
    /// </summary>
    public void NotifyHblank(bool isDisplayEnableActive)
    {
        if (!isDisplayEnableActive)
            return;
        AdvanceTimerBEventCount();
    }

    /// <summary>
    /// Advances Timer A by one external event pulse when configured in event-count mode.
    /// </summary>
    /// <remarks>
    /// Timer A event-count is driven by an external input on real hardware. This hook allows
    /// the machine integration to surface those pulses when/if needed without special-casing timer logic.
    /// </remarks>
    public void NotifyTimerAEvent() =>
        AdvanceTimerEventCount(
            TimerAControl,
            TimerAData,
            ref m_timerACurrent,
            InterruptEnableA,
            InterruptMaskA,
            InterruptPendingA,
            TimerAInterruptMask,
            TimerASourceNumber);

    /// <summary>
    /// Advances Timer B by one event-count pulse tied to Shifter display-enable edges.
    /// </summary>
    private void AdvanceTimerBEventCount() =>
        AdvanceTimerEventCount(
            TimerBControl,
            TimerBData,
            ref m_timerBCurrent,
            InterruptEnableA,
            InterruptMaskA,
            InterruptPendingA,
            TimerBInterruptMask,
            TimerBSourceNumber);

    private void AdvanceTimerEventCount(
        int timerControlRegister,
        int timerDataRegister,
        ref int timerCurrent,
        int interruptEnableRegister,
        int interruptMaskRegister,
        int interruptPendingRegister,
        byte interruptMask,
        byte sourceNumber)
    {
        if ((m_registers[timerControlRegister] & 0x0F) != 0x08)
            return;
        if (timerCurrent == 0)
            timerCurrent = NormalizeTimerData(m_registers[timerDataRegister]);

        timerCurrent--;
        if (timerCurrent > 0)
            return;

        timerCurrent = NormalizeTimerData(m_registers[timerDataRegister]);
        RaiseInterrupt(interruptEnableRegister, interruptMaskRegister, interruptPendingRegister, interruptMask, sourceNumber);
    }

    /// <summary>
    /// Signals a pending interrupt from GPIP4 (the line used by the keyboard ACIA on ST machines).
    /// Returns <c>true</c> when the interrupt is both enabled and unmasked and therefore raised.
    /// </summary>
    public bool RaiseGpip4Interrupt() =>
        RaiseInterrupt(InterruptEnableB, InterruptMaskB, InterruptPendingB, Gpip4InterruptMask, Gpip4SourceNumber);

    /// <summary>
    /// Returns and clears the highest-priority pending MFP vector, if any.
    /// </summary>
    public bool TryAcknowledgePendingInterrupt(out byte vectorNumber)
    {
        var pendingA = (byte)(m_registers[InterruptPendingA] & m_registers[InterruptEnableA] & m_registers[InterruptMaskA]);
        var pendingB = (byte)(m_registers[InterruptPendingB] & m_registers[InterruptEnableB] & m_registers[InterruptMaskB]);
        if (pendingA == 0 && pendingB == 0)
        {
            vectorNumber = 0;
            return false;
        }

        byte sourceNumber;
        if (pendingA != 0)
        {
            sourceNumber = (byte)(8 + HighestSetBitIndex(pendingA));
            m_registers[InterruptPendingA] = (byte)(m_registers[InterruptPendingA] & ~(1 << (sourceNumber - 8)));
        }
        else
        {
            sourceNumber = (byte)HighestSetBitIndex(pendingB);
            m_registers[InterruptPendingB] = (byte)(m_registers[InterruptPendingB] & ~(1 << sourceNumber));
        }

        var vectorBase = (byte)(m_registers[VectorRegister] & 0xF0);
        vectorNumber = (byte)(vectorBase + sourceNumber);
        return true;
    }

    /// <summary>
    /// Signals a pending interrupt from GPIP5 (the line used by floppy FDC completion on ST machines).
    /// Returns <c>true</c> when the interrupt is both enabled and unmasked and therefore raised.
    /// </summary>
    private void RaiseGpip5Interrupt() =>
        RaiseInterrupt(InterruptEnableB, InterruptMaskB, InterruptPendingB, Gpip5InterruptMask,
            Gpip5SourceNumber);

    /// <summary>
    /// Sets the monitor detect input line.
    /// Color monitor keeps GPIP bit 7 high; monochrome pulls it low.
    /// </summary>
    public void SetMonitorType(AtariMonitorType monitorType)
    {
        if (monitorType == AtariMonitorType.Monochrome)
            m_gpipInputState = (byte)(m_gpipInputState & ~MonitorDetectInputMask);
        else
            m_gpipInputState = (byte)(m_gpipInputState | MonitorDetectInputMask);
    }

    /// <summary>
    /// Updates the ACIA interrupt input line sampled by MFP GPIP4.
    /// </summary>
    public void SetAciaInterruptLine(bool isActiveLow)
    {
        if (m_aciaInterruptLineActiveLow == isActiveLow)
            return;

        m_aciaInterruptLineActiveLow = isActiveLow;
        if (isActiveLow)
        {
            m_gpipInputState = (byte)(m_gpipInputState & ~AciaInputMask);
            RaiseGpip4Interrupt();
        }
        else
            m_gpipInputState = (byte)(m_gpipInputState | AciaInputMask);
    }

    /// <summary>
    /// Updates the FDC completion/IRQ line sampled on GPIP5.
    /// This is used by TOS floppy polling loops that wait for GPIP5 to go low.
    /// </summary>
    public void SetFloppyInterruptLine(bool isActiveLow)
    {
        if (m_floppyInterruptLineActiveLow == isActiveLow)
            return;

        m_floppyInterruptLineActiveLow = isActiveLow;
        if (isActiveLow)
        {
            m_gpipInputState = (byte)(m_gpipInputState & ~FloppyInputMask);
            RaiseGpip5Interrupt();
        }
        else
            m_gpipInputState = (byte)(m_gpipInputState | FloppyInputMask);
    }

    /// <inheritdoc />
    public byte Read8(uint address)
    {
        var offset = (int)(address - BaseAddress);
        if (offset < 0 || offset >= RegisterSpace)
            return 0xFF;
        if ((offset & 1) == 0)
            return 0xFF;
        if (offset == GpipRegister)
            return ReadGpipState();
        if (offset == TimerBData)
            return (byte)m_timerBCurrent;
        if (offset == TimerAData)
            return (byte)m_timerACurrent;

        return m_registers[offset];
    }

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
        var offset = (int)(address - BaseAddress);
        if (offset < 0 || offset >= RegisterSpace)
            return;
        if ((offset & 1) == 0)
            return;

        if (offset is InterruptPendingB or InterruptInServiceB)
        {
            // Pending / in-service registers ignore '1' bits; writing 0 clears.
            m_registers[offset] &= value;
            return;
        }
        if (offset == GpipRegister)
        {
            m_gpipOutputLatch = value;
            return;
        }

        m_registers[offset] = value;
        if (offset == TimerAData)
            m_timerACurrent = NormalizeTimerData(value);
        else if (offset == TimerBData)
            m_timerBCurrent = NormalizeTimerData(value);
        else if (offset == TimerCData)
            m_timerCCurrent = NormalizeTimerData(value);
        else if (offset == TimerDData)
            m_timerDCurrent = NormalizeTimerData(value);
        else if (offset is InterruptEnableA or InterruptEnableB or InterruptMaskA or InterruptMaskB)
        {
            TryRaiseAciaInterruptIfPending();
            TryRaiseFloppyInterruptIfPending();
        }
    }

    private void AdvanceTimerA(int deltaTicks)
    {
        var timerControl = (byte)(m_registers[TimerAControl] & 0x0F);
        if (timerControl >= TimerPrescaleDivisors.Length)
            return;

        var prescale = TimerPrescaleDivisors[timerControl];
        if (prescale == 0)
            return;
        if (m_timerACurrent == 0)
            m_timerACurrent = NormalizeTimerData(m_registers[TimerAData]);

        m_timerAAccumulator += deltaTicks;
        while (m_timerAAccumulator >= prescale)
        {
            m_timerAAccumulator -= prescale;
            m_timerACurrent--;
            if (m_timerACurrent > 0)
                continue;

            m_timerACurrent = NormalizeTimerData(m_registers[TimerAData]);
            RaiseInterrupt(InterruptEnableA, InterruptMaskA, InterruptPendingA, TimerAInterruptMask, TimerASourceNumber);
        }
    }

    private void AdvanceTimerC(int deltaTicks)
    {
        var timerControl = (byte)((m_registers[TimerCdControl] >> 4) & 0x07);
        var prescale = TimerPrescaleDivisors[timerControl];
        if (prescale == 0)
            return;
        if (m_timerCCurrent == 0)
            m_timerCCurrent = NormalizeTimerData(m_registers[TimerCData]);

        m_timerCAccumulator += deltaTicks;
        while (m_timerCAccumulator >= prescale)
        {
            m_timerCAccumulator -= prescale;
            m_timerCCurrent--;
            if (m_timerCCurrent > 0)
                continue;

            m_timerCCurrent = NormalizeTimerData(m_registers[TimerCData]);
            RaiseTimerInterrupt(TimerCInterruptMask, TimerCSourceNumber);
        }
    }

    private void AdvanceTimerB(int deltaTicks)
    {
        var timerControl = (byte)(m_registers[TimerBControl] & 0x0F);
        if (timerControl >= TimerPrescaleDivisors.Length)
            return;

        var prescale = TimerPrescaleDivisors[timerControl];
        if (prescale == 0)
            return;
        if (m_timerBCurrent == 0)
            m_timerBCurrent = NormalizeTimerData(m_registers[TimerBData]);

        m_timerBAccumulator += deltaTicks;
        while (m_timerBAccumulator >= prescale)
        {
            m_timerBAccumulator -= prescale;
            m_timerBCurrent--;
            if (m_timerBCurrent > 0)
                continue;

            m_timerBCurrent = NormalizeTimerData(m_registers[TimerBData]);
            RaiseInterrupt(InterruptEnableA, InterruptMaskA, InterruptPendingA, TimerBInterruptMask, TimerBSourceNumber);
        }
    }

    private void AdvanceTimerD(int deltaTicks)
    {
        var timerControl = (byte)(m_registers[TimerCdControl] & 0x07);
        var prescale = TimerPrescaleDivisors[timerControl];
        if (prescale == 0)
            return;
        if (m_timerDCurrent == 0)
            m_timerDCurrent = NormalizeTimerData(m_registers[TimerDData]);

        m_timerDAccumulator += deltaTicks;
        while (m_timerDAccumulator >= prescale)
        {
            m_timerDAccumulator -= prescale;
            m_timerDCurrent--;
            if (m_timerDCurrent > 0)
                continue;

            m_timerDCurrent = NormalizeTimerData(m_registers[TimerDData]);
            RaiseTimerInterrupt(TimerDInterruptMask, TimerDSourceNumber);
        }
    }

    private void RaiseTimerInterrupt(byte interruptMask, byte sourceNumber) =>
        RaiseInterrupt(InterruptEnableB, InterruptMaskB, InterruptPendingB, interruptMask, sourceNumber);

    private bool RaiseInterrupt(int interruptEnableRegister, int interruptMaskRegister, int interruptPendingRegister, byte interruptMask, byte sourceNumber)
    {
        var interruptEnabled = (m_registers[interruptEnableRegister] & interruptMask) != 0;
        if (!interruptEnabled)
            return false;

        m_registers[interruptPendingRegister] |= interruptMask;
        var interruptUnmasked = (m_registers[interruptMaskRegister] & interruptMask) != 0;
        if (!interruptUnmasked)
            return false;

        var vectorBase = (byte)(m_registers[VectorRegister] & 0xF0);
        var vectorNumber = (byte)(vectorBase + sourceNumber);
        InterruptRequested?.Invoke(InterruptLevel, vectorNumber);
        return true;
    }

    private static int NormalizeTimerData(byte value) =>
        value == 0 ? 256 : value;

    private void TryRaiseAciaInterruptIfPending()
    {
        if (!m_aciaInterruptLineActiveLow)
            return;

        RaiseGpip4Interrupt();
    }

    private void TryRaiseFloppyInterruptIfPending()
    {
        if (!m_floppyInterruptLineActiveLow)
            return;

        RaiseGpip5Interrupt();
    }

    private byte ReadGpipState()
    {
        var dataDirection = m_registers[DataDirectionRegister];
        return (byte)((m_gpipOutputLatch & dataDirection) | (m_gpipInputState & ~dataDirection));
    }

    private static int HighestSetBitIndex(byte value)
    {
        for (var i = 7; i >= 0; i--)
        {
            if ((value & (1 << i)) != 0)
                return i;
        }

        return 0;
    }

    internal int GetStateSize() =>
        m_registers.Length +
        sizeof(int) * 8 +
        2 +
        sizeof(byte) * 2;

    internal void SaveState(ref StateWriter writer)
    {
        writer.WriteBytes(m_registers);
        writer.WriteInt32(m_timerCCurrent);
        writer.WriteInt32(m_timerDCurrent);
        writer.WriteInt32(m_timerACurrent);
        writer.WriteInt32(m_timerBCurrent);
        writer.WriteInt32(m_timerAAccumulator);
        writer.WriteInt32(m_timerCAccumulator);
        writer.WriteInt32(m_timerDAccumulator);
        writer.WriteInt32(m_timerBAccumulator);
        writer.WriteBool(m_aciaInterruptLineActiveLow);
        writer.WriteBool(m_floppyInterruptLineActiveLow);
        writer.WriteByte(m_gpipInputState);
        writer.WriteByte(m_gpipOutputLatch);
    }

    internal void LoadState(ref StateReader reader)
    {
        reader.ReadBytes(m_registers);
        m_timerCCurrent = reader.ReadInt32();
        m_timerDCurrent = reader.ReadInt32();
        m_timerACurrent = reader.ReadInt32();
        m_timerBCurrent = reader.ReadInt32();
        m_timerAAccumulator = reader.ReadInt32();
        m_timerCAccumulator = reader.ReadInt32();
        m_timerDAccumulator = reader.ReadInt32();
        m_timerBAccumulator = reader.ReadInt32();
        m_aciaInterruptLineActiveLow = reader.ReadBool();
        m_floppyInterruptLineActiveLow = reader.ReadBool();
        m_gpipInputState = reader.ReadByte();
        m_gpipOutputLatch = reader.ReadByte();
    }
}
