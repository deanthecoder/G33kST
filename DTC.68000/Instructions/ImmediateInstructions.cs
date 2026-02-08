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
/// Immediate-operation instruction decode and execution helpers.
/// </summary>
public static class ImmediateInstructions
{
    private const ushort ValidStatusRegisterMask = 0xA71F;
    private const ushort ConditionCodeRegisterMask = 0x001F;

    private static readonly Instruction InstrOriToCcr = new("ORI #<imm>,CCR", ExecuteOriToCcr);
    private static readonly Instruction InstrOriToSr = new("ORI #<imm>,SR", ExecuteOriToSr);
    private static readonly Instruction InstrAndiToCcr = new("ANDI #<imm>,CCR", ExecuteAndiToCcr);
    private static readonly Instruction InstrAndiToSr = new("ANDI #<imm>,SR", ExecuteAndiToSr);
    private static readonly Instruction InstrEoriToCcr = new("EORI #<imm>,CCR", ExecuteEoriToCcr);
    private static readonly Instruction InstrEoriToSr = new("EORI #<imm>,SR", ExecuteEoriToSr);

    /// <summary>
    /// Decodes immediate opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode) =>
        opcode switch
        {
            0x003C => InstrOriToCcr,
            0x007C => InstrOriToSr,
            0x023C => InstrAndiToCcr,
            0x027C => InstrAndiToSr,
            0x0A3C => InstrEoriToCcr,
            0x0A7C => InstrEoriToSr,
            _ => null
        };

    /// <summary>
    /// Executes <c>ORI #&lt;imm&gt;,CCR</c>.
    /// </summary>
    private static void ExecuteOriToCcr(Cpu cpu, ushort opcode)
    {
        var immediate = (ushort)(cpu.FetchPcWord() & ConditionCodeRegisterMask);
        var currentCcr = (ushort)(cpu.Registers.StatusRegister & ConditionCodeRegisterMask);
        var resultCcr = (ushort)(currentCcr | immediate);
        cpu.Registers.StatusRegister = (ushort)((cpu.Registers.StatusRegister & ~ConditionCodeRegisterMask) | resultCcr);
    }

    /// <summary>
    /// Executes <c>ORI #&lt;imm&gt;,SR</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteOriToSr(Cpu cpu, ushort opcode)
    {
        if (!cpu.Registers.IsSupervisor)
        {
            cpu.EnterPrivilegeViolation();
            return;
        }

        var immediate = cpu.FetchPcWord();
        var result = (ushort)((cpu.Registers.StatusRegister | immediate) & ValidStatusRegisterMask);
        cpu.Registers.StatusRegister = result;
    }

    /// <summary>
    /// Executes <c>ANDI #&lt;imm&gt;,CCR</c>.
    /// </summary>
    private static void ExecuteAndiToCcr(Cpu cpu, ushort opcode)
    {
        var immediate = (ushort)(cpu.FetchPcWord() & ConditionCodeRegisterMask);
        var currentCcr = (ushort)(cpu.Registers.StatusRegister & ConditionCodeRegisterMask);
        var resultCcr = (ushort)(currentCcr & immediate);
        cpu.Registers.StatusRegister = (ushort)((cpu.Registers.StatusRegister & ~ConditionCodeRegisterMask) | resultCcr);
    }

    /// <summary>
    /// Executes <c>ANDI #&lt;imm&gt;,SR</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteAndiToSr(Cpu cpu, ushort opcode)
    {
        if (!cpu.Registers.IsSupervisor)
        {
            cpu.EnterPrivilegeViolation();
            return;
        }

        var immediate = cpu.FetchPcWord();
        var result = (ushort)((cpu.Registers.StatusRegister & immediate) & ValidStatusRegisterMask);
        cpu.Registers.StatusRegister = result;
    }

    /// <summary>
    /// Executes <c>EORI #&lt;imm&gt;,CCR</c>.
    /// </summary>
    private static void ExecuteEoriToCcr(Cpu cpu, ushort opcode)
    {
        var immediate = (ushort)(cpu.FetchPcWord() & ConditionCodeRegisterMask);
        var currentCcr = (ushort)(cpu.Registers.StatusRegister & ConditionCodeRegisterMask);
        var resultCcr = (ushort)(currentCcr ^ immediate);
        cpu.Registers.StatusRegister = (ushort)((cpu.Registers.StatusRegister & ~ConditionCodeRegisterMask) | resultCcr);
    }

    /// <summary>
    /// Executes <c>EORI #&lt;imm&gt;,SR</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteEoriToSr(Cpu cpu, ushort opcode)
    {
        if (!cpu.Registers.IsSupervisor)
        {
            cpu.EnterPrivilegeViolation();
            return;
        }

        var immediate = cpu.FetchPcWord();
        var result = (ushort)((cpu.Registers.StatusRegister ^ immediate) & ValidStatusRegisterMask);
        cpu.Registers.StatusRegister = result;
    }
}
