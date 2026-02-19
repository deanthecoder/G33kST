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
using DTC.AtariST;
using DTC.Core.Extensions;

namespace UnitTests;

[TestFixture]
public sealed class RomImageLoaderTests
{
    [Test]
    public void ReadRomDataShouldReadImgFile()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var sourceFile = tempDir.GetFile("tos100.img");
            var expectedData = new byte[] { 0x12, 0x34, 0x56, 0x78 };
            sourceFile.WriteAllBytes(expectedData);

            var (romName, romData) = RomImageLoader.ReadRomData(sourceFile);

            Assert.Multiple(() =>
            {
                Assert.That(romName, Is.EqualTo(sourceFile.Name));
                Assert.That(romData, Is.EqualTo(expectedData));
            });
        }
        finally
        {
            _ = tempDir.TryDelete();
        }
    }

    [Test]
    public void ReadRomDataShouldReadFirstSupportedZipEntry()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var zipFile = tempDir.GetFile("roms.zip");
            var expectedData = new byte[] { 0xAA, 0xBB, 0xCC };
            using (var archive = ZipFile.Open(zipFile.FullName, ZipArchiveMode.Create))
            {
                archive.CreateEntry("README.txt");
                var entry = archive.CreateEntry("TOS/TOS100.rom");
                using var stream = entry.Open();
                stream.Write(expectedData);
            }

            var (romName, romData) = RomImageLoader.ReadRomData(zipFile);

            Assert.Multiple(() =>
            {
                Assert.That(romName, Is.EqualTo("TOS100.rom"));
                Assert.That(romData, Is.EqualTo(expectedData));
            });
        }
        finally
        {
            _ = tempDir.TryDelete();
        }
    }

    [Test]
    public void ReadRomDataShouldReturnNullWhenZipHasNoSupportedEntry()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var zipFile = tempDir.GetFile("roms.zip");
            using (var archive = ZipFile.Open(zipFile.FullName, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("README.txt");
                using var stream = entry.Open();
                stream.Write([1, 2, 3]);
            }

            var (_, romData) = RomImageLoader.ReadRomData(zipFile);

            Assert.That(romData, Is.Null);
        }
        finally
        {
            _ = tempDir.TryDelete();
        }
    }

    private static DirectoryInfo CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"G33kST_RomTests_{Guid.NewGuid():N}").ToDir();
        if (!dir.Exists())
            dir.Create();
        return dir;
    }
}
