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
/// Snapshot of recent floppy controller activity used for diagnosing hangs.
/// </summary>
public readonly record struct FloppyDebugStats(
    long CommandCount,
    long ReadSectorCommandCount,
    long SuccessfulReadSectorCommandCount,
    long DmaBytesWritten,
    byte LastCommand,
    byte LastStatus,
    ushort LastDmaStatusWord,
    string LastTraceLine);
