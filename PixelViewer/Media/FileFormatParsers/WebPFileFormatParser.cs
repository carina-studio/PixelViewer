using Carina.PixelViewer.Media.ImageRenderers;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.IO;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// <see cref="IFileFormatParser"/> to parse WebP file.
/// </summary>
class WebPFileFormatParser : SkiaFileFormatParser
{
    /// <summary>
    /// Initialize new <see cref="WebPFileFormatParser"/> instance.
    /// </summary>
    public WebPFileFormatParser() : base(FileFormats.WebP, SKEncodedImageFormat.Webp, ImageRenderers.ImageRenderers.All.First(it => it is WebPImageRenderer))
    { }


    /// <summary>
    /// Check whether header of file represents WebP or not.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if header represents WebP.</returns>
    public static bool CheckFileHeader(Stream stream)
    {
        var buffer = new byte[12];
        return stream.Read(buffer, 0, 12) == 12
            && buffer[0] == 'R'
            && buffer[1] == 'I'
            && buffer[2] == 'F'
            && buffer[3] == 'F'
            && buffer[8] == 'W'
            && buffer[9] == 'E'
            && buffer[10] == 'B'
            && buffer[11] == 'P';
    }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        CheckFileHeader(stream);


    /// <inheritdoc/>
    protected override byte[]? OnReadIccProfileToMemory(Stream stream)
    {
        // get data size
        var buffer = new byte[4];
        if (stream.Read(buffer, 0, 4) < 4)
            return null;
        
        // read ICC profile
        var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        if (dataSize >= 128L << 20) // 128 MB
            throw new NotSupportedException($"Unsupported size of ICC profile: {dataSize}.");
        buffer = new byte[dataSize];
        return stream.Read(buffer, 0, buffer.Length) == buffer.Length
            ? buffer
            : null;
    }


    /// <inheritdoc/>
    protected override bool OnSeekToIccProfile(Stream stream) =>
        SeekToIccProfile(stream);


    /// <summary>
    /// Seek to embedded ICC profile.
    /// </summary>
    /// <param name="stream">Stream to read WebP image.</param>
    /// <returns>True if seeking successfully.</returns>
    public static bool SeekToIccProfile(Stream stream)
    {
        // skip file header
        var chunkHeader = new byte[12];
        if (stream.Read(chunkHeader, 0, 12) < 12)
            return false;

        // seek to ICCP chunk
        while (true)
        {
            // read header of chunk
            if (stream.Read(chunkHeader, 0, 8) < 8)
                return false;
            if (chunkHeader[0] == 'I'
                && chunkHeader[1] == 'C'
                && chunkHeader[2] == 'C'
                && chunkHeader[3] == 'P')
            {
                stream.Seek(-4, SeekOrigin.Current);
                return true;
            }
            stream.Seek(BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4)), SeekOrigin.Current);
        }
    }
}