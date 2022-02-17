
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ARGB_8888 format.
	/// </summary>
	class Argb8888ImageRenderer : BaseArgb8888ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Argb8888ImageRenderer"/> instance.
		/// </summary>
		public Argb8888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "ARGB_8888", new ImagePlaneDescriptor(4), new string[]{ "ARGB", "ARGB8888", "ARGB_8888", "ARGB32" }))
		{ }


		// Select ARGB components.
		protected override void SelectArgb(byte component1, byte component2, byte component3, byte component4, out byte a, out byte r, out byte g, out byte b)
		{
			a = component1;
			r = component2;
			g = component3;
			b = component4;
		}
	}
}
