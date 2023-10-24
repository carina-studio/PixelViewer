using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// Implementation of <see cref="IFileFormatParser"/> for TIFF-based format.
/// </summary>
abstract class TiffBasedFileFormatParser : BaseFileFormatParser
{
    // Fields.
    readonly IImageRenderer imageRenderer;


    /// <summary>
    /// Initialize new <see cref="TiffBasedFileFormatParser"/> instance.
    /// </summary>
    /// <param name="format">File format.</param>
    /// <param name="renderer">Image renderer.</param>
    protected TiffBasedFileFormatParser(FileFormat format, IImageRenderer renderer) : base(format)
    {
        this.imageRenderer = renderer;
    }
    
    
    /// <summary>
    /// Check whether header of file represents TIFF or not.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if header represents TIFF.</returns>
    public static unsafe bool CheckFileHeader(Stream stream)
    {
        var buffer = stackalloc byte[4];
        if (stream.Read(new Span<byte>(buffer, 4)) < 4)
            return false;
        if (buffer[0] == 'I' && buffer[1] == 'I')
            return buffer[2] == 0x2a && buffer[3] == 0;
        if (buffer[0] == 'M' && buffer[1] == 'M')
            return buffer[2] == 0 && buffer[3] == 0x2a;
        return false;
    }


    /// <inheritdoc/>
    protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(Stream stream, CancellationToken cancellationToken)
    {
        // get image info
        var streamPosition = stream.Position;
        var imageWidth = 0;
        var imageHeight = 0;
        var thumbWidth = 0;
        var thumbHeight = 0;
        var colorSpace = default(ColorSpace);
        var orientation = -1;
        var thumbOrientation = -1;
        await Task.Run(() =>
        {
            // create entry reader
            IfdEntryReader entryReader;
            try
            {
                entryReader = new IfdEntryReader(stream);
            }
            catch
            {
                return;
            }

            // read entries
            var isFullSizeImage = false;
            ushort[]? ushortData;
            uint[]? uintData;
            while (entryReader.Read())
            {
                switch (entryReader.CurrentIfdName)
                {
                    case IfdNames.Default:
                    case "Raw":
                    {
                        switch (entryReader.CurrentEntryId)
                        {
                            case 0x00fe: // NewSubfileType
                                if (entryReader.TryGetEntryData(out uintData) && uintData != null)
                                    isFullSizeImage = (uintData[0] == 0);
                                break;
                            case 0x0100: // ImageWidth
                                if (entryReader.TryGetEntryData(out uintData) && uintData != null)
                                {
                                    if (isFullSizeImage)
                                        imageWidth = (int)uintData[0];
                                    else if (thumbWidth <= 0)
                                        thumbWidth = (int)uintData[0];
                                }
                                else if (entryReader.TryGetEntryData(out ushortData) && ushortData != null)
                                {
                                    if (isFullSizeImage)
                                        imageWidth = ushortData[0];
                                    else if (thumbWidth <= 0)
                                        thumbWidth = ushortData[0];
                                }
                                break;
                            case 0x0101: // ImageLength
                                if (entryReader.TryGetEntryData(out uintData) && uintData != null)
                                {
                                    if (isFullSizeImage)
                                        imageHeight = (int)uintData[0];
                                    else if (thumbHeight <= 0)
                                        thumbHeight = (int)uintData[0];
                                }
                                else if (entryReader.TryGetEntryData(out ushortData) && ushortData != null)
                                {
                                    if (isFullSizeImage)
                                        imageHeight = ushortData[0];
                                    else if (thumbHeight <= 0)
                                        thumbHeight = ushortData[0];
                                }
                                break;
                            case 0x0112: // Orientation
                                if (entryReader.TryGetEntryData(out ushortData) && ushortData != null)
                                {
                                    if (isFullSizeImage)
                                        orientation = ushortData[0];
                                    else if (thumbOrientation < 0)
                                        thumbOrientation = ushortData[0];
                                }
                                break;
                            case 0x014a: // SubIFDs
                                if (!isFullSizeImage && entryReader.TryGetEntryData(out uintData) && uintData != null)
                                {
                                    foreach (var offset in uintData)
                                        entryReader.EnqueueIfdToRead(entryReader.InitialStreamPosition + offset, "Raw");
                                }
                                break;
                            case 0x8773: // IccProfile
                                break;
                            case 0xa005: // InteropOffset
                                break;
                            case 0xc68f: // AsShotICCProfile
                                break;
                            case 0xc691: // CurrentICCProfile
                                break;
                        }
                        break;
                    }

                    case IfdNames.Exif:
                    {
                        switch (entryReader.CurrentEntryId)
                        {
                            case 0xa001: // ColorSpace
                                if (entryReader.TryGetEntryData(out ushortData) && ushortData != null && isFullSizeImage)
                                {
                                    switch (ushortData[0])
                                    {
                                        case 0x1:
                                            colorSpace = ColorSpace.Srgb;
                                            break;
                                        case 0x2:
                                            colorSpace = ColorSpace.AdobeRGB_1998;
                                            break;
                                    }
                                }
                                break;
                        }
                        break;
                    }
                }
            }
        }, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        
        // check image info
        if (imageWidth <= 0)
            imageWidth = thumbWidth;
        if (imageHeight <= 0)
            imageHeight = thumbHeight;
        if (imageWidth <= 0 || imageHeight <= 0)
            return null;

        // create profile
        return new ImageRenderingProfile(FileFormats.Nef, imageRenderer).Also(profile =>
        {
            if (colorSpace != null)
                profile.ColorSpace = colorSpace;
            profile.Height = imageHeight;
            profile.Orientation = Tiff.FromTiffOrientation(orientation >= 0 
                ? orientation 
                : (thumbOrientation >= 0 ? thumbOrientation : 0));
            profile.Width = imageWidth;
        });
    }
}