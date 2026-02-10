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
using DTC.M68000.Addressing;
using DTC.M68000.Decoding;

namespace DTC.M68000;

/// <summary>
/// Motorola 68000 CPU implementation.
/// </summary>
public sealed class Cpu : CpuBase
{
    private const uint ResetStackPointerVectorAddress = 0x000000;
    private const uint ResetProgramCounterVectorAddress = 0x000004;
    private const ushort ValidStatusRegisterMask = 0xA71F;
    private const ushort SupervisorFlagMask = 0x2000;
    private const ushort TraceFlagMask = 0x8000;
    private const uint PrivilegeViolationVectorAddress = 0x000020;

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
    /// Gets the total number of cycles accumulated since the last reset.
    /// </summary>
    public long CyclesSinceCpuStart { get; private set; }

    /// <summary>
    /// Creates a 68000 CPU bound to the supplied bus.
    /// </summary>
    public Cpu(Bus bus)
        : base(bus)
    {
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
        var value = ReadInstructionWord(address);
        Registers.ProgramCounter = address + 2;
        return value;
    }

    /// <summary>
    /// Returns the base PC used by PC-relative EA decoding before consuming an extension word.
    /// </summary>
    public uint GetPcRelativeBaseAddress() =>
        m_hasPrefetch ? unchecked(Registers.ProgramCounter - 4) : Registers.ProgramCounter;

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
    /// Reads a 16-bit value from memory in big-endian order.
    /// </summary>
    public ushort Read16(uint address)
    {
        address = EnsureEvenBusAddress(address, ".w");
        var hi = Read8(address);
        var lo = Read8(EffectiveAddressMath.NormalizeAddress24(address + 1));
        return (ushort)((hi << 8) | lo);
    }

    /// <summary>
    /// Writes a 16-bit value to memory in big-endian order.
    /// </summary>
    public void Write16(uint address, ushort value)
    {
        address = EnsureEvenBusAddress(address, ".w");
        Write8(address, (byte)(value >> 8));
        Write8(EffectiveAddressMath.NormalizeAddress24(address + 1), (byte)(value & 0xFF));
    }

    /// <summary>
    /// Reads a 32-bit value from memory in big-endian order.
    /// </summary>
    public uint Read32(uint address)
    {
        address = EnsureEvenBusAddress(address, ".l");
        var b0 = Read8(address);
        var b1 = Read8(EffectiveAddressMath.NormalizeAddress24(address + 1));
        var b2 = Read8(EffectiveAddressMath.NormalizeAddress24(address + 2));
        var b3 = Read8(EffectiveAddressMath.NormalizeAddress24(address + 3));
        return ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
    }

    /// <summary>
    /// Writes a 32-bit value to memory in big-endian order.
    /// </summary>
    public void Write32(uint address, uint value)
    {
        address = EnsureEvenBusAddress(address, ".l");
        Write8(address, (byte)(value >> 24));
        Write8(EffectiveAddressMath.NormalizeAddress24(address + 1), (byte)((value >> 16) & 0xFF));
        Write8(EffectiveAddressMath.NormalizeAddress24(address + 2), (byte)((value >> 8) & 0xFF));
        Write8(EffectiveAddressMath.NormalizeAddress24(address + 3), (byte)(value & 0xFF));
    }

    /// <summary>
    /// Pushes a 16-bit value onto the active stack using pre-decrement semantics.
    /// </summary>
    public void Push16(ushort value)
    {
        var newStackPointer = Registers.StackPointer - 2;
        if ((newStackPointer & 1) != 0)
            throw new AddressErrorException(newStackPointer, ".w");

        Registers.StackPointer = newStackPointer;
        Write16(newStackPointer, value);
    }

    /// <summary>
    /// Pushes a 32-bit value onto the active stack using pre-decrement semantics.
    /// </summary>
    public void Push32(uint value)
    {
        var newStackPointer = Registers.StackPointer - 4;
        if ((newStackPointer & 1) != 0)
            throw new AddressErrorException(newStackPointer, ".l");

        Registers.StackPointer = newStackPointer;
        Write32(newStackPointer, value);
    }

    /// <summary>
    /// Pops a 16-bit value from the active stack using post-increment semantics.
    /// </summary>
    public ushort Pop16()
    {
        var stackPointer = Registers.StackPointer;
        var value = Read16(stackPointer);
        Registers.StackPointer = stackPointer + 2;
        return value;
    }

    /// <summary>
    /// Pops a 32-bit value from the active stack using post-increment semantics.
    /// </summary>
    public uint Pop32()
    {
        var stackPointer = Registers.StackPointer;
        var value = Read32(stackPointer);
        Registers.StackPointer = stackPointer + 4;
        return value;
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

    /// <summary>
    /// Flushes stale queue entries by consuming two fetch slots from the current PC.
    /// </summary>
    public void RefreshPrefetchQueue()
    {
        _ = FetchPcWord();
        _ = FetchPcWord();
    }

    /// <summary>
    /// Executes <c>RTE</c> behavior by restoring SR and PC from the current exception frame.
    /// </summary>
    public void ExecuteReturnFromException()
    {
        if (!Registers.IsSupervisor)
        {
            EnterPrivilegeViolation();
            return;
        }

        var stackPointer = Registers.StackPointer;
        var restoredStatus = (ushort)(Read16(stackPointer) & ValidStatusRegisterMask);
        var returnAddress = Read32(stackPointer + 2);
        if ((returnAddress & 1) != 0)
            throw new AddressErrorException(returnAddress, ".w");

        Registers.StackPointer = stackPointer + 6;
        Registers.StatusRegister = restoredStatus;
        Registers.ProgramCounter = returnAddress;
        RefreshPrefetchQueue();
    }

    /// <summary>
    /// Enters privilege-violation exception flow for a privileged instruction executed in user mode.
    /// </summary>
    public void EnterPrivilegeViolation()
    {
        var oldStatus = (ushort)(Registers.StatusRegister & ValidStatusRegisterMask);
        var oldPc = unchecked(GetPcRelativeBaseAddress() - 2);

        // Switch to supervisor stack before building the exception frame.
        Registers.IsSupervisor = true;
        Push32(oldPc);
        Push16(oldStatus);

        // Exception entry sets supervisor mode and clears trace.
        Registers.StatusRegister = (ushort)((oldStatus & ~TraceFlagMask) | SupervisorFlagMask);
        Registers.ProgramCounter = Read32(PrivilegeViolationVectorAddress);
        RefreshPrefetchQueue();
    }

    private static uint ValidateEvenAddress(uint address) =>
        (address & 1) == 0 ? address : throw new InvalidOperationException($"Odd address fetch at 0x{address:X6}.");

    private static uint EnsureEvenBusAddress(uint address, string size)
    {
        var normalized = EffectiveAddressMath.NormalizeAddress24(address);
        if ((normalized & 1) == 0)
            return normalized;

        throw new AddressErrorException(normalized, size);
    }

    private ushort FetchPrefetchedWord()
    {
        var value = m_prefetch0;
        var fetchAddress = ValidateEvenAddress(Registers.ProgramCounter);
        var fetchedWord = ReadInstructionWord(fetchAddress);

        // Shift queue and refill the second slot from the next prefetch address.
        m_prefetch0 = m_prefetch1;
        m_prefetch1 = fetchedWord;
        Registers.ProgramCounter = fetchAddress + 2;
        return value;
    }

    private ushort ReadInstructionWord(uint address)
    {
        var hi = Read8(address);
        var lo = Read8(address + 1);
        return (ushort)((hi << 8) | lo);
    }
}
