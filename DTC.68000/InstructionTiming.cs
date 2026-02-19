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
using System.Numerics;

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
    private const uint BaselineUntimedInstructionCycles = 4;

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
            EffectiveAddressMode.AddressRegisterIndirectIndex => 8,
            EffectiveAddressMode.Other when effectiveAddress.Register is 0 or 2 => 4,
            EffectiveAddressMode.Other when effectiveAddress.Register is 1 => 8,
            EffectiveAddressMode.Other when effectiveAddress.Register is 3 => 8,
            _ => 0
        };

    /// <summary>
    /// Returns control-EA timing used by JMP/JSR tables.
    /// </summary>
    public static uint GetJumpControlEffectiveAddressCycles(EffectiveAddress effectiveAddress) =>
        effectiveAddress.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => 0,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => 2,
            EffectiveAddressMode.AddressRegisterIndirectIndex => 6,
            EffectiveAddressMode.Other when effectiveAddress.Register is 0 or 2 => 2,
            EffectiveAddressMode.Other when effectiveAddress.Register == 1 => 4,
            EffectiveAddressMode.Other when effectiveAddress.Register == 3 => 6,
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

    /// <summary>
    /// Returns register-form shift/rotate timing for the given operand size and count source.
    /// Immediate-count form has a smaller base cost than register-count form on 68000.
    /// </summary>
    public static uint GetRegisterShiftRotateCycles(OperandSize size, int count)
    {
        var effectiveCount = Math.Clamp(count, 0, 63);
        var baseCycles = size == OperandSize.Long ? 8u : 6u;

        return baseCycles + (uint)effectiveCount * 2;
    }

    /// <summary>
    /// Returns memory-form shift/rotate timing for 68000 word-sized destination operations.
    /// The table format is <c>8 + &lt;ea&gt;</c>, where this helper provides the EA component.
    /// </summary>
    public static uint GetMemoryShiftRotateCycles(EffectiveAddress effectiveAddress) =>
        8u + GetDataEffectiveAddressCycles(OperandSize.Word, effectiveAddress);

    /// <summary>
    /// Returns a coarse MOVE timing estimate based on source/destination EA costs.
    /// This follows a simple "read + write + base" model suitable for phase-1 software bring-up.
    /// </summary>
    public static uint GetMoveCycles(OperandSize size, EffectiveAddress source, EffectiveAddress destination)
    {
        const uint baseCycles = 4;
        var cycles = baseCycles
             + GetDataEffectiveAddressCycles(size, source)
             + GetDataEffectiveAddressCycles(size, destination);
        if (destination.Mode == EffectiveAddressMode.AddressRegisterIndirectPreDecrement)
            cycles -= 2;

        return cycles;
    }

    /// <summary>
    /// Returns a coarse MOVEA timing estimate.
    /// MOVEA consumes the source EA and writes only an address register destination.
    /// </summary>
    public static uint GetMoveAddressCycles(OperandSize size, EffectiveAddress source)
    {
        const uint baseCycles = 4;
        return baseCycles + GetDataEffectiveAddressCycles(size, source);
    }

    /// <summary>
    /// Returns a coarse unary-operation timing estimate for CLR/NEG/NEGX/NOT style instructions.
    /// </summary>
    public static uint GetUnaryModifyCycles(OperandSize size, EffectiveAddress destination)
    {
        if (destination.Mode == EffectiveAddressMode.DataRegisterDirect)
            return size == OperandSize.Long ? 6u : 4u;

        var baseCycles = size == OperandSize.Long ? 12u : 8u;
        return baseCycles + GetDataEffectiveAddressCycles(size, destination);
    }

    /// <summary>
    /// Returns CLR timing. CLR has higher memory-form costs than other unary modify instructions.
    /// </summary>
    public static uint GetClearCycles(OperandSize size, EffectiveAddress destination)
    {
        return GetUnaryModifyCycles(size, destination);
    }

    /// <summary>
    /// Returns a coarse unary-read timing estimate for TST.
    /// </summary>
    public static uint GetUnaryTestCycles(OperandSize size, EffectiveAddress source)
    {
        var baseCycles = 4u;
        return baseCycles + GetDataEffectiveAddressCycles(size, source);
    }

    /// <summary>
    /// Returns a coarse TAS timing estimate.
    /// </summary>
    public static uint GetTasCycles(EffectiveAddress destination) =>
        destination.Mode == EffectiveAddressMode.DataRegisterDirect
            ? 4u
            : 10u + GetDataEffectiveAddressCycles(OperandSize.Byte, destination);

    /// <summary>
    /// Returns ADDI/SUBI timing as a simple immediate-overhead plus destination EA model.
    /// </summary>
    public static uint GetAddSubtractImmediateCycles(OperandSize size, EffectiveAddress destination)
    {
        if (destination.Mode == EffectiveAddressMode.DataRegisterDirect)
            return size == OperandSize.Long ? 16u : 8u;

        var baseCycles = size == OperandSize.Long ? 20u : 12u;
        return baseCycles + GetDataEffectiveAddressCycles(size, destination);
    }

    /// <summary>
    /// Returns CMPI timing as compare timing plus immediate extension-word overhead.
    /// </summary>
    public static uint GetCompareImmediateCycles(OperandSize size, EffectiveAddress destination)
    {
        var baseCycles = size == OperandSize.Long
            ? destination.Mode == EffectiveAddressMode.DataRegisterDirect ? 14u : 12u
            : 8u;
        return baseCycles + GetDataEffectiveAddressCycles(size, destination);
    }

    /// <summary>
    /// Returns ORI/ANDI/EORI to CCR/SR timing.
    /// </summary>
    public static uint GetImmediateStatusCycles() =>
        20u;

    /// <summary>
    /// Returns ORI/ANDI/EORI immediate-to-EA timing.
    /// </summary>
    public static uint GetLogicalImmediateCycles(OperandSize size, EffectiveAddress destination)
    {
        if (destination.Mode == EffectiveAddressMode.DataRegisterDirect)
            return size == OperandSize.Long ? 16u : 8u;

        var baseCycles = size == OperandSize.Long ? 20u : 12u;
        return baseCycles + GetDataEffectiveAddressCycles(size, destination);
    }

    /// <summary>
    /// Returns coarse BTST/BCHG/BCLR/BSET timing.
    /// </summary>
    public static uint GetBitOperationCycles(bool modifiesDestination, bool immediateBitNumber, EffectiveAddress destination)
    {
        if (destination.Mode == EffectiveAddressMode.DataRegisterDirect)
        {
            if (!modifiesDestination)
                return immediateBitNumber ? 10u : 6u;
            return immediateBitNumber ? 12u : 8u;
        }

        uint memoryBase;
        if (!modifiesDestination)
            memoryBase = immediateBitNumber ? 8u : 4u;
        else
            memoryBase = immediateBitNumber ? 12u : 8u;

        return memoryBase + GetDataEffectiveAddressCycles(OperandSize.Byte, destination);
    }

    /// <summary>
    /// Returns coarse MOVEM timing from register count and addressing mode.
    /// </summary>
    public static uint GetMovemCycles(OperandSize size, bool memoryToRegisters, EffectiveAddress effectiveAddress, int registerCount)
    {
        var wordsPerRegister = size == OperandSize.Long ? 2u : 1u;
        var transferCyclesPerRegister = size == OperandSize.Long ? 8u : 4u;
        var baseCycles = memoryToRegisters
            ? 12u
            : effectiveAddress.Mode == EffectiveAddressMode.AddressRegisterIndirectPreDecrement
                ? size == OperandSize.Word ? 6u : 4u
                : 8u;
        var extensionCycles = GetMovemEaCycles(effectiveAddress, wordsPerRegister);
        return baseCycles + extensionCycles + (uint)registerCount * transferCyclesPerRegister;
    }

    /// <summary>
    /// Returns Scc timing for register and memory destinations.
    /// </summary>
    public static uint GetSetOnConditionCycles(EffectiveAddress destination) =>
        destination.Mode == EffectiveAddressMode.DataRegisterDirect
            ? 4u
            : 8u + GetDataEffectiveAddressCycles(OperandSize.Byte, destination);

    /// <summary>
    /// Returns CHK.W timing.
    /// </summary>
    public static uint GetCheckCycles(EffectiveAddress source, bool trapped, bool useLongTrapPath = false)
    {
        var eaCycles = GetDataEffectiveAddressCycles(OperandSize.Word, source);
        if (!trapped)
            return 10u + eaCycles;

        return (useLongTrapPath ? 40u : 38u) + eaCycles;
    }

    /// <summary>
    /// Returns MOVEP timing.
    /// </summary>
    public static uint GetMovepCycles(bool isLong) =>
        isLong ? 24u : 16u;

    /// <summary>
    /// Returns EXG timing.
    /// </summary>
    public static uint GetExgCycles() => 6u;

    /// <summary>
    /// Returns ADDX/SUBX timing.
    /// </summary>
    public static uint GetAddSubtractWithExtendCycles(OperandSize size, bool memoryForm)
    {
        if (memoryForm)
            return size == OperandSize.Long ? 30u : 18u;

        return size == OperandSize.Long ? 8u : 4u;
    }

    /// <summary>
    /// Returns ABCD/SBCD timing.
    /// </summary>
    public static uint GetDecimalAddSubtractCycles(bool memoryForm) =>
        memoryForm ? 18u : 6u;

    /// <summary>
    /// Returns NBCD timing.
    /// </summary>
    public static uint GetNegateDecimalCycles(EffectiveAddress destination) =>
        destination.Mode == EffectiveAddressMode.DataRegisterDirect
            ? 6u
            : 8u + GetDataEffectiveAddressCycles(OperandSize.Byte, destination);

    /// <summary>
    /// Returns coarse MULx timing.
    /// </summary>
    public static uint GetUnsignedMultiplyCycles(EffectiveAddress source, ushort multiplier) =>
        38u + (uint)(BitOperations.PopCount(multiplier) * 2) + GetDataEffectiveAddressCycles(OperandSize.Word, source);

    /// <summary>
    /// Returns cycle-accurate MULS.W timing based on source data pattern.
    /// </summary>
    public static uint GetSignedMultiplyCycles(EffectiveAddress source, short multiplier)
    {
        var bits = (ushort)multiplier;
        var transitions = BitOperations.PopCount((uint)((bits ^ (bits << 1)) & 0xFFFF));
        return 38u + (uint)(transitions * 2) + GetDataEffectiveAddressCycles(OperandSize.Word, source);
    }

    /// <summary>
    /// Returns cycle-accurate DIVU.W timing based on dividend/divisor values.
    /// </summary>
    public static uint GetUnsignedDivideCycles(EffectiveAddress source, uint dividend, ushort divisor)
    {
        var eaCycles = GetDataEffectiveAddressCycles(OperandSize.Word, source);
        if (divisor == 0)
            return GetDivideByZeroTrapCycles();

        if (dividend >> 16 >= divisor)
            return 10u + eaCycles;

        uint microcycles = 38;
        var shiftedDivisor = (uint)divisor << 16;
        var work = dividend;
        for (var i = 0; i < 15; i++)
        {
            var previous = work;
            work <<= 1;

            if ((previous & 0x80000000) != 0)
            {
                work -= shiftedDivisor;
            }
            else
            {
                microcycles += 2;
                if (work >= shiftedDivisor)
                {
                    work -= shiftedDivisor;
                    microcycles--;
                }
            }
        }

        return microcycles * 2 + eaCycles;
    }

    /// <summary>
    /// Returns cycle-accurate DIVS.W timing based on dividend/divisor values.
    /// </summary>
    public static uint GetSignedDivideCycles(EffectiveAddress source, int dividend, short divisor)
    {
        var eaCycles = GetDataEffectiveAddressCycles(OperandSize.Word, source);
        if (divisor == 0)
            return GetDivideByZeroTrapCycles();

        uint microcycles = 6;
        if (dividend < 0)
            microcycles++;

        var absoluteDividend = (uint)Math.Abs((long)dividend);
        var absoluteDivisor = (uint)Math.Abs((int)divisor);
        if (absoluteDividend >> 16 >= absoluteDivisor)
            return (microcycles + 2) * 2 + eaCycles;

        var absoluteQuotient = absoluteDividend / absoluteDivisor;
        microcycles += 55;
        if (divisor >= 0)
        {
            if (dividend >= 0)
                microcycles--;
            else
                microcycles++;
        }

        for (var i = 0; i < 15; i++)
        {
            if ((short)absoluteQuotient >= 0)
                microcycles++;
            absoluteQuotient = (absoluteQuotient << 1) & 0xFFFF;
        }

        return microcycles * 2 + eaCycles;
    }

    /// <summary>
    /// Returns divide-by-zero trap entry timing.
    /// </summary>
    public static uint GetDivideByZeroTrapCycles() => 38u;

    private static uint GetMovemEaCycles(EffectiveAddress effectiveAddress, uint wordsPerRegister) =>
        effectiveAddress.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => 0,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => 0,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => 2 * wordsPerRegister,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => 4,
            EffectiveAddressMode.AddressRegisterIndirectIndex => 6,
            EffectiveAddressMode.Other when effectiveAddress.Register is 0 or 2 => 4,
            EffectiveAddressMode.Other when effectiveAddress.Register is 1 => 8,
            EffectiveAddressMode.Other when effectiveAddress.Register == 3 => 6,
            _ => 0
        };
}
