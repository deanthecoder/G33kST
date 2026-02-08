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
    private static readonly Instruction InstrNop = new("NOP", static (_, _) => { });
    private static readonly Instruction InstrRts = new("RTS", ExecuteReturnFromSubroutine);

    /// <summary>
    /// Decodes system/control opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode) =>
        opcode switch
        {
            0x4E71 => InstrNop,
            0x4E75 => InstrRts,
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

        // Flush stale queue entries by consuming two fetch slots from the current PC.
        _ = cpu.FetchPcWord();
        _ = cpu.FetchPcWord();
    }
}
