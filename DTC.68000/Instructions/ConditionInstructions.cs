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
    private static readonly Instruction InstrBranchAlways = new("BRA <disp>", ExecuteBranchAlways);
    private static readonly Instruction InstrBranchOnCondition = new("Bcc <disp>", ExecuteBranchOnCondition);
    private static readonly Instruction InstrBranchToSubroutine = new("BSR <disp>", ExecuteBranchToSubroutine);
    private static readonly Instruction InstrDecrementAndBranchOnCondition = new("DBcc Dn,#<disp16>", ExecuteDecrementAndBranchOnCondition);
    private static readonly Instruction InstrSetOnCondition = new("Scc <ea>", ExecuteSetOnCondition);

    /// <summary>
    /// Decodes Bcc/BSR opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeBccOrBsr(ushort opcode)
    {
        // 0110 cccc dddddddd = Bcc/BSR/BRA with 8-bit or extension displacement.
        if ((opcode & 0xF000) != 0x6000)
            return null;

        var conditionCode = (byte)((opcode >> 8) & 0x0F);
        return conditionCode switch
        {
            0x00 => InstrBranchAlways,
            0x01 => InstrBranchToSubroutine,
            _ => conditionCode is >= 0x02 and <= 0x0F ? InstrBranchOnCondition : null
        };
    }

    /// <summary>
    /// Decodes DBcc opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeDbcc(ushort opcode)
    {
        // 0101 cccc 1100 1rrr = DBcc Dn,#<disp16>.
        return (opcode & 0xF0F8) == 0x50C8 ? InstrDecrementAndBranchOnCondition : null;

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

        // Base Scc timing: register-direct is cheaper than memory forms.
        cpu.InternalWait(destination.Mode == EffectiveAddressMode.DataRegisterDirect ? 4u : 8u);
    }

    /// <summary>
    /// Executes <c>Bcc &lt;disp&gt;</c> for condition codes <c>2..15</c>.
    /// </summary>
    private static void ExecuteBranchOnCondition(Cpu cpu, ushort opcode)
    {
        var conditionCode = (byte)((opcode >> 8) & 0x0F);
        var displacement = ReadBranchDisplacement(cpu, opcode, out var usedExtensionWord);
        var isTaken = ConditionCodeEvaluator.Evaluate(conditionCode, cpu.Registers);
        if (isTaken)
            BranchRelative(cpu, displacement, usedExtensionWord, useFaultAddressAsFrameProgramCounter: false);

        // Bcc timing depends on branch outcome and whether displacement uses an extension word.
        cpu.InternalWait(InstructionTiming.GetConditionalBranchCycles(isTaken, usedExtensionWord));
    }

    /// <summary>
    /// Executes <c>BRA &lt;disp&gt;</c>.
    /// </summary>
    private static void ExecuteBranchAlways(Cpu cpu, ushort opcode)
    {
        var displacement = ReadBranchDisplacement(cpu, opcode, out var usedExtensionWord);
        BranchRelative(cpu, displacement, usedExtensionWord, useFaultAddressAsFrameProgramCounter: false);
        // BRA is a fixed-cost branch in this initial timing model.
        cpu.InternalWait(10);
    }

    /// <summary>
    /// Executes <c>BSR &lt;disp&gt;</c> by pushing return PC and branching relative.
    /// </summary>
    private static void ExecuteBranchToSubroutine(Cpu cpu, ushort opcode)
    {
        var displacement = ReadBranchDisplacement(cpu, opcode, out var usedExtensionWord);
        cpu.Push32(cpu.GetPcRelativeBaseAddress());
        BranchRelative(cpu, displacement, usedExtensionWord, useFaultAddressAsFrameProgramCounter: true);
        // BSR includes stack push overhead in addition to branch work.
        cpu.InternalWait(18);
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
        {
            // DBcc with true condition: no decrement/branch path.
            cpu.InternalWait(12);
            return;
        }

        var registerIndex = opcode & 0x07;
        var registerValue = cpu.Registers.GetDataRegister(registerIndex);
        var decrementedLowWord = (ushort)(registerValue - 1);
        if (decrementedLowWord == 0xFFFF)
        {
            cpu.Registers.SetDataRegister(registerIndex, (registerValue & 0xFFFF0000) | decrementedLowWord);
            // DBcc exhausted counter path (no branch).
            cpu.InternalWait(14);
            return;
        }

        // DBcc displacement is relative to the extension-word base in this prefetch model.
        var branchTarget = unchecked((uint)(cpu.Registers.ProgramCounter + displacement - 2));
        if ((branchTarget & 1) != 0)
            throw new AddressErrorException(unchecked(branchTarget - 4), ".w", isRead: true, isProgramAccess: true);

        cpu.Registers.SetDataRegister(registerIndex, (registerValue & 0xFFFF0000) | decrementedLowWord);
        cpu.Registers.ProgramCounter = branchTarget;
        // DBcc branch-taken path.
        cpu.InternalWait(10);
    }

    private static int ReadBranchDisplacement(Cpu cpu, ushort opcode, out bool usedExtensionWord)
    {
        var displacement8 = (sbyte)(opcode & 0xFF);
        if (displacement8 != 0)
        {
            usedExtensionWord = false;
            return displacement8;
        }

        usedExtensionWord = true;
        return (short)cpu.FetchPcWord();
    }

    private static void BranchRelative(Cpu cpu, int displacement, bool usedExtensionWord, bool useFaultAddressAsFrameProgramCounter)
    {
        var baseAddress = cpu.Registers.ProgramCounter;
        if (usedExtensionWord)
            baseAddress = unchecked(baseAddress - 2);

        var branchTarget = unchecked((uint)(baseAddress + displacement));
        if ((branchTarget & 1) != 0)
        {
            var faultAddress = unchecked(branchTarget - 4);
            int? frameProgramCounterAdjust = null;
            if (useFaultAddressAsFrameProgramCounter)
                frameProgramCounterAdjust = unchecked((int)(faultAddress - cpu.GetPcRelativeBaseAddress()));
            else if (usedExtensionWord)
                frameProgramCounterAdjust = -2;

            throw new AddressErrorException(faultAddress, ".w", isRead: true, isProgramAccess: true, frameProgramCounterAdjust: frameProgramCounterAdjust);
        }

        cpu.Registers.ProgramCounter = branchTarget;
    }

}
