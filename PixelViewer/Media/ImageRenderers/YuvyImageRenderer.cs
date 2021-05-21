
namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with YUVY format.
	/// </summary>
	class YuvyImageRenderer : SinglePlaneYuv422ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="YuvyImageRenderer"/> instance.
		/// </summary>
		public YuvyImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "YUVY", "YUVY (YUV422)", new ImagePlaneDescriptor(4)))
		{ }


		// Select YUV component.
		protected override void SelectYuv(byte byte1, byte byte2, byte byte3, byte byte4, out byte y1, out byte y2, out byte u, out byte v)
		{
			y1 = byte1;
			y2 = byte4;
			u = byte2;
			v = byte3;
		}
	}
}
