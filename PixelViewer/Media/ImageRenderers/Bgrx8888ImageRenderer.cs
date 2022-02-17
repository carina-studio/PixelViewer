
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with BGRX_8888 format.
	/// </summary>
	class Bgrx8888ImageRenderer : BaseArgb8888ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Bgrx8888ImageRenderer"/> instance.
		/// </summary>
		public Bgrx8888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "BGRX_8888", new ImagePlaneDescriptor(4), new string[]{ "BGRX", "BGRX8888", "BGRX_8888", "BGRX32" }))
		{ }


		// Select ARGB components.
		protected override void SelectArgb(byte component1, byte component2, byte component3, byte component4, out byte a, out byte r, out byte g, out byte b)
		{
			a = 255;
			r = component3;
			g = component2;
			b = component1;
		}
	}
}
