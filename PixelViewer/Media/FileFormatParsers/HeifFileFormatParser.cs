using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using ImageMagick;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// <see cref="IFileFormatParser"/> to parse HEIF file.
/// </summary>
class HeifFileFormatParser : MagickFileFormatParser
{
    /// <summary>
    /// Initialize new <see cref="HeifFileFormatParser"/> instance.
    /// </summary>
    public HeifFileFormatParser() : base(FileFormats.Heif, new[] { MagickFormat.Heic, MagickFormat.Heif }, ImageRenderers.ImageRenderers.All.First(it => it is HeifImageRenderer))
    { }


    /// <summary>
    /// Check whether header of file represents HEIF or not.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if header represents HEIF.</returns>
    public static bool CheckFileHeader(Stream stream)
    {
        var buffer = new byte[24];
        if (stream.Read(buffer, 0, 24) < 24
            || buffer[4] != 'f'
            || buffer[5] != 't'
            || buffer[6] != 'y'
            || buffer[7] != 'p')
        {
            return false;
        }
        if (buffer[8] == 'h'
            && buffer[9] == 'e'
            && buffer[10] == 'i'
            && buffer[11] == 'c')
        {
            return true;
        }
        if (buffer[8] == 'm'
            && buffer[9] == 'i'
            && buffer[10] == 'f'
            && buffer[11] == '1')
        {
            return true;
        }
        if (buffer[8] == 'h'
            && buffer[9] == 'e'
            && buffer[10] == 'i'
            && buffer[11] == 'x')
        {
            return true;
        }
        return false;
    }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        CheckFileHeader(stream);


    /// <inheritdoc/>
    protected override async Task OnParseExtraInformationAsync(Stream stream, ImageRenderingProfile profile, CancellationToken cancellationToken)
    {
        // parse extra information
        var orientation = 0;
        await Task.Run(() =>
        {
            var reader = new IsoBaseMediaFileReader(stream);
            var exifItemIndex = 0;
            var exifDataOffset = 0u;
            var startPosition = stream.Position;
            while (reader.Read())
            {
                if (reader.CurrentBoxType == 0x6d657461u) // 'meta'
                {
                    var metaBoxReader = reader.GetCurrentBoxDataReader(4);
                    while (metaBoxReader.Read())
                    {
                        if (metaBoxReader.CurrentBoxType == 0x69696e66u) // 'iinf'
                        {
                            var iinfBoxReader = metaBoxReader.GetCurrentBoxDataReader(6);
                            var itemIndex = 1;
                            while (iinfBoxReader.Read())
                            {
                                if (iinfBoxReader.CurrentBoxType == 0x696e6665u) // 'infe'
                                {
                                    var data = iinfBoxReader.GetCurrentBoxData();
                                    if (data.Length > 12
                                        && data[8] == 'E'
                                        && data[9] == 'x'
                                        && data[10] == 'i'
                                        && data[11] == 'f')
                                    {
                                        exifItemIndex = itemIndex;
                                    }
                                    ++itemIndex;
                                }
                            }
                            if (exifItemIndex == 0)
                                return;
                        }
                        else if (metaBoxReader.CurrentBoxType == 0x696c6f63u) // 'iloc'
                        {
                            if (exifItemIndex == 0)
                                return;
                            var ilocData = metaBoxReader.GetCurrentBoxData();
                            var offset = 15 + ((exifItemIndex - 1) * 16);
                            if (offset >= ilocData.Length + 9)
                                return;
                            if (ilocData[offset] != 0x1) // exif must be stored in 'mdat' box
                                return;
                            exifDataOffset = BinaryPrimitives.ReadUInt32BigEndian(ilocData.Slice(offset + 1));
                        }
                    }
                    if (exifItemIndex == 0 || exifDataOffset == 0)
                        return;
                }
                else if (reader.CurrentBoxType == 0x6d646174u) // 'mdat'
                {
                    // check header
                    if (exifDataOffset == 0)
                        return;
                    stream.Seek(startPosition + exifDataOffset, SeekOrigin.Begin);
                    var buffer = new byte[10];
                    if (stream.Read(buffer, 0, 10) < 0
                        || buffer[4] != 'E'
                        || buffer[5] != 'x'
                        || buffer[6] != 'i'
                        || buffer[7] != 'f'
                        || buffer[8] != 0x0
                        || buffer[9] != 0x0)
                    {
                        return;
                    }

                    // parse ifd entries (currently there is no need to parse orientation because ImageMagick will handle it)
                    var entryReader = new IfdEntryReader(stream);
                    /*
                    entryReader.ReadEntries(() =>
                    {
                        var ushortData = (ushort[]?)null;
                        var uintData = (uint[]?)null;
                        switch (entryReader.CurrentIfdName)
                        {
                            case IfdNames.Default:
                                switch (entryReader.CurrentEntryId)
                                {
                                    case 0x0112: // Orientation
                                        if (entryReader.TryGetEntryData(out ushortData) && ushortData != null && ushortData.Length > 0)
                                            orientation = ushortData[0];
                                        break;
                                    case 0x8769: // ExifOffset
                                        if (entryReader.TryGetEntryData(out uintData) && uintData != null && uintData.Length > 0)
                                            entryReader.EnqueueIfdToRead(uintData[0], IfdNames.Exif);
                                        break;
                                }
                                break;
                            case IfdNames.Exif:
                                switch (entryReader.CurrentEntryId)
                                {
                                    case 0x0112: // Orientation
                                        if (entryReader.TryGetEntryData(out ushortData) && ushortData != null && ushortData.Length > 0)
                                            orientation = ushortData[0];
                                        break;
                                }
                                break;
                        }
                        return true;
                    });
                    */
                }
            }
        });

        // update profile
        profile.Orientation = Tiff.FromTiffOrientation(orientation);
    }


    /// <inheritdoc/>
    protected override bool OnSeekToIccProfile(Stream stream) =>
        SeekToIccProfile(stream);
    

    /// <summary>
    /// Seek to embedded ICC profile.
    /// </summary>
    /// <param name="stream">Stream to read HEIF image.</param>
    /// <returns>True if seeking successfully.</returns>
    public static bool SeekToIccProfile(Stream stream)
    {
        var reader = new IsoBaseMediaFileReader(stream);
        var imagePropertyPositions = new List<long>();
        var primaryImageIndex = 0u;
        while (reader.Read())
        {
            if (reader.CurrentBoxType == 0x6d657461u) // 'meta'
            {
                var metaBoxReader = reader.GetCurrentBoxDataReader(4);
                while (metaBoxReader.Read())
                {
                    if (metaBoxReader.CurrentBoxType == 0x7069746du) // 'pitm'
                    {
                        var data = metaBoxReader.GetCurrentBoxData();
                        if (data.Length < 6)
                            break;
                        primaryImageIndex = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(2));
                    }
                    else if (metaBoxReader.CurrentBoxType == 0x69707270u) // 'iprp'
                    {
                        var iprpBoxReader = metaBoxReader.GetCurrentBoxDataReader();
                        while (iprpBoxReader.Read())
                        {
                            if (iprpBoxReader.CurrentBoxType == 0x6970636fu) // 'ipco'
                            {
                                var ipcoBoxReader = iprpBoxReader.GetCurrentBoxDataReader();
                                while (ipcoBoxReader.Read())
                                {
                                    if (ipcoBoxReader.CurrentBoxType == 0x636f6c72u) // 'colr'
                                    {
                                        var buffer = new byte[4];
                                        if (stream.Read(buffer, 0, 4) == 4
                                            && buffer[0] == 'p'
                                            && buffer[1] == 'r'
                                            && buffer[2] == 'o'
                                            && buffer[3] == 'f')
                                        {
                                            imagePropertyPositions.Add(stream.Position);
                                        }
                                        else
                                            imagePropertyPositions.Add(-1L);
                                    }
                                    else
                                        imagePropertyPositions.Add(-1L);
                                }
                            }
                            else if (iprpBoxReader.CurrentBoxType == 0x69706d61u) // 'ipma'
                            {
                                // check properties of primary image
                            }
                        }
                        break;
                    }
                }
                break;
            }
        }
        foreach (var position in imagePropertyPositions) // select first ICC profile
        {
            if (position >= 0L)
            {
                stream.Position = position;
                return true;
            }
        }
        return false;
    }
}