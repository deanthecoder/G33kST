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
/// Address-generation instruction decode and execution helpers.
/// </summary>
public static class AddressInstructions
{
    private static readonly Instruction InstrLea = new("LEA <ea>,An", ExecuteLoadEffectiveAddress);
    private static readonly Instruction InstrPea = new("PEA <ea>", ExecutePushEffectiveAddress);

    /// <summary>
    /// Decodes address-generation opcodes handled by this module.
    /// </summary>
    public static Instruction TryDecode(ushort opcode)
    {
        // 0100 1000 01 mmm rrr = PEA <ea>.
        if ((opcode & 0xFFC0) == 0x4840)
        {
            var peaSource = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
            return EffectiveAddressControlResolver.SupportsControlTarget(peaSource) ? InstrPea : null;
        }

        // 0100 aaa 111 mmm rrr = LEA <ea>,Aa.
        if ((opcode & 0xF1C0) != 0x41C0)
            return null;

        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        return EffectiveAddressControlResolver.SupportsControlTarget(source) ? InstrLea : null;
    }

    /// <summary>
    /// Executes <c>LEA &lt;ea&gt;,An</c> by writing the resolved effective address to destination An.
    /// ea = effective address.
    /// </summary>
    private static void ExecuteLoadEffectiveAddress(Cpu cpu, ushort opcode)
    {
        var destinationRegisterIndex = (opcode >> 9) & 0x07;
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var effectiveAddress = EffectiveAddressControlResolver.ResolveControlTarget(cpu, source);
        cpu.Registers.SetAddressRegister(destinationRegisterIndex, effectiveAddress);
        // LEA timing = small base + control-EA resolution work.
        cpu.InternalWait(4 + InstructionTiming.GetControlEffectiveAddressCycles(source));
    }

    /// <summary>
    /// Executes <c>PEA &lt;ea&gt;</c> by resolving the effective address and pushing it as a longword.
    /// ea = effective address.
    /// </summary>
    private static void ExecutePushEffectiveAddress(Cpu cpu, ushort opcode)
    {
        var source = EffectiveAddressDecoder.DecodeLowSixBits(opcode);
        var effectiveAddress = EffectiveAddressControlResolver.ResolveControlTarget(cpu, source);
        cpu.Push32(effectiveAddress);
        // PEA timing = push cost + control-EA resolution work.
        cpu.InternalWait(12 + InstructionTiming.GetControlEffectiveAddressCycles(source));
    }
}
