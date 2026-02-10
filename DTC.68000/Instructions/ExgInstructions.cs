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
/// EXG instruction decode and execution helpers.
/// </summary>
public static class ExgInstructions
{
    private static readonly Instruction InstrExgDataData = new("EXG Dn,Dm", ExecuteExgDataData);
    private static readonly Instruction InstrExgAddressAddress = new("EXG An,Am", ExecuteExgAddressAddress);
    private static readonly Instruction InstrExgDataAddress = new("EXG Dn,Am", ExecuteExgDataAddress);

    /// <summary>
    /// Decodes EXG opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 1100 xxx1 ooooo yyy with EXG opmodes:
        // 01000 = Dn <-> Dm
        // 01001 = An <-> Am
        // 10001 = Dn <-> Am
        if ((opcode & 0xF100) != 0xC100)
            return null;

        var operationMode = (opcode >> 3) & 0x1F;
        return operationMode switch
        {
            0x08 => InstrExgDataData,
            0x09 => InstrExgAddressAddress,
            0x11 => InstrExgDataAddress,
            _ => null
        };
    }

    /// <summary>
    /// Executes <c>EXG Dn,Dm</c>.
    /// </summary>
    private static void ExecuteExgDataData(Cpu cpu, ushort opcode)
    {
        var firstRegisterIndex = (opcode >> 9) & 0x07;
        var secondRegisterIndex = opcode & 0x07;
        var firstValue = cpu.Registers.GetDataRegister(firstRegisterIndex);
        var secondValue = cpu.Registers.GetDataRegister(secondRegisterIndex);
        cpu.Registers.SetDataRegister(firstRegisterIndex, secondValue);
        cpu.Registers.SetDataRegister(secondRegisterIndex, firstValue);
    }

    /// <summary>
    /// Executes <c>EXG An,Am</c>.
    /// </summary>
    private static void ExecuteExgAddressAddress(Cpu cpu, ushort opcode)
    {
        var firstRegisterIndex = (opcode >> 9) & 0x07;
        var secondRegisterIndex = opcode & 0x07;
        var firstValue = cpu.Registers.GetAddressRegister(firstRegisterIndex);
        var secondValue = cpu.Registers.GetAddressRegister(secondRegisterIndex);
        cpu.Registers.SetAddressRegister(firstRegisterIndex, secondValue);
        cpu.Registers.SetAddressRegister(secondRegisterIndex, firstValue);
    }

    /// <summary>
    /// Executes <c>EXG Dn,Am</c>.
    /// </summary>
    private static void ExecuteExgDataAddress(Cpu cpu, ushort opcode)
    {
        var dataRegisterIndex = (opcode >> 9) & 0x07;
        var addressRegisterIndex = opcode & 0x07;
        var dataValue = cpu.Registers.GetDataRegister(dataRegisterIndex);
        var addressValue = cpu.Registers.GetAddressRegister(addressRegisterIndex);
        cpu.Registers.SetDataRegister(dataRegisterIndex, addressValue);
        cpu.Registers.SetAddressRegister(addressRegisterIndex, dataValue);
    }
}
