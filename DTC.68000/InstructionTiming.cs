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
using DTC.M68000.Instructions;

namespace DTC.M68000;

/// <summary>
/// Centralized "phase 1" instruction timing helpers.
/// This is intentionally coarse: only a subset of instruction families currently provide
/// explicit cycle counts, and anything else falls back to a baseline per instruction.
/// Values are based on the classic MC68000 timing tables (JMP/JSR/LEA/PEA, misc, conditional).
/// </summary>
public static class InstructionTiming
{
    /// <summary>
    /// Fallback cost for instructions that do not yet provide an explicit timing path.
    /// </summary>
    public const uint BaselineUntimedInstructionCycles = 4;

    /// <summary>
    /// Applies baseline timing when an instruction did not account for cycles itself.
    /// </summary>
    public static void ApplyFallbackIfUntimed(Cpu cpu, long cyclesBeforeInstruction)
    {
        if (cpu.CyclesSinceCpuStart != cyclesBeforeInstruction)
            return;

        cpu.InternalWait(BaselineUntimedInstructionCycles);
    }

    /// <summary>
    /// Returns the cycle cost used for control-address EA calculations.
    /// Used by JMP/JSR/LEA/PEA style operations where the table format is "base + &lt;ea&gt;".
    /// </summary>
    public static uint GetControlEffectiveAddressCycles(EffectiveAddress effectiveAddress) =>
        effectiveAddress.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => 0,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => 4,
            EffectiveAddressMode.AddressRegisterIndirectIndex => 6,
            EffectiveAddressMode.Other when effectiveAddress.Register is 0 or 2 => 4,
            EffectiveAddressMode.Other when effectiveAddress.Register is 1 => 8,
            EffectiveAddressMode.Other when effectiveAddress.Register is 3 => 6,
            _ => 0
        };

    /// <summary>
    /// Returns the effective-address timing cost used by most data-path operations.
    /// This is the phase-1 approximation for " + &lt;ea&gt; " entries in arithmetic/logical tables.
    /// </summary>
    public static uint GetDataEffectiveAddressCycles(OperandSize size, EffectiveAddress effectiveAddress)
    {
        var mode = effectiveAddress.Mode;
        var register = effectiveAddress.Register;
        if (size == OperandSize.Long)
        {
            return mode switch
            {
                EffectiveAddressMode.DataRegisterDirect => 0,
                EffectiveAddressMode.AddressRegisterDirect => 0,
                EffectiveAddressMode.AddressRegisterIndirect => 8,
                EffectiveAddressMode.AddressRegisterIndirectPostIncrement => 8,
                EffectiveAddressMode.AddressRegisterIndirectPreDecrement => 10,
                EffectiveAddressMode.AddressRegisterIndirectDisplacement => 12,
                EffectiveAddressMode.AddressRegisterIndirectIndex => 14,
                EffectiveAddressMode.Other when register == 0 => 12,
                EffectiveAddressMode.Other when register == 1 => 16,
                EffectiveAddressMode.Other when register == 2 => 12,
                EffectiveAddressMode.Other when register == 3 => 14,
                EffectiveAddressMode.Other when register == 4 => 8,
                _ => 0
            };
        }

        return mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => 0,
            EffectiveAddressMode.AddressRegisterDirect => 0,
            EffectiveAddressMode.AddressRegisterIndirect => 4,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => 4,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => 6,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => 8,
            EffectiveAddressMode.AddressRegisterIndirectIndex => 10,
            EffectiveAddressMode.Other when register == 0 => 8,
            EffectiveAddressMode.Other when register == 1 => 12,
            EffectiveAddressMode.Other when register == 2 => 8,
            EffectiveAddressMode.Other when register == 3 => 10,
            EffectiveAddressMode.Other when register == 4 => 4,
            _ => 0
        };
    }

    /// <summary>
    /// Returns Bcc timing from branch outcome and displacement form.
    /// </summary>
    public static uint GetConditionalBranchCycles(bool branchTaken, bool usedExtensionWord)
    {
        if (branchTaken)
            return 10;

        return usedExtensionWord ? 12u : 8u;
    }
}
