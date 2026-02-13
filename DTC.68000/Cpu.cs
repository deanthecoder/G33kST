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
    private const ushort InterruptPriorityMaskBits = 0x0700;
    private const uint AddressErrorVectorAddress = 0x00000C;
    private const uint TraceVectorAddress = 0x000024;
    private const uint PrivilegeViolationVectorAddress = 0x000020;
    private const uint SpuriousInterruptVectorAddress = 0x000060;
    private const uint InterruptAutovectorBaseAddress = 0x000060;
    private const uint StopIdleCyclesPerStep = 4;

    // 0x2700 = supervisor mode with IPL 7, trace off, and XNZVC clear.
    private const ushort InitialStatusRegister = 0x2700;

    private ushort m_prefetch0;
    private ushort m_prefetch1;
    private bool m_hasPrefetch;
    private bool m_isStopped;
    private uint m_stoppedResumeProgramCounter;
    private byte m_pendingInterruptLevel;

    /// <summary>
    /// Gets the active CPU register set.
    /// </summary>
    public Registers Registers { get; }

    /// <summary>
    /// Gets the total number of cycles accumulated since the last reset.
    /// </summary>
    public long CyclesSinceCpuStart { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the CPU is halted by a <c>STOP</c> instruction.
    /// </summary>
    public bool IsStopped => m_isStopped;

    /// <summary>
    /// Optional callback for interrupt acknowledge cycles.
    /// This hook lets machine/bus code decide whether an interrupt resolves as an autovector,
    /// a spurious interrupt, or a device-supplied vector number.
    /// </summary>
    public Func<byte, InterruptAcknowledgeResult> InterruptAcknowledge { get; set; }

    /// <summary>
    /// Enables post-instruction trace exceptions when the T bit is set.
    /// It is off by default so existing single-step tests can stay stable while trace behavior is phased in.
    /// </summary>
    public bool EnableTraceExceptions { get; set; }

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
        m_isStopped = false;
        m_stoppedResumeProgramCounter = 0;
        m_pendingInterruptLevel = 0;
    }

    /// <summary>
    /// Adds internal CPU wait cycles.
    /// </summary>
    public void InternalWait(uint cycles) =>
        CyclesSinceCpuStart += cycles;

    /// <summary>
    /// Latches a pending external interrupt request level (1-7).
    /// The CPU checks this latch at instruction boundaries to model asynchronous interrupt delivery
    /// without requiring cycle-level bus scheduling yet.
    /// </summary>
    public void RequestInterrupt(byte level)
    {
        if (level is < 1 or > 7)
            throw new ArgumentOutOfRangeException(nameof(level), "Interrupt level must be in the range 1..7.");
        if (level > m_pendingInterruptLevel)
            m_pendingInterruptLevel = level;
    }

    /// <summary>
    /// Seeds the two-word prefetch queue used by single-step test vectors.
    /// </summary>
    public void SeedPrefetch(ushort firstWord, ushort secondWord)
    {
        m_prefetch0 = firstWord;
        m_prefetch1 = secondWord;
        m_hasPrefetch = true;

        // Single-step tests reuse a CPU instance across many independent cases.
        // Seeding a new prefetch pair defines a fresh instruction context, so clear transient latches.
        m_isStopped = false;
        m_stoppedResumeProgramCounter = 0;
        m_pendingInterruptLevel = 0;
    }

    /// <summary>
    /// Fetches an instruction word from PC and advances PC by two.
    /// </summary>
    public ushort FetchPcWord()
    {
        // Single-step vectors provide queue contents separately, so consume that first.
        if (m_hasPrefetch)
            return FetchPrefetchedWord();

        var address = ValidateEvenProgramAddress(Registers.ProgramCounter);
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
        var normalized = EffectiveAddressMath.NormalizeAddress24(address);
        var value = Bus.Read8(normalized);
        NotifyMemoryRead(normalized, value);
        return value;
    }

    /// <summary>
    /// Writes one byte to bus memory.
    /// </summary>
    public override void Write8(uint address, byte value)
    {
        var normalized = EffectiveAddressMath.NormalizeAddress24(address);
        Bus.Write8(normalized, value);
        NotifyMemoryWrite(normalized, value);
    }

    /// <summary>
    /// Reads a 16-bit value from memory in big-endian order.
    /// </summary>
    public ushort Read16(uint address)
    {
        address = EnsureEvenBusAddress(address, ".w", isRead: true);
        var hi = Read8(address);
        var lo = Read8(address + 1);
        return (ushort)((hi << 8) | lo);
    }

    /// <summary>
    /// Writes a 16-bit value to memory in big-endian order.
    /// </summary>
    public void Write16(uint address, ushort value)
    {
        address = EnsureEvenBusAddress(address, ".w", isRead: false);
        Write8(address, (byte)(value >> 8));
        Write8(address + 1, (byte)(value & 0xFF));
    }

    /// <summary>
    /// Reads a 32-bit value from memory in big-endian order.
    /// </summary>
    public uint Read32(uint address)
    {
        address = EnsureEvenBusAddress(address, ".l", isRead: true);
        var b0 = Read8(address);
        var b1 = Read8(address + 1);
        var b2 = Read8(address + 2);
        var b3 = Read8(address + 3);
        return ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
    }

    /// <summary>
    /// Writes a 32-bit value to memory in big-endian order.
    /// </summary>
    public void Write32(uint address, uint value)
    {
        address = EnsureEvenBusAddress(address, ".l", isRead: false);
        Write8(address, (byte)(value >> 24));
        Write8(address + 1, (byte)((value >> 16) & 0xFF));
        Write8(address + 2, (byte)((value >> 8) & 0xFF));
        Write8(address + 3, (byte)(value & 0xFF));
    }

    /// <summary>
    /// Pushes a 16-bit value onto the active stack using pre-decrement semantics.
    /// </summary>
    public void Push16(ushort value)
    {
        var newStackPointer = Registers.StackPointer - 2;
        if ((newStackPointer & 1) != 0)
            throw new AddressErrorException(newStackPointer, ".w", isRead: false);

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
            throw new AddressErrorException(newStackPointer, ".l", isRead: false);

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
        if (TryHandlePendingInterrupt())
        {
            NotifyAfterStep();
            return;
        }

        if (m_isStopped)
        {
            // STOP halts instruction execution but machine time still advances.
            InternalWait(StopIdleCyclesPerStep);
            NotifyAfterStep();
            return;
        }

        var opcodeAddress = Registers.ProgramCounter;
        ushort opcode = 0;
        var traceWasEnabled = Registers.TraceFlag;
        var cyclesBeforeInstruction = CyclesSinceCpuStart;
        try
        {
            opcode = FetchPcWord();
            var instruction = InstructionDecoder.Decode(opcode) ?? throw new NotImplementedException($"Opcode 0x{opcode:X4} is not implemented.");
            var instructionText = InstructionTraceTextFormatter.Format(opcode, instruction, this, opcodeAddress);
            NotifyBeforeInstruction(opcodeAddress, opcode, instructionText);
            instruction.Execute(this, opcode);
            if (EnableTraceExceptions && traceWasEnabled && Registers.TraceFlag)
                EnterTraceException();

            // Instructions with explicit timing call InternalWait themselves.
            // Everything else currently uses the baseline fallback.
            InstructionTiming.ApplyFallbackIfUntimed(this, cyclesBeforeInstruction);
        }
        catch (AddressErrorException ex)
        {
            EnterAddressError(ex, opcode);
        }

        NotifyAfterStep();
    }

    /// <summary>
    /// Flushes stale queue entries by consuming two fetch slots from the current PC.
    /// </summary>
    public void RefreshPrefetchQueue()
    {
        if (!m_hasPrefetch)
            return;

        _ = FetchPcWord();
        _ = FetchPcWord();
    }

    /// <summary>
    /// Halts instruction execution until an eligible interrupt is accepted.
    /// </summary>
    public void EnterStoppedState(uint resumeProgramCounter)
    {
        m_isStopped = true;
        m_stoppedResumeProgramCounter = resumeProgramCounter;
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
        Registers.StackPointer = stackPointer + 6;
        Registers.StatusRegister = restoredStatus;
        if ((returnAddress & 1) != 0)
            throw new AddressErrorException(returnAddress, ".w", isRead: true, isProgramAccess: true);

        Registers.ProgramCounter = returnAddress;
        RefreshPrefetchQueue();
        InternalWait(20);
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

    /// <summary>
    /// Accepts a pending interrupt (if one is eligible) and enters the resolved exception vector.
    /// This centralizes interrupt mask checks and acknowledge resolution so normal instruction execution
    /// can remain focused on opcode semantics.
    /// </summary>
    private bool TryHandlePendingInterrupt()
    {
        if (m_pendingInterruptLevel == 0)
            return false;

        var level = m_pendingInterruptLevel;
        var isAccepted = level == 7 || level > Registers.InterruptPriorityMask;
        if (!isAccepted)
            return false;

        var interruptReturnProgramCounter = m_isStopped
            ? m_stoppedResumeProgramCounter
            : Registers.ProgramCounter;
        m_pendingInterruptLevel = 0;
        m_isStopped = false;
        m_stoppedResumeProgramCounter = 0;
        var acknowledgeResult = InterruptAcknowledge?.Invoke(level) ?? InterruptAcknowledgeResult.Autovector();
        var vectorAddress = acknowledgeResult.Type switch
        {
            InterruptAcknowledgeType.Autovector => InterruptAutovectorBaseAddress + ((uint)level << 2),
            InterruptAcknowledgeType.Spurious => SpuriousInterruptVectorAddress,
            InterruptAcknowledgeType.VectorNumber => acknowledgeResult.VectorNumber * 4u,
            _ => throw new ArgumentOutOfRangeException(nameof(acknowledgeResult))
        };

        EnterExceptionVector(vectorAddress, interruptReturnProgramCounter, level);
        return true;
    }

    /// <summary>
    /// Enters the trace exception vector after an instruction completes with trace enabled.
    /// This keeps trace behavior aligned with 68000 flow where tracing happens between instructions.
    /// </summary>
    private void EnterTraceException()
    {
        var oldPc = Registers.ProgramCounter;
        EnterExceptionVector(TraceVectorAddress, oldPc, null);
    }

    /// <summary>
    /// Shared exception entry routine for trace and interrupt vectors.
    /// It performs consistent frame stacking, supervisor transition, and vector fetch so
    /// exception sources do not duplicate subtle state-transition logic.
    /// </summary>
    private void EnterExceptionVector(uint vectorAddress, uint oldPc, byte? interruptPriorityMask)
    {
        var oldStatus = (ushort)(Registers.StatusRegister & ValidStatusRegisterMask);
        Registers.IsSupervisor = true;
        Push32(oldPc);
        Push16(oldStatus);

        var newStatus = (ushort)((oldStatus & ~TraceFlagMask) | SupervisorFlagMask);
        if (interruptPriorityMask.HasValue)
            newStatus = (ushort)((newStatus & ~InterruptPriorityMaskBits) | ((interruptPriorityMask.Value & 0x07) << 8));

        Registers.StatusRegister = newStatus;
        Registers.ProgramCounter = Read32(vectorAddress);
        RefreshPrefetchQueue();
    }

    private void EnterAddressError(AddressErrorException error, ushort opcode)
    {
        var oldStatus = (ushort)(Registers.StatusRegister & ValidStatusRegisterMask);
        var frameProgramCounter = GetPcRelativeBaseAddress();
        if (error.FrameProgramCounterAdjust.HasValue)
            frameProgramCounter = unchecked(frameProgramCounter + (uint)error.FrameProgramCounterAdjust.Value);
        else if (!error.IsRead)
            frameProgramCounter += 2;

        var instructionRegister = error.UsePrefetchInstructionRegister
            ? m_prefetch0
            : opcode != 0
                ? opcode
                : m_prefetch0;
        var functionCode = GetFunctionCode(error.IsProgramAccess);
        var specialStatusWord = BuildSpecialStatusWord(instructionRegister, functionCode, error.IsRead);
        var faultAddress = error.Address | 1u;

        // Address-error exception entry always stacks to supervisor mode.
        Registers.IsSupervisor = true;
        Push32(frameProgramCounter);
        Push16(oldStatus);
        Push16(instructionRegister);
        Push32(faultAddress);
        Push16(specialStatusWord);

        Registers.StatusRegister = (ushort)((oldStatus & ~TraceFlagMask) | SupervisorFlagMask);
        Registers.ProgramCounter = Read32(AddressErrorVectorAddress);
        RefreshPrefetchQueue();
    }

    private byte GetFunctionCode(bool programAccess)
    {
        if (programAccess)
            return Registers.IsSupervisor ? (byte)6 : (byte)2;

        return Registers.IsSupervisor ? (byte)5 : (byte)1;
    }

    private static ushort BuildSpecialStatusWord(ushort instructionRegister, byte functionCode, bool isRead)
    {
        var lowBits = (byte)(functionCode & 0x07);
        if (isRead)
            lowBits |= 0x10;

        return (ushort)((instructionRegister & 0xFFE0) | lowBits);
    }

    private static uint ValidateEvenProgramAddress(uint address) =>
        (address & 1) == 0
            ? address
            : throw new AddressErrorException(address, ".w", isRead: true, isProgramAccess: true);

    private static uint EnsureEvenBusAddress(uint address, string size, bool isRead)
    {
        var normalized = EffectiveAddressMath.NormalizeAddress24(address);
        if ((normalized & 1) == 0)
            return normalized;

        throw new AddressErrorException(address, size, isRead);
    }

    private ushort FetchPrefetchedWord()
    {
        var value = m_prefetch0;
        var fetchAddress = ValidateEvenProgramAddress(Registers.ProgramCounter);
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
