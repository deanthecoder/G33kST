// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.M68000.Instructions;

/// <summary>
/// System/control instruction definitions.
/// </summary>
public static class SystemInstructions
{
    private const ushort ConditionCodeRegisterMask = 0x001F;
    private const ushort TraceFlagMask = 0x8000;
    private const ushort SupervisorFlagMask = 0x2000;
    private const uint TrapOnOverflowVectorAddress = 0x00001C;

    private static readonly Instruction InstrNop = new("NOP", static (_, _) => { });
    private static readonly Instruction InstrTrapv = new("TRAPV", ExecuteTrapOnOverflow);
    private static readonly Instruction InstrRtr = new("RTR", ExecuteReturnAndRestore);
    private static readonly Instruction InstrRts = new("RTS", ExecuteReturnFromSubroutine);
    private static readonly Instruction InstrRte = new("RTE", static (cpu, _) => cpu.ExecuteReturnFromException());

    /// <summary>
    /// Decodes system/control opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode) =>
        opcode switch
        {
            0x4E71 => InstrNop,
            0x4E73 => InstrRte,
            0x4E75 => InstrRts,
            0x4E76 => InstrTrapv,
            0x4E77 => InstrRtr,
            _ => null
        };

    /// <summary>
    /// Executes <c>RTS</c> by popping the return address from the active stack into PC.
    /// </summary>
    private static void ExecuteReturnFromSubroutine(Cpu cpu, ushort opcode)
    {
        var stackPointer = cpu.Registers.StackPointer;
        var returnAddress = cpu.Read32(stackPointer);
        cpu.Registers.StackPointer = stackPointer + 4;
        if ((returnAddress & 1) != 0)
            throw new AddressErrorException(returnAddress, ".w");

        cpu.Registers.ProgramCounter = returnAddress;
        cpu.RefreshPrefetchQueue();
    }

    /// <summary>
    /// Executes <c>RTR</c> by restoring CCR from stack, then popping return PC.
    /// </summary>
    private static void ExecuteReturnAndRestore(Cpu cpu, ushort opcode)
    {
        var restoredCcr = (ushort)(cpu.Pop16() & ConditionCodeRegisterMask);
        var returnAddress = cpu.Pop32();
        if ((returnAddress & 1) != 0)
            throw new AddressErrorException(returnAddress, ".w");

        var highStatusByte = (ushort)(cpu.Registers.StatusRegister & 0xFF00);
        cpu.Registers.StatusRegister = (ushort)(highStatusByte | restoredCcr);
        cpu.Registers.ProgramCounter = returnAddress;
        cpu.RefreshPrefetchQueue();
    }

    /// <summary>
    /// Executes <c>TRAPV</c>, entering trap vector 7 when overflow is set.
    /// </summary>
    private static void ExecuteTrapOnOverflow(Cpu cpu, ushort opcode)
    {
        if (!cpu.Registers.OverflowFlag)
            return;

        var oldStatus = cpu.Registers.StatusRegister;
        var oldPc = cpu.GetPcRelativeBaseAddress();
        cpu.Registers.IsSupervisor = true;
        cpu.Push32(oldPc);
        cpu.Push16(oldStatus);
        cpu.Registers.StatusRegister = (ushort)((oldStatus & ~TraceFlagMask) | SupervisorFlagMask);
        cpu.Registers.ProgramCounter = cpu.Read32(TrapOnOverflowVectorAddress);
        cpu.RefreshPrefetchQueue();
    }
}
