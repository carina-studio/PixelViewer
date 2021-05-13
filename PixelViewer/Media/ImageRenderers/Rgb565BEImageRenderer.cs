
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with RGB_565 (BE) format.
	/// </summary>
	class Rgb565BEImageRenderer : Rgb565ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Rgb565BEImageRenderer"/> instance.
		/// </summary>
		public Rgb565BEImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "RGB_565_BE", "RGB_565 (BE)", new ImagePlaneDescriptor(2)), false)
		{ }
	}
}
