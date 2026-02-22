// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using DTC.Emulation.Snapshot;

namespace G33kST.Snapshot;

/// <summary>
/// Reads and writes G33kST snapshot files (metadata + serialized machine state).
/// </summary>
public static class SnapshotFile
{
    private const uint FileMagic = 0x56535453; // "STSV" (little-endian).
    private const ushort FileVersion = 1;

    public static void Save(FileInfo file, MachineState state, string romPath, string floppyDriveAPath, string floppyDriveBPath = null)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var romPathBytes = Encoding.UTF8.GetBytes(romPath ?? string.Empty);
        var floppyAPathBytes = Encoding.UTF8.GetBytes(floppyDriveAPath ?? string.Empty);
        var floppyBPathBytes = Encoding.UTF8.GetBytes(floppyDriveBPath ?? string.Empty);

        using var stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(FileMagic);
        writer.Write(FileVersion);
        writer.Write((ushort)0);
        writer.Write(romPathBytes.Length);
        writer.Write(floppyAPathBytes.Length);
        writer.Write(floppyBPathBytes.Length);
        writer.Write(state.Size);
        writer.Write(romPathBytes);
        writer.Write(floppyAPathBytes);
        writer.Write(floppyBPathBytes);

        var buffer = new byte[state.Size];
        var stateReader = state.CreateReader();
        stateReader.ReadBytes(buffer);
        writer.Write(buffer);
    }

    public static MachineState Load(FileInfo file, out string romPath, out string floppyDriveAPath, out string floppyDriveBPath)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        using var stream = file.OpenRead();
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        var magic = reader.ReadUInt32();
        if (magic != FileMagic)
            throw new InvalidOperationException("Invalid snapshot file.");

        var version = reader.ReadUInt16();
        if (version != FileVersion)
            throw new InvalidOperationException($"Unsupported snapshot version {version}.");

        reader.ReadUInt16(); // reserved
        var romPathLength = reader.ReadInt32();
        var floppyAPathLength = reader.ReadInt32();
        var floppyBPathLength = reader.ReadInt32();
        var stateSize = reader.ReadInt32();
        if (romPathLength < 0 || floppyAPathLength < 0 || floppyBPathLength < 0 || stateSize <= 0)
            throw new InvalidOperationException("Snapshot file is corrupt.");

        var romPathBytes = reader.ReadBytes(romPathLength);
        var floppyAPathBytes = reader.ReadBytes(floppyAPathLength);
        var floppyBPathBytes = reader.ReadBytes(floppyBPathLength);
        var stateBytes = reader.ReadBytes(stateSize);
        if (romPathBytes.Length != romPathLength ||
            floppyAPathBytes.Length != floppyAPathLength ||
            floppyBPathBytes.Length != floppyBPathLength ||
            stateBytes.Length != stateSize)
            throw new InvalidOperationException("Snapshot file is corrupt.");

        romPath = Encoding.UTF8.GetString(romPathBytes);
        floppyDriveAPath = Encoding.UTF8.GetString(floppyAPathBytes);
        floppyDriveBPath = Encoding.UTF8.GetString(floppyBPathBytes);

        var state = new MachineState(stateSize);
        var stateWriter = state.CreateWriter();
        stateWriter.WriteBytes(stateBytes);
        return state;
    }
}
