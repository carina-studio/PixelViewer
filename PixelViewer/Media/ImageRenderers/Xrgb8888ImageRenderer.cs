
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with XRGB_8888 format.
	/// </summary>
	class Xrgb8888ImageRenderer : BaseArgb8888ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Xrgb8888ImageRenderer"/> instance.
		/// </summary>
		public Xrgb8888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "XRGB_8888", new ImagePlaneDescriptor(4), new string[]{ "XRGB", "XRGB8888", "XRGB_8888", "XRGB32" }))
		{ }


		// Select ARGB components.
		protected override void SelectArgb(byte component1, byte component2, byte component3, byte component4, out byte a, out byte r, out byte g, out byte b)
		{
			a = 255;
			r = component2;
			g = component3;
			b = component4;
		}
	}
}
