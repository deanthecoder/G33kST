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
/// Raised when a bus access targets a region with no responding device.
/// </summary>
public sealed class BusErrorException : Exception
{
    /// <summary>
    /// Gets the faulting 24-bit bus address.
    /// </summary>
    public uint Address { get; }

    /// <summary>
    /// Gets a value indicating whether the failing cycle was a read.
    /// </summary>
    public bool IsRead { get; }

    /// <summary>
    /// Gets a value indicating whether the cycle was a program-space access.
    /// </summary>
    public bool IsProgramAccess { get; }

    public BusErrorException(uint address, bool isRead, bool isProgramAccess = false)
        : base(FormatMessage(address, isRead, isProgramAccess))
    {
        Address = address & 0x00FF_FFFF;
        IsRead = isRead;
        IsProgramAccess = isProgramAccess;
    }

    private static string FormatMessage(uint address, bool isRead, bool isProgramAccess)
    {
        var operation = isRead ? "read" : "write";
        var accessSpace = isProgramAccess ? "program" : "data";
        return $"Bus error during {operation} at 0x{(address & 0x00FF_FFFF):X6} ({accessSpace} space).";
    }
}
