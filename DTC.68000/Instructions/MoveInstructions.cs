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
/// MOVE-family instruction decode and execution helpers.
/// </summary>
public static class MoveInstructions
{
    private static readonly Instruction s_instrMoveByte = new("MOVE.B <ea>,<ea>", ExecuteMoveByte);
    private static readonly Instruction s_instrMoveWord = new("MOVE.W <ea>,<ea>", ExecuteMoveWord);
    private static readonly Instruction s_instrMoveLong = new("MOVE.L <ea>,<ea>", ExecuteMoveLong);
    private static readonly Instruction s_instrMoveAddressWord = new("MOVEA.W <ea>,An", ExecuteMoveAddressWord);
    private static readonly Instruction s_instrMoveAddressLong = new("MOVEA.L <ea>,An", ExecuteMoveAddressLong);

    /// <summary>
    /// Decodes byte-sized MOVE opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeByte(ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        if (!EffectiveAddressByteAccess.SupportsByteRead(source))
            return null;
        return EffectiveAddressByteAccess.SupportsByteWrite(destination) ? s_instrMoveByte : null;
    }

    /// <summary>
    /// Decodes word-sized MOVE opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeWord(ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        if (destination.Mode == EffectiveAddressMode.AddressRegisterDirect)
            return EffectiveAddressWordAccess.SupportsWordRead(source) ? s_instrMoveAddressWord : null;
        if (!EffectiveAddressWordAccess.SupportsWordRead(source))
            return null;
        return EffectiveAddressWordAccess.SupportsWordWrite(destination) ? s_instrMoveWord : null;
    }

    /// <summary>
    /// Decodes long-sized MOVE opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeLong(ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        if (destination.Mode == EffectiveAddressMode.AddressRegisterDirect)
            return EffectiveAddressLongAccess.SupportsLongRead(source) ? s_instrMoveAddressLong : null;
        if (!EffectiveAddressLongAccess.SupportsLongRead(source))
            return null;
        return EffectiveAddressLongAccess.SupportsLongWrite(destination) ? s_instrMoveLong : null;
    }

    /// <summary>
    /// Executes <c>MOVEQ #&lt;imm8&gt;,Dn</c>, sign-extending the 8-bit literal to 32-bit.
    /// </summary>
    public static void ExecuteMoveQuick(Cpu cpu, ushort opcode)
    {
        var registerIndex = (opcode >> 9) & 0x7;
        var immediate = (sbyte)(opcode & 0xFF);
        var value = (uint)immediate;

        cpu.Registers.SetDataRegister(registerIndex, value);
        FlagMath.ApplyLogicalLong(cpu.Registers, value);
        cpu.InternalWait(4);
    }

    /// <summary>
    /// Executes <c>MOVE.B &lt;ea&gt;,&lt;ea&gt;</c> for supported byte-sized EA combinations.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteMoveByte(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = EffectiveAddressByteAccess.ReadByte(cpu, source);
        EffectiveAddressByteAccess.WriteByte(cpu, destination, sourceValue);
        FlagMath.ApplyLogicalByte(cpu.Registers, sourceValue);
        cpu.InternalWait(InstructionTiming.GetMoveCycles(OperandSize.Byte, source, destination));
    }

    /// <summary>
    /// Executes <c>MOVE.W &lt;ea&gt;,&lt;ea&gt;</c> for supported word-sized EA combinations.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteMoveWord(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = EffectiveAddressWordAccess.ReadWord(cpu, source);
        FlagMath.ApplyLogicalWord(cpu.Registers, sourceValue);
        var usePrefetchInstructionRegisterOnWriteFault = destination.Mode == EffectiveAddressMode.AddressRegisterIndirectPreDecrement;
        var frameProgramCounterAdjust = DestinationFrameProgramCounterAdjust(destination);
        if (destination.Mode == EffectiveAddressMode.Other && destination.Register == 1 && SourceUsesReadCycle(source))
            frameProgramCounterAdjust -= 2;

        WriteMoveWord(cpu, destination, sourceValue, usePrefetchInstructionRegisterOnWriteFault, frameProgramCounterAdjust);
        cpu.InternalWait(InstructionTiming.GetMoveCycles(OperandSize.Word, source, destination));
    }

    /// <summary>
    /// Executes <c>MOVE.L &lt;ea&gt;,&lt;ea&gt;</c> for supported long-sized EA combinations.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteMoveLong(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destination = EffectiveAddressDecoder.DecodeMoveDestination(opcode);
        var sourceValue = EffectiveAddressLongAccess.ReadLong(cpu, source);
        const bool usePrefetchInstructionRegisterOnWriteFault = false;
        var frameProgramCounterAdjust = DestinationFrameProgramCounterAdjustLong(source, destination);
        try
        {
            WriteMoveLong(cpu, destination, sourceValue, usePrefetchInstructionRegisterOnWriteFault, frameProgramCounterAdjust);
            FlagMath.ApplyLogicalLong(cpu.Registers, sourceValue);
            cpu.InternalWait(InstructionTiming.GetMoveCycles(OperandSize.Long, source, destination));
        }
        catch (AddressErrorException)
        {
            ApplyMoveLongAddressErrorFlags(cpu, source, destination, sourceValue);
            throw;
        }
    }

    /// <summary>
    /// Executes <c>MOVEA.W &lt;ea&gt;,An</c> using sign-extension from 16-bit source to 32-bit address register.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteMoveAddressWord(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var sourceValue = (short)EffectiveAddressWordAccess.ReadWord(cpu, source);
        cpu.Registers.SetAddressRegister(destinationRegisterIndex, unchecked((uint)sourceValue));
        cpu.InternalWait(InstructionTiming.GetMoveAddressCycles(OperandSize.Word, source));
    }

    /// <summary>
    /// Executes <c>MOVEA.L &lt;ea&gt;,An</c> by loading a 32-bit source into address register An.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteMoveAddressLong(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var sourceValue = EffectiveAddressLongAccess.ReadLong(cpu, source);
        cpu.Registers.SetAddressRegister(destinationRegisterIndex, sourceValue);
        cpu.InternalWait(InstructionTiming.GetMoveAddressCycles(OperandSize.Long, source));
    }

    private static void WriteMoveWord(Cpu cpu, EffectiveAddress destination, ushort value, bool usePrefetchInstructionRegisterOnWriteFault, int frameProgramCounterAdjust)
    {
        try
        {
            EffectiveAddressWordAccess.WriteWord(cpu, destination, value);
        }
        catch (AddressErrorException error)
        {
            throw RethrowMoveDestinationAddressError(error, usePrefetchInstructionRegisterOnWriteFault, frameProgramCounterAdjust);
        }
    }

    private static void WriteMoveLong(Cpu cpu, EffectiveAddress destination, uint value, bool usePrefetchInstructionRegisterOnWriteFault, int frameProgramCounterAdjust)
    {
        if (destination.Mode == EffectiveAddressMode.AddressRegisterIndirectPreDecrement)
        {
            WriteMoveLongPreDecrement(cpu, destination.Register, value, usePrefetchInstructionRegisterOnWriteFault, frameProgramCounterAdjust);
            return;
        }

        try
        {
            EffectiveAddressLongAccess.WriteLong(cpu, destination, value);
        }
        catch (AddressErrorException error)
        {
            throw RethrowMoveDestinationAddressError(error, usePrefetchInstructionRegisterOnWriteFault, frameProgramCounterAdjust);
        }
    }

    private static void WriteMoveLongPreDecrement(Cpu cpu, byte registerIndex, uint value, bool usePrefetchInstructionRegisterOnWriteFault, int frameProgramCounterAdjust)
    {
        var currentAddress = cpu.Registers.GetAddressRegister(registerIndex);
        var lowWordAddress = currentAddress - 2;
        try
        {
            cpu.Write16(lowWordAddress, (ushort)value);
        }
        catch (AddressErrorException error)
        {
            throw RethrowMoveDestinationAddressError(error, usePrefetchInstructionRegisterOnWriteFault, frameProgramCounterAdjust);
        }

        var highWordAddress = currentAddress - 4;
        try
        {
            cpu.Write16(highWordAddress, (ushort)(value >> 16));
        }
        catch (AddressErrorException error)
        {
            throw RethrowMoveDestinationAddressError(error, usePrefetchInstructionRegisterOnWriteFault, frameProgramCounterAdjust);
        }

        cpu.Registers.SetAddressRegister(registerIndex, highWordAddress);
    }

    private static AddressErrorException RethrowMoveDestinationAddressError(AddressErrorException error, bool usePrefetchInstructionRegister, int frameProgramCounterAdjust) =>
        new(
            error.Address,
            error.Size,
            error.IsRead,
            error.IsProgramAccess,
            frameProgramCounterAdjust,
            usePrefetchInstructionRegister);

    private static void ApplyMoveLongAddressErrorFlags(Cpu cpu, EffectiveAddress source, EffectiveAddress destination, uint value)
    {
        var current = cpu.Registers.StatusRegister;
        var ccr = current & 0x1F;
        var sourceIsRegisterOrImmediate = SourceUsesHighWordAddressErrorFlags(source);
        var destinationMode = destination.Mode;
        var destinationIsAddressIndirect = destinationMode == EffectiveAddressMode.AddressRegisterIndirect;
        var destinationIsAddressPostIncrement = destinationMode == EffectiveAddressMode.AddressRegisterIndirectPostIncrement;
        var destinationIsDisplacementOrIndex = destinationMode is EffectiveAddressMode.AddressRegisterIndirectDisplacement or EffectiveAddressMode.AddressRegisterIndirectIndex;

        if (sourceIsRegisterOrImmediate && (destinationIsAddressIndirect || destinationIsAddressPostIncrement))
            return;

        var destinationIsAbsoluteLong = destinationMode == EffectiveAddressMode.Other && destination.Register == 1;
        var useLowWordForNz = !sourceIsRegisterOrImmediate &&
                              (destinationIsAddressIndirect || destinationIsAddressPostIncrement || destinationIsAbsoluteLong);
        var preserveCarryAndOverflow = sourceIsRegisterOrImmediate && destinationIsDisplacementOrIndex;
        var selectedWord = useLowWordForNz ? (ushort)value : (ushort)(value >> 16);
        var next = ccr & 0x10; // Preserve extend.
        if ((selectedWord & 0x8000) != 0)
            next |= 0x08;
        if (selectedWord == 0)
            next |= 0x04;
        if (preserveCarryAndOverflow)
            next |= ccr & 0x03;

        cpu.Registers.StatusRegister = (ushort)((current & 0xFFE0) | next);
    }

    private static int DestinationFrameProgramCounterAdjust(EffectiveAddress destination) =>
        2 - (2 * DestinationExtensionWordCount(destination));

    private static int DestinationFrameProgramCounterAdjustLong(EffectiveAddress source, EffectiveAddress destination)
    {
        if (destination.Mode == EffectiveAddressMode.Other && destination.Register == 1 && SourceUsesHighWordAddressErrorFlags(source))
            return DestinationFrameProgramCounterAdjust(destination);

        return 2 - (2 * DestinationExtensionWordCountLong(destination));
    }

    private static int DestinationExtensionWordCount(EffectiveAddress destination) =>
        destination.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => 1,
            EffectiveAddressMode.AddressRegisterIndirectIndex => 1,
            EffectiveAddressMode.Other when destination.Register == 0 => 1,
            EffectiveAddressMode.Other when destination.Register == 1 => 1,
            _ => 0
        };

    private static int DestinationExtensionWordCountLong(EffectiveAddress destination) =>
        destination.Mode switch
        {
            EffectiveAddressMode.Other when destination.Register == 1 => 2,
            _ => DestinationExtensionWordCount(destination)
        };

    private static bool SourceUsesReadCycle(EffectiveAddress source) =>
        source.Mode is not EffectiveAddressMode.DataRegisterDirect and not EffectiveAddressMode.AddressRegisterDirect;

    private static bool SourceUsesHighWordAddressErrorFlags(EffectiveAddress source) =>
        source.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterDirect => true,
            EffectiveAddressMode.Other when source.Register == 4 => true,
            _ => false
        };
}
