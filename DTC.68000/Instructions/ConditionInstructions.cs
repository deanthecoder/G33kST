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
/// Condition-code instruction decode and execution helpers.
/// </summary>
public static class ConditionInstructions
{
    private static readonly Instruction InstrSetOnCondition = new("Scc <ea>", ExecuteSetOnCondition);

    /// <summary>
    /// Decodes Scc opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeScc(ushort opcode)
    {
        // 0101 cccc 11 mmm rrr = Scc <ea>.
        if ((opcode & 0xF0C0) != 0x50C0)
            return null;

        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        return EffectiveAddressByteAccess.SupportsByteWrite(destination) ? InstrSetOnCondition : null;
    }

    /// <summary>
    /// Executes <c>Scc &lt;ea&gt;</c>, storing <c>0xFF</c> when the condition is true, else <c>0x00</c>.
    /// </summary>
    private static void ExecuteSetOnCondition(Cpu cpu, ushort opcode)
    {
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var conditionCode = (byte)((opcode >> 8) & 0x0F);
        var isConditionTrue = ConditionCodeEvaluator.Evaluate(conditionCode, cpu.Registers);
        EffectiveAddressByteAccess.WriteByte(cpu, destination, isConditionTrue ? (byte)0xFF : (byte)0x00);
    }
}
