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
/// MOVEP decode and execution helpers.
/// MOVEP transfers interleaved bytes between Dn and memory at (d16,An).
/// </summary>
public static class MovepInstructions
{
    private static readonly Instruction InstrMovepWordMemoryToRegister = new("MOVEP.W (d16,An),Dn", ExecuteMovepWordMemoryToRegister);
    private static readonly Instruction InstrMovepLongMemoryToRegister = new("MOVEP.L (d16,An),Dn", ExecuteMovepLongMemoryToRegister);
    private static readonly Instruction InstrMovepWordRegisterToMemory = new("MOVEP.W Dn,(d16,An)", ExecuteMovepWordRegisterToMemory);
    private static readonly Instruction InstrMovepLongRegisterToMemory = new("MOVEP.L Dn,(d16,An)", ExecuteMovepLongRegisterToMemory);

    /// <summary>
    /// Decodes MOVEP opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0000 ddd ooo 001 rrr, where ooo 100..111 selects MOVEP size+direction.
        if ((opcode & 0xF038) != 0x0008)
            return null;

        var operationMode = (opcode >> 6) & 0x07;
        return operationMode switch
        {
            4 => InstrMovepWordMemoryToRegister,
            5 => InstrMovepLongMemoryToRegister,
            6 => InstrMovepWordRegisterToMemory,
            7 => InstrMovepLongRegisterToMemory,
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>MOVEP.W (d16,An),Dn</c>.
    /// </summary>
    private static void ExecuteMovepWordMemoryToRegister(Cpu cpu, ushort opcode)
    {
        var dataRegisterIndex = (opcode >> 9) & 0x07;
        var address = ResolveBaseAddress(cpu, opcode);
        var byte1 = cpu.Read8(address);
        var byte0 = cpu.Read8(OffsetAddress(address, 2));
        var existing = cpu.Registers.GetDataRegister(dataRegisterIndex) & 0xFFFF0000u;
        var value = existing | ((uint)byte1 << 8) | byte0;
        cpu.Registers.SetDataRegister(dataRegisterIndex, value);
    }

    /// <summary>
    /// Executes <c>MOVEP.L (d16,An),Dn</c>.
    /// </summary>
    private static void ExecuteMovepLongMemoryToRegister(Cpu cpu, ushort opcode)
    {
        var dataRegisterIndex = (opcode >> 9) & 0x07;
        var address = ResolveBaseAddress(cpu, opcode);
        var byte3 = cpu.Read8(address);
        var byte2 = cpu.Read8(OffsetAddress(address, 2));
        var byte1 = cpu.Read8(OffsetAddress(address, 4));
        var byte0 = cpu.Read8(OffsetAddress(address, 6));
        var value = ((uint)byte3 << 24) | ((uint)byte2 << 16) | ((uint)byte1 << 8) | byte0;
        cpu.Registers.SetDataRegister(dataRegisterIndex, value);
    }

    /// <summary>
    /// Executes <c>MOVEP.W Dn,(d16,An)</c>.
    /// </summary>
    private static void ExecuteMovepWordRegisterToMemory(Cpu cpu, ushort opcode)
    {
        var dataRegisterIndex = (opcode >> 9) & 0x07;
        var value = cpu.Registers.GetDataRegister(dataRegisterIndex);
        var address = ResolveBaseAddress(cpu, opcode);
        cpu.Write8(address, (byte)(value >> 8));
        cpu.Write8(OffsetAddress(address, 2), (byte)value);
    }

    /// <summary>
    /// Executes <c>MOVEP.L Dn,(d16,An)</c>.
    /// </summary>
    private static void ExecuteMovepLongRegisterToMemory(Cpu cpu, ushort opcode)
    {
        var dataRegisterIndex = (opcode >> 9) & 0x07;
        var value = cpu.Registers.GetDataRegister(dataRegisterIndex);
        var address = ResolveBaseAddress(cpu, opcode);
        cpu.Write8(address, (byte)(value >> 24));
        cpu.Write8(OffsetAddress(address, 2), (byte)(value >> 16));
        cpu.Write8(OffsetAddress(address, 4), (byte)(value >> 8));
        cpu.Write8(OffsetAddress(address, 6), (byte)value);
    }

    /// <summary>
    /// Resolves effective base address from An + sign-extended d16 extension.
    /// </summary>
    private static uint ResolveBaseAddress(Cpu cpu, ushort opcode)
    {
        var addressRegisterIndex = opcode & 0x07;
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(addressRegisterIndex);
        return EffectiveAddressMath.NormalizeAddress24(EffectiveAddressMath.AddDisplacement(baseAddress, displacement));
    }

    /// <summary>
    /// Applies a byte-lane offset and wraps into 24-bit bus space.
    /// </summary>
    private static uint OffsetAddress(uint address, uint offset) =>
        EffectiveAddressMath.NormalizeAddress24(unchecked(address + offset));
}
