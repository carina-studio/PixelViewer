
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with NV12 format.
	/// </summary>
	class NV12ImageRenderer : BaseYuv420spImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="NV21ImageRenderer"/> instance.
		/// </summary>
		public NV12ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "NV12", new ImagePlaneDescriptor[] {
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(2),
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
