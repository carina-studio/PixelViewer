
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with RGB_888 format.
	/// </summary>
	class Rgb888ImageRenderer : BaseRgb888ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Rgb888ImageRenderer"/> instance.
		/// </summary>
		public Rgb888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "RGB_888", new ImagePlaneDescriptor(3), new string[]{ "RGB", "RGB888", "RGB_888", "RGB24" }))
		{ }


		// Select ARGB components.
		protected override void SelectRgb(byte component1, byte component2, byte component3, out byte r, out byte g, out byte b)
		{
			r = component1;
			g = component2;
			b = component3;
		}
	}
}
