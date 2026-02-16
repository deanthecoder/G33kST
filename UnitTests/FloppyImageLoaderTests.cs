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

        var rootDirOffset = (reservedSectors + (fatCount * sectorsPerFat)) * bytesPerSector;
        for (var index = 0; index < fileNames.Length; index++)
        {
            var entryOffset = rootDirOffset + (index * 32);
            if (entryOffset + 32 > image.Length)
                break;
            WriteDosName(image.AsSpan(entryOffset, 11), fileNames[index]);
            image[entryOffset + 11] = 0x20;
        }

        return image;
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
}
