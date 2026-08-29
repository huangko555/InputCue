using System.Buffers.Binary;
using InputCue.Core.InputContext;
using InputCue.Core.Indicator;

namespace InputCue.Core.Tests.Indicator;

public sealed class CustomIconCatalogTests
{
    [Fact]
    public void MissingDirectoryScansAsEmpty()
    {
        var result = CustomIconCatalog.Scan(Path.Combine(Path.GetTempPath(), "inputcue-missing-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(CustomIconCatalogResult.Empty, result);
    }

    [Fact]
    public void NullDirectoryScansAsEmpty()
    {
        Assert.Equal(CustomIconCatalogResult.Empty, CustomIconCatalog.Scan(null));
    }

    [Fact]
    public void EmptyDirectoryReportsMissingSlots()
    {
        using var directory = new TemporaryDirectory();

        var result = CustomIconCatalog.Scan(directory.Path);

        Assert.Equal(CustomIconSlotStatus.Missing, result.Chinese.Status);
        Assert.Equal(CustomIconSlotStatus.Missing, result.English.Status);
        Assert.Equal(CustomIconSlotStatus.Missing, result.EnglishUs.Status);
        Assert.Equal(CustomIconSlotStatus.Missing, result.CapsLock.Status);
    }

    [Fact]
    public void CanonicalPngFileIsReportedValidWithDimensions()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(
            Path.Combine(directory.Path, CustomIconCatalog.ChineseFileName),
            PngHeaderBytes(256, 128));

        var result = CustomIconCatalog.Scan(directory.Path);

        Assert.True(result.Chinese.IsValid);
        Assert.Equal(256, result.Chinese.PixelWidth);
        Assert.Equal(128, result.Chinese.PixelHeight);
        Assert.True(result.English.Status is CustomIconSlotStatus.Missing);
    }

    [Fact]
    public void ResultSlotsResolveThroughForByInputState()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(
            Path.Combine(directory.Path, CustomIconCatalog.CapsLockFileName),
            PngHeaderBytes(64, 64));

        var result = CustomIconCatalog.Scan(directory.Path);

        Assert.True(result.For(InputState.CapsLock).IsValid);
        Assert.True(result.For(InputState.Chinese).Status is CustomIconSlotStatus.Missing);
        Assert.True(result.For(InputState.Unknown).Status is CustomIconSlotStatus.Missing);
    }

    [Fact]
    public void UpperCaseExtensionIsStillFoundOnWindows()
    {
        using var directory = new TemporaryDirectory();
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        File.WriteAllBytes(
            Path.Combine(directory.Path, "ime-english.PNG"),
            PngHeaderBytes(64, 64));

        var result = CustomIconCatalog.Scan(directory.Path);

        Assert.True(result.English.IsValid);
    }

    [Fact]
    public void UnparsableFileIsReportedInvalid()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(
            Path.Combine(directory.Path, CustomIconCatalog.CapsLockFileName),
            [0x00, 0x01, 0x02, 0x03]);

        var result = CustomIconCatalog.Scan(directory.Path);

        Assert.Equal(CustomIconSlotStatus.Invalid, result.CapsLock.Status);
        Assert.Equal(CustomIconInvalidReason.Unreadable, result.CapsLock.InvalidReason);
    }

    [Fact]
    public void OversizedDimensionsAreRejected()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(
            Path.Combine(directory.Path, CustomIconCatalog.EnglishFileName),
            PngHeaderBytes(CustomIconCatalog.MaxPixelDimension + 1, 64));

        var result = CustomIconCatalog.Scan(directory.Path);

        Assert.Equal(CustomIconSlotStatus.Invalid, result.English.Status);
        Assert.Equal(CustomIconInvalidReason.DimensionsTooLarge, result.English.InvalidReason);
    }

    [Fact]
    public void OversizedFileIsRejectedBeforeHeaderParsing()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(
            Path.Combine(directory.Path, CustomIconCatalog.ChineseFileName),
            new byte[CustomIconCatalog.MaxFileBytes + 1]);

        var result = CustomIconCatalog.Scan(directory.Path);

        Assert.Equal(CustomIconSlotStatus.Invalid, result.Chinese.Status);
        Assert.Equal(CustomIconInvalidReason.FileTooLarge, result.Chinese.InvalidReason);
    }

    [Fact]
    public void JpegDataWithPngExtensionGetsDedicatedReason()
    {
        using var directory = new TemporaryDirectory();
        var jpeg = new byte[24];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[2] = 0xFF;
        jpeg[3] = 0xE0;
        File.WriteAllBytes(
            Path.Combine(directory.Path, CustomIconCatalog.ChineseFileName),
            jpeg);

        var result = CustomIconCatalog.Scan(directory.Path);

        Assert.Equal(CustomIconSlotStatus.Invalid, result.Chinese.Status);
        Assert.Equal(CustomIconInvalidReason.JpegNotPng, result.Chinese.InvalidReason);
    }

    [Fact]
    public void ValidateRejectsNonPngExtension()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "renamed.txt");
        File.WriteAllBytes(path, PngHeaderBytes(64, 64));

        var slot = CustomIconCatalog.Validate(path);

        Assert.Equal(CustomIconSlotStatus.Invalid, slot.Status);
        Assert.Equal(CustomIconInvalidReason.Unreadable, slot.InvalidReason);
    }

    [Fact]
    public void ValidateReportsMissingCandidate()
    {
        using var directory = new TemporaryDirectory();

        Assert.Equal(
            CustomIconSlotStatus.Missing,
            CustomIconCatalog.Validate(Path.Combine(directory.Path, "absent.png")).Status);
    }

    private static byte[] PngHeaderBytes(int width, int height)
    {
        var bytes = new byte[33];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "inputcue-icon-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
