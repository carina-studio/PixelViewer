
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with XBGR_8888 format.
	/// </summary>
	class Xbgr8888ImageRenderer : BaseArgb8888ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Xbgr8888ImageRenderer"/> instance.
		/// </summary>
		public Xbgr8888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "XBGR_8888", new ImagePlaneDescriptor(4)))
		{ }


		// Select ARGB components.
		protected override void SelectArgb(byte component1, byte component2, byte component3, byte component4, out byte a, out byte r, out byte g, out byte b)
		{
			a = 255;
			r = component4;
			g = component3;
			b = component2;
		}
	}
}
