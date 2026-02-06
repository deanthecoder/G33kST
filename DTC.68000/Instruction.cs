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
/// Represents a decoded 68000 instruction and its execution callback.
/// </summary>
public sealed class Instruction
{
    /// <summary>
    /// Gets the instruction mnemonic.
    /// </summary>
    public string Mnemonic { get; }

    /// <summary>
    /// Gets the instruction execution delegate.
    /// </summary>
    public Action<Cpu, ushort> Execute { get; }

    /// <summary>
    /// Creates an instruction descriptor.
    /// </summary>
    public Instruction(string mnemonic, Action<Cpu, ushort> execute)
    {
        Mnemonic = mnemonic ?? throw new ArgumentNullException(nameof(mnemonic));
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    /// <inheritdoc />
    public override string ToString() => Mnemonic;
}
