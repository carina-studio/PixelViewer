using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> which renders image with 10-bit MIPI RAW.
	/// </summary>
	abstract class MipiBayerPattern10ImageRenderer : BayerPatternImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="MipiBayerPattern10ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		protected MipiBayerPattern10ImageRenderer(ImageFormat format) : base(format)
		{ }


		// Create default plane options.
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also((it) =>
		{
			width &= 0x7ffffffc;
			height &= 0x7ffffffc;
			it.Add(new ImagePlaneOptions(10, 0, (width >> 2) * 5));
		});


		/// <inheritdoc/>
		public override int EvaluatePixelCount(IImageDataSource source)
        {
			return (int)(source.Size / 5) << 2;
        }


		/// <inheritdoc/>
        public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
        {
			width &= 0x7ffffffc;
			height &= 0x7ffffffc;
			if (width <= 0 || height <= 0)
				return 0;
			var rowStride = Math.Max((width >> 2) * 5, planeOptions[0].RowStride);
			return rowStride * height;
		}


        // Render.
        protected override unsafe void OnRenderBayerPatternImage(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width & 0x7ffffffc;
			var height = bitmapBuffer.Height & 0x7ffffffc;
			var rowStride = planeOptions[0].RowStride;
			if ((width >> 2) * 5 > rowStride)
				throw new ArgumentException($"Invalid row stride: {rowStride}.");

			// prepare conversion
			var bitsCombinationFunc = renderingOptions.ByteOrdering == ByteOrdering.BigEndian
				? new Func<byte, byte, ushort>((b1, b2) => (ushort)(((b1 << 2) | (b2 & 0x3)) << 6))
				: new Func<byte, byte, ushort>((b1, b2) => (ushort)((b1 | ((b2 & 0x3) << 8)) << 6));

			// render
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				// render to 16-bit R/G/B
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				var bitmapRowStride = bitmapBuffer.RowBytes;
				byte[] row = new byte[rowStride];
				fixed (byte* rowPtr = row)
				{
					for (var y = 0; y < height; ++y, bitmapRowPtr += bitmapRowStride)
					{
						imageStream.Read(row, 0, rowStride);
						var packedPixelsPtr = rowPtr;
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						for (var x = 0; x < width; packedPixelsPtr += 5)
						{
							// 1st pixel
							var bytes4 = packedPixelsPtr[4];
							bitmapPixelPtr[(int)this.SelectColorComponent(x, y)] = bitsCombinationFunc(packedPixelsPtr[0], bytes4);
							bitmapPixelPtr[3] = 65535;
							bitmapPixelPtr += 4;
							++x;
							bytes4 >>= 2;

							// 2nd pixel
							bitmapPixelPtr[(int)this.SelectColorComponent(x, y)] = bitsCombinationFunc(packedPixelsPtr[1], bytes4);
							bitmapPixelPtr[3] = 65535;
							bitmapPixelPtr += 4;
							++x;
							bytes4 >>= 2;

							// 3rd pixel
							bitmapPixelPtr[(int)this.SelectColorComponent(x, y)] = bitsCombinationFunc(packedPixelsPtr[2], bytes4);
							bitmapPixelPtr[3] = 65535;
							bitmapPixelPtr += 4;
							++x;
							bytes4 >>= 2;

							// 4th pixel
							bitmapPixelPtr[(int)this.SelectColorComponent(x, y)] = bitsCombinationFunc(packedPixelsPtr[3], bytes4);
							bitmapPixelPtr[3] = 65535;
							bitmapPixelPtr += 4;
							++x;
						}
						if (cancellationToken.IsCancellationRequested)
							break;
						if (y < height - 1)
							Array.Clear(row, 0, rowStride);
					}
				}
			});
		}
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which renders image with 10-bit BGGR MIPI RAW.
	/// </summary>
	class BggrMipi10ImageRenderer : MipiBayerPattern10ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="BggrMipi10ImageRenderer"/> instance.
		/// </summary>
		public BggrMipi10ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "MIPI_BGGR_10", "BGGR_10 (MIPI)", true, new ImagePlaneDescriptor(0, 10, 10)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => BggrColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which renders image with 10-bit GBRG MIPI RAW.
	/// </summary>
	class GbrgMipi10ImageRenderer : MipiBayerPattern10ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="GbrgMipi10ImageRenderer"/> instance.
		/// </summary>
		public GbrgMipi10ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "MIPI_GBRG_10", "GBRG_10 (MIPI)", true, new ImagePlaneDescriptor(0, 10, 10)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => GbrgColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which renders image with 10-bit GRBG MIPI RAW.
	/// </summary>
	class GrbgMipi10ImageRenderer : MipiBayerPattern10ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="GrbgMipi10ImageRenderer"/> instance.
		/// </summary>
		public GrbgMipi10ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "MIPI_GRBG_10", "GRBG_10 (MIPI)", true, new ImagePlaneDescriptor(0, 10, 10)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => GrbgColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which renders image with 10-bit RGGB MIPI RAW.
	/// </summary>
	class RggbMipi10ImageRenderer : MipiBayerPattern10ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="RggbMipi10ImageRenderer"/> instance.
		/// </summary>
		public RggbMipi10ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "MIPI_RGGB_10", "RGGB_10 (MIPI)", true, new ImagePlaneDescriptor(0, 10, 10)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => RggbColorPattern[y & 0x1][x & 0x1];
	}
}
