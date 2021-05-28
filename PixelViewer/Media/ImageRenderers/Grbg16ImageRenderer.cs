using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> to render image with GRBG bayer pattern format.
	/// </summary>
	abstract class Grbg16ImageRenderer : BayerPattern16ImageRenderer
	{
		// Static fields.
		static readonly ColorComponent[][] ColorPattern = new ColorComponent[][]{
			new ColorComponent[]{ ColorComponent.Green, ColorComponent.Red },
			new ColorComponent[]{ ColorComponent.Blue, ColorComponent.Green },
		};


		/// <summary>
		/// Initialize new <see cref="Grbg16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		/// <param name="isLittleEndian">True to use little-endian for byte ordering.</param>
		protected Grbg16ImageRenderer(ImageFormat format, bool isLittleEndian) : base(format, isLittleEndian)
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => ColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with GRBG_16 (BE) bayer pattern format.
	/// </summary>
	class Grbg16BEImageRenderer : Grbg16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Grbg16BEImageRenderer"/> instance.
		/// </summary>
		public Grbg16BEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "GRBG_16_BE", "GRBG_16 (BE)", new ImagePlaneDescriptor(2, 9, 16)), false)
		{ }
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with GRBG_16 (LE) bayer pattern format.
	/// </summary>
	class Grbg16LEImageRenderer : Grbg16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Grbg16LEImageRenderer"/> instance.
		/// </summary>
		public Grbg16LEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "GRBG_16_LE", "GRBG_16 (LE)", new ImagePlaneDescriptor(2, 9, 16)), true)
		{ }
	}
}
