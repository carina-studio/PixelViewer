using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
		public BayerPattern10MipiImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "Bayer_Pattern_10_MIPI", true, new ImagePlaneDescriptor(0, 10, 10), new string[]{ "RAW10" }))
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
		protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, int> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
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
			var baseColorTransformationTable = (ushort*)NativeMemory.Alloc(65536 * sizeof(ushort) * 3);
			var partialMeanLut = (double*)null;
			var partialMean = stackalloc double[] { 0, 0, 0 };
			try
			{
				ushort** colorTransformationTables = stackalloc ushort*[3] {
					baseColorTransformationTable,
					baseColorTransformationTable + 65536,
					baseColorTransformationTable + 131072,
				};
				partialMeanLut = (double*)NativeMemory.Alloc(65535 * sizeof(double));
				BuildColorTransformationTableUnsafe(colorTransformationTables[0], ImageRenderingOptions.GetValidRgbGain(renderingOptions.BlueGain));
				BuildColorTransformationTableUnsafe(colorTransformationTables[1], ImageRenderingOptions.GetValidRgbGain(renderingOptions.GreenGain));
				BuildColorTransformationTableUnsafe(colorTransformationTables[2], ImageRenderingOptions.GetValidRgbGain(renderingOptions.RedGain));
				BuildColorTransformationTableUnsafe(partialMeanLut, 1.0 / (width * height));
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
								var colorComponent = colorComponentSelector(x, y);
								var color = bitsCombinationFunc(packedPixelsPtr[0], bytes4);
								partialMean[colorComponent] += partialMeanLut[color];
								bitmapPixelPtr[colorComponent] = colorTransformationTables[colorComponent][color];
								bitmapPixelPtr[3] = 65535;
								bitmapPixelPtr += 4;
								++x;
								bytes4 >>= 2;

								// 2nd pixel
								colorComponent = colorComponentSelector(x, y);
								color = bitsCombinationFunc(packedPixelsPtr[1], bytes4);
								partialMean[colorComponent] += partialMeanLut[color];
								bitmapPixelPtr[colorComponent] = colorTransformationTables[colorComponent][color];
								bitmapPixelPtr[3] = 65535;
								bitmapPixelPtr += 4;
								++x;
								bytes4 >>= 2;

								// 3rd pixel
								colorComponent = colorComponentSelector(x, y);
								color = bitsCombinationFunc(packedPixelsPtr[2], bytes4);
								partialMean[colorComponent] += partialMeanLut[color];
								bitmapPixelPtr[colorComponent] = colorTransformationTables[colorComponent][color];
								bitmapPixelPtr[3] = 65535;
								bitmapPixelPtr += 4;
								++x;
								bytes4 >>= 2;

								// 4th pixel
								colorComponent = colorComponentSelector(x, y);
								color = bitsCombinationFunc(packedPixelsPtr[3], bytes4);
								partialMean[colorComponent] += partialMeanLut[color];
								bitmapPixelPtr[colorComponent] = colorTransformationTables[colorComponent][color];
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
			finally
			{
				NativeMemory.Free(partialMeanLut);
				NativeMemory.Free(baseColorTransformationTable);
			}

			// complete
			return new ImageRenderingResult()
			{
				MeanOfBlue = partialMean[BlueColorComponent],
				MeanOfGreen = partialMean[GreenColorComponent],
				MeanOfRed = partialMean[RedColorComponent],
			};
		}
	}
}
