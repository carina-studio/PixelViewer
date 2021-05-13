
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ABGR_8888 format.
	/// </summary>
	class Abgr8888ImageRenderer : BaseArgb8888ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Abgr8888ImageRenderer"/> instance.
		/// </summary>
		public Abgr8888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "ABGR_8888", new ImagePlaneDescriptor(4)))
		{ }


		// Select ARGB components.
		protected override void SelectArgb(byte component1, byte component2, byte component3, byte component4, out byte a, out byte r, out byte g, out byte b)
		{
			a = component1;
			r = component4;
			g = component3;
			b = component2;
		}
	}
}
