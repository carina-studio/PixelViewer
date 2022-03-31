using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// Implementation of <see cref="IFileFormatParser"/> based-on ImageMagick.
/// </summary>
abstract class MagickFileFormatParser : BaseFileFormatParser
{
    // Fields.
    readonly IImageRenderer imageRenderer;
    readonly List<MagickFormat> magickFormats = new();


    /// <summary>
    /// Initialize new <see cref="MagickFileFormatParser"/> instance.
    /// </summary>
    /// <param name="format">File format.</param>
    /// <param name="magickFormats">Formats defined by ImageMagick.</param>
    /// <param name="renderer">Image renderer.</param>
    protected MagickFileFormatParser(FileFormat format, IEnumerable<MagickFormat> magickFormats, IImageRenderer renderer) : base(format)
    {
        this.imageRenderer = renderer;
        this.magickFormats.AddRange(magickFormats);
    }


    /// <summary>
    /// Called to check file header.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if header of file is correct.</returns>
    protected abstract bool OnCheckFileHeader(Stream stream);


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
        var imageInfo = await Task.Run(() =>
        {
            // check file header first to prevent decoding image
            if (!this.OnCheckFileHeader(stream))
                return null;
            stream.Position = position;

            // decode image info
            try
            {
                return new MagickImageInfoFactory().Create(stream);
            }
            catch
            {
                return null;
            }
        });
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        if (imageInfo == null || !this.magickFormats.Contains(imageInfo.Format))
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
            var iccProfileData = (byte[]?)null;
            try
            {
                iccProfileData = await Task.Run(() => this.OnReadIccProfileToMemory(stream));
            }
            catch
            { }
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();

            // load ICC profile
            try
            {
                if (iccProfileData == null)
                    colorSpaceFromIccProfile = await ColorSpace.LoadFromIccProfileAsync(stream, ColorSpaceSource.Embedded, cancellationToken);
                else
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
        if (colorSpaceFromIccProfile != null)
            profile.ColorSpace = colorSpaceFromIccProfile;
        profile.Width = imageInfo.Width;
        profile.Height = imageInfo.Height;
        return profile;
    }
}
