using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using ImageMagick;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// Implementation of <see cref="IFileFormatParser"/> for TIFF format.
/// </summary>
class TiffFileFormatParser : MagickFileFormatParser
{
    // Constants.
    const int PhotometricBlackIsZero = 1;
    const int PhotometricRgb = 2;
    const int Uncompressed = 1;


    /// <summary>
    /// Initialize new <see cref="TiffFileFormatParser"/> instance.
    /// </summary>
    public TiffFileFormatParser() : base(FileFormats.Tiff, [ MagickFormat.Tiff, MagickFormat.Tif, MagickFormat.Tiff64 ], ImageRenderers.ImageRenderers.All.First(it => it is ImageRenderers.TiffImageRenderer))
    { }


    // Find the registered renderer of the given type.
    static ImageRenderers.IImageRenderer? FindRenderer<T>() where T : ImageRenderers.IImageRenderer =>
        ImageRenderers.ImageRenderers.All.FirstOrDefault(it => it is T);


    // Load color space from embedded ICC profile, falling back to the Exif color space tag.
    static async Task<ColorSpace?> LoadColorSpaceAsync(byte[]? iccProfileData, ColorSpace? exifColorSpace, CancellationToken cancellationToken)
    {
        if (iccProfileData is not null)
        {
            try
            {
                using var iccProfileStream = new MemoryStream(iccProfileData);
                return await ColorSpace.LoadFromIccProfileAsync(iccProfileStream, ColorSpaceSource.Embedded, cancellationToken);
            }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                return exifColorSpace;
            }
        }
        return exifColorSpace;
    }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        Tiff.CheckFileHeader(stream, out _);


    /// <inheritdoc/>
    protected override async Task OnParseExtraInformationAsync(Stream stream, ImageRenderingProfile profile, CancellationToken cancellationToken)
    {
        // call base
        await base.OnParseExtraInformationAsync(stream, profile, cancellationToken);

        // read orientation, embedded ICC profile and color space from the first IFD
        var orientation = 0;
        var iccProfileData = (byte[]?)null;
        var exifColorSpace = (ColorSpace?)null;
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
            while (entryReader.Read())
            {
                if (entryReader.CurrentIfdName == IfdNames.Default && entryReader.CurrentIfdIndex == 0)
                {
                    switch (entryReader.CurrentEntryId)
                    {
                        case 0x0112: // Orientation
                            if (entryReader.TryGetEntryData(out ushort[]? orientationData) && orientationData is not null)
                                orientation = orientationData[0];
                            break;
                        case 0x8769: // ExifOffset
                            if (entryReader.TryGetEntryData(out uint[]? exifOffsetData) && exifOffsetData is not null)
                                entryReader.EnqueueIfdToRead(entryReader.InitialStreamPosition + exifOffsetData[0], IfdNames.Exif);
                            break;
                        case 0x8773: // IccProfile
                            if (entryReader.TryGetEntryData(out byte[]? iccData) && iccData.IsNotEmpty())
                                iccProfileData = iccData.AsNonNull();
                            break;
                    }
                }
                else if (entryReader.CurrentIfdName == IfdNames.Exif)
                {
                    switch (entryReader.CurrentEntryId)
                    {
                        case 0xa001: // ColorSpace
                            if (entryReader.TryGetEntryData(out ushort[]? colorSpaceData) && colorSpaceData is not null)
                            {
                                switch (colorSpaceData[0])
                                {
                                    case 0x1:
                                        exifColorSpace = ColorSpace.Srgb;
                                        break;
                                    case 0x2:
                                        exifColorSpace = ColorSpace.AdobeRGB_1998;
                                        break;
                                }
                            }
                            break;
                    }
                }
            }
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // apply orientation
        Tiff.FromTiffOrientation(orientation, out var rotation, out var flipX, out var flipY);
        profile.Orientation = rotation;
        profile.FlipX = flipX;
        profile.FlipY = flipY;

        // apply color space (embedded ICC profile takes precedence over the Exif color space tag)
        var colorSpace = await LoadColorSpaceAsync(iccProfileData, exifColorSpace, cancellationToken);
        if (colorSpace is not null)
            profile.ColorSpace = colorSpace;
    }


    /// <inheritdoc/>
    protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(IImageDataSource source, Stream stream, CancellationToken cancellationToken)
    {
        // try to read as an uncompressed image with a direct pixel-format renderer
        var profile = await this.TryParseUncompressedProfileAsync(stream, cancellationToken);
        if (profile is not null)
            return profile;

        // fall back to ImageMagick-based decoding for compressed or unsupported layouts
        stream.Position = 0;
        return await base.ParseImageRenderingProfileAsyncCore(source, stream, cancellationToken);
    }


    // Try to build a profile that reads an uncompressed TIFF directly with a raw pixel-format renderer.
    async Task<ImageRenderingProfile?> TryParseUncompressedProfileAsync(Stream stream, CancellationToken cancellationToken)
    {
        // read layout, orientation and color space information from the first IFD
        var byteOrdering = ByteOrdering.LittleEndian;
        var imageWidth = 0;
        var imageHeight = 0;
        var compression = 0;
        var photometric = -1;
        var samplesPerPixel = 0;
        var planarConfiguration = 1;
        var sampleFormat = 1;
        var bitsPerSample = (ushort[]?)null;
        var stripOffsets = (uint[]?)null;
        var stripByteCounts = (uint[]?)null;
        var orientation = 0;
        var iccProfileData = (byte[]?)null;
        var exifColorSpace = (ColorSpace?)null;
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
            byteOrdering = entryReader.IsLittleEndian ? ByteOrdering.LittleEndian : ByteOrdering.BigEndian;

            // read entries of the first IFD (and its Exif sub-IFD)
            while (entryReader.Read())
            {
                if (entryReader.CurrentIfdName == IfdNames.Default && entryReader.CurrentIfdIndex == 0)
                {
                    switch (entryReader.CurrentEntryId)
                    {
                        case 0x0100: // ImageWidth
                            if (entryReader.TryGetEntryData(out uint[]? widthData) && widthData is not null)
                                imageWidth = (int)widthData[0];
                            break;
                        case 0x0101: // ImageLength
                            if (entryReader.TryGetEntryData(out uint[]? heightData) && heightData is not null)
                                imageHeight = (int)heightData[0];
                            break;
                        case 0x0102: // BitsPerSample
                            entryReader.TryGetEntryData(out bitsPerSample);
                            break;
                        case 0x0103: // Compression
                            if (entryReader.TryGetEntryData(out ushort[]? compressionData) && compressionData is not null)
                                compression = compressionData[0];
                            break;
                        case 0x0106: // PhotometricInterpretation
                            if (entryReader.TryGetEntryData(out ushort[]? photometricData) && photometricData is not null)
                                photometric = photometricData[0];
                            break;
                        case 0x0111: // StripOffsets
                            entryReader.TryGetEntryData(out stripOffsets);
                            break;
                        case 0x0112: // Orientation
                            if (entryReader.TryGetEntryData(out ushort[]? orientationData) && orientationData is not null)
                                orientation = orientationData[0];
                            break;
                        case 0x0115: // SamplesPerPixel
                            if (entryReader.TryGetEntryData(out ushort[]? samplesData) && samplesData is not null)
                                samplesPerPixel = samplesData[0];
                            break;
                        case 0x0117: // StripByteCounts
                            entryReader.TryGetEntryData(out stripByteCounts);
                            break;
                        case 0x011c: // PlanarConfiguration
                            if (entryReader.TryGetEntryData(out ushort[]? planarData) && planarData is not null)
                                planarConfiguration = planarData[0];
                            break;
                        case 0x0153: // SampleFormat
                            if (entryReader.TryGetEntryData(out ushort[]? sampleFormatData) && sampleFormatData is not null)
                                sampleFormat = sampleFormatData[0];
                            break;
                        case 0x8769: // ExifOffset
                            if (entryReader.TryGetEntryData(out uint[]? exifOffsetData) && exifOffsetData is not null)
                                entryReader.EnqueueIfdToRead(entryReader.InitialStreamPosition + exifOffsetData[0], IfdNames.Exif);
                            break;
                        case 0x8773: // IccProfile
                            if (entryReader.TryGetEntryData(out byte[]? iccData) && iccData.IsNotEmpty())
                                iccProfileData = iccData.AsNonNull();
                            break;
                    }
                }
                else if (entryReader.CurrentIfdName == IfdNames.Exif)
                {
                    switch (entryReader.CurrentEntryId)
                    {
                        case 0xa001: // ColorSpace
                            if (entryReader.TryGetEntryData(out ushort[]? colorSpaceData) && colorSpaceData is not null)
                            {
                                switch (colorSpaceData[0])
                                {
                                    case 0x1:
                                        exifColorSpace = ColorSpace.Srgb;
                                        break;
                                    case 0x2:
                                        exifColorSpace = ColorSpace.AdobeRGB_1998;
                                        break;
                                }
                            }
                            break;
                    }
                }
            }
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // reject unless it is an uncompressed, chunky, unsigned image with valid dimensions
        if (imageWidth <= 0 || imageHeight <= 0)
            return null;
        if (compression != Uncompressed || planarConfiguration != 1 || sampleFormat != 1)
            return null;
        if (!bitsPerSample.IsNotEmpty())
            return null;
        var sampleBits = bitsPerSample.AsNonNull();

        // require a single supported bit depth shared by all samples
        var bits = sampleBits[0];
        if (bits != 8 && bits != 16)
            return null;
        foreach (var sampleBit in sampleBits)
        {
            if (sampleBit != bits)
                return null;
        }
        if (samplesPerPixel <= 0)
            samplesPerPixel = sampleBits.Length;
        if (samplesPerPixel != sampleBits.Length)
            return null;

        // select renderer by photometric interpretation, sample count and bit depth
        var renderer = (photometric, samplesPerPixel, bits) switch
        {
            (PhotometricBlackIsZero, 1, 8) => FindRenderer<ImageRenderers.L8ImageRenderer>(),
            (PhotometricBlackIsZero, 1, 16) => FindRenderer<ImageRenderers.L16ImageRenderer>(),
            (PhotometricRgb, 3, 8) => FindRenderer<ImageRenderers.Rgb888ImageRenderer>(),
            (PhotometricRgb, 3, 16) => FindRenderer<ImageRenderers.Rgb161616ImageRenderer>(),
            (PhotometricRgb, 4, 8) => FindRenderer<ImageRenderers.Rgba8888ImageRenderer>(),
            (PhotometricRgb, 4, 16) => FindRenderer<ImageRenderers.Rgba16161616ImageRenderer>(),
            _ => null,
        };
        if (renderer is null)
            return null;

        // combine strips into a single contiguous block starting at the first strip
        if (!stripOffsets.IsNotEmpty() || !stripByteCounts.IsNotEmpty())
            return null;
        var offsets = stripOffsets.AsNonNull();
        var byteCounts = stripByteCounts.AsNonNull();
        if (offsets.Length != byteCounts.Length)
            return null;
        var dataOffset = (long)offsets[0];
        var stripEnd = offsets[0] + byteCounts[0];
        for (var i = 1; i < offsets.Length; ++i)
        {
            if (offsets[i] != stripEnd)
                return null;
            stripEnd += byteCounts[i];
        }

        // uncompressed TIFF rows are contiguous with no inter-row padding for byte-aligned samples
        var pixelStride = samplesPerPixel * (bits >> 3);
        var rowStride = pixelStride * imageWidth;

        // load color space and create profile
        var colorSpace = await LoadColorSpaceAsync(iccProfileData, exifColorSpace, cancellationToken);
        return new ImageRenderingProfile(FileFormats.Tiff, renderer).Also(profile =>
        {
            profile.ByteOrdering = byteOrdering;
            if (colorSpace is not null)
                profile.ColorSpace = colorSpace;
            profile.DataOffset = dataOffset;
            profile.EffectiveBits = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = bits);
            profile.Height = imageHeight;
            Tiff.FromTiffOrientation(orientation, out var rotation, out var flipX, out var flipY);
            profile.Orientation = rotation;
            profile.FlipX = flipX;
            profile.FlipY = flipY;
            profile.PixelStrides = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = pixelStride);
            profile.RowStrides = new int[ImageFormat.MaxPlaneCount].Also(it => it[0] = rowStride);
            profile.Width = imageWidth;
        });
    }
}
