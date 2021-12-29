using ExifLibrary;
using SkiaSharp;
using System;
using System.IO;
using System.Text;
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
            // encode to JPEG
            using var bitmap = bitmapBuffer.CreateSkiaBitmap(options.Orientation);
            using var memoryStream = new MemoryStream();
            bitmap.Encode(memoryStream, SKEncodedImageFormat.Jpeg, Math.Max(1, Math.Min(100, options.QualityLevel)));

            // set Software tag
            memoryStream.Position = 0;
            var jpegFile = ImageFile.FromStream(memoryStream);
            memoryStream.SetLength(0);
            jpegFile.Properties.Set(ExifTag.Software, new ExifAscii(ExifTag.Software, App.Current.Name, Encoding.ASCII));
            jpegFile.Save(memoryStream);

            // output final JPEG data
            stream.Write(memoryStream.ToArray());
        }
    }
}
