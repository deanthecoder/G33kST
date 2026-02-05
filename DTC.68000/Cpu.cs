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

namespace DTC.M68000;

/// <summary>
/// Motorola 68000 CPU implementation.
/// </summary>
public sealed class Cpu : CpuBase
{
    public Cpu(Bus bus)
        : base(bus)
    {
    }

    public override void Reset() => throw new NotImplementedException();
    public override void Step() => throw new NotImplementedException();
    public override byte Read8(uint address) => throw new NotImplementedException();
    public override void Write8(uint address, byte value) => throw new NotImplementedException();
}
