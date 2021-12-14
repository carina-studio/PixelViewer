using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// Base implementation of <see cref="IImageRenderer"/> which renders image with 12-bit MIPI RAW.
    /// </summary>
    abstract class MipiBayerPattern12ImageRenderer : BayerPatternImageRenderer
    {
        /// <summary>
		/// Initialize new <see cref="MipiBayerPattern12ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		protected MipiBayerPattern12ImageRenderer(ImageFormat format) : base(format)
        { }


		// Create default plane options.
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also((it) =>
		{
			width &= 0x7ffffffe;
			height &= 0x7ffffffe;
			it.Add(new ImagePlaneOptions(12, 0, (width >> 1) * 3));
		});


		/// <inheritdoc/>
		public override int EvaluatePixelCount(IImageDataSource source)
		{
			return (int)(source.Size / 3) << 1;
		}


		/// <inheritdoc/>
		public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
		{
			width &= 0x7ffffffe;
			height &= 0x7ffffffe;
			if (width <= 0 || height <= 0)
				return 0;
			var rowStride = Math.Max((width >> 1) * 3, planeOptions[0].RowStride);
			return rowStride * height;
		}


		// Render.
		protected override unsafe void OnRenderBayerPatternImage(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width & 0x7ffffffe;
			var height = bitmapBuffer.Height & 0x7ffffffe;
			var rowStride = planeOptions[0].RowStride;
			if ((width >> 1) * 3 > rowStride)
				throw new ArgumentException($"Invalid row stride: {rowStride}.");

			// prepare conversion
			var bitsCombinationFunc = renderingOptions.ByteOrdering == ByteOrdering.BigEndian
				? new Func<byte, byte, ushort>((b1, b2) => (ushort)(((b1 << 4) | (b2 & 0xf)) << 4))
				: new Func<byte, byte, ushort>((b1, b2) => (ushort)((b1 | ((b2 & 0xf) << 8)) << 4));

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
						for (var x = 0; x < width; packedPixelsPtr += 3)
						{
							// 1st pixel
							var bytes2 = packedPixelsPtr[2];
							bitmapPixelPtr[(int)this.SelectColorComponent(x, y)] = bitsCombinationFunc(packedPixelsPtr[0], bytes2);
							bitmapPixelPtr[3] = 65535;
							bitmapPixelPtr += 4;
							++x;
							bytes2 >>= 4;

							// 2nd pixel
							bitmapPixelPtr[(int)this.SelectColorComponent(x, y)] = bitsCombinationFunc(packedPixelsPtr[1], bytes2);
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
	/// <see cref="IImageRenderer"/> which renders image with 12-bit BGGR MIPI RAW.
	/// </summary>
	class BggrMipi12ImageRenderer : MipiBayerPattern12ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="BggrMipi12ImageRenderer"/> instance.
		/// </summary>
		public BggrMipi12ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "MIPI_BGGR_12", "BGGR_12 (MIPI)", true, new ImagePlaneDescriptor(0, 12, 12)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => BggrColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which renders image with 12-bit GBRG MIPI RAW.
	/// </summary>
	class GbrgMipi12ImageRenderer : MipiBayerPattern12ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="GbrgMipi12ImageRenderer"/> instance.
		/// </summary>
		public GbrgMipi12ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "MIPI_GBRG_12", "GBRG_12 (MIPI)", true, new ImagePlaneDescriptor(0, 12, 12)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => GbrgColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which renders image with 12-bit GRBG MIPI RAW.
	/// </summary>
	class GrbgMipi12ImageRenderer : MipiBayerPattern12ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="GrbgMipi12ImageRenderer"/> instance.
		/// </summary>
		public GrbgMipi12ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "MIPI_GRBG_12", "GRBG_12 (MIPI)", true, new ImagePlaneDescriptor(0, 12, 12)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => GrbgColorPattern[y & 0x1][x & 0x1];
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which renders image with 12-bit RGGB MIPI RAW.
	/// </summary>
	class RggbMipi12ImageRenderer : MipiBayerPattern12ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="RggbMipi12ImageRenderer"/> instance.
		/// </summary>
		public RggbMipi12ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "MIPI_RGGB_12", "RGGB_12 (MIPI)", true, new ImagePlaneDescriptor(0, 12, 12)))
		{ }


		// Select color component.
		protected override ColorComponent SelectColorComponent(int x, int y) => RggbColorPattern[y & 0x3][x & 0x3];
	}
}
