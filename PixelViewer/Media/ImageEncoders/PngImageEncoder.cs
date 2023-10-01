using SkiaSharp;
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
            // use GDI+ to encode
            if (options.ColorSpace is null)
            {
                using var gdiBitmap = bitmapBuffer.CreateSystemDrawingBitmap(options.Orientation);
                gdiBitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return;
            }
#endif
            // use Skia to encode
            using var bitmap = bitmapBuffer.CreateSkiaBitmap(options.Orientation, options.ColorSpace);
            using var memoryStream = new MemoryStream();
            bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 0);
            stream.Write(memoryStream.ToArray());
        }
    }
}
