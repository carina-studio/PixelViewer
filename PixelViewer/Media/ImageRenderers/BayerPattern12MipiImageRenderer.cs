﻿using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// Base implementation of <see cref="IImageRenderer"/> which renders image with 12-bit Bayer Filter MIPI RAW.
    /// </summary>
    class BayerPattern12MipiImageRenderer : BayerPatternImageRenderer
    {
        /// <summary>
		/// Initialize new <see cref="BayerPattern12MipiImageRenderer"/> instance.
		/// </summary>
		public BayerPattern12MipiImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "Bayer_Pattern_12_MIPI", true, new ImagePlaneDescriptor(0, 12, 12, true), new[]{ "RAW12" }))
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


		/// <inheritdoc/>
		protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, int> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width & 0x7ffffffe;
			var height = bitmapBuffer.Height & 0x7ffffffe;
			var rowStride = planeOptions[0].RowStride;
			if ((width >> 1) * 3 > rowStride)
				throw new ArgumentException($"Invalid row stride: {rowStride}.");

			// prepare conversion
			var blackLevel = planeOptions[0].BlackLevel.GetValueOrDefault();
			var whiteLevel = planeOptions[0].WhiteLevel ?? 4095;
			if (blackLevel >= whiteLevel || whiteLevel > 4095)
				throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");
			var bitsCombinationFunc = (blackLevel == 0 && whiteLevel == 4095)
				? Global.Run(() =>
				{
					return renderingOptions.ByteOrdering == ByteOrdering.BigEndian
						? new Func<byte, byte, ushort>((msb, lsb) => (ushort)((msb << 8) | ((lsb & 0xf) << 4) | ((msb >> 4) & 0xf)))
						: (lsb, msb) =>
						{
							msb &= 0xf;
							return (ushort)((msb << 12) | (lsb << 4) | msb);
						};
				})
				: Global.Run(() =>
				{
					var correctedColors = new ushort[4096].Also(it =>
					{
						var scale = 4095.0 / (whiteLevel - blackLevel);
						for (var i = whiteLevel; i > blackLevel; --i)
							it[i] = (ushort)((i - blackLevel) * scale + 0.5);
						for (var i = it.Length - 1; i > whiteLevel; --i)
							it[i] = 4095;
					});
					return renderingOptions.ByteOrdering == ByteOrdering.BigEndian
						? new Func<byte, byte, ushort>((msb, lsb) => 
						{
							var color = correctedColors[(msb << 4) | (lsb & 0xf)];
							return (ushort)(color << 4);
						})
						: (lsb, msb) => 
						{
							var color = correctedColors[lsb | ((msb & 0xf) << 8)];
							return (ushort)(color << 4);
						};
				});

			// render
			var baseColorTransformationTable = (ushort*)NativeMemory.Alloc(65536 * sizeof(ushort) * 3);
			var accuColor = stackalloc ulong[] { 0L, 0L, 0L };
			var accuPixelCount = stackalloc int[] { 0, 0, 0 };
			var wAccuColor = stackalloc ulong[] { 0L, 0L, 0L };
			var wAccuPixelCount = stackalloc int[] { 0, 0, 0 };
			var wLeft = width / 3;
			var wRight = width - wLeft;
			var wTop = height / 3;
			var wBottom = height - wTop;
			try
			{
				ushort** colorTransformationTables = stackalloc ushort*[3] {
					baseColorTransformationTable,
					baseColorTransformationTable + 65536,
					baseColorTransformationTable + 131072,
				};
				BuildColorTransformationTableUnsafe(colorTransformationTables[0], ImageRenderingOptions.GetValidRgbGain(renderingOptions.BlueGain));
				BuildColorTransformationTableUnsafe(colorTransformationTables[1], ImageRenderingOptions.GetValidRgbGain(renderingOptions.GreenGain));
				BuildColorTransformationTableUnsafe(colorTransformationTables[2], ImageRenderingOptions.GetValidRgbGain(renderingOptions.RedGain));
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
							// ReSharper disable once MustUseReturnValue
							imageStream.Read(row, 0, rowStride);
							var packedPixelsPtr = rowPtr;
							var bitmapPixelPtr = (ushort*)bitmapRowPtr;
							var isVerticalWeightedArea = (y >= wTop && y <= wBottom);
							for (var x = 0; x < width; packedPixelsPtr += 3)
							{
								// 1st pixel
								var bytes2 = packedPixelsPtr[2];
								var colorComponent = colorComponentSelector(x, y);
								var color = bitsCombinationFunc(packedPixelsPtr[0], (byte)(bytes2 >> 4));
								accuColor[colorComponent] += color;
								++accuPixelCount[colorComponent];
								if (isVerticalWeightedArea && x >= wLeft && x <= wRight)
								{
									wAccuColor[colorComponent] += (ushort)(color << 1);
									wAccuPixelCount[colorComponent] += 2;
								}
								else
								{
									wAccuColor[colorComponent] += color;
									++wAccuPixelCount[colorComponent];
								}
								bitmapPixelPtr[colorComponent] = colorTransformationTables[colorComponent][color];
								bitmapPixelPtr[3] = 65535;
								bitmapPixelPtr += 4;
								++x;

								// 2nd pixel
								colorComponent = colorComponentSelector(x, y);
								color = bitsCombinationFunc(packedPixelsPtr[1], bytes2);
								accuColor[colorComponent] += color;
								++accuPixelCount[colorComponent];
								if (isVerticalWeightedArea && x >= wLeft && x <= wRight)
								{
									wAccuColor[colorComponent] += (ushort)(color << 1);
									wAccuPixelCount[colorComponent] += 2;
								}
								else
								{
									wAccuColor[colorComponent] += color;
									++wAccuPixelCount[colorComponent];
								}
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
				NativeMemory.Free(baseColorTransformationTable);
            }

			// complete
			return new ImageRenderingResult()
			{
				MeanOfBlue = accuColor[BlueColorComponent] / (double)accuPixelCount[BlueColorComponent],
				MeanOfGreen = accuColor[GreenColorComponent] / (double)accuPixelCount[GreenColorComponent],
				MeanOfRed = accuColor[RedColorComponent] / (double)accuPixelCount[RedColorComponent],
				WeightedMeanOfBlue = wAccuColor[BlueColorComponent] / (double)wAccuPixelCount[BlueColorComponent],
				WeightedMeanOfGreen = wAccuColor[GreenColorComponent] / (double)wAccuPixelCount[GreenColorComponent],
				WeightedMeanOfRed = wAccuColor[RedColorComponent] / (double)wAccuPixelCount[RedColorComponent],
			};
		}
	}
}
