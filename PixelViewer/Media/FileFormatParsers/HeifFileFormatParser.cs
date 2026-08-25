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
        // parse metadata (there is no need to parse orientation because ImageMagick will handle it)
        var orientation = 0;
        TiffMediaMetadata? exifMetadata = null;
        await Task.Run(() =>
        {
            if (SeekToExifData(stream) && TiffMediaMetadata.TryCreate(stream, out var parsedMetadata))
                exifMetadata = parsedMetadata;
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // update profile
        Tiff.FromTiffOrientation(orientation, out var rotation, out var flipX, out var flipY);
        profile.Orientation = rotation;
        profile.FlipX = flipX;
        profile.FlipY = flipY;
        if (exifMetadata is not null)
            profile.MediaMetadata = new HeifCompoundMediaMetadata(exifMetadata, null);
    }


    // Find the identifier of the item which keeps the Exif data, or 0 if there is no such item.
    static uint FindExifItemId(ReadOnlySpan<byte> iinfData)
    {
        // the number of entries is kept by a field whose size is defined by the version of box
        if (iinfData.Length < 6)
            return 0;
        var version = iinfData[0];
        var offset = version == 0 ? 6 : 8;

        // find the entry which describes the item of Exif data
        while (offset + 8 <= iinfData.Length)
        {
            var boxSize = BinaryPrimitives.ReadUInt32BigEndian(iinfData[offset..]);
            var boxType = BinaryPrimitives.ReadUInt32BigEndian(iinfData[(offset + 4)..]);
            if (boxSize < 8 || offset + boxSize > iinfData.Length)
                return 0;
            if (boxType == 0x696e6665u) // 'infe'
            {
                var entry = iinfData.Slice(offset + 8, (int)boxSize - 8);
                var entryVersion = entry.Length > 0 ? entry[0] : 0;
                var itemIdSize = entryVersion >= 3 ? 4 : 2;
                var itemTypeOffset = 4 + itemIdSize + 2;
                if (entry.Length >= itemTypeOffset + 4
                    && entry[itemTypeOffset] == 'E'
                    && entry[itemTypeOffset + 1] == 'x'
                    && entry[itemTypeOffset + 2] == 'i'
                    && entry[itemTypeOffset + 3] == 'f')
                {
                    return itemIdSize == 4
                        ? BinaryPrimitives.ReadUInt32BigEndian(entry[4..])
                        : BinaryPrimitives.ReadUInt16BigEndian(entry[4..]);
                }
            }
            offset += (int)boxSize;
        }
        return 0;
    }


    // Find the offset to the data of the item with the given identifier, or 0 if the offset cannot be found.
    static long FindItemDataOffset(ReadOnlySpan<byte> ilocData, uint itemId)
    {
        // the sizes of the fields which describe an item are defined by the header of box
        if (ilocData.Length < 8)
            return 0;
        var version = ilocData[0];
        var offsetSize = ilocData[4] >> 4;
        var lengthSize = ilocData[4] & 0xf;
        var baseOffsetSize = ilocData[5] >> 4;
        var indexSize = version == 1 || version == 2 ? (ilocData[5] & 0xf) : 0;
        var offset = 6;
        var itemCount = 0L;
        if (version < 2)
        {
            itemCount = BinaryPrimitives.ReadUInt16BigEndian(ilocData[offset..]);
            offset += 2;
        }
        else
        {
            if (ilocData.Length < 10)
                return 0;
            itemCount = BinaryPrimitives.ReadUInt32BigEndian(ilocData[offset..]);
            offset += 4;
        }

        // find the item and take the offset to the first extent of it
        for (var i = 0L; i < itemCount; ++i)
        {
            // read the identifier of item and how its data is constructed
            var itemIdSize = version < 2 ? 2 : 4;
            if (offset + itemIdSize + 2 > ilocData.Length)
                return 0;
            var currentItemId = itemIdSize == 2
                ? BinaryPrimitives.ReadUInt16BigEndian(ilocData[offset..])
                : BinaryPrimitives.ReadUInt32BigEndian(ilocData[offset..]);
            offset += itemIdSize;
            var constructionMethod = 0;
            if (version == 1 || version == 2)
            {
                constructionMethod = BinaryPrimitives.ReadUInt16BigEndian(ilocData[offset..]) & 0xf;
                offset += 2;
            }

            // read the base offset which every extent of the item is relative to
            offset += 2; // data_reference_index
            if (offset + baseOffsetSize + 2 > ilocData.Length)
                return 0;
            var baseOffset = ReadUIntBigEndian(ilocData[offset..], baseOffsetSize);
            offset += baseOffsetSize;
            var extentCount = BinaryPrimitives.ReadUInt16BigEndian(ilocData[offset..]);
            offset += 2;

            // read extents of the item, only the data which is placed in the file itself can be located
            var extentSize = indexSize + offsetSize + lengthSize;
            if (offset + (extentCount * extentSize) > ilocData.Length)
                return 0;
            if (currentItemId == itemId && extentCount > 0 && constructionMethod == 0)
                return baseOffset + ReadUIntBigEndian(ilocData[(offset + indexSize)..], offsetSize);
            offset += extentCount * extentSize;
        }
        return 0;
    }


    // Read an unsigned integer with the given size in bytes.
    static long ReadUIntBigEndian(ReadOnlySpan<byte> data, int size)
    {
        var value = 0L;
        for (var i = 0; i < size; ++i)
            value = (value << 8) | data[i];
        return value;
    }


    /// <summary>
    /// Seek to the Exif data which is kept by the file.
    /// </summary>
    /// <param name="stream">Stream to read HEIF image.</param>
    /// <returns>True if seeking successfully, the stream will be positioned at the header of TIFF-based data.</returns>
    public static bool SeekToExifData(Stream stream)
    {
        // find the item of Exif data and the offset to it
        var startPosition = stream.Position;
        var reader = new IsoBaseMediaFileReader(stream);
        var exifItemId = 0u;
        var exifDataOffset = 0L;
        try
        {
            while (reader.Read())
            {
                if (reader.CurrentBoxType != 0x6d657461u) // 'meta'
                    continue;
                var metaBoxReader = reader.GetCurrentBoxDataReader(4);
                while (metaBoxReader.Read())
                {
                    if (metaBoxReader.CurrentBoxType == 0x69696e66u) // 'iinf'
                        exifItemId = FindExifItemId(metaBoxReader.GetCurrentBoxData());
                    else if (metaBoxReader.CurrentBoxType == 0x696c6f63u && exifItemId != 0) // 'iloc'
                        exifDataOffset = FindItemDataOffset(metaBoxReader.GetCurrentBoxData(), exifItemId);
                }
                break;
            }
        }
        catch
        {
            return false;
        }
        if (exifDataOffset <= 0)
            return false;

        // seek to the header of TIFF-based data which is placed after the identifier of Exif data
        stream.Seek(startPosition + exifDataOffset, SeekOrigin.Begin);
        var buffer = new byte[10];
        return stream.Read(buffer, 0, 10) == 10
            && buffer[4] == 'E'
            && buffer[5] == 'x'
            && buffer[6] == 'i'
            && buffer[7] == 'f'
            && buffer[8] == 0x0
            && buffer[9] == 0x0;
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