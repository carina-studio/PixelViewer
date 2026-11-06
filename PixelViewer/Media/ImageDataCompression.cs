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
	/// Data of ARW by Sony, which is tiles of lossless JPEG whose frames have 4 components with each pixel of frame carrying a 2x2 block of tile, or the lossy data compressed by Sony when not stored as tiles.
	/// </summary>
	Arw,
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
