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
/// Evaluates 68000 condition-code selectors (<c>cc</c>) against the current status flags.
/// </summary>
public static class ConditionCodeEvaluator
{
    /// <summary>
    /// Evaluates a condition-code selector.
    /// Condition codes: 0=T, 1=F, 2=HI, 3=LS, 4=CC, 5=CS, 6=NE, 7=EQ,
    /// 8=VC, 9=VS, A=PL, B=MI, C=GE, D=LT, E=GT, F=LE.
    /// </summary>
    public static bool Evaluate(byte conditionCode, Registers registers) =>
        conditionCode switch
        {
            0x0 => true,
            0x1 => false,
            0x2 => !registers.CarryFlag && !registers.ZeroFlag,
            0x3 => registers.CarryFlag || registers.ZeroFlag,
            0x4 => !registers.CarryFlag,
            0x5 => registers.CarryFlag,
            0x6 => !registers.ZeroFlag,
            0x7 => registers.ZeroFlag,
            0x8 => !registers.OverflowFlag,
            0x9 => registers.OverflowFlag,
            0xA => !registers.NegativeFlag,
            0xB => registers.NegativeFlag,
            0xC => registers.NegativeFlag == registers.OverflowFlag,
            0xD => registers.NegativeFlag != registers.OverflowFlag,
            0xE => !registers.ZeroFlag && registers.NegativeFlag == registers.OverflowFlag,
            0xF => registers.ZeroFlag || registers.NegativeFlag != registers.OverflowFlag,
            _ => throw new ArgumentOutOfRangeException(nameof(conditionCode))
        };
}
