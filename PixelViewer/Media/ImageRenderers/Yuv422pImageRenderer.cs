
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with YUV422p format.
	/// </summary>
	class Yuv422pImageRenderer : BaseYuv422pImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Yuv422pImageRenderer"/> instance.
		/// </summary>
		public Yuv422pImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "YUV422p", new ImagePlaneDescriptor[] {
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
		}, new string[]{ "YUV422p" }))
		{ }


		// Select UV component.
		protected override void SelectUV(byte uv1, byte uv2, out byte u, out byte v)
		{
			u = uv1;
			v = uv2;
		}
	}
}
