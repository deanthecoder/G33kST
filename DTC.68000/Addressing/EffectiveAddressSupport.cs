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
/// Shared capability checks for effective-address read/write support.
/// </summary>
public static class EffectiveAddressSupport
{
    /// <summary>
    /// Returns true when the EA can be used as a source operand.
    /// </summary>
    public static bool SupportsRead(EffectiveAddress ea, bool allowsAddressRegisterDirect) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterDirect => allowsAddressRegisterDirect,
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => true,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => ea.Register is 0 or 1 or 2 or 3 or 4,
            _ => false
        };

    /// <summary>
    /// Returns true when the EA can be used as a destination operand.
    /// </summary>
    public static bool SupportsWrite(EffectiveAddress ea) =>
        ea.Mode switch
        {
            EffectiveAddressMode.DataRegisterDirect => true,
            EffectiveAddressMode.AddressRegisterIndirect => true,
            EffectiveAddressMode.AddressRegisterIndirectPostIncrement => true,
            EffectiveAddressMode.AddressRegisterIndirectPreDecrement => true,
            EffectiveAddressMode.AddressRegisterIndirectDisplacement => true,
            EffectiveAddressMode.AddressRegisterIndirectIndex => true,
            EffectiveAddressMode.Other => ea.Register is 0 or 1,
            _ => false
        };
}
