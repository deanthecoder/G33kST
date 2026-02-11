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
/// Describes how the interrupt acknowledge cycle resolved.
/// </summary>
public readonly record struct InterruptAcknowledgeResult
{
    /// <summary>
    /// Creates an autovector acknowledge result.
    /// </summary>
    public static InterruptAcknowledgeResult Autovector() =>
        new(InterruptAcknowledgeType.Autovector, 0);

    /// <summary>
    /// Creates a spurious-interrupt acknowledge result.
    /// </summary>
    public static InterruptAcknowledgeResult Spurious() =>
        new(InterruptAcknowledgeType.Spurious, 0);

    /// <summary>
    /// Creates a result that supplies an explicit vector number.
    /// </summary>
    public static InterruptAcknowledgeResult Vector(byte vectorNumber) =>
        new(InterruptAcknowledgeType.VectorNumber, vectorNumber);

    /// <summary>
    /// Gets the acknowledge mode.
    /// </summary>
    public InterruptAcknowledgeType Type { get; }

    /// <summary>
    /// Gets the vector number when <see cref="Type"/> is <see cref="InterruptAcknowledgeType.VectorNumber"/>.
    /// </summary>
    public byte VectorNumber { get; }

    private InterruptAcknowledgeResult(InterruptAcknowledgeType type, byte vectorNumber)
    {
        Type = type;
        VectorNumber = vectorNumber;
    }
}

/// <summary>
/// Specifies how an interrupt acknowledge cycle completed.
/// </summary>
public enum InterruptAcknowledgeType
{
    /// <summary>
    /// Use the level-specific autovector.
    /// </summary>
    Autovector,

    /// <summary>
    /// Use the spurious interrupt vector.
    /// </summary>
    Spurious,

    /// <summary>
    /// Use an explicit vector number provided by hardware.
    /// </summary>
    VectorNumber
}
