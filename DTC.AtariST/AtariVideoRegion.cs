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
/// Selects the machine's video region timing preset.
/// </summary>
public enum AtariVideoRegion
{
    /// <summary>
    /// NTSC timing (60 Hz vertical refresh).
    /// </summary>
    Ntsc,

    /// <summary>
    /// PAL timing (50 Hz vertical refresh).
    /// </summary>
    Pal
}
