
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with RGB_565 (LE) format.
	/// </summary>
	class Rgb565LEImageRenderer : Rgb565ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Rgb565LEImageRenderer"/> instance.
		/// </summary>
		public Rgb565LEImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "RGB_565_LE", "RGB_565 (LE)", new ImagePlaneDescriptor(2)), true)
		{ }
	}
}
