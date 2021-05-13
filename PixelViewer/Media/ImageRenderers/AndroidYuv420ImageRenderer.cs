using System.Collections.Generic;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with Android YUV_420_888 format.
	/// </summary>
	class AndroidYuv420ImageRenderer : BaseYuv420pImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="AndroidYuv420ImageRenderer"/> instance.
		/// </summary>
		public AndroidYuv420ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "Android_YUV_420_888", "YUV_420_888 (Android)", new ImagePlaneDescriptor[] { 
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
			new ImagePlaneDescriptor(1),
		}), true)
		{ }


		// Create default plane options.
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also((it) =>
		{
			it.Add(new ImagePlaneOptions(1, width));
			it.Add(new ImagePlaneOptions(2, width));
			it.Add(new ImagePlaneOptions(2, width));
		});


		// Evaluate pixel count.
		public override int EvaluatePixelCount(IImageDataSource source) => (int)((source.Size + 2) / 2);


		// Select UV component.
		protected override void SelectUV(byte uv1, byte uv2, out byte u, out byte v)
		{
			u = uv1;
			v = uv2;
		}
	}
}
