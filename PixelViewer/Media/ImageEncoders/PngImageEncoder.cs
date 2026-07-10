using CarinaStudio;
using SkiaSharp;
using System;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageEncoders;

/// <summary>
/// <see cref="IImageEncoder"/> to encode image in <see cref="FileFormats.Png"/>.
/// </summary>
class PngImageEncoder : BaseImageEncoder
{
    // Constants.
    const int ZLibCompressionLevel = 3;


    /// <summary>
    /// Initialize new <see cref="PngImageEncoder"/> instance.
    /// </summary>
    public PngImageEncoder() : base("Png", FileFormats.Png)
    { }


    // Encode.
    protected override void OnEncode(IBitmapBuffer bitmapBuffer, Stream stream, ImageEncodingOptions options, CancellationToken cancellationToken)
    {
        // encode with a single filter and a lower zlib level to keep encoding fast at the cost of a slightly larger file
        using var bitmap = bitmapBuffer.CreateSkiaBitmap(options.Orientation, options.ColorSpace);
        using var pixmap = bitmap.PeekPixels().AsNonNull();
        using var pngStream = new SKDynamicMemoryWStream();
        if (!pixmap.Encode(pngStream, new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, ZLibCompressionLevel)))
            throw new Exception("Failed to encode image to PNG.");
        using var pngData = pngStream.DetachAsData();
        stream.Write(pngData.ToArray());
    }
}