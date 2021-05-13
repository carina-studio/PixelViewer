
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with NV21 format.
	/// </summary>
	class NV21ImageRenderer : BaseYuv420spImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="NV21ImageRenderer"/> instance.
		/// </summary>
		public NV21ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "NV21", "NV21 (YUV420sp)", new ImagePlaneDescriptor[] { 
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(2),
		}))
		{ }


		// Select UV component.
		protected override void SelectUV(byte uv1, byte uv2, out byte u, out byte v)
		{
			u = uv2;
			v = uv1;
		}
	}
}
