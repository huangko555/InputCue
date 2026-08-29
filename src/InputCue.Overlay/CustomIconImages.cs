using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InputCue.Core.InputContext;
using InputCue.Core.Indicator;

namespace InputCue.Overlay;

/// <summary>
/// Decoded custom icon images for the four input states. Slots without a valid
/// catalog entry stay null and the caller falls back to the built-in glyphs.
/// All images are frozen so renders never touch the file system or decoders.
/// </summary>
public sealed class CustomIconImages
{
    private const int MaxDecodedPixelWidth = 256;

    private readonly ImageSource? _chinese;
    private readonly ImageSource? _english;
    private readonly ImageSource? _englishUs;
    private readonly ImageSource? _capsLock;

    private CustomIconImages(
        ImageSource? chinese,
        ImageSource? english,
        ImageSource? englishUs,
        ImageSource? capsLock)
    {
        _chinese = chinese;
        _english = english;
        _englishUs = englishUs;
        _capsLock = capsLock;
    }

    public static CustomIconImages Empty { get; } = new(null, null, null, null);

    public ImageSource? For(InputState state) => state switch
    {
        InputState.Chinese => _chinese,
        InputState.English => _english,
        InputState.EnglishUs => _englishUs,
        InputState.CapsLock => _capsLock,
        _ => null,
    };

    public static CustomIconImages From(CustomIconCatalogResult catalog) => new(
        Load(catalog.Chinese),
        Load(catalog.English),
        Load(catalog.EnglishUs),
        Load(catalog.CapsLock));

    private static BitmapImage? Load(CustomIconSlotResult slot)
    {
        if (!slot.IsValid || slot.FilePath is null)
        {
            return null;
        }

        try
        {
            // Decode from raw bytes: decoding by URI hits WPF's process-wide
            // bitmap cache, which keeps serving the previously decoded frame
            // after the file has been replaced in place.
            using var stream = new MemoryStream(File.ReadAllBytes(slot.FilePath));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            if (slot.PixelWidth > MaxDecodedPixelWidth)
            {
                image.DecodePixelWidth = MaxDecodedPixelWidth;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                FileFormatException)
        {
            return null;
        }
    }
}
