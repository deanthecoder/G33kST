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
/// System/control instruction definitions.
/// </summary>
public static class SystemInstructions
{
    private const ushort ValidStatusRegisterMask = 0xA71F;
    private const ushort ConditionCodeRegisterMask = 0x001F;
    private const ushort TraceFlagMask = 0x8000;
    private const ushort SupervisorFlagMask = 0x2000;
    private const uint CheckInstructionVectorAddress = 0x000018;
    private const uint IllegalInstructionVectorAddress = 0x000010;
    private const uint TrapOnOverflowVectorAddress = 0x00001C;
    private const uint LineAEmulatorVectorAddress = 0x000028;
    private const uint LineFEmulatorVectorAddress = 0x00002C;
    private const uint TrapInstructionVectorBaseAddress = 0x000080;
    private const ushort TrapInstructionVectorMask = 0x000F;

    private static readonly Instruction InstrIllegal = new("ILLEGAL", ExecuteIllegalInstruction);
    private static readonly Instruction InstrChkWord = new("CHK.W <ea>,Dn", ExecuteCheckWord);
    private static readonly Instruction InstrLineA = new("LINEA", ExecuteLineAEmulator);
    private static readonly Instruction InstrLineF = new("LINEF", ExecuteLineFEmulator);
    private static readonly Instruction InstrNop = new("NOP", static (_, _) => { });
    private static readonly Instruction InstrTrap = new("TRAP #<vector>", ExecuteTrap);
    private static readonly Instruction InstrTrapv = new("TRAPV", ExecuteTrapOnOverflow);
    private static readonly Instruction InstrStop = new("STOP #<imm16>", ExecuteStop);
    private static readonly Instruction InstrReset = new("RESET", ExecuteReset);
    private static readonly Instruction InstrRtr = new("RTR", ExecuteReturnAndRestore);
    private static readonly Instruction InstrRts = new("RTS", ExecuteReturnFromSubroutine);
    private static readonly Instruction InstrRte = new("RTE", static (cpu, _) => cpu.ExecuteReturnFromException());
    private static readonly Instruction InstrSwap = new("SWAP Dn", ExecuteSwap);
    private static readonly Instruction InstrExtWord = new("EXT.W Dn", ExecuteExtWord);
    private static readonly Instruction InstrExtLong = new("EXT.L Dn", ExecuteExtLong);
    private static readonly Instruction InstrMoveFromStatusRegister = new("MOVE SR,<ea>", ExecuteMoveFromStatusRegister);
    private static readonly Instruction InstrMoveToConditionCodeRegister = new("MOVE <ea>,CCR", ExecuteMoveToConditionCodeRegister);
    private static readonly Instruction InstrMoveToStatusRegister = new("MOVE <ea>,SR", ExecuteMoveToStatusRegister);
    private static readonly Instruction InstrMoveToUserStackPointer = new("MOVE An,USP", ExecuteMoveToUserStackPointer);
    private static readonly Instruction InstrMoveFromUserStackPointer = new("MOVE USP,An", ExecuteMoveFromUserStackPointer);

    /// <summary>
    /// Decodes system/control opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0100 0000 11 mmm rrr = MOVE SR,<ea>.
        if ((opcode & 0xFFC0) == 0x40C0)
        {
            var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
            return EffectiveAddressWordAccess.SupportsWordWrite(destination) ? InstrMoveFromStatusRegister : null;
        }

        // 0100 0100 11 mmm rrr = MOVE <ea>,CCR.
        if ((opcode & 0xFFC0) == 0x44C0)
        {
            var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
            return SupportsMoveToStatusSource(source) ? InstrMoveToConditionCodeRegister : null;
        }

        // 0100 0110 11 mmm rrr = MOVE <ea>,SR.
        if ((opcode & 0xFFC0) == 0x46C0)
        {
            var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
            return SupportsMoveToStatusSource(source) ? InstrMoveToStatusRegister : null;
        }

        // 0100 1110 0110 0 rrr = MOVE Ar,USP.
        if ((opcode & 0xFFF8) == 0x4E60)
            return InstrMoveToUserStackPointer;

        // 0100 1110 0110 1 rrr = MOVE USP,Ar.
        if ((opcode & 0xFFF8) == 0x4E68)
            return InstrMoveFromUserStackPointer;

        // 0100 ddd 110 mmm rrr = CHK.W <ea>,Dn.
        if ((opcode & 0xF1C0) == 0x4180)
        {
            var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
            return EffectiveAddressWordAccess.SupportsWordRead(source) ? InstrChkWord : null;
        }

        if (opcode == 0x4AFC)
            return InstrIllegal;

        // 0100 1110 0100 vvvv = TRAP #n.
        if ((opcode & 0xFFF0) == 0x4E40)
            return InstrTrap;

        // 0100 1000 0100 0 nnn = SWAP Dn.
        if ((opcode & 0xFFF8) == 0x4840)
            return InstrSwap;

        // 0100 1000 1000 0 nnn = EXT.W Dn.
        if ((opcode & 0xFFF8) == 0x4880)
            return InstrExtWord;

        // 0100 1000 1100 0 nnn = EXT.L Dn.
        if ((opcode & 0xFFF8) == 0x48C0)
            return InstrExtLong;

        return opcode switch
        {
            0x4E70 => InstrReset,
            0x4E71 => InstrNop,
            0x4E72 => InstrStop,
            0x4E73 => InstrRte,
            0x4E75 => InstrRts,
            0x4E76 => InstrTrapv,
            0x4E77 => InstrRtr,
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>RESET</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteReset(Cpu cpu, ushort opcode)
    {
        if (EnsureSupervisor(cpu))
        {
            // TODO: Model RESET bus/device side effects once memory-mapped hardware is wired.
        }
    }

    /// <summary>
    /// Executes <c>STOP #&lt;imm16&gt;</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteStop(Cpu cpu, ushort opcode)
    {
        if (!EnsureSupervisor(cpu))
            return;

        // TODO: Replace this with a real halted CPU state that resumes on eligible interrupt.
        // In this prefetch model, tests expect STOP to leave PC on the STOP instruction slot.
        var haltedProgramCounter = cpu.Registers.ProgramCounter - 2;
        var statusRegister = cpu.FetchPcWord();
        cpu.Registers.StatusRegister = (ushort)(statusRegister & ValidStatusRegisterMask);
        cpu.Registers.ProgramCounter = haltedProgramCounter;
    }

    /// <summary>
    /// Executes <c>MOVE SR,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteMoveFromStatusRegister(Cpu cpu, ushort opcode)
    {
        var destination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        EffectiveAddressWordAccess.WriteWord(cpu, destination, cpu.Registers.StatusRegister);
    }

    /// <summary>
    /// Executes <c>MOVE &lt;ea&gt;,CCR</c>.
    /// </summary>
    private static void ExecuteMoveToConditionCodeRegister(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var value = (ushort)(EffectiveAddressWordAccess.ReadWord(cpu, source) & ConditionCodeRegisterMask);
        var statusRegister = cpu.Registers.StatusRegister;
        cpu.Registers.StatusRegister = (ushort)((statusRegister & ~ConditionCodeRegisterMask) | value);
    }

    /// <summary>
    /// Executes <c>MOVE &lt;ea&gt;,SR</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteMoveToStatusRegister(Cpu cpu, ushort opcode)
    {
        if (!EnsureSupervisor(cpu))
            return;

        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var value = EffectiveAddressWordAccess.ReadWord(cpu, source);
        cpu.Registers.StatusRegister = (ushort)(value & ValidStatusRegisterMask);
    }

    /// <summary>
    /// Executes <c>MOVE An,USP</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteMoveToUserStackPointer(Cpu cpu, ushort opcode)
    {
        if (!EnsureSupervisor(cpu))
            return;

        var sourceRegisterIndex = opcode & 0x07;
        cpu.Registers.UserStackPointer = cpu.Registers.GetAddressRegister(sourceRegisterIndex);
    }

    /// <summary>
    /// Executes <c>MOVE USP,An</c>. Requires supervisor privilege.
    /// </summary>
    private static void ExecuteMoveFromUserStackPointer(Cpu cpu, ushort opcode)
    {
        if (!EnsureSupervisor(cpu))
            return;

        var destinationRegisterIndex = opcode & 0x07;
        cpu.Registers.SetAddressRegister(destinationRegisterIndex, cpu.Registers.UserStackPointer);
    }

    /// <summary>
    /// Executes <c>EXT.W Dn</c> by sign-extending low byte into low word of Dn.
    /// </summary>
    private static void ExecuteExtWord(Cpu cpu, ushort opcode)
    {
        var registerIndex = opcode & 0x07;
        var value = cpu.Registers.GetDataRegister(registerIndex);
        var resultWord = (ushort)(sbyte)(byte)value;
        var result = (value & 0xFFFF_0000) | resultWord;
        cpu.Registers.SetDataRegister(registerIndex, result);
        FlagMath.ApplyLogicalWord(cpu.Registers, resultWord);
    }

    /// <summary>
    /// Executes <c>EXT.L Dn</c> by sign-extending low word into full 32-bit Dn.
    /// </summary>
    private static void ExecuteExtLong(Cpu cpu, ushort opcode)
    {
        var registerIndex = opcode & 0x07;
        var value = cpu.Registers.GetDataRegister(registerIndex);
        var result = (uint)(short)(ushort)value;
        cpu.Registers.SetDataRegister(registerIndex, result);
        FlagMath.ApplyLogicalLong(cpu.Registers, result);
    }

    /// <summary>
    /// Executes <c>SWAP Dn</c> by exchanging high/low words in Dn and updating NZVC as a long logical result.
    /// </summary>
    private static void ExecuteSwap(Cpu cpu, ushort opcode)
    {
        var registerIndex = opcode & 0x07;
        var value = cpu.Registers.GetDataRegister(registerIndex);
        var result = (value << 16) | (value >> 16);
        cpu.Registers.SetDataRegister(registerIndex, result);
        FlagMath.ApplyLogicalLong(cpu.Registers, result);
    }

    /// <summary>
    /// Decodes the LINEA emulator-trap opcode family.
    /// </summary>
    public static Instruction TryDecodeLineA(ushort opcode) =>
        (opcode & 0xF000) == 0xA000 ? InstrLineA : null;

    /// <summary>
    /// Decodes the LINEF emulator-trap opcode family.
    /// </summary>
    public static Instruction TryDecodeLineF(ushort opcode) =>
        (opcode & 0xF000) == 0xF000 ? InstrLineF : null;

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
        cpu.RefreshPrefetchQueue();
    }

    /// <summary>
    /// Executes <c>RTR</c> by restoring CCR from stack, then popping return PC.
    /// </summary>
    private static void ExecuteReturnAndRestore(Cpu cpu, ushort opcode)
    {
        var restoredCcr = (ushort)(cpu.Pop16() & ConditionCodeRegisterMask);
        var returnAddress = cpu.Pop32();
        if ((returnAddress & 1) != 0)
            throw new AddressErrorException(returnAddress, ".w");

        var highStatusByte = (ushort)(cpu.Registers.StatusRegister & 0xFF00);
        cpu.Registers.StatusRegister = (ushort)(highStatusByte | restoredCcr);
        cpu.Registers.ProgramCounter = returnAddress;
        cpu.RefreshPrefetchQueue();
    }

    /// <summary>
    /// Executes <c>TRAPV</c>, entering trap vector 7 when overflow is set.
    /// </summary>
    private static void ExecuteTrapOnOverflow(Cpu cpu, ushort opcode)
    {
        if (cpu.Registers.OverflowFlag)
            EnterExceptionVector(cpu, TrapOnOverflowVectorAddress);
    }

    /// <summary>
    /// Executes <c>CHK.W &lt;ea&gt;,Dn</c> and enters vector 6 when Dn is outside <c>0..bound</c>.
    /// </summary>
    private static void ExecuteCheckWord(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var bound = (short)EffectiveAddressWordAccess.ReadWord(cpu, source);
        var registerIndex = (opcode >> 9) & 0x07;
        var value = (short)cpu.Registers.GetDataRegister(registerIndex);
        FlagMath.ApplyCheck(cpu.Registers, value < 0);
        if (value < 0)
        {
            EnterExceptionVector(cpu, CheckInstructionVectorAddress);
            return;
        }

        if (value > bound)
            EnterExceptionVector(cpu, CheckInstructionVectorAddress);
    }

    /// <summary>
    /// Executes <c>ILLEGAL</c>, entering illegal-instruction vector 4.
    /// </summary>
    private static void ExecuteIllegalInstruction(Cpu cpu, ushort opcode) =>
        EnterExceptionVector(cpu, IllegalInstructionVectorAddress, useCurrentInstructionAddress: true);

    /// <summary>
    /// Executes a line-A emulator trap by entering vector 10.
    /// </summary>
    private static void ExecuteLineAEmulator(Cpu cpu, ushort opcode) =>
        EnterExceptionVector(cpu, LineAEmulatorVectorAddress, useCurrentInstructionAddress: true);

    /// <summary>
    /// Executes a line-F emulator trap by entering vector 11.
    /// </summary>
    private static void ExecuteLineFEmulator(Cpu cpu, ushort opcode) =>
        EnterExceptionVector(cpu, LineFEmulatorVectorAddress, useCurrentInstructionAddress: true);

    /// <summary>
    /// Executes <c>TRAP #n</c>, entering trap vector <c>32 + n</c>.
    /// </summary>
    private static void ExecuteTrap(Cpu cpu, ushort opcode)
    {
        var trapNumber = (uint)(opcode & TrapInstructionVectorMask);
        var vectorAddress = TrapInstructionVectorBaseAddress + (trapNumber << 2);
        EnterExceptionVector(cpu, vectorAddress);
    }

    /// <summary>
    /// Enters an exception vector by stacking old PC/SR, forcing supervisor mode, then loading vector PC.
    /// </summary>
    private static void EnterExceptionVector(Cpu cpu, uint vectorAddress, bool useCurrentInstructionAddress = false)
    {
        var oldStatus = cpu.Registers.StatusRegister;
        var oldPc = cpu.GetPcRelativeBaseAddress();
        if (useCurrentInstructionAddress)
            oldPc = unchecked(oldPc - 2);

        cpu.Registers.IsSupervisor = true;
        cpu.Push32(oldPc);
        cpu.Push16(oldStatus);
        cpu.Registers.StatusRegister = (ushort)((oldStatus & ~TraceFlagMask) | SupervisorFlagMask);
        cpu.Registers.ProgramCounter = cpu.Read32(vectorAddress);
        cpu.RefreshPrefetchQueue();
    }

    private static bool EnsureSupervisor(Cpu cpu)
    {
        if (cpu.Registers.IsSupervisor)
            return true;

        cpu.EnterPrivilegeViolation();
        return false;
    }

    private static bool SupportsMoveToStatusSource(EffectiveAddress source) =>
        EffectiveAddressSupport.SupportsRead(source, allowsAddressRegisterDirect: false);
}
