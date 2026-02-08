// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.M68000.Addressing;

namespace DTC.M68000.Instructions;

/// <summary>
/// Branch/jump instruction implementations.
/// </summary>
public static class JumpInstructions
{
    private static readonly Instruction InstrJmp = new("JMP <ea>", ExecuteUnconditionalJump);
    private static readonly Instruction InstrJsr = new("JSR <ea>", ExecuteJumpToSubroutine);

    /// <summary>
    /// Decodes jump-family opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0100 1110 11 mmm rrr = JMP <ea>.
        if ((opcode & 0xFFC0) == 0x4EC0)
        {
            var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
            return EffectiveAddressControlResolver.SupportsControlTarget(ea) ? InstrJmp : null;
        }

        // 0100 1110 10 mmm rrr = JSR <ea>.
        if ((opcode & 0xFFC0) == 0x4E80)
        {
            var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
            return EffectiveAddressControlResolver.SupportsControlTarget(ea) ? InstrJsr : null;
        }

        return null;
    }

    /// <summary>
    /// Executes <c>JMP &lt;ea&gt;</c> by computing the control target address and loading it into PC.
    /// </summary>
    private static void ExecuteUnconditionalJump(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var targetAddress = EffectiveAddressControlResolver.ResolveControlTarget(cpu, ea);
        if ((targetAddress & 1) != 0)
            throw new AddressErrorException(targetAddress, ".w");

        cpu.Registers.ProgramCounter = targetAddress;
        RefreshPrefetch(cpu);
    }

    /// <summary>
    /// Executes <c>JSR &lt;ea&gt;</c> by pushing return PC to the active stack and branching to target.
    /// </summary>
    private static void ExecuteJumpToSubroutine(Cpu cpu, ushort opcode)
    {
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var targetAddress = EffectiveAddressControlResolver.ResolveControlTarget(cpu, ea);
        if ((targetAddress & 1) != 0)
            throw new AddressErrorException(targetAddress, ".w");

        // For prefetch-seeded runs, ProgramCounter tracks the next fetch slot; use prefetch-aware base.
        PushLongToStack(cpu, cpu.GetPcRelativeBaseAddress());
        cpu.Registers.ProgramCounter = targetAddress;
        RefreshPrefetch(cpu);
    }

    /// <summary>
    /// Flushes stale queue entries by consuming two fetch slots from the current PC.
    /// </summary>
    private static void RefreshPrefetch(Cpu cpu)
    {
        _ = cpu.FetchPcWord();
        _ = cpu.FetchPcWord();
    }

    /// <summary>
    /// Pushes a long value to the active stack using pre-decrement semantics.
    /// </summary>
    private static void PushLongToStack(Cpu cpu, uint value)
    {
        var newStackPointer = cpu.Registers.StackPointer - 4;
        if ((newStackPointer & 1) != 0)
            throw new AddressErrorException(newStackPointer, ".l");

        cpu.Registers.StackPointer = newStackPointer;
        cpu.Write32(newStackPointer, value);
    }
}
