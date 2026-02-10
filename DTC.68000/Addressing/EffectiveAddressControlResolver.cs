// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.M68000.Addressing;

/// <summary>
/// Resolves effective-address forms used as control-flow targets (for example, JMP/JSR).
/// </summary>
public static class EffectiveAddressControlResolver
{
    /// <summary>
    /// Returns true when an effective-address form is legal as a control-flow target.
    /// </summary>
    public static bool SupportsControlTarget(EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => ea.Register is 0 or 1 or 2 or 3,
            _ => false
        };

    /// <summary>
    /// Resolves the effective-address value as a control-flow target address.
    /// </summary>
    public static uint ResolveControlTarget(Cpu cpu, EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.AddressRegisterIndirect => cpu.Registers.GetAddressRegister(ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => ResolveAddressRegisterDisplacement(cpu, ea.Register),
            EffectiveAddressMode.AddressRegisterIndirectIndex => ResolveAddressRegisterIndex(cpu, ea.Register),
            EffectiveAddressMode.Other when ea.Register == 0 => ResolveAbsoluteShort(cpu),
            EffectiveAddressMode.Other when ea.Register == 1 => ResolveAbsoluteLong(cpu),
            EffectiveAddressMode.Other when ea.Register == 2 => ResolvePcDisplacement(cpu),
            EffectiveAddressMode.Other when ea.Register == 3 => ResolvePcIndex(cpu),
            _ => throw new NotSupportedException($"Control target EA not supported: mode {(byte)ea.Mode}, reg {ea.Register}.")
        };

    /// <summary>
    /// Resolves <c>(d16,An)</c> control targets.
    /// </summary>
    private static uint ResolveAddressRegisterDisplacement(Cpu cpu, byte registerIndex)
    {
        var displacement = (short)cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return EffectiveAddressMath.AddDisplacement(baseAddress, displacement);
    }

    /// <summary>
    /// Resolves <c>(d8,An,Xn)</c> control targets.
    /// </summary>
    private static uint ResolveAddressRegisterIndex(Cpu cpu, byte registerIndex)
    {
        var extension = cpu.FetchPcWord();
        var baseAddress = cpu.Registers.GetAddressRegister(registerIndex);
        return EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
    }

    /// <summary>
    /// Resolves absolute short <c>(xxx).w</c> control targets.
    /// </summary>
    private static uint ResolveAbsoluteShort(Cpu cpu) =>
        EffectiveAddressMath.ReadAbsoluteShortAddress(cpu);

    /// <summary>
    /// Resolves absolute long <c>(xxx).l</c> control targets.
    /// </summary>
    private static uint ResolveAbsoluteLong(Cpu cpu)
        => EffectiveAddressMath.ReadAbsoluteLongAddress(cpu);

    /// <summary>
    /// Resolves PC-relative <c>(d16,PC)</c> control targets.
    /// </summary>
    private static uint ResolvePcDisplacement(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var displacement = (short)cpu.FetchPcWord();
        return EffectiveAddressMath.AddDisplacement(baseAddress, displacement);
    }

    /// <summary>
    /// Resolves PC-relative indexed <c>(d8,PC,Xn)</c> control targets.
    /// </summary>
    private static uint ResolvePcIndex(Cpu cpu)
    {
        var baseAddress = cpu.GetPcRelativeBaseAddress();
        var extension = cpu.FetchPcWord();
        return EffectiveAddressMath.AddIndex(cpu, baseAddress, extension);
    }
}
