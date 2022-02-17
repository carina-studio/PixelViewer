
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with YV12 format.
	/// </summary>
	class YV12ImageRenderer : BaseYuv420pImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="YV12ImageRenderer"/> instance.
		/// </summary>
		public YV12ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "YV12", new ImagePlaneDescriptor[] {
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
		}, new string[]{ "YV12" }))
		{ }


		// Select UV component.
		protected override void SelectUV(byte uv1, byte uv2, out byte u, out byte v)
		{
			u = uv2;
			v = uv1;
		}
	}
}
