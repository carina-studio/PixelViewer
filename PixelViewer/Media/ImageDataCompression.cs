namespace Carina.PixelViewer.Media;

/// <summary>
/// Compression of raw image data.
/// </summary>
enum ImageDataCompression
{
	/// <summary>
	/// Not compressed.
	/// </summary>
	None,
	/// <summary>
	/// Lossless JPEG data of CR2 by Canon, whose samples are laid out as vertical slices of image.
	/// </summary>
	Cr2,
	/// <summary>
	/// Data of CR3 by Canon compressed by CRX.
	/// </summary>
	Cr3,
	/// <summary>
	/// Lossless JPEG (ITU-T T.81 process 14).
	/// </summary>
	LosslessJpeg,
	/// <summary>
	/// Compression of NEF data by Nikon, either lossless or lossy.
	/// </summary>
	Nef,
}
