
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with L16 (BE) format.
	/// </summary>
	class L16BEImageRenderer : L16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="L16BEImageRenderer"/> instance.
		/// </summary>
		public L16BEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Luminance, "L16_BE", "L16 (BE)", new ImagePlaneDescriptor(2, 9, 16)), false)
		{ }
	}
}
