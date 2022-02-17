
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with YUV444p format.
	/// </summary>
	class Yuv444pImageRenderer : BaseYuv444pImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Yuv444pImageRenderer"/> instance.
		/// </summary>
		public Yuv444pImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "YUV444p", new ImagePlaneDescriptor[] {
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
		}, new string[]{ "YUV444p" }))
		{ }


		// Select UV component.
		protected override void SelectUV(byte uv1, byte uv2, out byte u, out byte v)
		{
			u = uv1;
			v = uv2;
		}
	}
}
