// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.M68000;

/// <summary>
/// Raised when code attempts an odd-address word/long access that should trigger a 68000 address error.
/// </summary>
public sealed class AddressErrorException : Exception
{
    /// <summary>
    /// Gets the 24-bit bus address that caused the fault.
    /// </summary>
    public uint Address { get; }

    /// <summary>
    /// Gets the operand size associated with the failing access (<c>".w"</c> or <c>".l"</c>).
    /// </summary>
    public string Size { get; }

    /// <summary>
    /// Creates a new address error exception.
    /// </summary>
    public AddressErrorException(uint address, string size)
        : base($"Address error for odd {size} access at 0x{address:X6}.")
    {
        Address = address;
        Size = size;
    }
}
