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
/// ADD/SUB/ADDA/SUBA instruction decode and execution helpers.
/// </summary>
public static class AddSubtractInstructions
{
    private static readonly Instruction InstrAddByteEaToDataRegister = new("ADD.B <ea>,Dn", ExecuteAddByteEaToDataRegister);
    private static readonly Instruction InstrAddWordEaToDataRegister = new("ADD.W <ea>,Dn", ExecuteAddWordEaToDataRegister);
    private static readonly Instruction InstrAddLongEaToDataRegister = new("ADD.L <ea>,Dn", ExecuteAddLongEaToDataRegister);
    private static readonly Instruction InstrAddByteDataRegisterToEa = new("ADD.B Dn,<ea>", ExecuteAddByteDataRegisterToEa);
    private static readonly Instruction InstrAddWordDataRegisterToEa = new("ADD.W Dn,<ea>", ExecuteAddWordDataRegisterToEa);
    private static readonly Instruction InstrAddLongDataRegisterToEa = new("ADD.L Dn,<ea>", ExecuteAddLongDataRegisterToEa);
    private static readonly Instruction InstrAddAddressWord = new("ADDA.W <ea>,An", ExecuteAddAddressWord);
    private static readonly Instruction InstrAddAddressLong = new("ADDA.L <ea>,An", ExecuteAddAddressLong);

    private static readonly Instruction InstrSubByteEaToDataRegister = new("SUB.B <ea>,Dn", ExecuteSubByteEaToDataRegister);
    private static readonly Instruction InstrSubWordEaToDataRegister = new("SUB.W <ea>,Dn", ExecuteSubWordEaToDataRegister);
    private static readonly Instruction InstrSubLongEaToDataRegister = new("SUB.L <ea>,Dn", ExecuteSubLongEaToDataRegister);
    private static readonly Instruction InstrSubByteDataRegisterToEa = new("SUB.B Dn,<ea>", ExecuteSubByteDataRegisterToEa);
    private static readonly Instruction InstrSubWordDataRegisterToEa = new("SUB.W Dn,<ea>", ExecuteSubWordDataRegisterToEa);
    private static readonly Instruction InstrSubLongDataRegisterToEa = new("SUB.L Dn,<ea>", ExecuteSubLongDataRegisterToEa);
    private static readonly Instruction InstrSubAddressWord = new("SUBA.W <ea>,An", ExecuteSubAddressWord);
    private static readonly Instruction InstrSubAddressLong = new("SUBA.L <ea>,An", ExecuteSubAddressLong);

    /// <summary>
    /// Decodes ADD/ADDA opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeAdd(ushort opcode)
    {
        // 1101 ddd ooo mmm rrr = ADD/ADDA.<size> forms.
        if ((opcode & 0xF000) != 0xD000)
            return null;

        var sourceOrDestination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteRead(sourceOrDestination) ? InstrAddByteEaToDataRegister : null,
            1 => EffectiveAddressWordAccess.SupportsWordRead(sourceOrDestination) ? InstrAddWordEaToDataRegister : null,
            2 => EffectiveAddressLongAccess.SupportsLongRead(sourceOrDestination) ? InstrAddLongEaToDataRegister : null,
            3 => EffectiveAddressWordAccess.SupportsWordRead(sourceOrDestination) ? InstrAddAddressWord : null,
            4 => SupportsReadWrite(sourceOrDestination, OperandSize.Byte) ? InstrAddByteDataRegisterToEa : null,
            5 => SupportsReadWrite(sourceOrDestination, OperandSize.Word) ? InstrAddWordDataRegisterToEa : null,
            6 => SupportsReadWrite(sourceOrDestination, OperandSize.Long) ? InstrAddLongDataRegisterToEa : null,
            7 => EffectiveAddressLongAccess.SupportsLongRead(sourceOrDestination) ? InstrAddAddressLong : null,
            _ => null
        };
    }

    /// <summary>
    /// Decodes SUB/SUBA opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecodeSub(ushort opcode)
    {
        // 1001 ddd ooo mmm rrr = SUB/SUBA.<size> forms.
        if ((opcode & 0xF000) != 0x9000)
            return null;

        var sourceOrDestination = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            0 => EffectiveAddressByteAccess.SupportsByteRead(sourceOrDestination) ? InstrSubByteEaToDataRegister : null,
            1 => EffectiveAddressWordAccess.SupportsWordRead(sourceOrDestination) ? InstrSubWordEaToDataRegister : null,
            2 => EffectiveAddressLongAccess.SupportsLongRead(sourceOrDestination) ? InstrSubLongEaToDataRegister : null,
            3 => EffectiveAddressWordAccess.SupportsWordRead(sourceOrDestination) ? InstrSubAddressWord : null,
            4 => SupportsReadWrite(sourceOrDestination, OperandSize.Byte) ? InstrSubByteDataRegisterToEa : null,
            5 => SupportsReadWrite(sourceOrDestination, OperandSize.Word) ? InstrSubWordDataRegisterToEa : null,
            6 => SupportsReadWrite(sourceOrDestination, OperandSize.Long) ? InstrSubLongDataRegisterToEa : null,
            7 => EffectiveAddressLongAccess.SupportsLongRead(sourceOrDestination) ? InstrSubAddressLong : null,
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>ADD.B &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteAddByteEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteAddEaToDataRegister(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>ADD.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteAddWordEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteAddEaToDataRegister(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>ADD.L &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteAddLongEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteAddEaToDataRegister(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>ADD.B Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddByteDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteAddDataRegisterToEa(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>ADD.W Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddWordDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteAddDataRegisterToEa(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>ADD.L Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteAddLongDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteAddDataRegisterToEa(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>ADDA.W &lt;ea&gt;,An</c>.
    /// </summary>
    private static void ExecuteAddAddressWord(Cpu cpu, ushort opcode) =>
        ExecuteAddAddress(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>ADDA.L &lt;ea&gt;,An</c>.
    /// </summary>
    private static void ExecuteAddAddressLong(Cpu cpu, ushort opcode) =>
        ExecuteAddAddress(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>SUB.B &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteSubByteEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteSubEaToDataRegister(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>SUB.W &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteSubWordEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteSubEaToDataRegister(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>SUB.L &lt;ea&gt;,Dn</c>.
    /// </summary>
    private static void ExecuteSubLongEaToDataRegister(Cpu cpu, ushort opcode) =>
        ExecuteSubEaToDataRegister(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>SUB.B Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubByteDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteSubDataRegisterToEa(cpu, opcode, OperandSize.Byte);

    /// <summary>
    /// Executes <c>SUB.W Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubWordDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteSubDataRegisterToEa(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>SUB.L Dn,&lt;ea&gt;</c>.
    /// </summary>
    private static void ExecuteSubLongDataRegisterToEa(Cpu cpu, ushort opcode) =>
        ExecuteSubDataRegisterToEa(cpu, opcode, OperandSize.Long);

    /// <summary>
    /// Executes <c>SUBA.W &lt;ea&gt;,An</c>.
    /// </summary>
    private static void ExecuteSubAddressWord(Cpu cpu, ushort opcode) =>
        ExecuteSubAddress(cpu, opcode, OperandSize.Word);

    /// <summary>
    /// Executes <c>SUBA.L &lt;ea&gt;,An</c>.
    /// </summary>
    private static void ExecuteSubAddressLong(Cpu cpu, ushort opcode) =>
        ExecuteSubAddress(cpu, opcode, OperandSize.Long);

    private static void ExecuteAddEaToDataRegister(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = InstructionOperandAccess.ReadFromEa(cpu, sourceEa, size);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destination = InstructionOperandAccess.ReadDataRegister(cpu, destinationRegisterIndex, size);
        var result = destination + source;

        InstructionOperandAccess.WriteDataRegister(cpu, destinationRegisterIndex, size, result);
        FlagMath.ApplyAdd(cpu.Registers, size, destination, source, result);
        cpu.Registers.ExtendFlag = cpu.Registers.CarryFlag;
        var baseCycles = size == OperandSize.Long ? 6u : 4u;
        cpu.InternalWait(baseCycles + InstructionTiming.GetDataEffectiveAddressCycles(size, sourceEa));
    }

    private static void ExecuteAddDataRegisterToEa(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceRegisterIndex = (opcode >> 9) & 0x07;
        var source = InstructionOperandAccess.ReadDataRegister(cpu, sourceRegisterIndex, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = InstructionOperandAccess.ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, "ADD");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = destinationValue + source;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        FlagMath.ApplyAdd(cpu.Registers, size, destinationValue, source, result);
        cpu.Registers.ExtendFlag = cpu.Registers.CarryFlag;
        var baseCycles = size == OperandSize.Long ? 12u : 8u;
        if (destinationEa.Mode == EffectiveAddressMode.DataRegisterDirect)
            baseCycles = size == OperandSize.Long ? 6u : 4u;

        cpu.InternalWait(baseCycles + InstructionTiming.GetDataEffectiveAddressCycles(size, destinationEa));
    }

    private static void ExecuteSubEaToDataRegister(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var source = InstructionOperandAccess.ReadFromEa(cpu, sourceEa, size);
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destination = InstructionOperandAccess.ReadDataRegister(cpu, destinationRegisterIndex, size);
        var result = destination - source;

        InstructionOperandAccess.WriteDataRegister(cpu, destinationRegisterIndex, size, result);
        FlagMath.ApplySubtract(cpu.Registers, size, destination, source, result);
        cpu.Registers.ExtendFlag = cpu.Registers.CarryFlag;
        var baseCycles = size == OperandSize.Long ? 6u : 4u;
        cpu.InternalWait(baseCycles + InstructionTiming.GetDataEffectiveAddressCycles(size, sourceEa));
    }

    private static void ExecuteSubDataRegisterToEa(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceRegisterIndex = (opcode >> 9) & 0x07;
        var source = InstructionOperandAccess.ReadDataRegister(cpu, sourceRegisterIndex, size);
        var destinationEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var destinationSize = InstructionOperandAccess.ToDestinationOperandSize(size);
        var destination = DestinationOperandAccess.ResolveDataAlterable(cpu, destinationEa, destinationSize, "SUB");
        var destinationValue = DestinationOperandAccess.ReadUnsigned(cpu, destination, destinationSize);
        var result = destinationValue - source;

        DestinationOperandAccess.WriteUnsigned(cpu, destination, destinationSize, result);
        DestinationOperandAccess.ApplyPostIncrement(cpu, destination);
        FlagMath.ApplySubtract(cpu.Registers, size, destinationValue, source, result);
        cpu.Registers.ExtendFlag = cpu.Registers.CarryFlag;
        var baseCycles = size == OperandSize.Long ? 12u : 8u;
        if (destinationEa.Mode == EffectiveAddressMode.DataRegisterDirect)
            baseCycles = size == OperandSize.Long ? 6u : 4u;

        cpu.InternalWait(baseCycles + InstructionTiming.GetDataEffectiveAddressCycles(size, destinationEa));
    }

    private static void ExecuteAddAddress(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var sourceValue = size switch
        {
            OperandSize.Word => unchecked((uint)(short)EffectiveAddressWordAccess.ReadWord(cpu, sourceEa)),
            OperandSize.Long => EffectiveAddressLongAccess.ReadLong(cpu, sourceEa),
            _ => 0u
        };

        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destinationValue = cpu.Registers.GetAddressRegister(destinationRegisterIndex);
        cpu.Registers.SetAddressRegister(destinationRegisterIndex, destinationValue + sourceValue);
        var baseCycles = size == OperandSize.Long ? 6u : 8u;
        cpu.InternalWait(baseCycles + InstructionTiming.GetDataEffectiveAddressCycles(size, sourceEa));
    }

    private static void ExecuteSubAddress(Cpu cpu, ushort opcode, OperandSize size)
    {
        var sourceEa = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var sourceValue = size switch
        {
            OperandSize.Word => unchecked((uint)(short)EffectiveAddressWordAccess.ReadWord(cpu, sourceEa)),
            OperandSize.Long => EffectiveAddressLongAccess.ReadLong(cpu, sourceEa),
            _ => 0u
        };

        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var destinationValue = cpu.Registers.GetAddressRegister(destinationRegisterIndex);
        cpu.Registers.SetAddressRegister(destinationRegisterIndex, destinationValue - sourceValue);
        var baseCycles = size == OperandSize.Long ? 6u : 8u;
        cpu.InternalWait(baseCycles + InstructionTiming.GetDataEffectiveAddressCycles(size, sourceEa));
    }

    private static bool SupportsReadWrite(EffectiveAddress ea, OperandSize size) =>
        size switch
        {
            OperandSize.Byte => EffectiveAddressByteAccess.SupportsByteRead(ea) && EffectiveAddressByteAccess.SupportsByteWrite(ea),
            OperandSize.Word => EffectiveAddressWordAccess.SupportsWordRead(ea) && EffectiveAddressWordAccess.SupportsWordWrite(ea),
            OperandSize.Long => EffectiveAddressLongAccess.SupportsLongRead(ea) && EffectiveAddressLongAccess.SupportsLongWrite(ea),
            _ => false
        };

}
