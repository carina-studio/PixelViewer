using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> to render image with BGGR bayer pattern format.
	/// </summary>
	abstract class Bggr16ImageRenderer : BayerPattern16ImageRenderer
	{
		// Static fields.
		static readonly ColorComponent[][] ColorPattern = new ColorComponent[][]{
			new ColorComponent[]{ ColorComponent.Blue, ColorComponent.Green },
			new ColorComponent[]{ ColorComponent.Green, ColorComponent.Red },
		};


		/// <summary>
		/// Initialize new <see cref="Bggr16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		/// <param name="isLittleEndian">True to use little-endian for byte ordering.</param>
		protected Bggr16ImageRenderer(ImageFormat format, bool isLittleEndian) : base(format, isLittleEndian)
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => ColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with BGGR_16 (BE) bayer pattern format.
	/// </summary>
	class Bggr16BEImageRenderer : Bggr16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Bggr16BEImageRenderer"/> instance.
		/// </summary>
		public Bggr16BEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "BGGR_16_BE", "BGGR_16 (BE)", new ImagePlaneDescriptor(2, 9, 16)), false)
		{ }
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with BGGR_16 (LE) bayer pattern format.
	/// </summary>
	class Bggr16LEImageRenderer : Bggr16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Bggr16LEImageRenderer"/> instance.
		/// </summary>
		public Bggr16LEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "BGGR_16_LE", "BGGR_16 (LE)", new ImagePlaneDescriptor(2, 9, 16)), true)
		{ }
	}
}
