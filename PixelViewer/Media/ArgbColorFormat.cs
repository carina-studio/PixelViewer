namespace Carina.PixelViewer.Media;

/// <summary>
/// Format for displaying ARGB color values.
/// </summary>
public enum ArgbColorFormat
{
	/// <summary>
	/// Display each channel as an integer scaled to the source image's effective bit depth.
	/// </summary>
	Default,
	/// <summary>
	/// Display each channel as an integer in the fixed 8-bit range (0-255).
	/// </summary>
	Fixed8Bit,
	/// <summary>
	/// Display each channel as a real number normalized to the [0.0, 1.0] range.
	/// </summary>
	Normalized,
}
