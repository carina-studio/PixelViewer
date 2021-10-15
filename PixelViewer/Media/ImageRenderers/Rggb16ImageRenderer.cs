using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> to render image with RGGB bayer pattern format.
	/// </summary>
	class Rggb16ImageRenderer : BayerPattern16ImageRenderer
	{
		// Static fields.
		static readonly ColorComponent[][] ColorPattern = new ColorComponent[][]{
			new ColorComponent[]{ ColorComponent.Red, ColorComponent.Green },
			new ColorComponent[]{ ColorComponent.Green, ColorComponent.Blue },
		};


		/// <summary>
		/// Initialize new <see cref="Rggb16ImageRenderer"/> instance.
		/// </summary>
		public Rggb16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "RGGB_16", true, new ImagePlaneDescriptor(2, 9, 16)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => ColorPattern[y & 0x1][x & 0x1];
	}
}
