using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with GBRG bayer pattern format.
	/// </summary>
	class Gbrg16ImageRenderer : BayerPattern16ImageRenderer
	{

		/// <summary>
		/// Initialize new <see cref="Gbrg16ImageRenderer"/> instance.
		/// </summary>
		public Gbrg16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "GBRG_16", true, new ImagePlaneDescriptor(2, 9, 16)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => GbrgColorPattern[y & 0x1][x & 0x1];
	}
}
