using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// Implementation of <see cref="IImageRenderer"/> which renders image with 8-bit bayer filter pattern.
    /// </summary>
    class BayerPattern8ImageRenderer : BayerPatternImageRenderer
    {
        public BayerPattern8ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "Bayer_Pattern_8", false, new ImagePlaneDescriptor(1, 1, 8, true), new[] { "RAW8" }))
        { }


        /// <inheritdoc/>
        protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, int> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
        {
			// get parameters
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			var pixelStride = planeOptions[0].PixelStride;
			var rowStride = planeOptions[0].RowStride;
			var effectiveBits = planeOptions[0].EffectiveBits;
			if (width <= 0 || height <= 0)
				throw new ArgumentException($"Invalid size: {width}x{height}.");
			if (pixelStride <= 0 || (pixelStride * width) > rowStride)
				throw new ArgumentException($"Invalid pixel/row stride: {pixelStride}/{rowStride}.");
			if (effectiveBits <= 0 || effectiveBits > 8)
				throw new ArgumentException($"Invalid effective bits: {effectiveBits}.");
			
			// prepare conversion
			var blackLevel = planeOptions[0].BlackLevel.GetValueOrDefault();
			var whiteLevel = planeOptions[0].WhiteLevel ?? (uint)(1 << effectiveBits) - 1;
			if (blackLevel >= whiteLevel || whiteLevel >= (1 << effectiveBits))
				throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");
			var extractFunc = this.Create8BitColorExtraction(effectiveBits, blackLevel, whiteLevel);

			// render
			var baseColorTransformationTable = (byte*)NativeMemory.Alloc(256 * sizeof(byte) * 3);
			// ReSharper disable IdentifierTypo
			var accuColor = stackalloc ulong[] { 0L, 0L, 0L };
			var accuPixelCount = stackalloc int[] { 0, 0, 0 };
			var wAccuColor = stackalloc ulong[] { 0L, 0L, 0L };
			var wAccuPixelCount = stackalloc int[] { 0, 0, 0 };
			// ReSharper restore IdentifierTypo
			var wLeft = width / 3;
			var wRight = width - wLeft;
			var wTop = height / 3;
			var wBottom = height - wTop;
			try
			{
				var colorTransformationTables = stackalloc byte*[3] { 
					baseColorTransformationTable,
					baseColorTransformationTable + 256,
					baseColorTransformationTable + 512,
				};
				BuildColorTransformationTableUnsafe(colorTransformationTables[0], ImageRenderingOptions.GetValidRgbGain(renderingOptions.BlueGain));
				BuildColorTransformationTableUnsafe(colorTransformationTables[1], ImageRenderingOptions.GetValidRgbGain(renderingOptions.GreenGain));
				BuildColorTransformationTableUnsafe(colorTransformationTables[2], ImageRenderingOptions.GetValidRgbGain(renderingOptions.RedGain));
				bitmapBuffer.Memory.Pin(bitmapBaseAddress =>
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
							var pixelPtr = rowPtr;
							var bitmapPixelPtr = bitmapRowPtr;
							var isVerticalWeightedArea = (y >= wTop && y <= wBottom);
							for (var x = 0; x < width; ++x, pixelPtr += pixelStride, bitmapPixelPtr += 4)
							{
								var colorComponent = colorComponentSelector(x, y);
								var color = extractFunc(pixelPtr[0]);
								accuColor[colorComponent] += color;
								++accuPixelCount[colorComponent];
								if (isVerticalWeightedArea && x >= wLeft && x <= wRight)
								{
									wAccuColor[colorComponent] += (ulong)(color << 1);
									wAccuPixelCount[colorComponent] += 2;
								}
								else
								{
									wAccuColor[colorComponent] += color;
									++wAccuPixelCount[colorComponent];
								}
								bitmapPixelPtr[colorComponent] = colorTransformationTables[colorComponent][color];
								bitmapPixelPtr[3] = 255;
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


        /// <inheritdoc/>
        public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
	        Task.FromResult(BitmapFormat.Bgra32);
    }
}
