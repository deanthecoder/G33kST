// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.IO.Compression;

namespace DTC.AtariST;

/// <summary>
/// Loads Atari ST ROM bytes from plain files or from supported entries inside zip archives.
/// </summary>
public static class RomImageLoader
{
    private static readonly HashSet<string> SupportedRomExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".img",
        ".rom",
        ".bin"
    };

    /// <summary>
    /// Reads one ROM image from disk.
    /// Zip files return the first supported entry they contain.
    /// </summary>
    public static (string RomFileName, byte[] RomData) ReadRomData(FileInfo romFile)
    {
        var romName = romFile?.Name;
        if (romFile == null)
            return (null, null);

        if (!romFile.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsSupportedRomExtension(romFile.Extension))
                return (romName, null);

            using var stream = romFile.OpenRead();
            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer.AsSpan());
            return (romName, buffer);
        }

        using var archive = ZipFile.OpenRead(romFile.FullName);
        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0)
                continue;
            if (!IsSupportedRomExtension(Path.GetExtension(entry.FullName)))
                continue;

            var buffer = new byte[(int)entry.Length];
            using var stream = entry.Open();
            stream.ReadExactly(buffer.AsSpan());
            return (Path.GetFileName(entry.FullName), buffer);
        }

        return (romName, null);
    }

    private static bool IsSupportedRomExtension(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && SupportedRomExtensions.Contains(extension);
}
