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
/// Represents a 68000 effective-address field (mode + register).
/// </summary>
public readonly record struct EffectiveAddress(EffectiveAddressMode Mode, byte Register);

/// <summary>
/// Enumerates the 3-bit effective-address mode field used by 68000 opcodes.
/// </summary>
public enum EffectiveAddressMode : byte
{
    /// <summary>
    /// <c>Dn</c>: data register direct.
    /// The operand value is taken from (or written to) the selected data register.
    /// </summary>
    DataRegisterDirect = 0,

    /// <summary>
    /// <c>An</c>: address register direct.
    /// The operand is the selected address register itself (not memory at that address).
    /// </summary>
    AddressRegisterDirect = 1,

    /// <summary>
    /// <c>(An)</c>: address register indirect.
    /// The operand is read from or written to memory at the address held in <c>An</c>.
    /// The address register value is not modified.
    /// </summary>
    AddressRegisterIndirect = 2,

    /// <summary>
    /// <c>(An)+</c>: address register indirect with post-increment.
    /// The operand uses memory at <c>An</c>, then <c>An</c> is incremented after access.
    /// </summary>
    AddressRegisterIndirectPostIncrement = 3,

    /// <summary>
    /// <c>-(An)</c>: address register indirect with pre-decrement.
    /// <c>An</c> is decremented before access, then memory at the new address is used.
    /// </summary>
    AddressRegisterIndirectPreDecrement = 4,

    /// <summary>
    /// <c>(d16,An)</c>: address register indirect with 16-bit displacement.
    /// The effective address is <c>An + d16</c>, where <c>d16</c> is a sign-extended 16-bit displacement.
    /// </summary>
    AddressRegisterIndirectDisplacement = 5,

    /// <summary>
    /// <c>(d8,An,Xn)</c>: address register indirect with index and 8-bit displacement.
    /// The effective address is <c>An + Xn + d8</c>, where <c>d8</c> is sign-extended and <c>Xn</c> is the selected index register value.
    /// </summary>
    AddressRegisterIndirectIndex = 6,

    /// <summary>
    /// Mode selector for extended forms determined by the register field:
    /// <list type="table">
    /// <listheader>
    /// <term>reg</term>
    /// <description>extended EA</description>
    /// </listheader>
    /// <item><term>0</term><description><c>(xxx).w</c> (absolute short).</description></item>
    /// <item><term>1</term><description><c>(xxx).l</c> (absolute long).</description></item>
    /// <item><term>2</term><description><c>(d16,PC)</c> (PC-relative with 16-bit displacement).</description></item>
    /// <item><term>3</term><description><c>(d8,PC,Xn)</c> (PC-relative indexed).</description></item>
    /// <item><term>4</term><description><c>#&lt;data&gt;</c> (immediate).</description></item>
    /// <item><term>5-7</term><description>Reserved/illegal on 68000 for standard EA decoding.</description></item>
    /// </list>
    /// </summary>
    Other = 7
}
