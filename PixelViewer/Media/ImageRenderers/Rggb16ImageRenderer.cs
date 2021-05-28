using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> to render image with RGGB bayer pattern format.
	/// </summary>
	abstract class Rggb16ImageRenderer : BayerPattern16ImageRenderer
	{
		// Static fields.
		static readonly ColorComponent[][] ColorPattern = new ColorComponent[][]{
			new ColorComponent[]{ ColorComponent.Red, ColorComponent.Green },
			new ColorComponent[]{ ColorComponent.Green, ColorComponent.Blue },
		};


		/// <summary>
		/// Initialize new <see cref="Rggb16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		/// <param name="isLittleEndian">True to use little-endian for byte ordering.</param>
		protected Rggb16ImageRenderer(ImageFormat format, bool isLittleEndian) : base(format, isLittleEndian)
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => ColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with RGGB_16 (BE) bayer pattern format.
	/// </summary>
	class Rggb16BEImageRenderer : Rggb16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Rggb16BEImageRenderer"/> instance.
		/// </summary>
		public Rggb16BEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "RGGB_16_BE", "RGGB_16 (BE)", new ImagePlaneDescriptor(2, 9, 16)), false)
		{ }
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with RGGB_16 (LE) bayer pattern format.
	/// </summary>
	class Rggb16LEImageRenderer : Rggb16ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Rggb16BEImageRenderer"/> instance.
		/// </summary>
		public Rggb16LEImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "RGGB_16_LE", "RGGB_16 (LE)", new ImagePlaneDescriptor(2, 9, 16)), true)
		{ }
	}
}
