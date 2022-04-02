using Carina.PixelViewer.Media.ImageRenderers;
using ImageMagick;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        return stream.Read(buffer, 0, 24) == 24
            && buffer[4] == 'f'
            && buffer[5] == 't'
            && buffer[6] == 'y'
            && buffer[7] == 'p'
            && buffer[8] == 'h'
            && buffer[9] == 'e'
            && buffer[10] == 'i'
            && buffer[11] == 'c';
    }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        CheckFileHeader(stream);

    
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