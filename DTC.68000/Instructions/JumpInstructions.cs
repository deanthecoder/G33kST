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

    /// <summary>
    /// Decodes jump-family opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0100 1110 11 mmm rrr = JMP <ea>.
        if ((opcode & 0xFFC0) != 0x4EC0)
            return null;

        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        return EffectiveAddressControlResolver.SupportsControlTarget(ea) ? InstrJmp : null;
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

        // Flush stale prefetch contents by consuming two fetch slots at the new PC.
        _ = cpu.FetchPcWord();
        _ = cpu.FetchPcWord();
    }
}
