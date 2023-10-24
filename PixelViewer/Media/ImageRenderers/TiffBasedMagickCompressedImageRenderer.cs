using ImageMagick;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="MagickCompressedImageRenderer"/> which handles TIFF-based format.
/// </summary>
abstract class TiffBasedMagickCompressedImageRenderer : MagickCompressedImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="TiffBasedMagickCompressedImageRenderer"/> instance.
    /// </summary>
    /// <param name="format">Format supported by this instance.</param>
    /// <param name="magickFormat">Format defined by <see cref="MagickFormat"/>.</param>      
    protected TiffBasedMagickCompressedImageRenderer(ImageFormat format, MagickFormat magickFormat) : base(format, new[] { magickFormat, MagickFormat.Tiff })
    { }
    
    
    /// <summary>
    /// Initialize new <see cref="TiffBasedMagickCompressedImageRenderer"/> instance.
    /// </summary>
    /// <param name="format">Format supported by this instance.</param>
    /// <param name="magickFormats">Formats defined by <see cref="MagickFormat"/>.</param>      
    protected TiffBasedMagickCompressedImageRenderer(ImageFormat format, IEnumerable<MagickFormat> magickFormats) : base(format, magickFormats)
    { }


    /// <inheritdoc/>
    protected override bool OnCheckBufferDimensions(IImageDataSource source, Stream imageStream, int bufferWidth, int bufferHeight, out int expectedWidth, out int expectedHeight, CancellationToken cancellationToken)
    {
        // create entry reader
        expectedWidth = -1;
        expectedHeight = -1;
        var entryReader = new IfdEntryReader(imageStream);
        
        // read entries
        var isFullSizeImage = false;
        var thumbWidth = -1;
        var thumbHeight = -1;
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
                                    expectedWidth = (int)uintData[0];
                                else if (thumbWidth <= 0)
                                    thumbWidth = (int)uintData[0];
                            }
                            else if (entryReader.TryGetEntryData(out ushortData) && ushortData != null)
                            {
                                if (isFullSizeImage)
                                    expectedWidth = ushortData[0];
                                else if (thumbWidth <= 0)
                                    thumbWidth = ushortData[0];
                            }
                            break;
                        case 0x0101: // ImageLength
                            if (entryReader.TryGetEntryData(out uintData) && uintData != null)
                            {
                                if (isFullSizeImage)
                                    expectedHeight = (int)uintData[0];
                                else if (thumbHeight <= 0)
                                    thumbHeight = (int)uintData[0];
                            }
                            else if (entryReader.TryGetEntryData(out ushortData) && ushortData != null)
                            {
                                if (isFullSizeImage)
                                    expectedHeight = ushortData[0];
                                else if (thumbHeight <= 0)
                                    thumbHeight = ushortData[0];
                            }
                            break;
                        case 0x014a: // SubIFDs
                            if (!isFullSizeImage && entryReader.TryGetEntryData(out uintData) && uintData != null)
                            {
                                foreach (var offset in uintData)
                                    entryReader.EnqueueIfdToRead(entryReader.InitialStreamPosition + offset, "Raw");
                            }
                            break;
                        case 0xc61f: // DefaultCropOrigin
                            break;
                        case 0xc620: // DefaultCropSize
                            break;
                    }
                    break;
                }
            }
        }
        
        // use dimensions of thumbnail image
        if (expectedWidth < 0)
            expectedWidth = thumbWidth;
        if (expectedHeight < 0)
            expectedHeight = thumbHeight;
        
        // check dimensions
        return bufferWidth == expectedWidth && bufferHeight == expectedHeight;
    }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(IImageDataSource source, Stream imageStream) =>
        Tiff.CheckFileHeader(imageStream, out _);
}