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
using DTC.Emulation.Snapshot;

namespace DTC.AtariST;

/// <summary>
/// Read-only memory device for ROM storage.
/// </summary>
public sealed class RomDevice : IMemDevice
{
    public byte[] Data { get; }
    public uint FromAddr { get; }
    public uint ToAddr { get; }

    public RomDevice(int size, uint baseAddress)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        Data = new byte[size];
        FromAddr = baseAddress;
        ToAddr = baseAddress + (uint)size - 1;
    }

    public byte Read8(uint address)
    {
        var index = (int)(address - FromAddr);
        return Data[index];
    }

    public void Write8(uint address, byte value)
    {
        // ROM is read-only, ignore writes
    }

    public int GetStateSize() => Data.Length;

    public void SaveState(ref StateWriter writer) =>
        writer.WriteBytes(Data);

    public void LoadState(ref StateReader reader) =>
        reader.ReadBytes(Data);
}
