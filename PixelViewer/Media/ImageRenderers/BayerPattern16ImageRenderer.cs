using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Base implementation of <see cref="IImageRenderer"/> which renders image with 16-bit bayer filter pattern.
/// </summary>
class BayerPattern16ImageRenderer() : BayerPatternImageRenderer(new ImageFormat(ImageFormatCategory.Bayer, "Bayer_Pattern_16", true, new ImagePlaneDescriptor(2, 9, 16, true), [ "RAW16" ]))
{
	// Create the color table which maps the value of a color channel to its color, a component without color
	// table is rendered by its own value so the value covers the full range which the samples can carry.
	static ColorTable CreateSourceColorTable(ColorTable? colorTable)
	{
		if (colorTable is not null)
			return colorTable.Share();
		return new ColorTable(65536, 16).Also(it =>
		{
			var colors = it.Memory.Span;
			for (var i = 65535; i >= 0; --i)
				colors[i] = (uint)i;
		});
	}
	
	
	/// <inheritdoc/>
    public override bool IsColorTableSupported => true;


	/// <inheritdoc/>
    protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
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

		// the base class calls this method only when no color table is applied, so the effective bits still define
		// the values of color channels here
		if (effectiveBits <= 8 || effectiveBits > 16)
			throw new ArgumentException($"Invalid effective bits: {effectiveBits}.");

		// prepare conversion
		var blackLevel = planeOptions[0].BlackLevel.GetValueOrDefault();
		var whiteLevel = planeOptions[0].WhiteLevel ?? (uint)(1 << effectiveBits) - 1;
		if (blackLevel >= whiteLevel || whiteLevel >= (1 << effectiveBits))
			throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");
		var extractFunc = this.Create16BitColorExtraction(renderingOptions.ByteOrdering, effectiveBits, blackLevel, whiteLevel);

		// render
		var baseColorTransformationTable = (ushort*)NativeMemory.Alloc(65536 * sizeof(ushort) * 3);
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
			var colorTransformationTables = stackalloc ushort*[3] { 
				baseColorTransformationTable,
				baseColorTransformationTable + 65536,
				baseColorTransformationTable + 131072,
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
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						var isVerticalWeightedArea = (y >= wTop && y <= wBottom);
						for (var x = 0; x < width; ++x, pixelPtr += pixelStride, bitmapPixelPtr += 4)
						{
							var colorComponent = (int)colorComponentSelector(x, y);
							var color = extractFunc(pixelPtr[0], pixelPtr[1]);
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
		return new ImageRenderingResult
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
	/// <remarks>Unlike the 8-bit renderer the rendering without color table keeps its own implementation, because
	/// expressing it as a mapping would need a table of 65536 colors to be built for every rendering.</remarks>
	protected override unsafe ImageRenderingResult OnRenderWithColorTables(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// get parameters
		var width = bitmapBuffer.Width;
		var height = bitmapBuffer.Height;
		var pixelStride = planeOptions[0].PixelStride;
		var rowStride = planeOptions[0].RowStride;
		var isLittleEndian = renderingOptions.ByteOrdering == ByteOrdering.LittleEndian;

		// the levels are colors in the color tables instead of values of color channels, so they are not related to
		// the effective bits of the plane at all
		var blackLevel = planeOptions[0].BlackLevel.GetValueOrDefault();
		var whiteLevel = planeOptions[0].WhiteLevel ?? SelectDefaultWhiteLevel(renderingOptions);
		if (blackLevel >= whiteLevel)
			throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");

		// build the mapping of each color component, a component without color table is rendered by its own value
		using var blueSourceColorTable = CreateSourceColorTable(renderingOptions.BlueColorTable);
		using var greenSourceColorTable = CreateSourceColorTable(renderingOptions.GreenColorTable);
		using var redSourceColorTable = CreateSourceColorTable(renderingOptions.RedColorTable);
		var mappings = new ushort[3][];
		mappings[BlueColorComponent] = CreateColorTableTo16BitColorMapping(blueSourceColorTable, blackLevel, whiteLevel);
		mappings[GreenColorComponent] = CreateColorTableTo16BitColorMapping(greenSourceColorTable, blackLevel, whiteLevel);
		mappings[RedColorComponent] = CreateColorTableTo16BitColorMapping(redSourceColorTable, blackLevel, whiteLevel);

		// render
		var baseColorTransformationTable = (ushort*)NativeMemory.Alloc(65536 * sizeof(ushort) * 3);
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
			var colorTransformationTables = stackalloc ushort*[3] {
				baseColorTransformationTable,
				baseColorTransformationTable + 65536,
				baseColorTransformationTable + 131072,
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
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						var isVerticalWeightedArea = (y >= wTop && y <= wBottom);
						for (var x = 0; x < width; ++x, pixelPtr += pixelStride, bitmapPixelPtr += 4)
						{
							var colorComponent = (int)colorComponentSelector(x, y);
							var index = isLittleEndian
								? (pixelPtr[1] << 8) | pixelPtr[0]
								: (pixelPtr[0] << 8) | pixelPtr[1];
							var color = mappings[colorComponent][index];
							accuColor[colorComponent] += color;
							++accuPixelCount[colorComponent];
							if (isVerticalWeightedArea && x >= wLeft && x <= wRight)
							{
								wAccuColor[colorComponent] += (ulong)color << 1;
								wAccuPixelCount[colorComponent] += 2;
							}
							else
							{
								wAccuColor[colorComponent] += color;
								++wAccuPixelCount[colorComponent];
							}
							bitmapPixelPtr[colorComponent] = colorTransformationTables[colorComponent][color];
							bitmapPixelPtr[3] = 65535;
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
		return new ImageRenderingResult
		{
			MeanOfBlue = accuColor[BlueColorComponent] / (double)accuPixelCount[BlueColorComponent],
			MeanOfGreen = accuColor[GreenColorComponent] / (double)accuPixelCount[GreenColorComponent],
			MeanOfRed = accuColor[RedColorComponent] / (double)accuPixelCount[RedColorComponent],
			WeightedMeanOfBlue = wAccuColor[BlueColorComponent] / (double)wAccuPixelCount[BlueColorComponent],
			WeightedMeanOfGreen = wAccuColor[GreenColorComponent] / (double)wAccuPixelCount[GreenColorComponent],
			WeightedMeanOfRed = wAccuColor[RedColorComponent] / (double)wAccuPixelCount[RedColorComponent],
		};
	}


	// Select the white level to be used when it is not defined by the image plane.
	static uint SelectDefaultWhiteLevel(ImageRenderingOptions renderingOptions)
	{
		var colorBitDepth = Math.Max(renderingOptions.BlueColorTable?.ColorBitDepth ?? 0, renderingOptions.GreenColorTable?.ColorBitDepth ?? 0);
		colorBitDepth = Math.Max(colorBitDepth, renderingOptions.RedColorTable?.ColorBitDepth ?? 0);
		return (uint)((1L << colorBitDepth) - 1);
	}
}