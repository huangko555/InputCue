using System.Buffers.Binary;
using InputCue.Core.InputContext;

namespace InputCue.Core.Indicator;

/// <summary>Validity of one custom icon slot after scanning the icons directory.</summary>
public enum CustomIconSlotStatus
{
    Missing,
    Valid,
    Invalid,
}

/// <summary>Stable reason for rejecting a custom icon file; the UI maps it to display text.</summary>
public enum CustomIconInvalidReason
{
    Unreadable,
    FileTooLarge,
    DimensionsTooLarge,
    JpegNotPng,
}

public sealed record CustomIconSlotResult(
    CustomIconSlotStatus Status,
    string? FilePath = null,
    int PixelWidth = 0,
    int PixelHeight = 0,
    CustomIconInvalidReason InvalidReason = CustomIconInvalidReason.Unreadable)
{
    public static CustomIconSlotResult Missing { get; } = new(CustomIconSlotStatus.Missing);

    public bool IsValid => Status == CustomIconSlotStatus.Valid;
}

public sealed record CustomIconCatalogResult(
    CustomIconSlotResult Chinese,
    CustomIconSlotResult English,
    CustomIconSlotResult EnglishUs,
    CustomIconSlotResult CapsLock)
{
    public static CustomIconCatalogResult Empty { get; } = new(
        CustomIconSlotResult.Missing,
        CustomIconSlotResult.Missing,
        CustomIconSlotResult.Missing,
        CustomIconSlotResult.Missing);

    public CustomIconSlotResult For(InputState state) => state switch
    {
        InputState.Chinese => Chinese,
        InputState.English => English,
        InputState.EnglishUs => EnglishUs,
        InputState.CapsLock => CapsLock,
        _ => CustomIconSlotResult.Missing,
    };
}

/// <summary>
/// Scans the custom icons directory. The four canonical file names are the only
/// source of truth: a slot without a readable file falls back to the built-in glyphs.
/// </summary>
public static class CustomIconCatalog
{
    public const string ChineseFileName = "chinese.png";
    public const string EnglishFileName = "ime-english.png";
    public const string EnglishUsFileName = "us-english.png";
    public const string CapsLockFileName = "caps-lock.png";
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxPixelDimension = 4096;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static CustomIconCatalogResult Scan(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return CustomIconCatalogResult.Empty;
        }

        return new CustomIconCatalogResult(
            ScanFile(Path.Combine(directory, ChineseFileName)),
            ScanFile(Path.Combine(directory, EnglishFileName)),
            ScanFile(Path.Combine(directory, EnglishUsFileName)),
            ScanFile(Path.Combine(directory, CapsLockFileName)));
    }

    /// <summary>Validates an arbitrary candidate file before it is copied into the icons directory.</summary>
    public static CustomIconSlotResult Validate(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return CustomIconSlotResult.Missing;
            }

            if (!string.Equals(
                    Path.GetExtension(filePath),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(CustomIconInvalidReason.Unreadable);
            }

            return InspectFile(filePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Invalid(CustomIconInvalidReason.Unreadable);
        }
    }

    private static CustomIconSlotResult ScanFile(string filePath)
    {
        try
        {
            return File.Exists(filePath)
                ? InspectFile(filePath)
                : CustomIconSlotResult.Missing;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Invalid(CustomIconInvalidReason.Unreadable);
        }
    }

    private static CustomIconSlotResult InspectFile(string filePath)
    {
        var file = new FileInfo(filePath);
        if (file.Length > MaxFileBytes)
        {
            return Invalid(CustomIconInvalidReason.FileTooLarge);
        }

        using var stream = file.OpenRead();
        Span<byte> header = stackalloc byte[24];
        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException)
        {
            return Invalid(CustomIconInvalidReason.Unreadable);
        }

        if (!header[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            // Downloaded images frequently carry a .png extension while holding JPEG
            // data; give that trap its own actionable reason.
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return Invalid(CustomIconInvalidReason.JpegNotPng);
            }

            return Invalid(CustomIconInvalidReason.Unreadable);
        }

        if (!header[12..16].SequenceEqual("IHDR"u8))
        {
            return Invalid(CustomIconInvalidReason.Unreadable);
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        if (width <= 0 || height <= 0)
        {
            return Invalid(CustomIconInvalidReason.Unreadable);
        }

        if (width > MaxPixelDimension || height > MaxPixelDimension)
        {
            return Invalid(CustomIconInvalidReason.DimensionsTooLarge);
        }

        return new CustomIconSlotResult(
            CustomIconSlotStatus.Valid,
            filePath,
            width,
            height);
    }

    private static CustomIconSlotResult Invalid(CustomIconInvalidReason reason) => new(
        CustomIconSlotStatus.Invalid,
        InvalidReason: reason);
}
