using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// <see cref="IFileFormatParser"/> to parse DNG file.
/// </summary>
class DngFileFormatParser : BaseFileFormatParser
{
    // Constants.
    const int Uncompressed = 1;
    const int Compressed = 7;
    const int Deflate = 8;
    const int LossyJpeg = 34892;
    
    
    // Static fields.
    static readonly IList<Tuple<byte[], BayerPattern>> CfaPatternToBayerPatternMap =
    [
        new([ 2, 1, 1, 0 ], BayerPattern.BGGR_2x2),
        new([ 1, 2, 0, 1 ], BayerPattern.GBRG_2x2),
        new([ 1, 0, 2, 1 ], BayerPattern.GRBG_2x2),
        new([ 0, 1, 1, 2 ], BayerPattern.RGGB_2x2),
        new([ 2, 2, 1, 1, 2, 2, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0 ], BayerPattern.BGGR_4x4),
        new([ 1, 1, 2, 2, 1, 1, 2, 2, 0, 0, 1, 1, 0, 0, 1, 1 ], BayerPattern.GBRG_4x4),
        new([ 1, 1, 0, 0, 1, 1, 0, 0, 2, 2, 1, 1, 2, 2, 1, 1 ], BayerPattern.GRBG_4x4),
        new([ 0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 2, 2, 1, 1, 2, 2 ], BayerPattern.RGGB_4x4),
    ];


    /// <summary>
    /// Initialize new <see cref="DngFileFormatParser"/> instance.
    /// </summary>
    public DngFileFormatParser() : base(FileFormats.Dng)
    { }


    // Create the table which maps each value of source image to its color, according to the linearization table defined in the file.
    ColorTable? CreateColorTable(ushort[] linearizationTable, int pixelStride, uint whiteLevel)
    {
        // the white level defines the range which the table maps the values to. the default white level of DNG is
        // derived from the bits of source image, which says nothing about the range of a table, so the largest color
        // in the table is used instead when the file defines no white level
        var maxColor = whiteLevel > 0 ? whiteLevel : linearizationTable.Max();
        if (maxColor == 0 || pixelStride <= 0)
        {
            this.Logger.LogWarning("Unable to use linearization table, maximum color: {maxColor}, pixel stride: {pixelStride}", maxColor, pixelStride);
            return null;
        }

        // select the color depth needed to keep every color of the table
        var colorBitDepth = 1;
        var mask = 1u;
        while (mask < maxColor)
        {
            ++colorBitDepth;
            mask = ((mask << 1) | 1);
        }

        // the renderers index the table by the value read from source image rather than by its effective bits, so the
        // table needs to cover every value which the pixel stride can carry. the colors beyond the table defined by
        // the file are clamped to its last color, which is what the other DNG readers do with a shortened table
        var count = Math.Min(1 << (pixelStride << 3), ColorTable.MaxCount);
        var definedCount = Math.Min(linearizationTable.Length, count);
        var lastColor = (uint)linearizationTable[definedCount - 1];
        return new ColorTable(count, colorBitDepth).Also(it =>
        {
            var colors = it.Memory.Span;
            for (var i = definedCount - 1; i >= 0; --i)
                colors[i] = linearizationTable[i];
            for (var i = count - 1; i >= definedCount; --i)
                colors[i] = lastColor;
        });
    }


    /// <inheritdoc/>
    protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(IImageDataSource source, Stream stream, CancellationToken cancellationToken)
    {
        // get image info
        var byteOrdering = ByteOrdering.LittleEndian;
        var hasCompressedThumb = false;
        var compressedThumbWidth = 0;
        var compressedThumbHeight = 0;
        var compressedThumbOffset = 0u;
        var compressedThumbDataSize = 0u;
        var compressedThumbOrientation = -1;
        var compression = 0u;
        var imageWidth = 0;
        var imageHeight = 0;
        var orientation = 0;
        var analogBalance = (double[]?)null;
        var asShotNeutral = (double[]?)null;
        var calibrationIlluminant1 = 0;
        var calibrationIlluminant2 = 0;
        var cameraCalibration1 = (double[]?)null;
        var cameraCalibration2 = (double[]?)null;
        var colorMatrix1 = (double[]?)null;
        var colorMatrix2 = (double[]?)null;
        var forwardMatrix1 = (double[]?)null;
        var forwardMatrix2 = (double[]?)null;
        var uniqueCameraModel = (string?)null;
        var blackLevel = 0u;
        var whiteLevel = 0u;
        var linearizationTable = (ushort[]?)null;
        var effectiveBits = 0;
        var pixelStride = 0;
        var rowStride = 0;
        var photometricInterpolation = (ushort)0;
        var activeArea = (int[]?)null; // in LTRB
        var cfaLayout = 0;
        var cfaPattern = (byte[]?)null;
        var imageDataOffset = 0L;
        var colorSpace = (Media.ColorSpace?)null;
        var ifdMetadata = new TiffMediaMetadata();
        await Task.Run(async () =>
        {
            // create reader
            IfdEntryReader? entryReader;
            try
            {
                entryReader = new IfdEntryReader(stream);
            }
            catch
            {
                return;
            }
            byteOrdering = (entryReader.IsLittleEndian ? ByteOrdering.LittleEndian : ByteOrdering.BigEndian);

            // get image info
            var isFullSizeImage = false;
            var rowsPerStrip = (uint[]?)null;
            var stripOffsets = (uint[]?)null;
            var stripByteCounts = (uint[]?)null;
            double[]? doubleData;
            ushort[]? ushortData;
            uint[]? uintData;
            var thumbWidth = 0;
            var thumbHeight = 0;
            var isCompressedThumb = false;
            var thumbOrientation = 0;
            var thumbStripOffsets = (uint[]?)null;
            uint[]? thumbStripByteCounts;
            var compressedThumbFormat = default(SKEncodedImageFormat?);
            while (entryReader.Read())
            {
                switch (entryReader.CurrentIfdName)
                {
                    case IfdNames.Default:
                    case "Raw":
                        switch (entryReader.CurrentEntryId)
                        {
                            case 0x00fe: // NewSubfileType
                                if (entryReader.TryGetEntryData(out uintData))
                                    isFullSizeImage = (uintData[0] == 0);
                                break;
                            case 0x0100: // ImageWidth
                                if (entryReader.TryGetEntryData(out uintData))
                                {
                                    if (isFullSizeImage)
                                    {
                                        imageWidth = (int)uintData[0];
                                        this.Logger.LogTrace("Full image width: {width}", uintData[0]);
                                    }
                                    else
                                    {
                                        thumbWidth = (int)uintData[0];
                                        this.Logger.LogTrace("Thumbnail image width: {width}", uintData[0]);
                                    }
                                }
                                break;
                            case 0x0101: // ImageLength
                                if (entryReader.TryGetEntryData(out uintData))
                                {
                                    if (isFullSizeImage)
                                    {
                                        imageHeight = (int)uintData[0];
                                        this.Logger.LogTrace("Full image height: {height}", uintData[0]);
                                    }
                                    else
                                    {
                                        thumbHeight = (int)uintData[0];
                                        this.Logger.LogTrace("Thumbnail image height: {height}", uintData[0]);
                                    }
                                }
                                break;
                            case 0x0102: // BitsPerSample
                                if (isFullSizeImage && entryReader.TryGetEntryData(out ushortData))
                                {
                                    effectiveBits = ushortData[0];
                                    pixelStride = (effectiveBits >> 3);
                                    if ((effectiveBits & 0x7) != 0)
                                        ++pixelStride;
                                }
                                break;
                            case 0x0103: // Compression, should be 1 (Uncompressed data) for full-size image
                                if (entryReader.TryGetEntryData(out ushortData))
                                {
                                    if (isFullSizeImage)
                                    {
                                        isCompressedThumb = false;
                                        compression = ushortData[0];
                                    }
                                    else
                                        isCompressedThumb = ushortData[0] == Compressed;
                                }
                                break;
                            case 0x0106: // PhotometricInterpretation
                                if (isFullSizeImage && entryReader.TryGetEntryData(out ushortData))
                                    photometricInterpolation = ushortData[0];
                                break;
                            case 0x0111: // StripOffsets
                                if (isFullSizeImage)
                                    entryReader.TryGetEntryData(out stripOffsets);
                                else if (isCompressedThumb)
                                    entryReader.TryGetEntryData(out thumbStripOffsets);
                                break;
                            case 0x0112: // Orientation
                                if (entryReader.TryGetEntryData(out ushortData))
                                {
                                    if (isFullSizeImage)
                                        orientation = ushortData[0];
                                    else
                                    {
                                        thumbOrientation = ushortData[0];
                                        if (isCompressedThumb && compressedThumbOrientation < 0)
                                            compressedThumbOrientation = thumbOrientation;
                                    }
                                }
                                break;
                            case 0x0116: // RowsPerStrip:
                                if (isFullSizeImage)
                                    entryReader.TryGetEntryData(out rowsPerStrip);
                                break;
                            case 0x0117: // StripByteCounts
                                if (isFullSizeImage)
                                    entryReader.TryGetEntryData(out stripByteCounts);
                                else if (isCompressedThumb && entryReader.TryGetEntryData(out thumbStripByteCounts))
                                {
                                    // select this compressed thumbnail if it is the largest one
                                    if (thumbWidth > compressedThumbWidth && thumbHeight > compressedThumbHeight
                                        && thumbStripOffsets != null && thumbStripOffsets.Length == 1
                                        && thumbStripByteCounts.Length == 1)
                                    {
                                        hasCompressedThumb = true;
                                        compressedThumbWidth = thumbWidth;
                                        compressedThumbHeight = thumbHeight;
                                        compressedThumbOffset = thumbStripOffsets[0];
                                        compressedThumbDataSize = thumbStripByteCounts[0];
                                        compressedThumbOrientation = thumbOrientation;
                                    }
                                }
                                break;
                            case 0x0142: // TileWidth, should be same as image width
                                if (isFullSizeImage && entryReader.TryGetEntryData(out uintData) && uintData[0] != (uint)imageWidth)
                                    compression = 0;
                                break;
                            case 0x0143: // TileLength, should be same as image height
                                if (isFullSizeImage && entryReader.TryGetEntryData(out uintData) && uintData[0] != (uint)imageHeight)
                                    compression = 0;
                                break;
                            case 0x0144: // TileOffsets, only single tile is supported
                                if (isFullSizeImage && entryReader.TryGetEntryData(out uintData))
                                {
                                    if (uintData.Length == 1)
                                        imageDataOffset = (entryReader.InitialStreamPosition + uintData[0]);
                                    else
                                        compression = 0;
                                }
                                break;
                            case 0x0145: // TileByteCounts
                                break;
                            case 0x014a: // SubIFDs
                                if (!isFullSizeImage && entryReader.TryGetEntryData(out uintData))
                                {
                                    foreach (var offset in uintData)
                                        entryReader.EnqueueIfdToRead(entryReader.InitialStreamPosition + offset, "Raw");
                                }
                                break;
                            case 0x8769: // ExifOffset, the entries which describe how the image was captured are kept by the Exif IFD
                                if (entryReader.CurrentIfdName == IfdNames.Default && entryReader.CurrentIfdIndex == 0 && entryReader.TryGetEntryData(out uintData) && uintData.IsNotEmpty())
                                    entryReader.EnqueueIfdToRead(entryReader.InitialStreamPosition + uintData[0], IfdNames.Exif);
                                break;
                            case 0xc614: // UniqueCameraModel, it is defined in the IFD of main image
                                if (entryReader.TryGetEntryData(out string? stringData) && !string.IsNullOrWhiteSpace(stringData))
                                    uniqueCameraModel = stringData;
                                break;
                            case 0xc617: // CFALayout
                                if (isFullSizeImage && entryReader.TryGetEntryData(out ushortData))
                                    cfaLayout = ushortData[0];
                                break;
                            case 0xc618: // LinearizationTable
                                if (isFullSizeImage && entryReader.TryGetEntryData(out ushortData) && ushortData.IsNotEmpty())
                                {
                                    linearizationTable = ushortData;
                                    this.Logger.LogTrace("Linearization table: {count} colors, maximum: {maxColor}", ushortData.Length, ushortData.Max());
                                }
                                break;
                            case 0xc61a: // BlackLevel
                                if (isFullSizeImage)
                                {
                                    if (entryReader.CurrentEntryType == IfdEntryType.UInt16 
                                        && entryReader.TryGetEntryData(out ushortData))
                                    {
                                        blackLevel = ushortData[0];
                                    }
                                    else if (entryReader.CurrentEntryType == IfdEntryType.UInt32
                                        && entryReader.TryGetEntryData(out uintData))
                                    {
                                        blackLevel = uintData[0];
                                    }
                                    else if (entryReader.TryGetEntryData(out doubleData)
                                        && doubleData.IsNotEmpty()
                                        && double.IsFinite(doubleData[0])
                                        && doubleData[0] > 0)
                                    {
                                        blackLevel = (uint)(doubleData[0] + 0.5);
                                    }
                                }
                                break;
                            case 0xc61d: // WhiteLevel
                                if (isFullSizeImage)
                                {
                                    if (entryReader.CurrentEntryType == IfdEntryType.UInt16 
                                        && entryReader.TryGetEntryData(out ushortData))
                                    {
                                        whiteLevel = ushortData[0];
                                    }
                                    else if (entryReader.CurrentEntryType == IfdEntryType.UInt32
                                        && entryReader.TryGetEntryData(out uintData))
                                    {
                                        whiteLevel = uintData[0];
                                    }
                                    // the white level is checked against the value domain of source image only when no
                                    // linearization table is defined, otherwise it belongs to the color domain of the table.
                                    // the entries of an IFD are ordered by their ID, so the table has been read before reaching here
                                    if (linearizationTable is null)
                                    {
                                        if (whiteLevel >= (1 << effectiveBits))
                                        {
                                            this.Logger.LogWarning("Unexpected white level: {whiteLevel}, effect bits: {effectiveBits}", whiteLevel, effectiveBits);
                                            whiteLevel = (uint)((1 << effectiveBits) - 1);
                                        }
                                        else if (whiteLevel > 0)
                                        {
                                            var mask = 1u;
                                            effectiveBits = 1;
                                            while (mask < whiteLevel)
                                            {
                                                ++effectiveBits;
                                                mask = ((mask << 1) | 1);
                                            }
                                        }
                                    }
                                }
                                break;
                            case 0xc620: // DefaultCropSize
                                if (isFullSizeImage)
                                {
                                    if (entryReader.CurrentEntryType == IfdEntryType.UInt16
                                        && entryReader.TryGetEntryData(out ushortData)
                                        && ushortData.Length >= 2)
                                    {
                                        imageWidth = Math.Min(imageWidth, ushortData[0]);
                                        imageHeight = Math.Min(imageHeight, ushortData[1]);
                                        this.Logger.LogTrace("Full image crop size: {width}x{height}", imageWidth, imageHeight);
                                    }
                                    else if (entryReader.CurrentEntryType == IfdEntryType.UInt32
                                             && entryReader.TryGetEntryData(out uintData)
                                             && uintData.Length >= 2)
                                    {
                                        imageWidth = Math.Min(imageWidth, (int)uintData[0]);
                                        imageHeight = Math.Min(imageHeight, (int)uintData[1]);
                                        this.Logger.LogTrace("Full image crop size: {width}x{height}", imageWidth, imageHeight);
                                    }
                                }
                                break;
                            case 0xc621: // ColorMatrix1, it and the tags below are defined in the IFD of main image
                                entryReader.TryGetEntryData(out colorMatrix1);
                                break;
                            case 0xc622: // ColorMatrix2
                                entryReader.TryGetEntryData(out colorMatrix2);
                                break;
                            case 0xc623: // CameraCalibration1
                                entryReader.TryGetEntryData(out cameraCalibration1);
                                break;
                            case 0xc624: // CameraCalibration2
                                entryReader.TryGetEntryData(out cameraCalibration2);
                                break;
                            case 0xc627: // AnalogBalance
                                entryReader.TryGetEntryData(out analogBalance);
                                break;
                            case 0xc628: // AsShotNeutral, it is defined in the IFD of main image instead of the IFD of full-size image
                                if (entryReader.TryGetEntryData(out doubleData)
                                    && doubleData.Length >= 3
                                    && doubleData.Take(3).All(it => double.IsFinite(it) && it > 0))
                                {
                                    asShotNeutral = doubleData;
                                    this.Logger.LogTrace("As-shot neutral: {r}, {g}, {b}", doubleData[0], doubleData[1], doubleData[2]);
                                }
                                break;
                            case 0xc65a: // CalibrationIlluminant1
                                if (entryReader.TryGetEntryData(out ushortData))
                                    calibrationIlluminant1 = ushortData[0];
                                break;
                            case 0xc65b: // CalibrationIlluminant2
                                if (entryReader.TryGetEntryData(out ushortData))
                                    calibrationIlluminant2 = ushortData[0];
                                break;
                            case 0xc68d: // ActiveArea
                                if (isFullSizeImage)
                                {
                                    if (entryReader.CurrentEntryType == IfdEntryType.UInt16
                                        && entryReader.TryGetEntryData(out ushortData)
                                        && ushortData.Length >= 4)
                                    {
                                        activeArea = [ ushortData[1], ushortData[0], ushortData[3], ushortData[2] ];
                                    }
                                    else if (entryReader.CurrentEntryType == IfdEntryType.UInt32
                                        && entryReader.TryGetEntryData(out uintData)
                                        && uintData.Length >= 4)
                                    {
                                        activeArea = [ (int)uintData[1], (int)uintData[0], (int)uintData[3], (int)uintData[2] ];
                                    }
                                }
                                break;
                            case 0xc68f: // AsShotICCProfile
                                break;
                            case 0xc691: // CurrentICCProfile
                                break;
                            case 0xc714: // ForwardMatrix1
                                entryReader.TryGetEntryData(out forwardMatrix1);
                                break;
                            case 0xc715: // ForwardMatrix2
                                entryReader.TryGetEntryData(out forwardMatrix2);
                                break;
                            case 0x828e: // CFAPattern
                                if (isFullSizeImage)
                                    entryReader.TryGetEntryData(out cfaPattern);
                                break;
                        }
                        //if (isFullSizeImage)
                            //System.Diagnostics.Debug.WriteLine($"{entryReader.CurrentIfdName}[{entryReader.CurrentIfdIndex}] {entryReader.CurrentEntryId:x4} {entryReader.CurrentEntryType}");
                        break;
                }
                ifdMetadata.SetEntry(entryReader);
            }

            // try combining strips into single block
            var imageDataSize = 0L;
            if (imageDataOffset == 0 && stripOffsets != null && stripByteCounts != null && stripByteCounts.Length == stripOffsets.Length)
            {
                var stripCount = stripByteCounts.Length;
                var stripEnd = stripOffsets[0] + stripByteCounts[0];
                imageDataSize = stripByteCounts[0];
                for (var i = 1; i < stripCount; ++i)
                {
                    if (stripOffsets[i] != stripEnd)
                        return;
                    imageDataSize += stripByteCounts[i];
                    stripEnd += stripByteCounts[i];
                }
                imageDataOffset = stripOffsets[0];
            }

            // calculate row stride
            if (stripByteCounts != null)
            {
                if (rowsPerStrip != null)
                    rowStride = (int)(stripByteCounts[0] / rowsPerStrip[0]);
                else
                    rowStride = (int)(imageDataSize / imageHeight);
            }

            // calculate pixel stride
            if (pixelStride <= 0)
                pixelStride = rowStride / imageWidth;

            // calculate effective bits
            if (effectiveBits <= 0)
                effectiveBits = (pixelStride << 3);
            
            // get compression format and color space
            var useCompressedImage = false;
            switch (compression)
            {
                case Uncompressed:
                    break;
                case Compressed:
                    if (imageDataOffset > 0)
                    {
                        stream.Position = imageDataOffset;
                        useCompressedImage = true;
                    }
                    break;
                case LossyJpeg:
                    if (imageDataOffset > 0)
                    {
                        stream.Position = imageDataOffset;
                        useCompressedImage = true;
                        compressedThumbFormat = SKEncodedImageFormat.Jpeg;
                    }
                    break;
                default:
                    if (hasCompressedThumb && compressedThumbOffset > 0)
                    {
                        stream.Position = compressedThumbOffset;
                        useCompressedImage = true;
                    }
                    break;
            }
            if (useCompressedImage && !compressedThumbFormat.HasValue)
            {
                var headerBuffer = new byte[4];
                try
                {
                    // ReSharper disable once MethodHasAsyncOverloadWithCancellation
                    if (stream.Read(headerBuffer, 0, 4) == 4)
                    {
                        if (headerBuffer[0] == 0xff && headerBuffer[1] == 0xd8)
                            compressedThumbFormat = SKEncodedImageFormat.Jpeg;
                        else
                        {
                            this.Logger.LogError("Unrecognized format of thumbnail");
                            return;
                        }
                    }
                    else
                    {
                        this.Logger.LogError("Unable to read header of thumbnail");
                        return;
                    }
                }
                finally
                {
                    stream.Position = compressedThumbOffset;
                }
            }
            if (compressedThumbFormat.HasValue)
            {
                switch (compressedThumbFormat.Value)
                {
                    case SKEncodedImageFormat.Jpeg:
                        if (JpegFileFormatParser.SeekToIccProfile(stream))
                            colorSpace = await ColorSpace.LoadFromIccProfileAsync(stream, ColorSpaceSource.Embedded, cancellationToken);
                        break;
                }
            }
        }, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();

        // combine the metadata parsed from the file
        var mediaMetadata = Tiff.CombineMediaMetadata(ifdMetadata);

        // check image data and size
        if (imageWidth <= 0 || imageHeight <= 0)
            return null;
        if (compression != 0 && imageDataOffset <= 0)
            return null;
        if (compression == 1 // uncompressed raw
            && (effectiveBits <= 0 || pixelStride <= 0 || rowStride <= 0))
        {
            return null;
        }

        // treat as compressed format
        ImageRenderers.IImageRenderer? imageRenderer;
        switch (compression)
        {
            case Uncompressed:
                break;
            case Compressed:
            case LossyJpeg:
                imageRenderer = ImageRenderers.ImageRenderers.All.FirstOrDefault(it => it is ImageRenderers.JpegImageRenderer);
                if (imageRenderer != null)
                {
                    return new ImageRenderingProfile(FileFormats.Dng, imageRenderer).Also(profile =>
                    {
                        if (colorSpace != null)
                            profile.ColorSpace = colorSpace;
                        profile.DataOffset = imageDataOffset;
                        profile.MediaMetadata = mediaMetadata;
                        profile.Height = imageHeight;
                        profile.Width = imageWidth;
                        Tiff.FromTiffOrientation(compressedThumbOrientation >= 0 ? compressedThumbOrientation : orientation, out var rotation, out var flipX, out var flipY);
                        profile.Orientation = rotation;
                        profile.FlipX = flipX;
                        profile.FlipY = flipY;
                    });
                }
                return null;
            default:
                if (hasCompressedThumb && compressedThumbOffset > 0 && compressedThumbWidth > 0 && compressedThumbHeight > 0)
                {
                    imageRenderer = ImageRenderers.ImageRenderers.All.FirstOrDefault(it => it is ImageRenderers.JpegImageRenderer);
                    if (imageRenderer != null)
                    {
                        return new ImageRenderingProfile(FileFormats.Dng, imageRenderer).Also(profile =>
                        {
                            if (colorSpace != null)
                                profile.ColorSpace = colorSpace;
                            profile.DataOffset = compressedThumbOffset;
                            profile.MediaMetadata = mediaMetadata;
                            profile.Height = compressedThumbHeight;
                            profile.Width = compressedThumbWidth;
                            Tiff.FromTiffOrientation(compressedThumbOrientation >= 0 ? compressedThumbOrientation : orientation, out var rotation, out var flipX, out var flipY);
                            profile.Orientation = rotation;
                            profile.FlipX = flipX;
                            profile.FlipY = flipY;
                        });
                    }
                }
                return null;
        }

        // check CFA
        var bayerPattern = BayerPattern.RGGB_2x2;
        if (photometricInterpolation != 32803) // only CFA is supported
            return null;
        if (cfaLayout != 0 && cfaLayout != 1) // only rectangular CFA is supported
            return null;
        if (cfaPattern != null)
        {
            foreach (var entry in CfaPatternToBayerPatternMap)
            {
                if (entry.Item1.SequenceEqual(cfaPattern))
                {
                    bayerPattern = entry.Item2;
                    break;
                }
            }
        }

        // select renderer
        imageRenderer = pixelStride switch
        { 
            1 => ImageRenderers.ImageRenderers.All.FirstOrDefault(it => it is ImageRenderers.BayerPattern8ImageRenderer),
            2 => ImageRenderers.ImageRenderers.All.FirstOrDefault(it => it is ImageRenderers.BayerPattern16ImageRenderer),
            _ => null,
        };
        if (imageRenderer == null)
            return null;

        // create profile
        return new ImageRenderingProfile(FileFormats.Dng, imageRenderer).Also(profile =>
        {
            // common properties
            profile.BayerPattern = bayerPattern;
            profile.ByteOrdering = byteOrdering;
            if (colorSpace != null)
                profile.ColorSpace = colorSpace;
            profile.MediaMetadata = mediaMetadata;
            profile.UseLinearColorSpace = true;
            profile.DataOffset = imageDataOffset;
            profile.EffectiveBits = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = effectiveBits);
            profile.BlackLevels = new uint[ImageFormat.MaxPlaneCount].Also(it => it[0] = blackLevel);
            profile.WhiteLevels = new uint[ImageFormat.MaxPlaneCount].Also(it => it[0] = whiteLevel);
            profile.Height = imageHeight;
            Tiff.FromTiffOrientation(orientation, out var rotation, out var flipX, out var flipY);
            profile.Orientation = rotation;
            profile.FlipX = flipX;
            profile.FlipY = flipY;
            profile.PixelStrides = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = pixelStride);
            profile.RowStrides = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = rowStride);
            profile.Width = imageWidth;

            // apply the linearization table, DNG defines one table for every color channel of the mosaic. the profile
            // shares the table by itself so the one created here is released after it has been set
            if (linearizationTable is not null)
            {
                using var colorTable = this.CreateColorTable(linearizationTable, pixelStride, whiteLevel);
                if (colorTable is not null)
                {
                    profile.RedColorTable = colorTable;
                    profile.GreenColorTable = colorTable;
                    profile.BlueColorTable = colorTable;
                    this.Logger.LogTrace("Linearization table applied, colors: {count}, color bit depth: {colorBitDepth}", colorTable.Count, colorTable.ColorBitDepth);
                }
            }

            // apply white balance defined by as-shot neutral, the gains are normalized to make gain of green be 1
            if (asShotNeutral is not null)
            {
                var greenNeutral = asShotNeutral[1];
                profile.RedColorGain = Math.Round(greenNeutral / asShotNeutral[0], 4);
                profile.GreenColorGain = 1.0;
                profile.BlueColorGain = Math.Round(greenNeutral / asShotNeutral[2], 4);
            }

            // convert the color characterization of the camera into the color space of the image, the gains applied
            // above are part of its input so they are passed to keep the color space consistent with them
            if (DngCameraProfile.TryCreate(asShotNeutral, analogBalance, colorMatrix1, colorMatrix2, forwardMatrix1, forwardMatrix2, cameraCalibration1, cameraCalibration2, calibrationIlluminant1, calibrationIlluminant2, out var cameraProfile))
            {
                var cameraColorSpace = cameraProfile!.CreateColorSpace(uniqueCameraModel, profile.RedColorGain, profile.GreenColorGain, profile.BlueColorGain);
                if (cameraColorSpace is not null)
                {
                    profile.ColorSpace = cameraColorSpace;
                    this.Logger.LogTrace("Color space of camera: {colorSpace}", cameraColorSpace);
                }
                else
                    this.Logger.LogWarning("Unable to convert color characterization of camera into color space");
            }
        });
    }
}