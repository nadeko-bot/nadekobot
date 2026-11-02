using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Tiff;

namespace NadekoBot.Services;

// ImageSharp 3.1 has memory bugs in its TIFF decoder, and 3.2, which fixes them, needs a license key to build.
public static class ImageFormatPolicy
{
    [ModuleInitializer]
    public static void Apply()
        => SixLabors.ImageSharp.Configuration.Default.ImageFormatsManager
            .SetDecoder(TiffFormat.Instance, RefusingDecoder.Instance);

    private sealed class RefusingDecoder : ImageDecoder
    {
        public static readonly RefusingDecoder Instance = new();

        protected override SixLabors.ImageSharp.Image<TPixel> Decode<TPixel>(
            DecoderOptions options,
            Stream stream,
            CancellationToken cancellationToken)
            => throw Refuse();

        protected override SixLabors.ImageSharp.Image Decode(
            DecoderOptions options,
            Stream stream,
            CancellationToken cancellationToken)
            => throw Refuse();

        protected override SixLabors.ImageSharp.ImageInfo Identify(
            DecoderOptions options,
            Stream stream,
            CancellationToken cancellationToken)
            => throw Refuse();

        private static SixLabors.ImageSharp.UnknownImageFormatException Refuse()
            => new("The bot does not read TIFF images.");
    }
}
