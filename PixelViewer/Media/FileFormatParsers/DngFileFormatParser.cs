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

namespace Carina.PixelViewer.Media.FileFormatParsers
{
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
        static readonly IList<Tuple<byte[], BayerPattern>> CfaPatternToBayerPatternMap = new Tuple<byte[], BayerPattern>[]
        {
            new(new byte[] { 2, 1, 1, 0 }, BayerPattern.BGGR_2x2),
            new(new byte[] { 1, 2, 0, 1 }, BayerPattern.GBRG_2x2),
            new(new byte[] { 1, 0, 2, 1 }, BayerPattern.GRBG_2x2),
            new(new byte[] { 0, 1, 1, 2 }, BayerPattern.RGGB_2x2),
            new(new byte[] { 2, 2, 1, 1, 2, 2, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0 }, BayerPattern.BGGR_4x4),
            new(new byte[] { 1, 1, 2, 2, 1, 1, 2, 2, 0, 0, 1, 1, 0, 0, 1, 1 }, BayerPattern.GBRG_2x2),
            new(new byte[] { 1, 1, 0, 0, 1, 1, 0, 0, 2, 2, 1, 1, 2, 2, 1, 1 }, BayerPattern.GRBG_4x4),
            new(new byte[] { 0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 2, 2, 1, 1, 2, 2 }, BayerPattern.RGGB_4x4),
        };


        /// <summary>
        /// Initialize new <see cref="DngFileFormatParser"/> instance.
        /// </summary>
        public DngFileFormatParser() : base(FileFormats.Dng)
        { }


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
            var blackLevel = 0u;
            var whiteLevel = 0u;
            var effectiveBits = 0;
            var pixelStride = 0;
            var rowStride = 0;
            var photometricInterpolation = (ushort)0;
            var activeArea = (int[]?)null; // in LTRB
            var cfaLayout = 0;
            var cfaPattern = (byte[]?)null;
            var imageDataOffset = 0L;
            var colorSpace = (Media.ColorSpace?)null;
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
                                    if (entryReader.TryGetEntryData(out uintData) && uintData != null)
                                        isFullSizeImage = (uintData[0] == 0);
                                    break;
                                case 0x0100: // ImageWidth
                                    if (entryReader.TryGetEntryData(out uintData) && uintData != null)
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
                                    if (entryReader.TryGetEntryData(out uintData) && uintData != null)
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
                                    if (isFullSizeImage && entryReader.TryGetEntryData(out ushortData) && ushortData != null)
                                    {
                                        effectiveBits = ushortData[0];
                                        pixelStride = (effectiveBits >> 3);
                                        if ((effectiveBits & 0x7) != 0)
                                            ++pixelStride;
                                    }
                                    break;
                                case 0x0103: // Compression, should be 1 (Uncompressed data) for full-size image
                                    if (entryReader.TryGetEntryData(out ushortData) && ushortData != null)
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
                                    if (isFullSizeImage && entryReader.TryGetEntryData(out ushortData) && ushortData != null)
                                        photometricInterpolation = ushortData[0];
                                    break;
                                case 0x0111: // StripOffsets
                                    if (isFullSizeImage)
                                        entryReader.TryGetEntryData(out stripOffsets);
                                    else if (isCompressedThumb)
                                        entryReader.TryGetEntryData(out thumbStripOffsets);
                                    break;
                                case 0x0112: // Orientation
                                    if (entryReader.TryGetEntryData(out ushortData) && ushortData != null)
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
                                            && thumbStripByteCounts != null && thumbStripByteCounts.Length == 1)
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
                                    if (isFullSizeImage && entryReader.TryGetEntryData(out uintData) && uintData != null && uintData[0] != (uint)imageWidth)
                                        compression = 0;
                                    break;
                                case 0x0143: // TileLength, should be same as image height
                                    if (isFullSizeImage && entryReader.TryGetEntryData(out uintData) && uintData != null && uintData[0] != (uint)imageHeight)
                                        compression = 0;
                                    break;
                                case 0x0144: // TileOffsets, only single tile is supported
                                    if (isFullSizeImage && entryReader.TryGetEntryData(out uintData) && uintData != null)
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
                                    if (!isFullSizeImage && entryReader.TryGetEntryData(out uintData) && uintData != null)
                                    {
                                        foreach (var offset in uintData)
                                            entryReader.EnqueueIfdToRead(entryReader.InitialStreamPosition + offset, "Raw");
                                    }
                                    break;
                                case 0xc617: // CFALayout
                                    if (isFullSizeImage && entryReader.TryGetEntryData(out ushortData) && ushortData != null)
                                        cfaLayout = ushortData[0];
                                    break;
                                case 0xc61a: // BlackLevel
                                    if (isFullSizeImage)
                                    {
                                        if (entryReader.CurrentEntryType == IfdEntryType.UInt16 
                                            && entryReader.TryGetEntryData(out ushortData)
                                            && ushortData != null)
                                        {
                                            blackLevel = ushortData[0];
                                        }
                                        else if (entryReader.CurrentEntryType == IfdEntryType.UInt32
                                            && entryReader.TryGetEntryData(out uintData)
                                            && uintData != null)
                                        {
                                            blackLevel = uintData[0];
                                        }
                                    }
                                    break;
                                case 0xc61d: // WhiteLevel
                                    if (isFullSizeImage)
                                    {
                                        if (entryReader.CurrentEntryType == IfdEntryType.UInt16 
                                            && entryReader.TryGetEntryData(out ushortData)
                                            && ushortData != null)
                                        {
                                            whiteLevel = ushortData[0];
                                        }
                                        else if (entryReader.CurrentEntryType == IfdEntryType.UInt32
                                            && entryReader.TryGetEntryData(out uintData)
                                            && uintData != null)
                                        {
                                            whiteLevel = uintData[0];
                                        }
                                        if (whiteLevel >= (1 << effectiveBits))
                                        {
                                            whiteLevel = (uint)((1 << effectiveBits) - 1);
                                            this.Logger.LogWarning("Unexpected white level: {whiteLevel}, effect bits: {effectiveBits}", whiteLevel, effectiveBits);
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
                                    break;
                                case 0xc620: // DefaultCropSize
                                    if (isFullSizeImage)
                                    {
                                        if (entryReader.CurrentEntryType == IfdEntryType.UInt16
                                            && entryReader.TryGetEntryData(out ushortData)
                                            && ushortData != null
                                            && ushortData.Length >= 2)
                                        {
                                            imageWidth = Math.Min(imageWidth, ushortData[0]);
                                            imageHeight = Math.Min(imageHeight, ushortData[1]);
                                            this.Logger.LogTrace("Full image crop size: {width}x{height}", imageWidth, imageHeight);
                                        }
                                        else if (entryReader.CurrentEntryType == IfdEntryType.UInt32
                                                 && entryReader.TryGetEntryData(out uintData)
                                                 && uintData != null
                                                 && uintData.Length >= 2)
                                        {
                                            imageWidth = Math.Min(imageWidth, (int)uintData[0]);
                                            imageHeight = Math.Min(imageHeight, (int)uintData[1]);
                                            this.Logger.LogTrace("Full image crop size: {width}x{height}", imageWidth, imageHeight);
                                        }
                                    }
                                    break;
                                case 0xc68d: // ActiveArea
                                    if (isFullSizeImage)
                                    {
                                        if (entryReader.CurrentEntryType == IfdEntryType.UInt16
                                            && entryReader.TryGetEntryData(out ushortData)
                                            && ushortData != null
                                            && ushortData.Length >= 4)
                                        {
                                            activeArea = new int[] { ushortData[1], ushortData[0], ushortData[3], ushortData[2] };
                                        }
                                        else if (entryReader.CurrentEntryType == IfdEntryType.UInt32
                                            && entryReader.TryGetEntryData(out uintData)
                                            && uintData != null
                                            && uintData.Length >= 4)
                                        {
                                            activeArea = new[] { (int)uintData[1], (int)uintData[0], (int)uintData[3], (int)uintData[2] };
                                        }
                                    }
                                    break;
                                case 0xc68f: // AsShotICCProfile
                                    break;
                                case 0xc691: // CurrentICCProfile
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
                            profile.Height = imageHeight;
                            profile.Width = imageWidth;
                            profile.Orientation = Tiff.FromTiffOrientation(compressedThumbOrientation >= 0 ? compressedThumbOrientation : orientation);
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
                                profile.Height = compressedThumbHeight;
                                profile.Width = compressedThumbWidth;
                                profile.Orientation = Tiff.FromTiffOrientation(compressedThumbOrientation >= 0 ? compressedThumbOrientation : orientation);
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
                profile.BayerPattern = bayerPattern;
                profile.ByteOrdering = byteOrdering;
                if (colorSpace != null)
                    profile.ColorSpace = colorSpace;
                profile.UseLinearColorSpace = true;
                profile.DataOffset = imageDataOffset;
                profile.EffectiveBits = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = effectiveBits);
                profile.BlackLevels = new uint[ImageFormat.MaxPlaneCount].Also(it => it[0] = blackLevel);
                profile.WhiteLevels = new uint[ImageFormat.MaxPlaneCount].Also(it => it[0] = whiteLevel);
                profile.Height = imageHeight;
                profile.Orientation = Tiff.FromTiffOrientation(orientation);
                profile.PixelStrides = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = pixelStride);
                profile.RowStrides = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = rowStride);
                profile.Width = imageWidth;
            });
        }
    }
}
