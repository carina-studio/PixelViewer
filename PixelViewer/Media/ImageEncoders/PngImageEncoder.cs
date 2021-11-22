using System;
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
#if WINDOWS10_0_17763_0_OR_GREATER
            using var bitmap = bitmapBuffer.CreateSystemDrawingBitmap();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
#else
            using var bitmap = bitmapBuffer.CreateAvaloniaBitmap();
            bitmap.Save(stream);
#endif
        }
    }
}
