using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using SkiaSharp;
using System.IO;
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


        /// <summary>
        /// Called to parse extra information.
        /// </summary>
        /// <param name="stream">Stream to read image data.</param>
        /// <param name="profile">Profile.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of parsing.</returns>
        protected virtual Task OnParseExtraInformationAsync(Stream stream, ImageRenderingProfile profile, CancellationToken cancellationToken) =>
            Task.CompletedTask;


        /// <summary>
        /// Called to read ICC profile into memory.
        /// </summary>
        /// <param name="stream">Stream which is positioned at start of ICC profile.</param>
        /// <returns>Read ICC profile, or Null if it is unsupported.</returns>
        protected virtual byte[]? OnReadIccProfileToMemory(Stream stream) => null;


        /// <summary>
        /// Called to seek stream to position of embedded ICC profile.
        /// </summary>
        /// <param name="stream">Stream to read image data.</param>
        /// <returns>True if seeking successfully.</returns>
        protected virtual bool OnSeekToIccProfile(Stream stream) => false;


        /// <inheritdoc/>
        protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(Stream stream, CancellationToken cancellationToken)
        {
            // decode image info
            var position = stream.Position;
            using var codec = await Task.Run(() =>
            {
                // check file header first to prevent decoding image
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
            
            // load ICC profile
            var colorSpaceFromIccProfile = (ColorSpace?)null;
            var hasIccProfile = false;
            await Task.Run(() => 
            {
                try
                {
                    stream.Position = position;
                    hasIccProfile = this.OnSeekToIccProfile(stream);
                }
                catch
                { }
            });
            if (hasIccProfile)
            {
                // read ICC profile into memory
                var iccProfileData = await Task.Run(() =>
                {
                    try
                    {
                        return this.OnReadIccProfileToMemory(stream);
                    }
                    catch 
                    {
                        return null;
                    }
                    finally
                    {
                        stream.Position = position;
                    }
                });
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                
                // load ICC profile
                try
                {
                    if (iccProfileData != null)
                    {
                        using var iccProfileStream = new MemoryStream(iccProfileData);
                        colorSpaceFromIccProfile = await ColorSpace.LoadFromIccProfileAsync(iccProfileStream, ColorSpaceSource.Embedded, cancellationToken);
                    }
                }
                catch
                { }
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
            }
            
            // create profile
            var profile = new ImageRenderingProfile(this.FileFormat, this.imageRenderer);
            profile.ColorSpace = colorSpaceFromIccProfile ?? codec.Info.ColorSpace.Let(it =>
            {
                if (it.IsSrgb)
                    return ColorSpace.Srgb;
                return ColorSpace.FromSkiaColorSpace(ColorSpaceSource.Embedded, null, it, null);
            });
            profile.Width = codec.Info.Width;
            profile.Height = codec.Info.Height;

            // parse extra info
            await this.OnParseExtraInformationAsync(stream, profile, cancellationToken);

            // complete
            return profile;
        }
    }
}