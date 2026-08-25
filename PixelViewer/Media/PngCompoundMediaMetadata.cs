namespace Carina.PixelViewer.Media;

/// <summary>
/// Metadata of media which is parsed from the different sources in a PNG file.
/// </summary>
/// <param name="exif">Metadata parsed from the Exif data in the file.</param>
/// <param name="xmp">Metadata parsed from the XMP data in the file.</param>
class PngCompoundMediaMetadata(TiffMediaMetadata? exif, XmpMediaMetadata? xmp) : CompoundMediaMetadata(exif, xmp);
