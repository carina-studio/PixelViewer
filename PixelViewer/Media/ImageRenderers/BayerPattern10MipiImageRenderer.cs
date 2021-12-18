using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> which renders image with 10-bit Bayer Filter MIPI RAW.
	/// </summary>
	class BayerPattern10MipiImageRenderer : BayerPatternImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="BayerPattern10MipiImageRenderer"/> instance.
		/// </summary>
		public BayerPattern10MipiImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "Bayer_Pattern_10_MIPI", true, new ImagePlaneDescriptor(0, 10, 10)))
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


		/// <inheritdoc/>
		protected override unsafe void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, int> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
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
							bitmapPixelPtr[colorComponentSelector(x, y)] = bitsCombinationFunc(packedPixelsPtr[0], bytes4);
							bitmapPixelPtr[3] = 65535;
							bitmapPixelPtr += 4;
							++x;
							bytes4 >>= 2;

							// 2nd pixel
							bitmapPixelPtr[colorComponentSelector(x, y)] = bitsCombinationFunc(packedPixelsPtr[1], bytes4);
							bitmapPixelPtr[3] = 65535;
							bitmapPixelPtr += 4;
							++x;
							bytes4 >>= 2;

							// 3rd pixel
							bitmapPixelPtr[colorComponentSelector(x, y)] = bitsCombinationFunc(packedPixelsPtr[2], bytes4);
							bitmapPixelPtr[3] = 65535;
							bitmapPixelPtr += 4;
							++x;
							bytes4 >>= 2;

							// 4th pixel
							bitmapPixelPtr[colorComponentSelector(x, y)] = bitsCombinationFunc(packedPixelsPtr[3], bytes4);
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
}
