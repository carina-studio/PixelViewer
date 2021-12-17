using SkiaSharp;
using System;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageEncoders
{
    /// <summary>
    /// <see cref="IImageEncoder"/> to encode image in <see cref="FileFormats.Jpeg"/>.
    /// </summary>
    class JpegImageEncoder : BaseImageEncoder
    {
        /// <summary>
        /// Initialize new <see cref="JpegImageEncoder"/> instance.
        /// </summary>
        public JpegImageEncoder() : base("Jpeg", FileFormats.Jpeg)
        { }


        // Encode.
        protected override void OnEncode(IBitmapBuffer bitmapBuffer, Stream stream, ImageEncodingOptions options, CancellationToken cancellationToken)
        {
            using var bitmap = bitmapBuffer.CreateSkiaBitmap();
            using var memoryStream = new MemoryStream();
            bitmap.Encode(memoryStream, SKEncodedImageFormat.Jpeg, Math.Max(1, Math.Min(100, options.QualityLevel)));
            stream.Write(memoryStream.ToArray());
        }
    }
}
