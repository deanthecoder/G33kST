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

namespace DTC.AtariST;

/// <summary>
/// Loads Atari ST floppy image bytes from <c>.st</c> files or matching entries inside zip archives.
/// </summary>
public static class FloppyImageLoader
{
    private const int StxHeaderSize = 16;
    private const int StxTrackHeaderSize = 16;
    private const int StxSectorHeaderSize = 16;
    private const int SectorSizeBytes = 512;
    private const byte StxRecordNotFoundFlag = 0x10;
    private static readonly int[] PreferredSectorSizes = [9, 10, 11, 8, 12];
    private static ReadOnlySpan<byte> StxHeaderId => "RSY\0"u8;
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".st",
        ".stx"
    };

    /// <summary>
    /// Reads one floppy image from disk. Zip files return the first matching image entry.
    /// </summary>
    public static (string ImageName, byte[] ImageData) ReadImageData(FileInfo imageFile)
    {
        var imageName = imageFile?.Name;
        if (imageFile == null)
            return (null, null);

        if (!imageFile.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsSupportedExtension(imageFile.Extension))
                return (imageName, null);
            imageName = Path.GetFileNameWithoutExtension(imageName);
            using var stream = imageFile.OpenRead();
            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer.AsSpan());
            if (imageFile.Extension.Equals(".stx", StringComparison.OrdinalIgnoreCase))
                buffer = ConvertStxToSectorImage(buffer);
            return (imageName, buffer);
        }

        using var archive = ZipFile.OpenRead(imageFile.FullName);
        foreach (var entry in archive.Entries)
        {
            if (!IsSupportedExtension(Path.GetExtension(entry.Name)))
                continue;

            var buffer = new byte[(int)entry.Length];
            using var stream = entry.Open();
            stream.ReadExactly(buffer.AsSpan());
            if (Path.GetExtension(entry.Name).Equals(".stx", StringComparison.OrdinalIgnoreCase))
                buffer = ConvertStxToSectorImage(buffer);
            if (buffer == null || buffer.Length == 0)
                continue;

            imageName = Path.GetFileNameWithoutExtension(entry.Name);
            return (imageName, buffer);
        }

        return (imageName, null);
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

    private static bool IsSupportedExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;
        return SupportedImageExtensions.Contains(extension);
    }

    private static byte[] ConvertStxToSectorImage(byte[] stxData)
    {
        if (stxData == null || stxData.Length < StxHeaderSize)
            return null;
        if (!stxData.AsSpan(0, StxHeaderId.Length).SequenceEqual(StxHeaderId))
            return null;

        var trackBlockCount = stxData[10];
        if (trackBlockCount == 0)
            return null;

        var sectors = new Dictionary<(int Track, int Side, int Sector), byte[]>();
        var offset = StxHeaderSize;
        var maxTrack = -1;
        var maxSide = -1;
        var maxSector = -1;

        for (var trackBlockIndex = 0; trackBlockIndex < trackBlockCount; trackBlockIndex++)
        {
            if (offset + StxTrackHeaderSize > stxData.Length)
                break;

            var trackBlockSize = ReadUInt32LittleEndian(stxData, offset);
            var sectorsCount = ReadUInt16LittleEndian(stxData, offset + 8);
            var trackNumberWithSide = stxData[offset + 14];
            if (trackBlockSize < StxTrackHeaderSize)
                break;

            var trackBlockStart = offset;
            var sectorBlockStart = offset + StxTrackHeaderSize;
            var fallbackTrack = trackNumberWithSide & 0x7F;
            var fallbackSide = (trackNumberWithSide >> 7) & 0x01;
            for (var sectorIndex = 0; sectorIndex < sectorsCount; sectorIndex++)
            {
                var sectorOffset = sectorBlockStart + (sectorIndex * StxSectorHeaderSize);
                if (sectorOffset + StxSectorHeaderSize > stxData.Length)
                    break;

                var dataOffset = ReadUInt32LittleEndian(stxData, sectorOffset);
                var idTrack = stxData[sectorOffset + 8];
                var idHead = stxData[sectorOffset + 9];
                var idSector = stxData[sectorOffset + 10];
                var idSizeCode = stxData[sectorOffset + 11];
                var fdcStatus = stxData[sectorOffset + 14];
                if ((fdcStatus & StxRecordNotFoundFlag) != 0)
                    continue;

                var sectorSize = 128 << (idSizeCode & 0x03);
                if (sectorSize <= 0)
                    continue;

                var dataStart = trackBlockStart + (int)dataOffset;
                if (dataStart < 0 || dataStart + sectorSize > stxData.Length)
                    continue;

                var track = (int)idTrack;
                if (track == 0 && fallbackTrack != 0)
                    track = fallbackTrack;
                var side = idHead & 0x01;
                if (idHead == 0 && fallbackSide != 0)
                    side = fallbackSide;
                var sectorNumber = (int)idSector;
                if (sectorNumber <= 0)
                    continue;

                var normalizedSector = new byte[512];
                if (sectorSize >= normalizedSector.Length)
                    Buffer.BlockCopy(stxData, dataStart, normalizedSector, 0, normalizedSector.Length);
                else
                    Buffer.BlockCopy(stxData, dataStart, normalizedSector, 0, sectorSize);

                sectors[(track, side, sectorNumber)] = normalizedSector;
                if (track > maxTrack)
                    maxTrack = track;
                if (side > maxSide)
                    maxSide = side;
                if (sectorNumber > maxSector)
                    maxSector = sectorNumber;
            }

            offset += (int)trackBlockSize;
            if (offset > stxData.Length)
                break;
        }

        if (sectors.Count == 0 || maxTrack < 0 || maxSide < 0 || maxSector <= 0)
            return null;

        var trackCount = maxTrack + 1;
        var sideCount = maxSide + 1;
        var sectorsPerTrack = maxSector;
        var image = new byte[trackCount * sideCount * sectorsPerTrack * 512];
        foreach (var entry in sectors)
        {
            var (track, side, sectorNumber) = entry.Key;
            var linearSector = ((track * sideCount) + side) * sectorsPerTrack + (sectorNumber - 1);
            if (linearSector < 0)
                continue;
            var destinationOffset = linearSector * 512;
            if (destinationOffset + 512 > image.Length)
                continue;
            Buffer.BlockCopy(entry.Value, 0, image, destinationOffset, 512);
        }

        return image;
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
        var rootDirOffset = (bootSector.ReservedSectorCount + (bootSector.FatCount * bootSector.SectorsPerFat)) * SectorSizeBytes;
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

    private readonly record struct FatBootSector(
        ushort BytesPerSector,
        byte SectorsPerCluster,
        ushort ReservedSectorCount,
        byte FatCount,
        ushort RootEntryCount,
        ushort SectorsPerFat,
        ushort SectorsPerTrack,
        ushort SideCount,
        uint TotalSectors);
}
