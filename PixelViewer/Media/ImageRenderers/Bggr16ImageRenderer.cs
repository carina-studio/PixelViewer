using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with BGGR bayer pattern format.
	/// </summary>
	class Bggr16ImageRenderer : BayerPattern16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Bggr16ImageRenderer"/> instance.
		/// </summary>
		public Bggr16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "BGGR_16", true, new ImagePlaneDescriptor(2, 9, 16)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => BggrColorPattern[y & 0x1][x & 0x1];
	}
}
