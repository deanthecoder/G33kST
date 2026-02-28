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
using DTC.AtariST;

namespace UnitTests;

[TestFixture]
public sealed class FloppyImageLoaderTests
{
    [Test]
    public void ReadImageDataShouldLoadRawStFile()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = new FileInfo(Path.Combine(tempDir.FullName, "disk.st"));
            var expected = new byte[] { 0xAA, 0xBB, 0xCC };
            File.WriteAllBytes(file.FullName, expected);

            var (imageName, imageData) = FloppyImageLoader.ReadImageData(file);

            Assert.Multiple(() =>
            {
                Assert.That(imageName, Is.EqualTo("disk"));
                Assert.That(imageData, Is.EqualTo(expected));
            });

            var result = FloppyImageLoader.ReadImage(file);
            Assert.That(result.SourceFormat, Is.EqualTo(FloppyImageFormat.St));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void ReadImageDataShouldLoadFirstStEntryFromZip()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var zipFile = new FileInfo(Path.Combine(tempDir.FullName, "images.zip"));
            using (var archive = ZipFile.Open(zipFile.FullName, ZipArchiveMode.Create))
            {
                var ignoredEntry = archive.CreateEntry("readme.txt");
                using (var stream = ignoredEntry.Open())
                {
                    var data = new byte[] { 0x01 };
                    stream.Write(data, 0, data.Length);
                }

                var diskEntry = archive.CreateEntry("disk-a.st");
                using var diskStream = diskEntry.Open();
                var diskData = new byte[] { 0x10, 0x20, 0x30 };
                diskStream.Write(diskData, 0, diskData.Length);
            }

            var (imageName, imageData) = FloppyImageLoader.ReadImageData(zipFile);

            Assert.Multiple(() =>
            {
                Assert.That(imageName, Is.EqualTo("disk-a"));
                Assert.That(imageData, Is.EqualTo(new byte[] { 0x10, 0x20, 0x30 }));
            });
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void ReadImageShouldConvertSimpleStxTrackToRawSectorImage()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = new FileInfo(Path.Combine(tempDir.FullName, "disk.stx"));
            var expectedSector = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
            File.WriteAllBytes(file.FullName, CreateSimpleStxWithSectorBlocks(expectedSector));

            var result = FloppyImageLoader.ReadImage(file);

            Assert.Multiple(() =>
            {
                Assert.That(result.ImageName, Is.EqualTo("disk"));
                Assert.That(result.SourceFormat, Is.EqualTo(FloppyImageFormat.Stx));
                Assert.That(result.ImageData, Is.Not.Null);
                Assert.That(result.ImageData!.Length, Is.EqualTo(512));
                Assert.That(result.ImageData, Is.EqualTo(expectedSector));
            });
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void ReadImageShouldLoadFirstStxEntryFromZip()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var zipFile = new FileInfo(Path.Combine(tempDir.FullName, "images.zip"));
            var expectedSector = Enumerable.Repeat((byte)0xA5, 512).ToArray();
            using (var archive = ZipFile.Open(zipFile.FullName, ZipArchiveMode.Create))
            {
                var stxEntry = archive.CreateEntry("disk-b.stx");
                using var stxStream = stxEntry.Open();
                var stxData = CreateSimpleStxWithSectorBlocks(expectedSector);
                stxStream.Write(stxData, 0, stxData.Length);
            }

            var result = FloppyImageLoader.ReadImage(zipFile);

            Assert.Multiple(() =>
            {
                Assert.That(result.ImageName, Is.EqualTo("disk-b"));
                Assert.That(result.SourceFormat, Is.EqualTo(FloppyImageFormat.Stx));
                Assert.That(result.ImageData, Is.EqualTo(expectedSector));
            });
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void ReadImageShouldPreferUnsuffixedDiskImageOverBSideEntryInZip()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var zipFile = new FileInfo(Path.Combine(tempDir.FullName, "multi-disk.zip"));
            using (var archive = ZipFile.Open(zipFile.FullName, ZipArchiveMode.Create))
            {
                var sideBEntry = archive.CreateEntry("Mega lo Mania (B).stx");
                using (var sideBStream = sideBEntry.Open())
                {
                    var sideBData = CreateSimpleStxWithSectorBlocks(Enumerable.Repeat((byte)0xDB, 512).ToArray());
                    sideBStream.Write(sideBData, 0, sideBData.Length);
                }

                var sideAEntry = archive.CreateEntry("Mega lo Mania.stx");
                using var sideAStream = sideAEntry.Open();
                var sideAData = CreateSimpleStxWithSectorBlocks(Enumerable.Repeat((byte)0xA5, 512).ToArray());
                sideAStream.Write(sideAData, 0, sideAData.Length);
            }

            var result = FloppyImageLoader.ReadImage(zipFile);

            Assert.Multiple(() =>
            {
                Assert.That(result.ImageName, Is.EqualTo("Mega lo Mania"));
                Assert.That(result.SourceFormat, Is.EqualTo(FloppyImageFormat.Stx));
                Assert.That(result.ImageData, Is.Not.Null);
                Assert.That(result.ImageData![0], Is.EqualTo(0xA5));
            });
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void ReadImageShouldPreferBootSectorGeometryWhenStxContainsExtraPhysicalSideData()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = new FileInfo(Path.Combine(tempDir.FullName, "single-sided.stx"));
            var bootSector = CreateBootSector(
                totalSectors: 4,
                sectorsPerTrack: 2,
                sideCount: 1,
                reservedSectors: 1,
                fatCount: 1,
                sectorsPerFat: 1,
                rootEntryCount: 16);
            var side1Garbage = Enumerable.Repeat((byte)0xDB, 512).ToArray();
            File.WriteAllBytes(file.FullName, CreateSimpleStxWithSectorBlocks(
                new StxSectorSpec(0, 0, 1, bootSector),      // logical sector 0
                new StxSectorSpec(0, 0, 2, new byte[512]),   // logical sector 1 (FAT)
                new StxSectorSpec(0, 1, 1, side1Garbage),    // wrong data if treated as 2-sided provisional layout
                new StxSectorSpec(0, 1, 2, new byte[512]),
                new StxSectorSpec(1, 0, 1, CreateRootDirectorySector("README.TXT")), // logical sector 2 (root) after one-sided remap
                new StxSectorSpec(1, 0, 2, new byte[512])));

            var result = FloppyImageLoader.ReadImage(file);
            var summary = FloppyImageLoader.DescribeImage(result.ImageData);

            Assert.Multiple(() =>
            {
                Assert.That(result.SourceFormat, Is.EqualTo(FloppyImageFormat.Stx));
                Assert.That(result.ImageData, Is.Not.Null);
                Assert.That(result.ImageData!.Length, Is.EqualTo(4 * 512));
                Assert.That(summary, Does.Contain("README.TXT"), "Loader should apply the BPB one-sided remap when it improves FAT root directory plausibility.");
            });
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void ReadImageShouldKeepPhysicalLayoutWhenBpbRemapProducesGarbageRootEntries()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = new FileInfo(Path.Combine(tempDir.FullName, "conflict.stx"));
            var sectors = new List<StxSectorSpec>();
            var bootSector = CreateBootSector(
                totalSectors: 20,
                sectorsPerTrack: 10,
                sideCount: 1,
                reservedSectors: 1,
                fatCount: 2,
                sectorsPerFat: 5,
                rootEntryCount: 16);

            // Physical track 0 side 0 sectors 1..10.
            sectors.Add(new StxSectorSpec(0, 0, 1, bootSector));
            for (byte sectorId = 2; sectorId <= 10; sectorId++)
                sectors.Add(new StxSectorSpec(0, 0, sectorId, new byte[512]));

            // Physical track 0 side 1 sector 2 maps to logical sector 11 when treated as a 2-sided physical dump.
            sectors.Add(new StxSectorSpec(0, 1, 1, new byte[512]));
            sectors.Add(new StxSectorSpec(0, 1, 2, CreateRootDirectorySector("README.TXT")));
            for (byte sectorId = 3; sectorId <= 10; sectorId++)
                sectors.Add(new StxSectorSpec(0, 1, sectorId, new byte[512]));

            File.WriteAllBytes(file.FullName, CreateSimpleStxWithSectorBlocks(sectors.ToArray()));

            var result = FloppyImageLoader.ReadImage(file);
            var summary = FloppyImageLoader.DescribeImage(result.ImageData);

            Assert.Multiple(() =>
            {
                Assert.That(result.SourceFormat, Is.EqualTo(FloppyImageFormat.Stx));
                Assert.That(result.ImageData, Is.Not.Null);
                Assert.That(summary, Does.Contain("README.TXT"), "Loader should keep the physical-sector layout when BPB remap breaks root directory data.");
            });
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void DescribeImageShouldReportGeometryAndRootDirectorySummary()
    {
        var imageData = CreateFat12ImageWithRootEntries("DEGAS.PRG", "README.TXT");

        var summary = FloppyImageLoader.DescribeImage(imageData);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("bytes=737,280"));
            Assert.That(summary, Does.Contain("geometry=80t/2s/9spt (BPB)"));
            Assert.That(summary, Does.Contain("rootUsed=2"));
            Assert.That(summary, Does.Contain("DEGAS.PRG"));
            Assert.That(summary, Does.Contain("README.TXT"));
        });
    }

    [Test]
    public void ReadImageShouldKeepRawStLayoutWithoutHeuristicRemap()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = new FileInfo(Path.Combine(tempDir.FullName, "side-major.st"));
            var trackMajorImage = CreateFat12ImageWithRootEntries("README.TXT");
            var sideMajorImage = ConvertTrackMajorToSideMajorLayout(trackMajorImage, tracks: 80, sides: 2, sectorsPerTrack: 9);
            File.WriteAllBytes(file.FullName, sideMajorImage);

            var result = FloppyImageLoader.ReadImage(file);

            Assert.Multiple(() =>
            {
                Assert.That(result.SourceFormat, Is.EqualTo(FloppyImageFormat.St));
                Assert.That(result.ImageData, Is.EqualTo(sideMajorImage));
            });
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void ReadImageShouldKeepTrackMajorRawStLayoutWhenAlreadyPlausible()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = new FileInfo(Path.Combine(tempDir.FullName, "track-major.st"));
            var trackMajorImage = CreateFat12ImageWithRootEntries("README.TXT");
            File.WriteAllBytes(file.FullName, trackMajorImage);

            var result = FloppyImageLoader.ReadImage(file);
            var summary = FloppyImageLoader.DescribeImage(result.ImageData);

            Assert.Multiple(() =>
            {
                Assert.That(result.SourceFormat, Is.EqualTo(FloppyImageFormat.St));
                Assert.That(result.ImageData, Is.EqualTo(trackMajorImage));
                Assert.That(summary, Does.Contain("README.TXT"));
            });
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void DescribeImageShouldSanitizeNonAsciiRootEntryCharacters()
    {
        var imageData = CreateFat12ImageWithRootEntries("README.TXT");
        const int rootDirOffset = (1 + 2 * 3) * 512;

        imageData[rootDirOffset] = 0xC3;
        var summary = FloppyImageLoader.DescribeImage(imageData);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("?EADME.TXT"));
            Assert.That(summary.Any(c => c > 0x7E), Is.False);
        });
    }

    private static DirectoryInfo CreateTempDirectory()
    {
        var dir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"g33kst-tests-{Guid.NewGuid():N}"));
        dir.Create();
        return dir;
    }

    private static byte[] CreateFat12ImageWithRootEntries(params string[] fileNames)
    {
        const int bytesPerSector = 512;
        const int sectorsPerTrack = 9;
        const int sideCount = 2;
        const int totalSectors = 80 * sectorsPerTrack * sideCount;
        const int reservedSectors = 1;
        const int fatCount = 2;
        const int sectorsPerFat = 3;
        const int rootEntryCount = 112;
        var image = new byte[totalSectors * bytesPerSector];
        image[0] = 0xEB;
        image[1] = 0x3C;
        image[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(11, 2), bytesPerSector);
        image[13] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(14, 2), reservedSectors);
        image[16] = fatCount;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(17, 2), rootEntryCount);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(19, 2), totalSectors);
        image[21] = 0xF9;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(22, 2), sectorsPerFat);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(24, 2), sectorsPerTrack);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(26, 2), sideCount);
        image[510] = 0x55;
        image[511] = 0xAA;

        const int rootDirOffset = (reservedSectors + fatCount * sectorsPerFat) * bytesPerSector;
        for (var index = 0; index < fileNames.Length; index++)
        {
            var entryOffset = rootDirOffset + index * 32;
            if (entryOffset + 32 > image.Length)
                break;
            WriteDosName(image.AsSpan(entryOffset, 11), fileNames[index]);
            image[entryOffset + 11] = 0x20;
        }

        return image;
    }

    private static byte[] ConvertTrackMajorToSideMajorLayout(byte[] trackMajorImage, int tracks, int sides, int sectorsPerTrack)
    {
        if (trackMajorImage == null || trackMajorImage.Length == 0 || trackMajorImage.Length % 512 != 0)
            throw new ArgumentException("Expected non-empty 512-byte aligned image data.", nameof(trackMajorImage));
        if (tracks <= 0 || sides <= 0 || sectorsPerTrack <= 0)
            throw new ArgumentOutOfRangeException(nameof(tracks), "Expected positive geometry values.");

        var totalSectors = trackMajorImage.Length / 512;
        if (tracks * sides * sectorsPerTrack != totalSectors)
            throw new ArgumentException("Geometry does not match image size.", nameof(trackMajorImage));

        var sideMajorImage = new byte[trackMajorImage.Length];
        for (var track = 0; track < tracks; track++)
        {
            for (var side = 0; side < sides; side++)
            {
                for (var sector = 0; sector < sectorsPerTrack; sector++)
                {
                    var sourceLinearSector = ((track * sides) + side) * sectorsPerTrack + sector;
                    var destinationLinearSector = ((side * tracks) + track) * sectorsPerTrack + sector;
                    var sourceOffset = sourceLinearSector * 512;
                    var destinationOffset = destinationLinearSector * 512;
                    trackMajorImage.AsSpan(sourceOffset, 512).CopyTo(sideMajorImage.AsSpan(destinationOffset, 512));
                }
            }
        }

        return sideMajorImage;
    }

    private static void WriteDosName(Span<byte> destination, string name)
    {
        destination.Fill((byte)' ');
        if (string.IsNullOrWhiteSpace(name))
            return;

        var parts = name.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var baseName = parts.Length > 0 ? parts[0].ToUpperInvariant() : string.Empty;
        var extension = parts.Length > 1 ? parts[1].ToUpperInvariant() : string.Empty;
        for (var i = 0; i < baseName.Length && i < 8; i++)
            destination[i] = (byte)baseName[i];
        for (var i = 0; i < extension.Length && i < 3; i++)
            destination[8 + i] = (byte)extension[i];
    }

    private static byte[] CreateSimpleStxWithSectorBlocks(byte[] sectorData) =>
        CreateSimpleStxWithSectorBlocks(new StxSectorSpec(0, 0, 1, sectorData));

    private static byte[] CreateSimpleStxWithSectorBlocks(params StxSectorSpec[] sectors)
    {
        const int stxHeaderSize = 16;
        const int trackHeaderSize = 16;
        const int sectorHeaderSize = 16;
        const int trackImageHeaderSize = 2;
        if (sectors == null || sectors.Length == 0)
            throw new ArgumentException("Expected at least one sector.", nameof(sectors));

        foreach (var sector in sectors)
            if (sector.Data == null || sector.Data.Length != 512)
                throw new ArgumentException("Expected 512-byte STX sector data.", nameof(sectors));

        var groupedTracks = sectors
            .GroupBy(s => (s.Track, s.Side))
            .OrderBy(g => g.Key.Track)
            .ThenBy(g => g.Key.Side)
            .ToList();
        var trackBlockSizes = groupedTracks
            .Select(g => trackHeaderSize + g.Count() * sectorHeaderSize + trackImageHeaderSize + g.Count() * 512)
            .ToArray();
        var file = new byte[stxHeaderSize + trackBlockSizes.Sum()];

        file[0] = (byte)'R';
        file[1] = (byte)'S';
        file[2] = (byte)'Y';
        file[3] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4, 2), 3); // Version.
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6, 2), 1); // Tool.
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(8, 2), 0);
        file[10] = (byte)groupedTracks.Count;
        file[11] = 2; // Revision.
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12, 4), 0);

        var fileOffset = stxHeaderSize;
        for (var trackIndex = 0; trackIndex < groupedTracks.Count; trackIndex++)
        {
            var trackGroup = groupedTracks[trackIndex].OrderBy(s => s.SectorId).ToList();
            var trackBlockSize = trackBlockSizes[trackIndex];
            var trackOffset = fileOffset;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(trackOffset, 4), (uint)trackBlockSize);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(trackOffset + 4, 4), 0); // Fuzzy size.
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(trackOffset + 8, 2), (ushort)trackGroup.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(trackOffset + 10, 2), 0x41); // Sector blocks + track image.
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(trackOffset + 12, 2), 0);
            file[trackOffset + 14] = (byte)(trackGroup[0].Track | (trackGroup[0].Side << 7));
            file[trackOffset + 15] = 0;

            var sectorHeaderOffset = trackOffset + trackHeaderSize;
            var trackDataOffset = sectorHeaderOffset + trackGroup.Count * sectorHeaderSize;
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(trackDataOffset, 2), 0); // Track image size.

            for (var sectorIndex = 0; sectorIndex < trackGroup.Count; sectorIndex++)
            {
                var sector = trackGroup[sectorIndex];
                var currentSectorHeaderOffset = sectorHeaderOffset + sectorIndex * sectorHeaderSize;
                BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(currentSectorHeaderOffset, 4), (uint)(2 + sectorIndex * 512));
                BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(currentSectorHeaderOffset + 4, 2), 0);
                BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(currentSectorHeaderOffset + 6, 2), 0);
                file[currentSectorHeaderOffset + 8] = sector.Track;
                file[currentSectorHeaderOffset + 9] = sector.Side;
                file[currentSectorHeaderOffset + 10] = sector.SectorId;
                file[currentSectorHeaderOffset + 11] = 2; // 512-byte sector.
                file[currentSectorHeaderOffset + 12] = 0;
                file[currentSectorHeaderOffset + 13] = 0;
                file[currentSectorHeaderOffset + 14] = 0;
                file[currentSectorHeaderOffset + 15] = 0;

                sector.Data.CopyTo(file.AsSpan(trackDataOffset + trackImageHeaderSize + sectorIndex * 512, 512));
            }

            fileOffset += trackBlockSize;
        }

        return file;
    }
    
    private readonly record struct StxSectorSpec(byte Track, byte Side, byte SectorId, byte[] Data);

    private static byte[] CreateBootSector(int totalSectors, int sectorsPerTrack, int sideCount, int reservedSectors, int fatCount, int sectorsPerFat, int rootEntryCount)
    {
        var sector = new byte[512];
        BinaryPrimitives.WriteUInt16LittleEndian(sector.AsSpan(11, 2), 512);
        sector[13] = 1; // sectors/cluster
        BinaryPrimitives.WriteUInt16LittleEndian(sector.AsSpan(14, 2), (ushort)reservedSectors);
        sector[16] = (byte)fatCount;
        BinaryPrimitives.WriteUInt16LittleEndian(sector.AsSpan(17, 2), (ushort)rootEntryCount);
        BinaryPrimitives.WriteUInt16LittleEndian(sector.AsSpan(19, 2), (ushort)totalSectors);
        sector[21] = 0xF9;
        BinaryPrimitives.WriteUInt16LittleEndian(sector.AsSpan(22, 2), (ushort)sectorsPerFat);
        BinaryPrimitives.WriteUInt16LittleEndian(sector.AsSpan(24, 2), (ushort)sectorsPerTrack);
        BinaryPrimitives.WriteUInt16LittleEndian(sector.AsSpan(26, 2), (ushort)sideCount);
        sector[510] = 0x55;
        sector[511] = 0xAA;
        return sector;
    }

    private static byte[] CreateRootDirectorySector(string fileName)
    {
        var sector = new byte[512];
        WriteDosName(sector.AsSpan(0, 11), fileName);
        sector[11] = 0x20; // archive
        return sector;
    }
}
