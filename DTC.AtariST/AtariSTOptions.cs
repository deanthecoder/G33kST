// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.AtariST;

/// <summary>
/// Configuration values used when constructing an Atari ST machine instance.
/// </summary>
public sealed class AtariSTOptions
{
    /// <summary>
    /// Defaults for a practical Atari 1040 STFM target:
    /// 1 MB ST-RAM, color monitor, no Mega ST real-time clock.
    /// </summary>
    public static AtariSTOptions Default => new();

    /// <summary>
    /// Amount of ST-RAM exposed to the machine in bytes.
    /// </summary>
    public uint RamSizeBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Monitor type reported through MFP monitor-detect input.
    /// </summary>
    public AtariMonitorType MonitorType { get; init; } = AtariMonitorType.Color;

    /// <summary>
    /// Enables the Mega ST real-time clock window at $FFFC20-$FFFC3F.
    /// Keep this disabled for STFM.
    /// </summary>
    public bool HasRealTimeClock { get; init; }

    /// <summary>
    /// Speeds up floppy command completion for faster boot/test loops.
    /// </summary>
    /// <remarks>
    /// When enabled, floppy timing latency is bypassed so commands complete immediately.
    /// </remarks>
    public bool AccelerateFloppyAccess { get; init; }

    /// <summary>
    /// Mirrors host joystick input to IKBD joystick port 0 in addition to port 1.
    /// </summary>
    /// <remarks>
    /// Useful for software that expects input on the mouse-port joystick path.
    /// </remarks>
    public bool MirrorJoystickToPort0 { get; init; }
}
