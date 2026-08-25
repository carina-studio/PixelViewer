using Carina.PixelViewer.Media.ImageRenderers;
using System;
using System.IO;
using System.Linq;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// <see cref="IFileFormatParser"/> to parse HEIF file.
/// </summary>
class MacOSHeifFileFormatParser : MacOSNativeFileFormatParser
{
    /// <summary>
    /// Initialize new <see cref="MacOSHeifFileFormatParser"/> instance.
    /// </summary>
    public MacOSHeifFileFormatParser() : base(FileFormats.Heif, ImageRenderers.ImageRenderers.All.First(it => it is MacOSHeifImageRenderer))
    { }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        HeifFileFormatParser.CheckFileHeader(stream);


    /// <inheritdoc/>
    protected override IMediaMetadata? OnParseMediaMetadata(Stream stream)
    {
        if (HeifFileFormatParser.SeekToExifData(stream) && TiffMediaMetadata.TryCreate(stream, out var exifMetadata))
            return new HeifCompoundMediaMetadata(exifMetadata, null);
        return null;
    }


    /// <inheritdoc/>
    protected override bool OnSeekToIccProfile(Stream stream) =>
        HeifFileFormatParser.SeekToIccProfile(stream);
}