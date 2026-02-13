// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation;

namespace DTC.AtariST;

/// <summary>
/// Represents an unmapped address range that behaves like open bus.
/// Reads return <c>0xFF</c> and writes are ignored.
/// </summary>
public sealed class OpenBusDevice : IMemDevice
{
    /// <inheritdoc />
    public uint FromAddr { get; }

    /// <inheritdoc />
    public uint ToAddr { get; }

    public OpenBusDevice(uint fromAddr, uint toAddr)
    {
        if (toAddr < fromAddr)
            throw new ArgumentOutOfRangeException(nameof(toAddr), "Open-bus range end must be greater than or equal to range start.");

        FromAddr = fromAddr;
        ToAddr = toAddr;
    }

    /// <inheritdoc />
    public byte Read8(uint address) =>
        0xFF;

    /// <inheritdoc />
    public void Write8(uint address, byte value)
    {
    }
}
