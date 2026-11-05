using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Base implementation of <see cref="IImageRenderer"/> which renders image with Bayer filter pattern whose samples are packed into bits.
/// </summary>
/// <remarks>
/// The samples of each row are a continuous stream of bits which starts at a byte boundary, and the rest of the row stride is padding.
/// The byte ordering selects the order of bits: <see cref="ByteOrdering.BigEndian"/> takes the most significant bit of each byte first as
/// TIFF and DNG pack samples, and <see cref="ByteOrdering.LittleEndian"/> takes the least significant bit first as the packed formats of GenICam do.
/// </remarks>
/// <param name="bitsPerSample">Number of bits per sample, which is in [9, 16).</param>
/// <param name="keywords">Keywords of format.</param>
abstract class PackedBayerPatternImageRenderer(int bitsPerSample, IEnumerable<string> keywords) : BayerPatternImageRenderer(new ImageFormat(ImageFormatCategory.Bayer, $"Bayer_Pattern_{bitsPerSample}_Packed", true, new ImagePlaneDescriptor(0, bitsPerSample, bitsPerSample, true), keywords))
{
	/// <inheritdoc/>
	public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) =>
		[ new ImagePlaneOptions(bitsPerSample, 0, this.GetMinRowStride(width)) ];


	// Create the mapping from each value of sample to 16-bit color, which stretches the range between the black and white levels.
	ushort[] CreateColorMapping(uint blackLevel, uint whiteLevel)
	{
		var maxValue = (1 << bitsPerSample) - 1;
		var scale = (double)maxValue / (whiteLevel - blackLevel);
		var paddingBitsShiftCount = (bitsPerSample << 1) - 16;
		var mapping = new ushort[maxValue + 1];
		for (var i = maxValue; i >= 0; --i)
		{
			var value = i <= blackLevel ? 0 : i >= whiteLevel ? maxValue : (int)((i - blackLevel) * scale + 0.5);
			mapping[i] = (ushort)((value << (16 - bitsPerSample)) | (value >> paddingBitsShiftCount));
		}
		return mapping;
	}


	/// <inheritdoc/>
	public override int EvaluatePixelCount(IImageDataSource source) =>
		(int)Math.Min(int.MaxValue, source.Size * 8 / bitsPerSample);


	/// <inheritdoc/>
	public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
	{
		if (width <= 0 || height <= 0)
			return 0;
		return (long)Math.Max(this.GetMinRowStride(width), planeOptions[0].RowStride) * height;
	}


	// Get the number of bytes of a row without padding.
	int GetMinRowStride(int width) =>
		(int)(((long)width * bitsPerSample + 7) >> 3);


	/// <inheritdoc/>
	protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// get parameters
		var width = bitmapBuffer.Width;
		var height = bitmapBuffer.Height;
		var rowStride = planeOptions[0].RowStride;
		if (width <= 0 || height <= 0)
			throw new ArgumentException($"Invalid size: {width}x{height}.");
		if (this.GetMinRowStride(width) > rowStride)
			throw new ArgumentException($"Invalid row stride: {rowStride}.");

		// prepare conversion
		var maxValue = (1u << bitsPerSample) - 1;
		var blackLevel = planeOptions[0].BlackLevel.GetValueOrDefault();
		var whiteLevel = planeOptions[0].WhiteLevel ?? maxValue;
		if (blackLevel >= whiteLevel || whiteLevel > maxValue)
			throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");
		var colorMapping = this.CreateColorMapping(blackLevel, whiteLevel);
		var isMsbFirst = renderingOptions.ByteOrdering == ByteOrdering.BigEndian;

		// render
		var baseColorTransformationTable = (ushort*)NativeMemory.Alloc(65536 * sizeof(ushort) * 3);
		var meansAccumulator = new RgbMeansAccumulator(width, height, 65535, renderingOptions);
		try
		{
			var colorTransformationTables = stackalloc ushort*[3] {
				baseColorTransformationTable,
				baseColorTransformationTable + 65536,
				baseColorTransformationTable + 131072,
			};
			BuildColorTransformationTableUnsafe(colorTransformationTables[0], renderingOptions, gain: ImageRenderingOptions.GetValidRgbGain(renderingOptions.BlueGain));
			BuildColorTransformationTableUnsafe(colorTransformationTables[1], renderingOptions, gain: ImageRenderingOptions.GetValidRgbGain(renderingOptions.GreenGain));
			BuildColorTransformationTableUnsafe(colorTransformationTables[2], renderingOptions, gain: ImageRenderingOptions.GetValidRgbGain(renderingOptions.RedGain));
			bitmapBuffer.Memory.Pin(bitmapBaseAddress =>
			{
				// render to 16-bit R/G/B
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				var bitmapRowStride = bitmapBuffer.RowBytes;
				var row = new byte[rowStride];
				fixed (byte* rowPtr = row)
				{
					for (var y = 0; y < height; ++y, bitmapRowPtr += bitmapRowStride)
					{
						// read samples of the row, the bits not taken yet are kept at the end of the buffer for the most significant bit first,
						// or at the start of the buffer for the least significant bit first
						imageStream.ReadAtLeast(row, rowStride, throwOnEndOfStream: false);
						var bytePtr = rowPtr;
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						var bits = 0u;
						var bitCount = 0;
						for (var x = 0; x < width; ++x, bitmapPixelPtr += 4)
						{
							uint value;
							if (isMsbFirst)
							{
								while (bitCount < bitsPerSample)
								{
									bits = (bits << 8) | *(bytePtr++);
									bitCount += 8;
								}
								bitCount -= bitsPerSample;
								value = (bits >> bitCount) & maxValue;
							}
							else
							{
								while (bitCount < bitsPerSample)
								{
									bits |= (uint)*(bytePtr++) << bitCount;
									bitCount += 8;
								}
								value = bits & maxValue;
								bits >>= bitsPerSample;
								bitCount -= bitsPerSample;
							}
							var colorComponent = (int)colorComponentSelector(x, y);
							var color = colorMapping[value];
							meansAccumulator.Accumulate(x, y, colorComponent, color);
							bitmapPixelPtr[colorComponent] = colorTransformationTables[colorComponent][color];
							bitmapPixelPtr[3] = 65535;
						}

						// prepare next row
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
		var result = new ImageRenderingResult();
		meansAccumulator.WriteToRenderingResult(ref result);
		return result;
	}
}
