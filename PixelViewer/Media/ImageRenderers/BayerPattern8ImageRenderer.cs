using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Implementation of <see cref="IImageRenderer"/> which renders image with 8-bit bayer filter pattern.
/// </summary>
class BayerPattern8ImageRenderer() : BayerPatternImageRenderer(new ImageFormat(ImageFormatCategory.Bayer, "Bayer_Pattern_8", false, new ImagePlaneDescriptor(1, 1, 8, true), [ "RAW8" ]))
{
    // Create the mapping from the value of a color channel to the color to be rendered, either through the given
    // color table or, when there is none, by applying the effective bits and the levels to the value itself.
    ColorTable CreateSourceColorTable(ColorTable? colorTable, int effectiveBits, uint blackLevel, uint whiteLevel)
    {
	    if (colorTable is not null)
		    return colorTable.Share();
	    var extractFunc = this.Create8BitColorExtraction(effectiveBits, blackLevel, whiteLevel);
	    return new ColorTable(256, 8).Also(it =>
	    {
		    var colors = it.Memory.Span;
		    for (var i = 255; i >= 0; --i)
			    colors[i] = extractFunc((byte)i);
	    });
    }


    /// <inheritdoc/>
    public override bool IsColorTableSupported => true;


    /// <inheritdoc/>
    protected override ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken) =>
	    this.Render(imageStream, bitmapBuffer, colorComponentSelector, renderingOptions, planeOptions, cancellationToken);


    /// <inheritdoc/>
    /// <remarks>The values of color channels are mapped through color tables whether they are defined or not, so
    /// rendering with and without them shares one implementation here. The mapping is cheap to build because the
    /// values are 8-bit, which is not the case for the renderers of wider values.</remarks>
    protected override ImageRenderingResult OnRenderWithColorTables(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken) =>
	    this.Render(imageStream, bitmapBuffer, colorComponentSelector, renderingOptions, planeOptions, cancellationToken);


    // Render the image by mapping the value of each color channel to its color.
    unsafe ImageRenderingResult Render(Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
    {
		// get parameters
		var width = bitmapBuffer.Width;
		var height = bitmapBuffer.Height;
		var pixelStride = planeOptions[0].PixelStride;
		var rowStride = planeOptions[0].RowStride;
		if (width <= 0 || height <= 0)
			throw new ArgumentException($"Invalid size: {width}x{height}.");
		if (pixelStride <= 0 || (pixelStride * width) > rowStride)
			throw new ArgumentException($"Invalid pixel/row stride: {pixelStride}/{rowStride}.");

		// the color tables define the colors of the image, so the effective bits are applied only without them
		var hasColorTable = renderingOptions.HasColorTables;
		var effectiveBits = planeOptions[0].EffectiveBits;
		if (!hasColorTable && (effectiveBits <= 0 || effectiveBits > 8))
			throw new ArgumentException($"Invalid effective bits: {effectiveBits}.");

		// the levels are colors in the color tables instead of values of color channels, so they are bounded by the
		// color tables when they are present and by the effective bits otherwise
		var blackLevel = planeOptions[0].BlackLevel.GetValueOrDefault();
		var whiteLevel = planeOptions[0].WhiteLevel ?? SelectDefaultWhiteLevel(renderingOptions, effectiveBits);
		if (blackLevel >= whiteLevel)
			throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");
		if (!hasColorTable && whiteLevel >= (1 << effectiveBits))
			throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");

		// build the mapping of each color component, a component without color table is rendered by its own value
		using var blueSourceColorTable = this.CreateSourceColorTable(renderingOptions.BlueColorTable, effectiveBits, blackLevel, whiteLevel);
		using var greenSourceColorTable = this.CreateSourceColorTable(renderingOptions.GreenColorTable, effectiveBits, blackLevel, whiteLevel);
		using var redSourceColorTable = this.CreateSourceColorTable(renderingOptions.RedColorTable, effectiveBits, blackLevel, whiteLevel);
		var isRenderingTo64BitBitmap = bitmapBuffer.Format == BitmapFormat.Bgra64;
		var mappings8 = new byte[3][];
		var mappings16 = new ushort[3][];
		if (isRenderingTo64BitBitmap)
		{
			mappings16[BlueColorComponent] = CreateColorTableTo16BitColorMapping(blueSourceColorTable, blackLevel, whiteLevel);
			mappings16[GreenColorComponent] = CreateColorTableTo16BitColorMapping(greenSourceColorTable, blackLevel, whiteLevel);
			mappings16[RedColorComponent] = CreateColorTableTo16BitColorMapping(redSourceColorTable, blackLevel, whiteLevel);
		}
		else
		{
			mappings8[BlueColorComponent] = CreateColorTableTo8BitColorMapping(blueSourceColorTable, blackLevel, whiteLevel);
			mappings8[GreenColorComponent] = CreateColorTableTo8BitColorMapping(greenSourceColorTable, blackLevel, whiteLevel);
			mappings8[RedColorComponent] = CreateColorTableTo8BitColorMapping(redSourceColorTable, blackLevel, whiteLevel);
		}

		// render
		var colorTransformationTableSize = isRenderingTo64BitBitmap ? 65536 : 256;
		var baseColorTransformationTable = (byte*)NativeMemory.Alloc((nuint)(colorTransformationTableSize * (isRenderingTo64BitBitmap ? sizeof(ushort) : sizeof(byte)) * 3));
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
			// build the tables which apply the RGB gains to the rendered colors
			var blueGain = ImageRenderingOptions.GetValidRgbGain(renderingOptions.BlueGain);
			var greenGain = ImageRenderingOptions.GetValidRgbGain(renderingOptions.GreenGain);
			var redGain = ImageRenderingOptions.GetValidRgbGain(renderingOptions.RedGain);
			var colorTransformationTables8 = stackalloc byte*[3];
			var colorTransformationTables16 = stackalloc ushort*[3];
			if (isRenderingTo64BitBitmap)
			{
				var table = (ushort*)baseColorTransformationTable;
				colorTransformationTables16[0] = table;
				colorTransformationTables16[1] = table + 65536;
				colorTransformationTables16[2] = table + 131072;
				BuildColorTransformationTableUnsafe(colorTransformationTables16[0], blueGain);
				BuildColorTransformationTableUnsafe(colorTransformationTables16[1], greenGain);
				BuildColorTransformationTableUnsafe(colorTransformationTables16[2], redGain);
			}
			else
			{
				colorTransformationTables8[0] = baseColorTransformationTable;
				colorTransformationTables8[1] = baseColorTransformationTable + 256;
				colorTransformationTables8[2] = baseColorTransformationTable + 512;
				BuildColorTransformationTableUnsafe(colorTransformationTables8[0], blueGain);
				BuildColorTransformationTableUnsafe(colorTransformationTables8[1], greenGain);
				BuildColorTransformationTableUnsafe(colorTransformationTables8[2], redGain);
			}
			bitmapBuffer.Memory.Pin(bitmapBaseAddress =>
			{
				// render each row to the color components selected by the bayer pattern
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
						var bitmapPixelPtr8 = bitmapRowPtr;
						var bitmapPixelPtr16 = (ushort*)bitmapRowPtr;
						var isVerticalWeightedArea = (y >= wTop && y <= wBottom);
						for (var x = 0; x < width; ++x, pixelPtr += pixelStride, bitmapPixelPtr8 += 4, bitmapPixelPtr16 += 4)
						{
							var colorComponent = (int)colorComponentSelector(x, y);
							uint color;
							if (isRenderingTo64BitBitmap)
							{
								color = mappings16[colorComponent][pixelPtr[0]];
								bitmapPixelPtr16[colorComponent] = colorTransformationTables16[colorComponent][color];
								bitmapPixelPtr16[3] = 65535;
							}
							else
							{
								color = mappings8[colorComponent][pixelPtr[0]];
								bitmapPixelPtr8[colorComponent] = colorTransformationTables8[colorComponent][color];
								bitmapPixelPtr8[3] = 255;
							}
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
    static uint SelectDefaultWhiteLevel(ImageRenderingOptions renderingOptions, int effectiveBits)
    {
	    var colorBitDepth = Math.Max(renderingOptions.BlueColorTable?.ColorBitDepth ?? 0, renderingOptions.GreenColorTable?.ColorBitDepth ?? 0);
	    colorBitDepth = Math.Max(colorBitDepth, renderingOptions.RedColorTable?.ColorBitDepth ?? 0);
	    if (colorBitDepth <= 0)
		    colorBitDepth = effectiveBits;
	    return (uint)((1L << colorBitDepth) - 1);
    }
    /// <inheritdoc/>
    public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
        Task.FromResult(SelectRenderedFormatByColorTables(renderingOptions, BitmapFormat.Bgra32));


}
