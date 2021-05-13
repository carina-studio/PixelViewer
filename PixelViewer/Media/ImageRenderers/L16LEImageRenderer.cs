
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with L16 (LE) format.
	/// </summary>
	class L16LEImageRenderer : L16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="L16LEImageRenderer"/> instance.
		/// </summary>
		public L16LEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Luminance, "L16_LE", "L16 (LE)", new ImagePlaneDescriptor(2, 9, 16)), true)
		{ }
	}
}
