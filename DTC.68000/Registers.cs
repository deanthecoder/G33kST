// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using DTC.Emulation.Snapshot;

namespace DTC.M68000;

/// <summary>
/// Represents the Motorola 68000 CPU register file and status flags.
/// </summary>
public sealed class Registers
{
    private const ushort TraceFlagMask = 0x8000;
    private const ushort SupervisorFlagMask = 0x2000;
    private const ushort InterruptMask = 0x0700;
    private const ushort ExtendFlagMask = 0x0010;
    private const ushort NegativeFlagMask = 0x0008;
    private const ushort ZeroFlagMask = 0x0004;
    private const ushort OverflowFlagMask = 0x0002;
    private const ushort CarryFlagMask = 0x0001;

    private readonly uint[] m_d = new uint[8];
    private readonly uint[] m_a = new uint[8];
    private ushort m_statusRegister;

    /// <summary>
    /// Initializes a register file in the reset state.
    /// </summary>
    public Registers() => Reset();

    /// <summary>
    /// Gets or sets the full 16-bit status register (SR).
    /// </summary>
    public ushort StatusRegister
    {
        get => m_statusRegister;
        set
        {
            var wasSupervisor = IsSupervisor;
            m_statusRegister = value;
            var nowSupervisor = IsSupervisor;

            if (wasSupervisor == nowSupervisor)
                return;

            SwapStackPointers(nowSupervisor);
        }
    }

    /// <summary>
    /// Gets or sets the user stack pointer (USP).
    /// </summary>
    public uint UserStackPointer { get; set; }

    /// <summary>
    /// Gets or sets the supervisor stack pointer (SSP).
    /// </summary>
    public uint SupervisorStackPointer { get; set; }

    /// <summary>
    /// Gets or sets the program counter (PC).
    /// </summary>
    public uint ProgramCounter { get; set; }

    /// <summary>
    /// Gets or sets the trace flag (T).
    /// </summary>
    public bool TraceFlag
    {
        get => IsFlagSet(TraceFlagMask);
        set => SetFlag(TraceFlagMask, value);
    }

    /// <summary>
    /// Gets or sets supervisor mode (S bit).
    /// </summary>
    public bool IsSupervisor
    {
        get => IsFlagSet(SupervisorFlagMask);
        set => SetSupervisorMode(value);
    }

    /// <summary>
    /// Gets or sets the interrupt priority mask (I2:I0).
    /// </summary>
    public byte InterruptPriorityMask
    {
        get => (byte)((m_statusRegister & InterruptMask) >> 8);
        set => m_statusRegister = (ushort)((m_statusRegister & ~InterruptMask) | ((value & 0x07) << 8));
    }

    /// <summary>
    /// Gets or sets the extend flag (X).
    /// </summary>
    public bool ExtendFlag
    {
        get => IsFlagSet(ExtendFlagMask);
        set => SetFlag(ExtendFlagMask, value);
    }

    /// <summary>
    /// Gets or sets the negative flag (N).
    /// </summary>
    public bool NegativeFlag
    {
        get => IsFlagSet(NegativeFlagMask);
        set => SetFlag(NegativeFlagMask, value);
    }

    /// <summary>
    /// Gets or sets the zero flag (Z).
    /// </summary>
    public bool ZeroFlag
    {
        get => IsFlagSet(ZeroFlagMask);
        set => SetFlag(ZeroFlagMask, value);
    }

    /// <summary>
    /// Gets or sets the overflow flag (V).
    /// </summary>
    public bool OverflowFlag
    {
        get => IsFlagSet(OverflowFlagMask);
        set => SetFlag(OverflowFlagMask, value);
    }

    /// <summary>
    /// Gets or sets the carry flag (C).
    /// </summary>
    public bool CarryFlag
    {
        get => IsFlagSet(CarryFlagMask);
        set => SetFlag(CarryFlagMask, value);
    }

    /// <summary>
    /// Gets or sets the active stack pointer (A7).
    /// </summary>
    public uint StackPointer
    {
        get => GetAddressRegister(7);
        set => SetAddressRegister(7, value);
    }

    /// <summary>
    /// Resets all registers and flags to zero.
    /// </summary>
    public void Reset()
    {
        Array.Clear(m_d, 0, m_d.Length);
        Array.Clear(m_a, 0, m_a.Length);
        m_statusRegister = 0;
        ProgramCounter = 0;
        UserStackPointer = 0;
        SupervisorStackPointer = 0;
    }

    /// <summary>
    /// Returns a compact textual view of status bits (T S I2 I1 I0 X N Z V C).
    /// </summary>
    public string FlagsAsString() =>
        $"{(TraceFlag ? "T" : "-")}" +
        $"{(IsSupervisor ? "S" : "-")}" +
        $"{((InterruptPriorityMask & 0x04) != 0 ? "2" : "-")}" +
        $"{((InterruptPriorityMask & 0x02) != 0 ? "1" : "-")}" +
        $"{((InterruptPriorityMask & 0x01) != 0 ? "0" : "-")}" +
        $"{(ExtendFlag ? "X" : "-")}" +
        $"{(NegativeFlag ? "N" : "-")}" +
        $"{(ZeroFlag ? "Z" : "-")}" +
        $"{(OverflowFlag ? "V" : "-")}" +
        $"{(CarryFlag ? "C" : "-")}";

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder(220);
        builder.Append("PC:").Append(ProgramCounter.ToString("X8"));
        builder.Append(" SR:").Append(StatusRegister.ToString("X4"));
        builder.Append(" F:").Append(FlagsAsString());

        for (var i = 0; i < 8; i++)
            builder.Append(" D").Append(i).Append(':').Append(m_d[i].ToString("X8"));

        for (var i = 0; i < 8; i++)
            builder.Append(" A").Append(i).Append(':').Append(m_a[i].ToString("X8"));

        builder.Append(" USP:").Append(UserStackPointer.ToString("X8"));
        builder.Append(" SSP:").Append(SupervisorStackPointer.ToString("X8"));
        return builder.ToString();
    }

    /// <summary>
    /// Returns the data register value (D0-D7).
    /// </summary>
    public uint GetDataRegister(int index)
    {
        ValidateIndex(index, nameof(index));
        return m_d[index];
    }

    /// <summary>
    /// Sets the data register value (D0-D7).
    /// </summary>
    public void SetDataRegister(int index, uint value)
    {
        ValidateIndex(index, nameof(index));
        m_d[index] = value;
    }

    /// <summary>
    /// Returns the address register value (A0-A7).
    /// </summary>
    public uint GetAddressRegister(int index)
    {
        ValidateIndex(index, nameof(index));
        return m_a[index];
    }

    /// <summary>
    /// Sets the address register value (A0-A7).
    /// </summary>
    public void SetAddressRegister(int index, uint value)
    {
        ValidateIndex(index, nameof(index));
        m_a[index] = value;

        if (index != 7)
            return;

        if (IsSupervisor)
            SupervisorStackPointer = value;
        else
            UserStackPointer = value;
    }

    /// <summary>
    /// Switches supervisor/user mode and swaps USP/SSP with A7.
    /// </summary>
    public void SetSupervisorMode(bool enable)
    {
        if (IsSupervisor == enable)
            return;

        SwapStackPointers(enable);
        SetFlag(SupervisorFlagMask, enable);
    }

    private void SwapStackPointers(bool enteringSupervisor)
    {
        if (enteringSupervisor)
        {
            UserStackPointer = m_a[7];
            m_a[7] = SupervisorStackPointer;
        }
        else
        {
            SupervisorStackPointer = m_a[7];
            m_a[7] = UserStackPointer;
        }
    }

    private bool IsFlagSet(ushort mask) =>
        (m_statusRegister & mask) != 0;

    private void SetFlag(ushort mask, bool value)
    {
        if (value)
            m_statusRegister |= mask;
        else
            m_statusRegister = (ushort)(m_statusRegister & ~mask);
    }

    private static void ValidateIndex(int index, string paramName)
    {
        if (index < 0 || index > 7)
            throw new ArgumentOutOfRangeException(paramName);
    }

    internal int GetStateSize() =>
        (m_d.Length + m_a.Length) * sizeof(uint) +
        sizeof(ushort) +
        sizeof(uint) * 3;

    internal void SaveState(ref StateWriter writer)
    {
        for (var i = 0; i < m_d.Length; i++)
            writer.WriteUInt32(m_d[i]);
        for (var i = 0; i < m_a.Length; i++)
            writer.WriteUInt32(m_a[i]);
        writer.WriteUInt16(m_statusRegister);
        writer.WriteUInt32(UserStackPointer);
        writer.WriteUInt32(SupervisorStackPointer);
        writer.WriteUInt32(ProgramCounter);
    }

    internal void LoadState(ref StateReader reader)
    {
        for (var i = 0; i < m_d.Length; i++)
            m_d[i] = reader.ReadUInt32();
        for (var i = 0; i < m_a.Length; i++)
            m_a[i] = reader.ReadUInt32();
        m_statusRegister = reader.ReadUInt16();
        UserStackPointer = reader.ReadUInt32();
        SupervisorStackPointer = reader.ReadUInt32();
        ProgramCounter = reader.ReadUInt32();
    }
}
