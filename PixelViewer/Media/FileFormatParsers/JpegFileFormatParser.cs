using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// <see cref="IFileFormatParser"/> to parse JPEG file.
/// </summary>
class JpegFileFormatParser : SkiaFileFormatParser
{
    // Constants.
    const string XmpIdentifier = "http://ns.adobe.com/xap/1.0/\0";


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


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        CheckFileHeader(stream);


    /// <inheritdoc/>
    protected override async Task OnParseExtraInformationAsync(Stream stream, ImageRenderingProfile profile, CancellationToken cancellationToken)
    {
        // parse EXIF and XMP
        var orientation = 0;
        TiffMediaMetadata? exifMetadata = null;
        XmpMediaMetadata? xmpMetadata = null;
        await Task.Run(() =>
        {
            // skip file header
            var segmentHeaderBuffer = new byte[4];
            stream.Seek(2, SeekOrigin.Current);

            // read EXIF and XMP in APP segments, both of them are placed before the start of scan
            while (true)
            {
                if (stream.Read(segmentHeaderBuffer, 0, 4) < 4)
                    return;
                if (segmentHeaderBuffer[0] != 0xff)
                    return;
                if (segmentHeaderBuffer[1] == 0xda) // SOS
                    return;
                var segmentSize = BinaryPrimitives.ReadUInt16BigEndian(segmentHeaderBuffer.AsSpan(2));
                if (segmentSize < 2)
                    return;
                if (segmentHeaderBuffer[1] != 0xe1 // APP1
                    || segmentSize <= 6)
                {
                    stream.Seek(segmentSize - 2, SeekOrigin.Current);
                    continue;
                }
                var segmentDataBuffer = new byte[segmentSize - 2];
                if (stream.Read(segmentDataBuffer, 0, segmentDataBuffer.Length) < segmentDataBuffer.Length)
                    return;

                // parse EXIF which is placed after its identifier
                if (segmentDataBuffer[0] == 'E'
                    && segmentDataBuffer[1] == 'x'
                    && segmentDataBuffer[2] == 'i'
                    && segmentDataBuffer[3] == 'f'
                    && segmentDataBuffer[4] == 0x0
                    && segmentDataBuffer[5] == 0x0)
                {
                    if (exifMetadata is not null)
                        continue;
                    var metadata = new TiffMediaMetadata();
                    var entryReader = new IfdEntryReader(new MemoryStream(segmentDataBuffer).Also(it => it.Position = 6));
                    while (entryReader.Read())
                    {
                        ushort[]? ushortData;
                        uint[]? uintData;
                        switch (entryReader.CurrentIfdName)
                        {
                            case IfdNames.Default:
                                switch (entryReader.CurrentEntryId)
                                {
                                    case 0x0112: // Orientation
                                        if (entryReader.TryGetEntryData(out ushortData) && ushortData is not null && ushortData.Length > 0)
                                            orientation = ushortData[0];
                                        break;
                                    case 0x8769: // ExifOffset
                                        if (entryReader.TryGetEntryData(out uintData) && uintData is not null && uintData.Length > 0)
                                            entryReader.EnqueueIfdToRead(entryReader.InitialStreamPosition + uintData[0], IfdNames.Exif);
                                        break;
                                }
                                break;
                            case IfdNames.Exif:
                                switch (entryReader.CurrentEntryId)
                                {
                                    case 0x0112: // Orientation
                                        if (entryReader.TryGetEntryData(out ushortData) && ushortData is not null && ushortData.Length > 0)
                                            orientation = ushortData[0];
                                        break;
                                }
                                break;
                        }
                        metadata.SetEntry(entryReader);
                    }
                    if (!metadata.IsEmpty)
                        exifMetadata = metadata;
                    continue;
                }

                // parse XMP which is placed after its identifier
                if (xmpMetadata is null
                    && segmentDataBuffer.Length > XmpIdentifier.Length
                    && Encoding.ASCII.GetString(segmentDataBuffer, 0, XmpIdentifier.Length) == XmpIdentifier
                    && XmpMediaMetadata.TryCreate(segmentDataBuffer, XmpIdentifier.Length, segmentDataBuffer.Length - XmpIdentifier.Length, out var parsedXmpMetadata))
                {
                    xmpMetadata = parsedXmpMetadata;
                }
            }
        }, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();

        // setup profile
        Tiff.FromTiffOrientation(orientation, out var rotation, out var flipX, out var flipY);
        profile.Orientation = rotation;
        profile.FlipX = flipX;
        profile.FlipY = flipY;
        if (exifMetadata is not null || xmpMetadata is not null)
            profile.MediaMetadata = new JpegCompoundMediaMetadata(exifMetadata, xmpMetadata);
    }


    /// <inheritdoc/>
    protected override byte[]? OnReadIccProfileToMemory(Stream stream)
    {
        // read the profile size from the ICC profile header (the stream is positioned at the start of the profile by SeekToIccProfile, which accepts single-segment profiles only)
        var header = new byte[4];
        if (stream.Read(header, 0, 4) < 4)
            return null;
        var profileSize = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (profileSize < 128 || profileSize >= 1L << 20)
            return null;

        // read the whole profile (embedded as a single APP2 segment)
        var profile = new byte[profileSize];
        header.CopyTo(profile.AsSpan());
        return stream.Read(profile, 4, (int)profileSize - 4) == (int)profileSize - 4
            ? profile
            : null;
    }


    /// <inheritdoc/>
    protected override bool OnSeekToIccProfile(Stream stream) =>
        SeekToIccProfile(stream);


    /// <summary>
    /// Seek to embedded ICC profile.
    /// </summary>
    /// <param name="stream">Stream to read JPEG image.</param>
    /// <returns>True if seeking successfully.</returns>
    public static bool SeekToIccProfile(Stream stream)
    {
        // skip file header
        var segmentHeaderBuffer = new byte[4];
        stream.Seek(2, SeekOrigin.Current);

        // find ICC profile in APP segment
        while (true)
        {
            if (stream.Read(segmentHeaderBuffer, 0, 4) < 4)
                return false;
            if (segmentHeaderBuffer[0] != 0xff)
                return false;
            if (segmentHeaderBuffer[1] == 0xda) // SOS
                return false;
            var segmentSize = BinaryPrimitives.ReadUInt16BigEndian(segmentHeaderBuffer.AsSpan(2));
            if (segmentSize < 2)
                return false;
            if ((segmentHeaderBuffer[1] == 0xe1 || segmentHeaderBuffer[1] == 0xe2) // APP1 or APP2
                && segmentSize > 16)
            {
                var segmentDataBuffer = new byte[segmentSize - 2];
                if (stream.Read(segmentDataBuffer, 0, segmentDataBuffer.Length) < segmentDataBuffer.Length)
                    return false;
                if (Encoding.ASCII.GetString(segmentDataBuffer, 0, 11) == "ICC_PROFILE" 
                    && segmentDataBuffer[11] == 0x0
                    && segmentDataBuffer[12] == 0x1
                    && segmentDataBuffer[13] == 0x1)
                {
                    stream.Seek(-segmentSize + 16, SeekOrigin.Current);
                    return true;
                }
            }
            else
                stream.Seek(segmentSize - 2, SeekOrigin.Current);
        }
    }
}