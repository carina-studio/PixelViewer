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
}