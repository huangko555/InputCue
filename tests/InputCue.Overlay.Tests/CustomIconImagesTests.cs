using InputCue.Core.InputContext;
using InputCue.Core.Indicator;

namespace InputCue.Overlay.Tests;

public sealed class CustomIconImagesTests
{
    private const string OnePixelTransparentPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [Fact]
    public void EmptyCatalogProducesNoImages()
    {
        var images = CustomIconImages.From(CustomIconCatalogResult.Empty);

        Assert.Null(images.For(InputState.Chinese));
        Assert.Null(images.For(InputState.English));
        Assert.Null(images.For(InputState.EnglishUs));
        Assert.Null(images.For(InputState.CapsLock));
        Assert.Null(images.For(InputState.Unknown));
    }

    [Fact]
    public void ValidSlotDecodesToFrozenImageAndOtherSlotsStayEmpty()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(
                Path.Combine(directory, CustomIconCatalog.ChineseFileName),
                Convert.FromBase64String(OnePixelTransparentPngBase64));

            var images = CustomIconImages.From(CustomIconCatalog.Scan(directory));

            var image = images.For(InputState.Chinese);
            Assert.NotNull(image);
            Assert.True(image.IsFrozen);
            Assert.Null(images.For(InputState.CapsLock));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnreadableSlotFallsBackToNullInsteadOfThrowing()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(
                Path.Combine(directory, CustomIconCatalog.EnglishFileName),
                [0x00, 0x01, 0x02]);

            var images = CustomIconImages.From(CustomIconCatalog.Scan(directory));

            Assert.Null(images.For(InputState.English));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "inputcue-icon-image-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
