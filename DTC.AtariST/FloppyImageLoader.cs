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
using System.Buffers.Binary;
using System.Text;
using JetBrains.Annotations;

namespace DTC.AtariST;

/// <summary>
/// Loads Atari ST floppy image bytes from <c>.st</c> files or matching entries inside zip archives.
/// </summary>
public static class FloppyImageLoader
{
    private const int SectorSizeBytes = 512;
    private const int StxHeaderSizeBytes = 16;
    private const int StxTrackHeaderSizeBytes = 16;
    private const int StxSectorHeaderSizeBytes = 16;
    private const ushort StxVersion3 = 3;
    private const ushort StxTrackFlagSectorBlocks = 1 << 0;
    private const byte StxSectorFlagRnf = 1 << 4;
    private const int MaxTracks = 168;
    private const int MaxSides = 2;
    private const int MaxSectorsPerTrack = 32;
    private static readonly int[] PreferredSectorSizes = [9, 10, 11, 8, 12];
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".st",
        ".stx"
    };

    /// <summary>
    /// Reads one floppy image from disk. Zip files return the first matching image entry.
    /// </summary>
    public static FloppyImageLoadResult ReadImage(FileInfo imageFile)
    {
        var imageName = imageFile?.Name;
        if (imageFile == null)
            return default;

        if (!imageFile.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsSupportedExtension(imageFile.Extension))
                return new FloppyImageLoadResult(imageName, null, FloppyImageFormat.Unknown);
            imageName = Path.GetFileNameWithoutExtension(imageName);
            using var stream = imageFile.OpenRead();
            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer.AsSpan());
            return ConvertLoadedImage(imageName, imageFile.Extension, buffer);
        }

        using var archive = ZipFile.OpenRead(imageFile.FullName);
        foreach (var entry in GetPreferredZipImageEntries(archive))
        {
            if (!IsSupportedExtension(Path.GetExtension(entry.Name)))
                continue;

            var buffer = new byte[(int)entry.Length];
            using var stream = entry.Open();
            stream.ReadExactly(buffer.AsSpan());
            if (buffer.Length == 0)
                continue;

            imageName = Path.GetFileNameWithoutExtension(entry.Name);
            return ConvertLoadedImage(imageName, Path.GetExtension(entry.Name), buffer);
        }

        return new FloppyImageLoadResult(imageName, null, FloppyImageFormat.Unknown);
    }

    /// <summary>
    /// Reads one floppy image from disk and returns only name + decoded bytes.
    /// </summary>
    public static (string ImageName, byte[] ImageData) ReadImageData(FileInfo imageFile)
    {
        var result = ReadImage(imageFile);
        return (result.ImageName, result.ImageData);
    }

    /// <summary>
    /// Builds a short diagnostic summary of floppy image structure (geometry, BPB, root directory).
    /// </summary>
    public static string DescribeImage(byte[] imageData)
    {
        if (imageData == null || imageData.Length == 0)
            return "empty image";

        var description = new StringBuilder();
        var totalSectors = imageData.Length / SectorSizeBytes;
        description.Append($"bytes={imageData.Length:N0}, sectors={totalSectors:N0}");

        if (TryInferGeometryFromBootSector(imageData, out var geometry))
            description.Append($", geometry={geometry.Tracks}t/{geometry.Sides}s/{geometry.SectorsPerTrack}spt (BPB)");
        else if (TryInferGeometryFromSize(imageData, out geometry))
            description.Append($", geometry={geometry.Tracks}t/{geometry.Sides}s/{geometry.SectorsPerTrack}spt (size)");
        else
            description.Append(", geometry=unknown");

        if (!TryReadFatBootSector(imageData, out var bpb))
            return description.ToString();

        description.Append($", FAT={bpb.FatCount}x{bpb.SectorsPerFat}, root={bpb.RootEntryCount} entries");
        if (!TryCountRootDirectoryEntries(imageData, bpb, out var fileCount, out var sampleNames))
            return description.ToString();

        description.Append($", rootUsed={fileCount}");
        if (sampleNames.Count > 0)
            description.Append($", sample=[{string.Join(", ", sampleNames)}]");
        return description.ToString();
    }

    private static bool IsSupportedExtension(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && SupportedImageExtensions.Contains(extension);

    private static IEnumerable<ZipArchiveEntry> GetPreferredZipImageEntries(ZipArchive archive) =>
        archive.Entries
            .Where(entry => IsSupportedExtension(Path.GetExtension(entry.Name)))
            .OrderBy(entry => GetDiskSidePreference(entry.Name))
            .ThenBy(entry => GetFormatPreference(Path.GetExtension(entry.Name)))
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    private static int GetFormatPreference(string extension) =>
        extension.Equals(".st", StringComparison.OrdinalIgnoreCase) ? 0 :
        extension.Equals(".stx", StringComparison.OrdinalIgnoreCase) ? 1 :
        2;

    private static int GetDiskSidePreference(string entryName)
    {
        var name = Path.GetFileNameWithoutExtension(entryName) ?? string.Empty;
        var normalized = name.Trim();
        if (normalized.EndsWith("(A)", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("[A]", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(" A", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (normalized.EndsWith("(1)", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("[1]", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(" 1", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (normalized.EndsWith("(B)", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("[B]", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(" B", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (normalized.EndsWith("(2)", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("[2]", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(" 2", StringComparison.OrdinalIgnoreCase))
            return 3;
        return 0; // Prefer unsuffixed image names first.
    }

    private static FloppyImageLoadResult ConvertLoadedImage(string imageName, string extension, byte[] imageData)
    {
        if (imageData == null || imageData.Length == 0)
            return new FloppyImageLoadResult(imageName, null, ParseImageFormat(extension));

        var format = ParseImageFormat(extension);
        if (format != FloppyImageFormat.Stx)
            return new FloppyImageLoadResult(imageName, imageData, format);

        return TryConvertStxToSt(imageData, out var stImageData)
            ? new FloppyImageLoadResult(imageName, stImageData, FloppyImageFormat.Stx)
            : new FloppyImageLoadResult(imageName, null, FloppyImageFormat.Stx);
    }

    private static FloppyImageFormat ParseImageFormat(string extension)
    {
        if (extension.Equals(".st", StringComparison.OrdinalIgnoreCase))
            return FloppyImageFormat.St;
        if (extension.Equals(".stx", StringComparison.OrdinalIgnoreCase))
            return FloppyImageFormat.Stx;
        return FloppyImageFormat.Unknown;
    }

    private static bool TryConvertStxToSt(byte[] stxData, out byte[] stImageData)
    {
        stImageData = null;
        if (stxData == null || stxData.Length < StxHeaderSizeBytes)
            return false;
        if (stxData[0] != (byte)'R' || stxData[1] != (byte)'S' || stxData[2] != (byte)'Y' || stxData[3] != 0)
            return false;

        var version = ReadUInt16LittleEndian(stxData, 4);
        if (version != StxVersion3)
            return false;

        var tracksCount = stxData[10];
        if (tracksCount == 0)
            return false;

        var extractedSectors = new List<ExtractedStxSector>(tracksCount * 9);
        var offset = StxHeaderSizeBytes;
        for (var trackIndex = 0; trackIndex < tracksCount; trackIndex++)
        {
            if (offset + StxTrackHeaderSizeBytes > stxData.Length)
                return false;

            var blockSize = (int)ReadUInt32LittleEndian(stxData, offset);
            var fuzzySize = (int)ReadUInt32LittleEndian(stxData, offset + 4);
            var sectorCount = ReadUInt16LittleEndian(stxData, offset + 8);
            var flags = ReadUInt16LittleEndian(stxData, offset + 10);
            var trackNumberByte = stxData[offset + 14];
            if (blockSize < StxTrackHeaderSizeBytes || offset + blockSize > stxData.Length)
                return false;

            var trackNumber = trackNumberByte & 0x7F;
            var side = (trackNumberByte >> 7) & 0x01;
            if (trackNumber >= MaxTracks || side >= MaxSides)
                return false;

            if (sectorCount == 0)
            {
                offset += blockSize;
                continue;
            }

            if ((flags & StxTrackFlagSectorBlocks) == 0)
            {
                var sectorDataStart = offset + StxTrackHeaderSizeBytes;
                var sectorBytes = sectorCount * SectorSizeBytes;
                if (sectorDataStart + sectorBytes > offset + blockSize)
                    return false;

                for (var sectorIndex = 0; sectorIndex < sectorCount; sectorIndex++)
                {
                    var sectorId = sectorIndex + 1;
                    if (sectorId > MaxSectorsPerTrack)
                        continue;

                    var sectorOffset = sectorDataStart + sectorIndex * SectorSizeBytes;
                    extractedSectors.Add(new ExtractedStxSector(
                        (byte)trackNumber,
                        (byte)side,
                        (byte)sectorId,
                        stxData.AsSpan(sectorOffset, SectorSizeBytes).ToArray()));
                }

                offset += blockSize;
                continue;
            }

            var sectorHeadersStart = offset + StxTrackHeaderSizeBytes;
            var sectorHeadersByteLength = sectorCount * StxSectorHeaderSizeBytes;
            var fuzzyDataStart = sectorHeadersStart + sectorHeadersByteLength;
            var trackDataStart = fuzzyDataStart + fuzzySize;
            if (trackDataStart > offset + blockSize)
                return false;

            for (var sectorIndex = 0; sectorIndex < sectorCount; sectorIndex++)
            {
                var headerOffset = sectorHeadersStart + sectorIndex * StxSectorHeaderSizeBytes;
                var dataOffset = (int)ReadUInt32LittleEndian(stxData, headerOffset);
                var sectorId = stxData[headerOffset + 10];
                var idSize = stxData[headerOffset + 11];
                var fdcStatus = stxData[headerOffset + 14];
                if ((fdcStatus & StxSectorFlagRnf) != 0)
                    continue;
                if (sectorId is 0 or > MaxSectorsPerTrack)
                    continue;

                var sectorSize = 128 << (idSize & 0x03);
                if (sectorSize != SectorSizeBytes)
                    continue;

                var sectorDataOffset = trackDataStart + dataOffset;
                if (sectorDataOffset < trackDataStart || sectorDataOffset + sectorSize > offset + blockSize)
                    continue;

                extractedSectors.Add(new ExtractedStxSector(
                    (byte)trackNumber,
                    (byte)side,
                    sectorId,
                    stxData.AsSpan(sectorDataOffset, sectorSize).ToArray()));
            }

            offset += blockSize;
        }

        return TryBuildRawStImage(extractedSectors, out stImageData);
    }

    private static bool TryBuildRawStImage(List<ExtractedStxSector> extractedSectors, out byte[] stImageData)
    {
        stImageData = null;
        if (extractedSectors == null || extractedSectors.Count == 0)
            return false;

        var maxTrack = -1;
        var maxSide = -1;
        var maxSectorId = 0;
        foreach (var sector in extractedSectors)
        {
            if (sector.Track > maxTrack)
                maxTrack = sector.Track;
            if (sector.Side > maxSide)
                maxSide = sector.Side;
            if (sector.SectorId > maxSectorId)
                maxSectorId = sector.SectorId;
        }

        var tracks = maxTrack + 1;
        var sides = maxSide + 1;
        var sectorsPerTrack = maxSectorId;
        if (tracks is <= 0 or > MaxTracks || sides is <= 0 or > MaxSides || sectorsPerTrack is <= 0 or > MaxSectorsPerTrack)
            return false;

        if (!TryBuildRawStImageForGeometry(extractedSectors, tracks, sides, sectorsPerTrack, tracks * sides * sectorsPerTrack, out stImageData))
            return false;

        if (!TryReadFatBootSector(stImageData, out var bootSector))
            return true;

        var logicalSides = bootSector.SideCount;
        var logicalSectorsPerTrack = bootSector.SectorsPerTrack;
        var logicalTotalSectors = (int)bootSector.TotalSectors;
        if (logicalSides is < 1 or > MaxSides || logicalSectorsPerTrack is < 1 or > MaxSectorsPerTrack || logicalTotalSectors <= 0)
            return true;

        var sectorsPerCylinder = logicalSides * logicalSectorsPerTrack;
        var logicalTracks = (logicalTotalSectors + sectorsPerCylinder - 1) / sectorsPerCylinder;
        if (logicalTracks is <= 0 or > MaxTracks)
            return true;
        if (logicalTracks == tracks && logicalSides == sides && logicalSectorsPerTrack == sectorsPerTrack && logicalTotalSectors == tracks * sides * sectorsPerTrack)
            return true;

        if (!TryBuildRawStImageForGeometry(extractedSectors, logicalTracks, logicalSides, logicalSectorsPerTrack, logicalTotalSectors, out var logicalImage))
            return true;

        var provisionalScore = GetFatFilesystemPlausibilityScore(stImageData);
        var logicalScore = GetFatFilesystemPlausibilityScore(logicalImage);
        if (logicalScore > provisionalScore)
            stImageData = logicalImage;

        return true;
    }

    private static bool TryBuildRawStImageForGeometry(
        IEnumerable<ExtractedStxSector> extractedSectors,
        int tracks,
        int sides,
        int sectorsPerTrack,
        int totalSectors,
        out byte[] stImageData)
    {
        stImageData = null;
        if (tracks <= 0 || sides <= 0 || sectorsPerTrack <= 0 || totalSectors <= 0)
            return false;

        stImageData = new byte[totalSectors * SectorSizeBytes];
        var written = new HashSet<StSectorAddress>();
        foreach (var sector in extractedSectors)
        {
            if (sector.Track >= tracks || sector.Side >= sides || sector.SectorId == 0 || sector.SectorId > sectorsPerTrack)
                continue;

            var address = new StSectorAddress(sector.Track, sector.Side, sector.SectorId);
            if (!written.Add(address))
                continue;

            var linearSector = ((sector.Track * sides) + sector.Side) * sectorsPerTrack + (sector.SectorId - 1);
            if (linearSector < 0 || linearSector >= totalSectors)
                continue;

            var byteOffset = linearSector * SectorSizeBytes;
            sector.Data.AsSpan().CopyTo(stImageData.AsSpan(byteOffset, SectorSizeBytes));
        }

        return written.Count > 0;
    }

    private static int GetFatFilesystemPlausibilityScore(byte[] imageData)
    {
        if (!TryReadFatBootSector(imageData, out var bootSector))
            return 0;

        var score = 100; // Valid BPB and bounds checks passed.
        var rootDirByteLength = bootSector.RootEntryCount * 32;
        var rootDirOffset = (bootSector.ReservedSectorCount + bootSector.FatCount * bootSector.SectorsPerFat) * SectorSizeBytes;
        if (rootDirOffset < 0 || rootDirOffset + rootDirByteLength > imageData.Length)
            return 0;

        var activeEntries = 0;
        var plausibleEntries = 0;
        var suspiciousEntries = 0;
        for (var offset = rootDirOffset; offset < rootDirOffset + rootDirByteLength; offset += 32)
        {
            var firstByte = imageData[offset];
            if (firstByte == 0x00)
                break;
            if (firstByte == 0xE5)
                continue;

            activeEntries++;
            var attributes = imageData[offset + 11];
            if ((attributes & 0xC0) != 0)
            {
                suspiciousEntries++;
                continue;
            }

            var isLongFileName = (attributes & 0x0F) == 0x0F;
            if (isLongFileName)
            {
                plausibleEntries++;
                continue;
            }

            if (IsPlausibleDosName(imageData.AsSpan(offset, 11)))
                plausibleEntries++;
            else
                suspiciousEntries++;
        }

        score += plausibleEntries * 12;
        score -= suspiciousEntries * 20;
        if (activeEntries == 0)
            score -= 10;
        return score;
    }

    private static bool IsPlausibleDosName(ReadOnlySpan<byte> rawName)
    {
        var hasVisibleChar = false;
        for (var i = 0; i < rawName.Length; i++)
        {
            var value = rawName[i];
            if (value == (byte)' ')
                continue;
            if (value < 0x20 || value > 0x7E)
                return false;

            if (value is (byte)'\"' or (byte)'*' or (byte)'+' or (byte)',' or (byte)'/' or (byte)':'
                or (byte)';' or (byte)'<' or (byte)'=' or (byte)'>' or (byte)'?' or (byte)'[' or (byte)'\\' or (byte)']' or (byte)'|')
                return false;

            hasVisibleChar = true;
        }

        return hasVisibleChar;
    }

    private static ushort ReadUInt16LittleEndian(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, sizeof(ushort)));

    private static uint ReadUInt32LittleEndian(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)));

    private static bool TryReadFatBootSector(byte[] imageData, out FatBootSector bootSector)
    {
        bootSector = default;
        if (imageData == null || imageData.Length < SectorSizeBytes)
            return false;

        var bytesPerSector = ReadUInt16LittleEndian(imageData, 11);
        var sectorsPerCluster = imageData[13];
        var reservedSectorCount = ReadUInt16LittleEndian(imageData, 14);
        var fatCount = imageData[16];
        var rootEntryCount = ReadUInt16LittleEndian(imageData, 17);
        var totalSectors16 = ReadUInt16LittleEndian(imageData, 19);
        var sectorsPerFat = ReadUInt16LittleEndian(imageData, 22);
        var sectorsPerTrack = ReadUInt16LittleEndian(imageData, 24);
        var sideCount = ReadUInt16LittleEndian(imageData, 26);
        var totalSectors32 = ReadUInt32LittleEndian(imageData, 32);
        var totalSectors = totalSectors16 != 0 ? totalSectors16 : totalSectors32;
        if (bytesPerSector != SectorSizeBytes)
            return false;
        if (sectorsPerCluster == 0 || reservedSectorCount == 0 || fatCount == 0 || sectorsPerFat == 0 || rootEntryCount == 0)
            return false;
        if (sectorsPerTrack is < 1 or > 32 || sideCount is < 1 or > 2)
            return false;
        if (totalSectors == 0)
            totalSectors = (uint)(imageData.Length / SectorSizeBytes);
        if (totalSectors * bytesPerSector > imageData.Length)
            return false;

        bootSector = new FatBootSector(
            bytesPerSector,
            sectorsPerCluster,
            reservedSectorCount,
            fatCount,
            rootEntryCount,
            sectorsPerFat,
            sectorsPerTrack,
            sideCount,
            totalSectors);
        return true;
    }

    private static bool TryCountRootDirectoryEntries(byte[] imageData, FatBootSector bootSector, out int fileCount, out IReadOnlyList<string> sampleNames)
    {
        fileCount = 0;
        sampleNames = [];
        var rootDirByteLength = bootSector.RootEntryCount * 32;
        var rootDirOffset = (bootSector.ReservedSectorCount + bootSector.FatCount * bootSector.SectorsPerFat) * SectorSizeBytes;
        if (rootDirOffset < 0 || rootDirOffset + rootDirByteLength > imageData.Length)
            return false;

        var names = new List<string>(4);
        for (var offset = rootDirOffset; offset < rootDirOffset + rootDirByteLength; offset += 32)
        {
            var firstByte = imageData[offset];
            if (firstByte == 0x00)
                break;
            if (firstByte == 0xE5)
                continue;

            var attributes = imageData[offset + 11];
            var isVolumeLabel = (attributes & 0x08) != 0;
            var isLongFileName = (attributes & 0x0F) == 0x0F;
            if (isVolumeLabel || isLongFileName)
                continue;

            fileCount++;
            if (names.Count >= 4)
                continue;

            names.Add(FormatDosName(imageData.AsSpan(offset, 11)));
        }

        sampleNames = names;
        return true;
    }

    private static bool TryInferGeometryFromBootSector(byte[] imageData, out FloppyGeometry geometry)
    {
        geometry = default;
        if (!TryReadFatBootSector(imageData, out var bootSector))
            return false;

        var totalSectors = (int)bootSector.TotalSectors;
        var sectorsPerCylinder = bootSector.SectorsPerTrack * bootSector.SideCount;
        if (sectorsPerCylinder == 0 || totalSectors % sectorsPerCylinder != 0)
            return false;

        var tracks = totalSectors / sectorsPerCylinder;
        if (tracks is < 35 or > 90)
            return false;

        geometry = new FloppyGeometry(tracks, bootSector.SideCount, bootSector.SectorsPerTrack);
        return true;
    }

    private static bool TryInferGeometryFromSize(byte[] imageData, out FloppyGeometry geometry)
    {
        geometry = default;
        if (imageData == null || imageData.Length < SectorSizeBytes || imageData.Length % SectorSizeBytes != 0)
            return false;

        var totalSectors = imageData.Length / SectorSizeBytes;
        var hasCandidate = false;
        var bestScore = int.MaxValue;
        foreach (var sides in new[] { 2, 1 })
        {
            for (var sectorsIndex = 0; sectorsIndex < PreferredSectorSizes.Length; sectorsIndex++)
            {
                var sectorsPerTrack = PreferredSectorSizes[sectorsIndex];
                var sectorsPerCylinder = sectorsPerTrack * sides;
                if (totalSectors % sectorsPerCylinder != 0)
                    continue;

                var tracks = totalSectors / sectorsPerCylinder;
                if (tracks is < 35 or > 90)
                    continue;

                var score = Math.Abs(tracks - 80) * 100 + Math.Abs(sides - 2) * 10 + sectorsIndex;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                geometry = new FloppyGeometry(tracks, sides, sectorsPerTrack);
                hasCandidate = true;
            }
        }

        return hasCandidate;
    }

    private static string FormatDosName(ReadOnlySpan<byte> rawName)
    {
        Span<char> nameBuffer = stackalloc char[12];
        var nameLength = 0;
        for (var i = 0; i < 8; i++)
        {
            var c = rawName[i];
            if (c == (byte)' ')
                break;
            nameBuffer[nameLength++] = (char)c;
        }

        var extStart = nameLength;
        var extLength = 0;
        for (var i = 8; i < 11; i++)
        {
            var c = rawName[i];
            if (c == (byte)' ')
                break;
            if (extLength == 0)
                nameBuffer[nameLength++] = '.';
            nameBuffer[nameLength++] = (char)c;
            extLength++;
        }

        if (extLength == 0)
            nameLength = extStart;
        return new string(nameBuffer[..nameLength]);
    }

    private readonly record struct FloppyGeometry(int Tracks, int Sides, int SectorsPerTrack);

    private readonly record struct ExtractedStxSector(byte Track, byte Side, byte SectorId, byte[] Data);

    private readonly record struct StSectorAddress(byte Track, byte Side, byte SectorId);

    private readonly record struct FatBootSector(
        [UsedImplicitly] ushort BytesPerSector,
        byte SectorsPerCluster,
        ushort ReservedSectorCount,
        byte FatCount,
        ushort RootEntryCount,
        ushort SectorsPerFat,
        ushort SectorsPerTrack,
        ushort SideCount,
        uint TotalSectors);
}

/// <summary>
/// Describes the source floppy image container format that was loaded.
/// </summary>
public enum FloppyImageFormat
{
    /// <summary>
    /// Unknown or unsupported format.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Raw sector-dump floppy image (<c>.st</c>).
    /// </summary>
    St,

    /// <summary>
    /// Pasti STX image (<c>.stx</c>) converted to a raw sector image for this emulator's FDC model.
    /// </summary>
    Stx
}

/// <summary>
/// Result of loading one floppy image from disk or zip archive.
/// </summary>
public readonly record struct FloppyImageLoadResult(string ImageName, byte[] ImageData, FloppyImageFormat SourceFormat)
{
    /// <summary>
    /// Gets a user-facing format label (for example <c>.st</c> or <c>.stx</c>).
    /// </summary>
    public string FormatLabel =>
        SourceFormat switch
        {
            FloppyImageFormat.St => ".st",
            FloppyImageFormat.Stx => ".stx",
            _ => "unknown"
        };
}
