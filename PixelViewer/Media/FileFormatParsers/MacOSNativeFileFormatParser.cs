using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.Native;
using CarinaStudio;
using CarinaStudio.MacOS.CoreFoundation;
using CarinaStudio.MacOS.ImageIO;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// Implementation of <see cref="IFileFormatParser"/> based-on macOS native API.
/// </summary>
abstract class MacOSNativeFileFormatParser : BaseFileFormatParser
{
    // Fields.
    readonly IImageRenderer imageRenderer;


    /// <summary>
    /// Initialize new <see cref="MacOSNativeFileFormatParser"/> instance.
    /// </summary>
    /// <param name="format">File format.</param>
    /// <param name="renderer">Image renderer.</param>
    protected MacOSNativeFileFormatParser(FileFormat format, IImageRenderer renderer) : base(format)
    { 
        this.imageRenderer = renderer;
    }


    /// <summary>
    /// Called to check file header.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if header of file is correct.</returns>
    protected abstract bool OnCheckFileHeader(Stream stream);


    /// <summary>
    /// Called to seek stream to position of embedded ICC profile.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if seeking successfully.</returns>
    protected virtual bool OnSeekToIccProfile(Stream stream) => false;


    /// <inheritdoc/>
    protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(Stream stream, CancellationToken cancellationToken)
    {
        // check file header first to prevent decoding image
        var position = stream.Position;
        var checkingResult = await Task.Run(() => 
        {
            try
            {
                return this.OnCheckFileHeader(stream);
            }
            finally
            {
                stream.Position = position;
            }
        });
        if (!checkingResult)
            return null;

        // check data size
        var dataSize = Math.Max(0, stream.Length - position);
        if (dataSize > 256L << 20) // 256 MB
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
        });
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        
        // parse image
        var width = 0;
        var height = 0;
        var orientation = 0;
        await Task.Run(() =>
        {
            // create image source
            using var imageSource = CGImageSource.FromStream(stream);
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            var primaryImageIndex = imageSource.PrimaryImageIndex;
            
            // get dimensions
            using var imageProperties = CFObject.FromHandle(MacOS.CGImageSourceCopyPropertiesAtIndex(imageSource.Handle, (nuint)primaryImageIndex, IntPtr.Zero), true);
            MacOS.CFDictionaryGetValue(imageProperties.Handle, MacOS.kCGImagePropertyPixelWidth, out width);
            MacOS.CFDictionaryGetValue(imageProperties.Handle, MacOS.kCGImagePropertyPixelHeight, out height);
            if (MacOS.CFDictionaryGetValue(imageProperties.Handle, MacOS.kCGImagePropertyOrientation, out int rawOrientation))
            {
                orientation = rawOrientation switch
                {
                    3 or 4 => 180,
                    5 or 8 => 270,
                    6 or 7 => 90,
                    _ => 0,
                };
            }
        });

        // create profile
        if (width <= 0 || height <= 0)
            return null;
        return new ImageRenderingProfile(this.FileFormat, this.imageRenderer).Also(it =>
        {
            if (colorSpace != null)
                it.ColorSpace = colorSpace;
            it.Height = height;
            it.Orientation = orientation;
            it.Width = width;
        });
    }
}