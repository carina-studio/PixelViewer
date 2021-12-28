using SkiaSharp;
using System;
#if WINDOWS
using System.Drawing.Imaging;
#endif
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageEncoders
{
    /// <summary>
    /// <see cref="IImageEncoder"/> to encode image in <see cref="FileFormats.Png"/>.
    /// </summary>
    class PngImageEncoder : BaseImageEncoder
    {
        /// <summary>
        /// Initialize new <see cref="PngImageEncoder"/> instance.
        /// </summary>
        public PngImageEncoder() : base("Png", FileFormats.Png)
        { }


        // Encode.
        protected override void OnEncode(IBitmapBuffer bitmapBuffer, Stream stream, ImageEncodingOptions options, CancellationToken cancellationToken)
        {
#if WINDOWS
            using var bitmap = bitmapBuffer.CreateSystemDrawingBitmap(options.Orientation);
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
#else
            using var bitmap = bitmapBuffer.CreateSkiaBitmap(options.Orientation);
            using var memoryStream = new MemoryStream();
            bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 0);
            stream.Write(memoryStream.ToArray());
#endif
        }
    }
}
