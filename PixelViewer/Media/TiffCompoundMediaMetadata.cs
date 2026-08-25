namespace Carina.PixelViewer.Media;

/// <summary>
/// Metadata of media which is parsed from the different sources in a TIFF-based file, such as a TIFF file or a DNG file.
/// </summary>
/// <param name="ifd">Metadata parsed from the IFDs in the file.</param>
/// <param name="xmp">Metadata parsed from the XMP data in the file.</param>
class TiffCompoundMediaMetadata(TiffMediaMetadata? ifd, XmpMediaMetadata? xmp) : CompoundMediaMetadata(ifd, xmp);
