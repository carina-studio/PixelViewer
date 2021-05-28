using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> to render image with GBRG bayer pattern format.
	/// </summary>
	abstract class Gbrg16ImageRenderer : BayerPattern16ImageRenderer
	{
		// Static fields.
		static readonly ColorComponent[][] ColorPattern = new ColorComponent[][]{
			new ColorComponent[]{ ColorComponent.Green, ColorComponent.Blue },
			new ColorComponent[]{ ColorComponent.Red, ColorComponent.Green },
		};


		/// <summary>
		/// Initialize new <see cref="Gbrg16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		/// <param name="isLittleEndian">True to use little-endian for byte ordering.</param>
		protected Gbrg16ImageRenderer(ImageFormat format, bool isLittleEndian) : base(format, isLittleEndian)
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => ColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with GBRG_16 (BE) bayer pattern format.
	/// </summary>
	class Gbrg16BEImageRenderer : Gbrg16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Gbrg16BEImageRenderer"/> instance.
		/// </summary>
		public Gbrg16BEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "GBRG_16_BE", "GBRG_16 (BE)", new ImagePlaneDescriptor(2, 9, 16)), false)
		{ }
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with GBRG_16 (LE) bayer pattern format.
	/// </summary>
	class Gbrg16LEImageRenderer : Gbrg16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Gbrg16LEImageRenderer"/> instance.
		/// </summary>
		public Gbrg16LEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "GBRG_16_LE", "GBRG_16 (LE)", new ImagePlaneDescriptor(2, 9, 16)), true)
		{ }
	}
}
