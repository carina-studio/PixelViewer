using System;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageEncoders
{
    /// <summary>
    /// <see cref="IImageEncoder"/> to encode image in <see cref="FileFormats.RawBgra"/>.
    /// </summary>
    class RawBgraImageEncoder : BaseImageEncoder
    {
        /// <summary>
        /// Initialize new <see cref="RawBgraImageEncoder"/> instance.
        /// </summary>
        public RawBgraImageEncoder() : base("RawBgra", FileFormats.RawBgra)
        { }


        // Encode.
        protected override void OnEncode(IBitmapBuffer bitmapBuffer, Stream stream, ImageEncodingOptions options, CancellationToken cancellationToken)
        {
            using var bitmapBufferToSave = options.Orientation == 0
                ? bitmapBuffer.Share()
                : bitmapBuffer.Rotate(options.Orientation);
            stream.Write(bitmapBufferToSave.Memory.Span);
        }
    }
}
