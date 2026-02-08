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
    private static readonly Instruction InstrRtr = new("RTR", ExecuteReturnAndRestore);
    private static readonly Instruction InstrRts = new("RTS", ExecuteReturnFromSubroutine);
    private static readonly Instruction InstrRte = new("RTE", static (cpu, _) => cpu.ExecuteReturnFromException());

    /// <summary>
    /// Decodes system/control opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
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

        return opcode switch
        {
            0x4E71 => InstrNop,
            0x4E73 => InstrRte,
            0x4E75 => InstrRts,
            0x4E76 => InstrTrapv,
            0x4E77 => InstrRtr,
            _ => null
        };
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
}
