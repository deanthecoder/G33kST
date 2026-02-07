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
using DTC.Emulation.Devices;
using DTC.M68000.Decoding;

namespace DTC.M68000;

/// <summary>
/// Motorola 68000 CPU implementation.
/// </summary>
public sealed class Cpu : CpuBase
{
    private const uint ResetStackPointerVectorAddress = 0x000000;
    private const uint ResetProgramCounterVectorAddress = 0x000004;

    // 0x2700 = supervisor mode with IPL 7, trace off, and XNZVC clear.
    private const ushort InitialStatusRegister = 0x2700;

    private ushort m_prefetch0;
    private ushort m_prefetch1;
    private bool m_hasPrefetch;

    /// <summary>
    /// Gets the active CPU register set.
    /// </summary>
    public Registers Registers { get; }

    /// <summary>
    /// Gets the main addressable memory attached to the bus.
    /// </summary>
    public Memory MainMemory { get; }

    /// <summary>
    /// Gets the total number of cycles accumulated since the last reset.
    /// </summary>
    public long CyclesSinceCpuStart { get; private set; }

    /// <summary>
    /// Creates a 68000 CPU bound to the supplied bus.
    /// </summary>
    public Cpu(Bus bus)
        : base(bus)
    {
        MainMemory = bus?.MainMemory ?? throw new ArgumentNullException(nameof(bus));
        Registers = new Registers();
    }

    /// <summary>
    /// Resets CPU state and loads SSP/PC from the reset vectors.
    /// </summary>
    public override void Reset()
    {
        Registers.Reset();
        CyclesSinceCpuStart = 0;
        
        // Vector 0 = initial SSP, vector 1 = initial PC.
        Registers.SupervisorStackPointer = Bus.Read32BigEndian(ResetStackPointerVectorAddress);
        Registers.ProgramCounter = Bus.Read32BigEndian(ResetProgramCounterVectorAddress);
        Registers.StatusRegister = InitialStatusRegister;
        m_hasPrefetch = false;
    }

    /// <summary>
    /// Adds internal CPU wait cycles.
    /// </summary>
    public void InternalWait(uint cycles) =>
        CyclesSinceCpuStart += cycles;

    /// <summary>
    /// Seeds the two-word prefetch queue used by single-step test vectors.
    /// </summary>
    public void SeedPrefetch(ushort firstWord, ushort secondWord)
    {
        m_prefetch0 = firstWord;
        m_prefetch1 = secondWord;
        m_hasPrefetch = true;
    }

    /// <summary>
    /// Fetches an instruction word from PC and advances PC by two.
    /// </summary>
    public ushort FetchPcWord()
    {
        // Single-step vectors provide queue contents separately, so consume that first.
        if (m_hasPrefetch)
            return FetchPrefetchedWord();

        var address = ValidateEvenAddress(Registers.ProgramCounter);
        var value = Bus.Read16BigEndian(address);
        Registers.ProgramCounter = address + 2;
        NotifyMemoryRead(address, (byte)(value >> 8));
        NotifyMemoryRead(address + 1, (byte)(value & 0xFF));
        return value;
    }

    /// <summary>
    /// Reads one byte from bus memory.
    /// </summary>
    public override byte Read8(uint address)
    {
        var value = Bus.Read8(address);
        NotifyMemoryRead(address, value);
        return value;
    }

    /// <summary>
    /// Writes one byte to bus memory.
    /// </summary>
    public override void Write8(uint address, byte value)
    {
        Bus.Write8(address, value);
        NotifyMemoryWrite(address, value);
    }

    /// <summary>
    /// Executes a single instruction at the current program counter.
    /// </summary>
    public override void Step()
    {
        var oldPC = Registers.ProgramCounter;
        var opcode = FetchPcWord();
        NotifyBeforeInstruction(oldPC, opcode);
        
        var instruction = InstructionDecoder.Decode(opcode) ?? throw new NotImplementedException($"Opcode 0x{opcode:X4} is not implemented.");
        instruction.Execute(this, opcode);

        NotifyAfterStep();
    }

    private static uint ValidateEvenAddress(uint address) =>
        (address & 1) == 0 ? address : throw new InvalidOperationException($"Odd address fetch at 0x{address:X6}.");

    private ushort FetchPrefetchedWord()
    {
        var value = m_prefetch0;
        var fetchAddress = ValidateEvenAddress(Registers.ProgramCounter);
        var fetchedWord = Bus.Read16BigEndian(fetchAddress);
        NotifyMemoryRead(fetchAddress, (byte)(fetchedWord >> 8));
        NotifyMemoryRead(fetchAddress + 1, (byte)(fetchedWord & 0xFF));

        // Shift queue and refill the second slot from the next prefetch address.
        m_prefetch0 = m_prefetch1;
        m_prefetch1 = fetchedWord;
        Registers.ProgramCounter = fetchAddress + 2;
        return value;
    }
}
