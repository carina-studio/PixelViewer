
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with UYVY format.
	/// </summary>
	class UyvyImageRenderer : SinglePlaneYuv422ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="UyvyImageRenderer"/> instance.
		/// </summary>
		public UyvyImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "UYVY", "UYVY (YUV422)", new ImagePlaneDescriptor(4)))
		{ }


		// Select YUV component.
		protected override void SelectYuv(byte byte1, byte byte2, byte byte3, byte byte4, out byte y1, out byte y2, out byte u, out byte v)
		{
			y1 = byte2;
			y2 = byte4;
			u = byte1;
			v = byte3;
		}
	}
}
