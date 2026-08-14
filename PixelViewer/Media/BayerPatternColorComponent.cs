namespace Carina.PixelViewer.Media;

/// <summary>
/// Color component of pixel in Bayer Filter pattern.
/// </summary>
/// <remarks>The value of each component is the offset of its color channel in a BGRA pixel, so the component can be used to select the channel of pixel directly.</remarks>
public enum BayerPatternColorComponent
{
	/// <summary>
	/// Blue.
	/// </summary>
	Blue = 0,
	/// <summary>
	/// Green.
	/// </summary>
	Green = 1,
	/// <summary>
	/// Red.
	/// </summary>
	Red = 2,
}
