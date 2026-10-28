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
	/// Lossless JPEG (ITU-T T.81 process 14).
	/// </summary>
	LosslessJpeg,
}
