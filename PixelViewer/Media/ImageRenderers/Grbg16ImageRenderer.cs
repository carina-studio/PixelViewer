using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with GRBG bayer pattern format.
	/// </summary>
	class Grbg16ImageRenderer : BayerPattern16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Grbg16ImageRenderer"/> instance.
		/// </summary>
		public Grbg16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "GRBG_16", true, new ImagePlaneDescriptor(2, 9, 16)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => GrbgColorPattern[y & 0x1][x & 0x1];
	}
}
