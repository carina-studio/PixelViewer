using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Implementation of <see cref="IImageRenderer"/> which renders image with 14-bit Bayer Filter MIPI RAW.
/// </summary>
class BayerPattern14MipiImageRenderer : BayerPatternImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="BayerPattern12MipiImageRenderer"/> instance.
    /// </summary>
    public BayerPattern14MipiImageRenderer() : base(new ImageFormat(ImageFormatCategory.Bayer, "Bayer_Pattern_14_MIPI", true, new ImagePlaneDescriptor(0, 14, 14, true), new[]{ "MIPI14", "RAW14" }))
    { }
    
    
    /// <inheritdoc/>
    public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also(it =>
    {
        // 7 bytes for 4 pixels
        width &= 0x7ffffffc;
        height &= 0x7ffffffc;
        it.Add(new ImagePlaneOptions(14, 0, (width >> 2) * 7));
    });


    /// <inheritdoc/>
    public override int EvaluatePixelCount(IImageDataSource source)
    {
        // 7 bytes for 4 pixels
        return (int)(source.Size / 7) << 2;
    }


    /// <inheritdoc/>
    public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
    {
        // 7 bytes for 4 pixels
        width &= 0x7ffffffc;
        height &= 0x7ffffffc;
        if (width <= 0 || height <= 0)
            return 0;
        var rowStride = Math.Max((width >> 2) * 7, planeOptions[0].RowStride);
        return rowStride * height;
    }
    
    
    /// <inheritdoc/>
	protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, int> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// get parameters
		var width = bitmapBuffer.Width & 0x7ffffffc;
		var height = bitmapBuffer.Height & 0x7ffffffc;
		var rowStride = planeOptions[0].RowStride;
		if ((width >> 2) * 7 > rowStride)
			throw new ArgumentException($"Invalid row stride: {rowStride}.");

		// prepare conversion
		var blackLevel = planeOptions[0].BlackLevel.GetValueOrDefault();
		var whiteLevel = planeOptions[0].WhiteLevel ?? 16383;
		if (blackLevel >= whiteLevel || whiteLevel > 16383)
			throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");
		var bitsCombinationFunc = (blackLevel == 0 && whiteLevel == 16383)
			? Global.Run(() =>
			{
				return renderingOptions.ByteOrdering == ByteOrdering.BigEndian
					? new Func<byte, byte, ushort>((b1, b2) => (ushort)((b1 << 8) | ((b2 & 0x3f) << 2) | ((b1 >> 6) & 0x3)))
					: (b1, b2) => (ushort)(((b2 & 0x3f) << 8) | (b1 << 2) | ((b1 >> 6) & 0x3));
			})
			: Global.Run(() =>
			{
				var correctedColors = new ushort[16384].Also(it =>
				{
					var scale = 16383.0 / (whiteLevel - blackLevel);
					for (var i = whiteLevel; i > blackLevel; --i)
						it[i] = (ushort)((i - blackLevel) * scale + 0.5);
					for (var i = it.Length - 1; i > whiteLevel; --i)
						it[i] = 16383;
				});
				return renderingOptions.ByteOrdering == ByteOrdering.BigEndian
					? new Func<byte, byte, ushort>((b1, b2) => 
					{
						var color = correctedColors[(b1 << 6) | ((b2 & 0x3f) << 2)];
						return (ushort)((color << 2) | ((color >> 12) & 0x3));
					})
					: (b1, b2) => 
					{
						var color = correctedColors[b1 | ((b2 & 0x3f) << 8)];
						return (ushort)((color << 2) | ((color >> 12) & 0x3));
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
						var packedPixelsPtr = rowPtr;
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						var isVerticalWeightedArea = (y >= wTop && y <= wBottom);
						for (var x = 0; x < width; packedPixelsPtr += 7)
						{
							// prepare extra bits
							var extraBits = (uint)((packedPixelsPtr[4] << 16) | (packedPixelsPtr[5] << 8) | packedPixelsPtr[6]);
							
							// 1st pixel
							var b2 = (byte)((extraBits >> 18) & 0x3f);
							var colorComponent = colorComponentSelector(x, y);
							var color = bitsCombinationFunc(packedPixelsPtr[0], b2);
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
							b2 = (byte)((extraBits >> 12) & 0x3f);
							colorComponent = colorComponentSelector(x, y);
							color = bitsCombinationFunc(packedPixelsPtr[1], b2);
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
							
							// 3rd pixel
							b2 = (byte)((extraBits >> 6) & 0x3f);
							colorComponent = colorComponentSelector(x, y);
							color = bitsCombinationFunc(packedPixelsPtr[2], b2);
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
							
							// 4th pixel
							b2 = (byte)(extraBits & 0x3f);
							colorComponent = colorComponentSelector(x, y);
							color = bitsCombinationFunc(packedPixelsPtr[3], b2);
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
}