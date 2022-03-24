using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers
{
    /// <summary>
    /// Implementation of <see cref="IFileFormatParser"/> based-on Skia.
    /// </summary>
    abstract class SkiaFileFormatParser : BaseFileFormatParser
    {
        // Fields.
        readonly SKEncodedImageFormat encodedFormat;
        readonly IImageRenderer imageRenderer;


        /// <summary>
        /// Initialize new <see cref="SkiaFileFormatParser"/> instance.
        /// </summary>
        /// <param name="format">File format.</param>
        /// <param name="encodedFormat">Format defined by Skia.</param>
        /// <param name="renderer">Image renderer.</param>
        protected SkiaFileFormatParser(FileFormat format, SKEncodedImageFormat encodedFormat, IImageRenderer renderer) : base(format)
        {
            this.encodedFormat = encodedFormat;
            this.imageRenderer = renderer;
        }


        /// <summary>
        /// Called to check file header.
        /// </summary>
        /// <param name="stream">Stream to read image data.</param>
        /// <returns>True if header of file is correct.</returns>
        protected abstract bool OnCheckFileHeader(Stream stream);


        /// <inheritdoc/>
        protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(Stream stream, CancellationToken cancellationToken)
        {
            // decode image info
            using var codec = await Task.Run(() =>
            {
                // check file header first to prevent decoding image
                var position = stream.Position;
                if (!this.OnCheckFileHeader(stream))
                    return null;
                stream.Position = position;

                // [Workaround] Read to memory first to prevent unrecoverable crash on some images
                // Please refer to https://github.com/mono/SkiaSharp/issues/1551
                using var data = SKData.Create(stream);
                return SKCodec.Create(data);
            });
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            if (codec == null || codec.EncodedFormat != this.encodedFormat)
                return null;
            
            // create profile
            var profile = new ImageRenderingProfile(this.FileFormat, this.imageRenderer);
            profile.ColorSpace = codec.Info.ColorSpace.Let(it =>
            {
                if (it.IsSrgb)
                    return ColorSpace.Srgb;
                var colorSpace = ColorSpace.CreateFromSkiaColorSpace("Embedded ICC profile", it);
                if (ColorSpace.TryGetColorSpace(colorSpace, out var existingColorSpace))
                    return existingColorSpace;
                return colorSpace;
            });
            profile.Width = codec.Info.Width;
            profile.Height = codec.Info.Height;
            return profile;
        }
    }


    /// <summary>
    /// <see cref="IFileFormatParser"/> to parse JPEG file.
    /// </summary>
    class JpegFileFormatParser : SkiaFileFormatParser
    {
        /// <summary>
        /// Initialize new <see cref="JpegFileFormatParser"/> instance.
        /// </summary>
        public JpegFileFormatParser() : base(FileFormats.Jpeg, SKEncodedImageFormat.Jpeg, ImageRenderers.ImageRenderers.All.First(it => it is JpegImageRenderer))
        { }


        /// <summary>
        /// Check whether header of file represents JPEG/JFIF or not.
        /// </summary>
        /// <param name="stream">Stream to read image data.</param>
        /// <returns>True if header represents JPEG/JFIF.</returns>
        public static bool CheckFileHeader(Stream stream)
        {
            var buffer = new byte[3];
            return stream.Read(buffer, 0, 3) == 3
                && buffer[0] == 0xff
                && buffer[1] == 0xd8
                && buffer[2] == 0xff;
        }


        // Check file header.
        protected override bool OnCheckFileHeader(Stream stream) =>
            CheckFileHeader(stream);
    }
}