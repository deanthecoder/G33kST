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
    private static readonly Instruction InstrDecrementAndBranchOnCondition = new("DBcc Dn,#<disp16>", ExecuteDecrementAndBranchOnCondition);
    private static readonly Instruction InstrSetOnCondition = new("Scc <ea>", ExecuteSetOnCondition);

    /// <summary>
    /// Decodes DBcc opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeDbcc(ushort opcode)
    {
        // 0101 cccc 1100 1rrr = DBcc Dn,#<disp16>.
        if ((opcode & 0xF0F8) == 0x50C8)
            return InstrDecrementAndBranchOnCondition;
        
        return null;
    }

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

    /// <summary>
    /// Executes <c>DBcc Dn,#&lt;disp16&gt;</c>.
    /// Branches when the condition is false and the decremented low word of Dn is not <c>0xFFFF</c>.
    /// </summary>
    private static void ExecuteDecrementAndBranchOnCondition(Cpu cpu, ushort opcode)
    {
        var conditionCode = (byte)((opcode >> 8) & 0x0F);
        var displacement = (short)cpu.FetchPcWord();
        if (ConditionCodeEvaluator.Evaluate(conditionCode, cpu.Registers))
            return;

        var registerIndex = opcode & 0x07;
        var registerValue = cpu.Registers.GetDataRegister(registerIndex);
        var decrementedLowWord = (ushort)(registerValue - 1);
        cpu.Registers.SetDataRegister(registerIndex, (registerValue & 0xFFFF0000) | decrementedLowWord);
        if (decrementedLowWord == 0xFFFF)
            return;

        // DBcc displacement is relative to the extension-word base in this prefetch model.
        var branchTarget = unchecked((uint)(cpu.Registers.ProgramCounter + displacement - 2));
        if ((branchTarget & 1) != 0)
            throw new AddressErrorException(branchTarget, ".w");

        cpu.Registers.ProgramCounter = branchTarget;
    }
}
