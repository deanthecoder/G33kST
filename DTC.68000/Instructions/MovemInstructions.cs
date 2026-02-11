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
/// MOVEM.W/MOVEM.L instruction decode and execution helpers.
/// </summary>
public static class MovemInstructions
{
    private static readonly Instruction InstrMovemWordRegistersToMemory = new("MOVEM.W <list>,<ea>", ExecuteWordRegistersToMemory);
    private static readonly Instruction InstrMovemLongRegistersToMemory = new("MOVEM.L <list>,<ea>", ExecuteLongRegistersToMemory);
    private static readonly Instruction InstrMovemWordMemoryToRegisters = new("MOVEM.W <ea>,<list>", ExecuteWordMemoryToRegisters);
    private static readonly Instruction InstrMovemLongMemoryToRegisters = new("MOVEM.L <ea>,<list>", ExecuteLongMemoryToRegisters);

    /// <summary>
    /// Decodes MOVEM opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0100 1d00 1s mmm rrr, where d=direction (0 reg->mem, 1 mem->reg) and s=size (0 word, 1 long).
        if ((opcode & 0xFB80) != 0x4880)
            return null;

        var memoryToRegisters = (opcode & 0x0400) != 0;
        var isLong = (opcode & 0x0040) != 0;
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        if (memoryToRegisters)
        {
            if (!SupportsMemoryToRegisters(ea))
                return null;

            return isLong ? InstrMovemLongMemoryToRegisters : InstrMovemWordMemoryToRegisters;
        }

        if (!SupportsRegistersToMemory(ea))
            return null;

        return isLong ? InstrMovemLongRegistersToMemory : InstrMovemWordRegistersToMemory;
    }

    /// <summary>
    /// Executes <c>MOVEM.W &lt;list&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteWordRegistersToMemory(Cpu cpu, ushort opcode) =>
        ExecuteRegistersToMemory(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>MOVEM.L &lt;list&gt;,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteLongRegistersToMemory(Cpu cpu, ushort opcode) =>
        ExecuteRegistersToMemory(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>MOVEM.W &lt;ea&gt;,&lt;list&gt;</c>.
    /// </summary>
    private static void ExecuteWordMemoryToRegisters(Cpu cpu, ushort opcode) =>
        ExecuteMemoryToRegisters(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>MOVEM.L &lt;ea&gt;,&lt;list&gt;</c>.
    /// </summary>
    private static void ExecuteLongMemoryToRegisters(Cpu cpu, ushort opcode) =>
        ExecuteMemoryToRegisters(cpu, opcode, OperandSize.Long);

    private static void ExecuteRegistersToMemory(Cpu cpu, ushort opcode, OperandSize size)
    {
        var registerMask = cpu.FetchPcWord();
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var step = size == OperandSize.Word ? 2u : 4u;
        if (ea.Mode == EffectiveAddressMode.AddressRegisterIndirectPreDecrement)
        {
            ExecuteRegistersToMemoryPredecrement(cpu, ea.Register, size, registerMask, step);
            return;
        }

        var address = ResolveRegistersToMemoryAddress(cpu, ea);
        for (var registerCode = 0; registerCode <= 15; registerCode++)
        {
            if (!MaskIncludesRegister(registerMask, registerCode))
                continue;

            var value = ReadMovemRegister(cpu, registerCode);
            WriteMemory(cpu, address, size, value);
            address = EffectiveAddressMath.NormalizeAddress24(address + step);
        }
    }

    private static void ExecuteRegistersToMemoryPredecrement(Cpu cpu, byte addressRegisterIndex, OperandSize size, ushort registerMask, uint step)
    {
        var address = cpu.Registers.GetAddressRegister(addressRegisterIndex);
        var registerSnapshot = CaptureRegisters(cpu);
        var addressRegisterCode = addressRegisterIndex + 8;
        var initialAddressValue = cpu.Registers.GetAddressRegister(addressRegisterIndex);
        for (var registerCode = 15; registerCode >= 0; registerCode--)
        {
            if (!MaskIncludesRegisterPredecrement(registerMask, registerCode))
                continue;

            address -= step;
            cpu.Registers.SetAddressRegister(addressRegisterIndex, address);
            var value = registerCode == addressRegisterCode
                ? initialAddressValue
                : ReadMovemRegisterFromSnapshot(registerSnapshot, registerCode);
            WriteMemory(cpu, address, size, value);
        }
    }

    private static void ExecuteMemoryToRegisters(Cpu cpu, ushort opcode, OperandSize size)
    {
        var registerMask = cpu.FetchPcWord();
        var ea = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = ResolveMemoryToRegistersSource(cpu, ea);
        var address = source.Address;
        var step = size == OperandSize.Word ? 2u : 4u;
        var transferredRegisterCount = 0u;

        for (var registerCode = 0; registerCode <= 15; registerCode++)
        {
            if (!MaskIncludesRegister(registerMask, registerCode))
                continue;

            var value = ReadMemory(cpu, address, size);
            WriteMovemRegister(cpu, registerCode, size, value);
            address = EffectiveAddressMath.NormalizeAddress24(address + step);
            transferredRegisterCount++;
        }

        if (source.HasPostIncrement)
            cpu.Registers.SetAddressRegister(
                source.PostIncrementRegisterIndex,
                unchecked(source.PostIncrementBaseAddress + (transferredRegisterCount * step)));
    }

    private static uint ResolveRegistersToMemoryAddress(Cpu cpu, EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => EffectiveAddressMath.NormalizeAddress24(cpu.Registers.GetAddressRegister(ea.Register)),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => ResolveAddressRegisterDisplacement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectIndex => ResolveAddressRegisterIndex(cpu, ea.Register),
            EffectiveAddressMode.Other when ea.Register == 0 => EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.ReadAbsoluteShortAddress(cpu)),
            EffectiveAddressMode.Other when ea.Register == 1 => EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.ReadAbsoluteLongAddress(cpu)),
            _ => throw new NotSupportedException($"MOVEM destination EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.")
        };

    private static MovemSource ResolveMemoryToRegistersSource(Cpu cpu, EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => new MovemSource(
                EffectiveAddressMath.NormalizeAddress24(cpu.Registers.GetAddressRegister(ea.Register)),
                false,
                0,
                0),
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => new MovemSource(
                EffectiveAddressMath.NormalizeAddress24(cpu.Registers.GetAddressRegister(ea.Register)),
                true,
                ea.Register,
                cpu.Registers.GetAddressRegister(ea.Register)),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => new MovemSource(
                ResolveAddressRegisterDisplacement(cpu, ea.Register),
                false,
                0,
                0),
            EffectiveAddressMode.AddressRegisterIndirectIndex => new MovemSource(
                ResolveAddressRegisterIndex(cpu, ea.Register),
                false,
                0,
                0),
            EffectiveAddressMode.Other when ea.Register == 0 => new MovemSource(
                EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.ReadAbsoluteShortAddress(cpu)),
                false,
                0,
                0),
            EffectiveAddressMode.Other when ea.Register == 1 => new MovemSource(
                EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.ReadAbsoluteLongAddress(cpu)),
                false,
                0,
                0),
            EffectiveAddressMode.Other when ea.Register == 2 => new MovemSource(
                ResolvePcDisplacement(cpu),
                false,
                0,
                0),
            EffectiveAddressMode.Other when ea.Register == 3 => new MovemSource(
                ResolvePcIndex(cpu),
                false,
                0,
                0),
            _ => throw new NotSupportedException($"MOVEM source EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.")
        };

    private static uint ResolveAddressRegisterDisplacement(Cpu cpu, byte registerIndex)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.AddDisplacement(baseAddress, displacement));
    }

    private static uint ResolveAddressRegisterIndex(Cpu cpu, byte registerIndex)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.AddIndex(cpu, baseAddress, extension));
    }

    private static uint ResolvePcDisplacement(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var displacement = (short)cpu.FetchPcWord();
        return EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.AddDisplacement(baseAddress, displacement));
    }

    private static uint ResolvePcIndex(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var extension = cpu.FetchPcWord();
        return EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.AddIndex(cpu, baseAddress, extension));
    }

    private static uint ReadMemory(Cpu cpu, uint address, OperandSize size) =>
        size switch
        {
            OperandSize.Word => cpu.Read16(address),
            OperandSize.Long => cpu.Read32(address),
            _ => 0
        };

    private static void WriteMemory(Cpu cpu, uint address, OperandSize size, uint value)
    {
        switch (size)
        {
            case OperandSize.Word:
                cpu.Write16(address, (ushort)value);
                return;
            case OperandSize.Long:
                cpu.Write32(address, value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }
    }

    private static bool MaskIncludesRegister(ushort registerMask, int registerCode) =>
        (registerMask & (1 << registerCode)) != 0;

    private static bool MaskIncludesRegisterPredecrement(ushort registerMask, int registerCode) =>
        (registerMask & (1 << (15 - registerCode))) != 0;

    private static uint ReadMovemRegister(Cpu cpu, int registerCode) =>
        registerCode < 8
            ? cpu.Registers.GetDataRegister(registerCode)
            : cpu.Registers.GetAddressRegister(registerCode - 8);

    private static uint ReadMovemRegisterFromSnapshot(MovemRegisterSnapshot snapshot, int registerCode) =>
        registerCode < 8
            ? snapshot.Data[registerCode]
            : snapshot.Address[registerCode - 8];

    private static MovemRegisterSnapshot CaptureRegisters(Cpu cpu)
    {
        var data = new uint[8];
        var address = new uint[8];
        for (var i = 0; i < 8; i++)
        {
            data[i] = cpu.Registers.GetDataRegister(i);
            address[i] = cpu.Registers.GetAddressRegister(i);
        }

        return new MovemRegisterSnapshot(data, address);
    }

    private static void WriteMovemRegister(Cpu cpu, int registerCode, OperandSize size, uint value)
    {
        var registerValue = size == OperandSize.Word
            ? unchecked((uint)(short)(ushort)value)
            : value;

        if (registerCode < 8)
        {
            cpu.Registers.SetDataRegister(registerCode, registerValue);
            return;
        }

        cpu.Registers.SetAddressRegister(registerCode - 8, registerValue);
    }

    private static bool SupportsRegistersToMemory(EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => ea.Register is 0 or 1,
            _ => false
        };

    private static bool SupportsMemoryToRegisters(EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => ea.Register is 0 or 1 or 2 or 3,
            _ => false
        };

    private readonly record struct MovemSource(uint Address, bool HasPostIncrement, byte PostIncrementRegisterIndex, uint PostIncrementBaseAddress);
    private readonly record struct MovemRegisterSnapshot(uint[] Data, uint[] Address);
}
