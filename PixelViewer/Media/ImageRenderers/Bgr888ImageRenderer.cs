
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with BGR_888 format.
	/// </summary>
	class Bgr888ImageRenderer : BaseRgb888ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Bgr888ImageRenderer"/> instance.
		/// </summary>
		public Bgr888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "BGR_888", new ImagePlaneDescriptor(3)))
		{ }


		// Select ARGB components.
		protected override void SelectRgb(byte component1, byte component2, byte component3, out byte r, out byte g, out byte b)
		{
			r = component3;
			g = component2;
			b = component1;
		}
	}
}
