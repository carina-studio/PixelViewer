using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// <see cref="IImageRenderer"/> for JPEG format.
    /// </summary>
    class JpegImageRenderer : SkiaCompressedFormatImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="JpegImageRenderer"/> instance.
        /// </summary>
        public JpegImageRenderer() : base(new ImageFormat(ImageFormatCategory.Compressed, "JPEG", new ImagePlaneDescriptor(0), new string[] { "JPEG" }), SkiaSharp.SKEncodedImageFormat.Jpeg)
        { }
    }
}