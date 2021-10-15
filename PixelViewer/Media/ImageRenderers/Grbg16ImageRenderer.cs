using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to render image with GRBG bayer pattern format.
	/// </summary>
	class Grbg16ImageRenderer : BayerPattern16ImageRenderer
	{
		// Static fields.
		static readonly ColorComponent[][] ColorPattern = new ColorComponent[][]{
			new ColorComponent[]{ ColorComponent.Green, ColorComponent.Red },
			new ColorComponent[]{ ColorComponent.Blue, ColorComponent.Green },
		};


		/// <summary>
		/// Initialize new <see cref="Grbg16ImageRenderer"/> instance.
		/// </summary>
		public Grbg16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "GRBG_16", true, new ImagePlaneDescriptor(2, 9, 16)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => ColorPattern[y & 0x1][x & 0x1];
	}
}
