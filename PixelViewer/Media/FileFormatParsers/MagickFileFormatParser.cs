using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using ImageMagick;
using ImageMagick.Factories;
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
    /// Called to parse extra information.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <param name="profile">Profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of parsing.</returns>
    protected virtual Task OnParseExtraInformationAsync(Stream stream, ImageRenderingProfile profile, CancellationToken cancellationToken) =>
        Task.CompletedTask;


    /// <summary>
    /// Called to seek stream to position of embedded ICC profile.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if seeking successfully.</returns>
    protected virtual bool OnSeekToIccProfile(Stream stream) => false;


    /// <inheritdoc/>
    protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(IImageDataSource source, Stream stream, CancellationToken cancellationToken)
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
                if (source is FileImageDataSource fileImageDataSource)
                    return new MagickImageInfoFactory().Create(fileImageDataSource.FileName);
                return null;
            }
            finally
            {
                stream.Position = position;
            }
        }, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        if (imageInfo == null || !this.magickFormats.Contains(imageInfo.Format))
            return null;

        // load ICC profile
        var colorSpace = await Task.Run(async () =>
        {
            try
            {
                if (!this.OnSeekToIccProfile(stream))
                    return null;
                return await ColorSpace.LoadFromIccProfileAsync(stream, ColorSpaceSource.Embedded, cancellationToken);
            }
            catch
            {
                return null;
            }
            finally
            {
                stream.Position = position;
            }
        }, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();

        // create profile
        var profile = new ImageRenderingProfile(this.FileFormat, this.imageRenderer);
        if (colorSpace != null)
            profile.ColorSpace = colorSpace;
        profile.Width = (int)imageInfo.Width;
        profile.Height = (int)imageInfo.Height;

        // parse extra info
        await this.OnParseExtraInformationAsync(stream, profile, cancellationToken);

        // complete
        return profile;
    }
}
