
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with RGBX_8888 format.
	/// </summary>
	class Rgbx8888ImageRenderer : BaseArgb8888ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Rgbx8888ImageRenderer"/> instance.
		/// </summary>
		public Rgbx8888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "RGBX_8888", new ImagePlaneDescriptor(4)))
		{ }


		// Select ARGB components.
		protected override void SelectArgb(byte component1, byte component2, byte component3, byte component4, out byte a, out byte r, out byte g, out byte b)
		{
			a = 255;
			r = component1;
			g = component2;
			b = component3;
		}
	}
}
