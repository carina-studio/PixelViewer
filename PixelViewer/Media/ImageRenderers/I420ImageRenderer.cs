
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with I420 format.
	/// </summary>
	class I420ImageRenderer : BaseYuv420pImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="YV12ImageRenderer"/> instance.
		/// </summary>
		public I420ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "I420", new ImagePlaneDescriptor[] { 
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
		}))
		{ }


		// Select UV component.
		protected override void SelectUV(byte uv1, byte uv2, out byte u, out byte v)
		{
			u = uv1;
			v = uv2;
		}
	}
}
