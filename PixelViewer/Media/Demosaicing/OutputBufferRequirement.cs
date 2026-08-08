namespace Carina.PixelViewer.Media.Demosaicing;

/// <summary>
/// Requirement of dedicated buffer to receive the result of demosaicing.
/// </summary>
enum OutputBufferRequirement
{
	/// <summary>
	/// Dedicated buffer is not needed, the same buffer can be used as both the source and the destination of demosaicing.
	/// </summary>
	NotRequired,
	/// <summary>
	/// The same buffer can be used as both the source and the destination of demosaicing, but demosaicing into a dedicated buffer produces a better result.
	/// </summary>
	Preferred,
	/// <summary>
	/// Dedicated buffer is necessary, using the same buffer as both the source and the destination of demosaicing is not supported.
	/// </summary>
	Required,
}
