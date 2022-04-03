using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio.Collections;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// <see cref="IFileFormatParser"/> to parse PNG file.
/// </summary>
class PngFileFormatParser : SkiaFileFormatParser
{
    /// <summary>
    /// Initialize new <see cref="PngFileFormatParser"/> instance.
    /// </summary>
    public PngFileFormatParser() : base(FileFormats.Png, SKEncodedImageFormat.Png, ImageRenderers.ImageRenderers.All.First(it => it is PngImageRenderer))
    { }


    /// <summary>
    /// Check whether header of file represents PNG or not.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if header represents PNG.</returns>
    public static bool CheckFileHeader(Stream stream)
    {
        var buffer = new byte[8];
        return stream.Read(buffer, 0, 8) == 8
            && buffer[0] == 0x89
            && buffer[1] == 0x50
            && buffer[2] == 0x4e
            && buffer[3] == 0x47
            && buffer[4] == 0x0d
            && buffer[5] == 0x0a
            && buffer[6] == 0x1a
            && buffer[7] == 0x0a;
    }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        CheckFileHeader(stream);
    

    /// <inheritdoc/>
    protected override byte[]? OnReadIccProfileToMemory(Stream stream)
    {
        using var compressedStream = new InflaterInputStream(stream)
        {
            IsStreamOwner = false,
        };
        var data = new List<byte>(256);
        var buffer = new byte[256];
        var count = compressedStream.Read(buffer, 0, buffer.Length);
        while (count > 0)
        {
            if (count < buffer.Length)
                data.AddRange(buffer.ToArray(0, count));
            else
                data.AddRange(buffer);
            count = compressedStream.Read(buffer, 0, buffer.Length);
        }
        return data.ToArray();
    }
    

    /// <inheritdoc/>
    protected override bool OnSeekToIccProfile(Stream stream) =>
        SeekToIccProfile(stream);


    /// <summary>
    /// Seek to embedded ICC profile.
    /// </summary>
    /// <param name="stream">Stream to read PNG image.</param>
    /// <returns>True if seeking successfully.</returns>
    public static bool SeekToIccProfile(Stream stream)
    {
        // skip file header
        var chunkHeaderBuffer = new byte[8];
        stream.Seek(8, SeekOrigin.Current);

        // find ICC profile in 'iCCP' chunk
        while (stream.Read(chunkHeaderBuffer, 0, 8) == 8)
        {
            var dataSize = BinaryPrimitives.ReadUInt32BigEndian(chunkHeaderBuffer.AsSpan());
            if (chunkHeaderBuffer[4] == 'i' 
                && chunkHeaderBuffer[5] == 'C'
                && chunkHeaderBuffer[6] == 'C'
                && chunkHeaderBuffer[7] == 'P'
                && dataSize > 0)
            {
                // skip profile name
                --dataSize;
                var n = stream.ReadByte();
                while (n > 0 && dataSize > 0)
                {
                    --dataSize;
                    n = stream.ReadByte();
                }
                if (n < 0 || dataSize < 2)
                    return false;
                
                // check compression method
                if (stream.ReadByte() != 0)
                    return false;
                
                // complete
                return true;
            }
            stream.Seek(dataSize + 4, SeekOrigin.Current);
        }
        return false;
    }
}