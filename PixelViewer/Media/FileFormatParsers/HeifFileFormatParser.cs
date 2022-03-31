using Carina.PixelViewer.Media.ImageRenderers;
using ImageMagick;
using System;
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
            && buffer[0] == 0x0
            && buffer[1] == 0x0
            && buffer[2] == 0x0
            && buffer[3] == 0x18
            && buffer[4] == 'f'
            && buffer[5] == 't'
            && buffer[6] == 'y'
            && buffer[7] == 'p'
            && buffer[20] == 'h'
            && buffer[21] == 'e'
            && buffer[22] == 'i'
            && buffer[23] == 'c';
    }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        CheckFileHeader(stream);
}