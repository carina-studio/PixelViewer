using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Base implementation of <see cref="IImageRenderer"/> which renders image with MIPI CSI-2 YUV420 format.
/// </summary>
/// <remarks>The format alternates 2 kinds of line: a line which carries only Y (lines 1, 3, 5…), followed by a line which carries U, Y, V, Y (lines 2, 4, 6…) packed the same way as the YUV422 format. The U and V carried by the 2nd line of a pair are shared by the 2x2 pixels of both lines. The 2 kinds of line are described as 2 planes so that their row strides can be set separately.</remarks>
abstract class BaseYuv420MipiImageRenderer : BaseImageRenderer
{
	// Fields.
	readonly int effectiveBits;


	/// <summary>
	/// Initialize new <see cref="BaseYuv420MipiImageRenderer"/> instance.
	/// </summary>
	/// <param name="format">Supported format.</param>
	/// <param name="effectiveBits">Effective bits of each Y/U/V component, which must be 8 or 10.</param>
	protected BaseYuv420MipiImageRenderer(ImageFormat format, int effectiveBits) : base(format)
	{
		if (effectiveBits != 8 && effectiveBits != 10)
			throw new ArgumentOutOfRangeException(nameof(effectiveBits));
		this.effectiveBits = effectiveBits;
	}


	/// <inheritdoc/>
	public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height)
	{
		width &= 0x7ffffffc;
		return [
			new(this.effectiveBits, 0, this.GetLumaRowSize(width)),
			new(this.effectiveBits, 0, this.GetLumaChromaRowSize(width)),
		];
	}


	/// <inheritdoc/>
	public override int EvaluatePixelCount(IImageDataSource source) =>
		(int)(source.Size * 16 / (this.effectiveBits * 3));


	/// <inheritdoc/>
	public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
	{
		width &= 0x7ffffffc;
		height &= 0x7ffffffe;
		if (width <= 0 || height <= 0)
			return 0;
		var lumaRowStride = Math.Max(this.GetLumaRowSize(width), planeOptions[0].RowStride);
		var lumaChromaRowStride = Math.Max(this.GetLumaChromaRowSize(width), planeOptions[1].RowStride);
		return (long)(lumaRowStride + lumaChromaRowStride) * (height >> 1);
	}


	// Get number of bytes of a line which carries U, Y, V, Y.
	int GetLumaChromaRowSize(int width) => (width * this.effectiveBits) >> 2;


	// Get number of bytes of a line which carries only Y.
	int GetLumaRowSize(int width) => (width * this.effectiveBits) >> 3;


	/// <inheritdoc/>
	protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// get state
		var width = bitmapBuffer.Width & 0x7ffffffc;
		var height = bitmapBuffer.Height & 0x7ffffffe;
		var lumaRowStride = planeOptions[0].RowStride;
		var lumaChromaRowStride = planeOptions[1].RowStride;
		if (width <= 0 || height <= 0)
			throw new ArgumentException($"Invalid size: {width}x{height}.");
		if (this.GetLumaRowSize(width) > lumaRowStride || this.GetLumaChromaRowSize(width) > lumaChromaRowStride)
			throw new ArgumentException($"Invalid row stride: {lumaRowStride}, {lumaChromaRowStride}.");

		// select color converter
		var converter = renderingOptions.YuvToBgraConverter ?? YuvToBgraConverter.Default;
		var is16BitOutput = this.effectiveBits > 8;

		// render
		bitmapBuffer.Memory.Pin(bitmapBaseAddress =>
		{
			var lumaRow = new byte[lumaRowStride];
			var lumaChromaRow = new byte[lumaChromaRowStride];
			var yComponents = new ushort[width * 2];
			var uComponents = new ushort[width >> 1];
			var vComponents = new ushort[width >> 1];
			fixed (byte* lumaRowPtr = lumaRow, lumaChromaRowPtr = lumaChromaRow)
			fixed (ushort* yPtr = yComponents, uPtr = uComponents, vPtr = vComponents)
			{
				var bitmapRowStride = bitmapBuffer.RowBytes;
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				for (var y = 0; y < height; y += 2, bitmapRowPtr += bitmapRowStride * 2)
				{
					// read the pair of lines, missing data is treated as zero
					if (imageStream.ReadAtLeast(lumaRow, lumaRowStride, throwOnEndOfStream: false) < lumaRowStride)
						Array.Clear(lumaRow);
					if (imageStream.ReadAtLeast(lumaChromaRow, lumaChromaRowStride, throwOnEndOfStream: false) < lumaChromaRowStride)
						Array.Clear(lumaChromaRow);

					// unpack components of the pair of lines
					this.ReadLumaRow(lumaRowPtr, width, yPtr);
					this.ReadLumaChromaRow(lumaChromaRowPtr, width, yPtr + width, uPtr, vPtr);

					// convert both lines with the shared U and V
					for (var i = 0; i < 2; ++i)
					{
						var rowYPtr = yPtr + (i * width);
						var bitmapPixelPtr = bitmapRowPtr + (i * bitmapRowStride);
						if (is16BitOutput)
						{
							for (var x = 0; x < width; x += 2, bitmapPixelPtr += 16)
								converter.ConvertFromYuv422ToBgra64(rowYPtr[x], rowYPtr[x + 1], uPtr[x >> 1], vPtr[x >> 1], (ulong*)bitmapPixelPtr, (ulong*)(bitmapPixelPtr + 8));
						}
						else
						{
							for (var x = 0; x < width; x += 2, bitmapPixelPtr += 8)
								converter.ConvertFromYuv422ToBgra32((byte)rowYPtr[x], (byte)rowYPtr[x + 1], (byte)uPtr[x >> 1], (byte)vPtr[x >> 1], (uint*)bitmapPixelPtr, (uint*)(bitmapPixelPtr + 4));
						}
					}

					// check cancellation
					cancellationToken.ThrowIfCancellationRequested();
				}
			}
		});

		// complete
		return new ImageRenderingResult();
	}


	/// <summary>
	/// Read Y, U and V components from a line which carries U, Y, V, Y.
	/// </summary>
	/// <param name="row">Pointer to data of the line.</param>
	/// <param name="width">Width of image, which is a multiple of 4.</param>
	/// <param name="y">Pointer to buffer to receive <paramref name="width"/> Y components.</param>
	/// <param name="u">Pointer to buffer to receive <paramref name="width"/>/2 U components.</param>
	/// <param name="v">Pointer to buffer to receive <paramref name="width"/>/2 V components.</param>
	/// <remarks>Components are expected to be 8-bit for 8-bit format, or scaled to 16-bit otherwise.</remarks>
	protected abstract unsafe void ReadLumaChromaRow(byte* row, int width, ushort* y, ushort* u, ushort* v);


	/// <summary>
	/// Read Y components from a line which carries only Y.
	/// </summary>
	/// <param name="row">Pointer to data of the line.</param>
	/// <param name="width">Width of image, which is a multiple of 4.</param>
	/// <param name="y">Pointer to buffer to receive <paramref name="width"/> Y components.</param>
	/// <remarks>Components are expected to be 8-bit for 8-bit format, or scaled to 16-bit otherwise.</remarks>
	protected abstract unsafe void ReadLumaRow(byte* row, int width, ushort* y);


	/// <inheritdoc/>
	public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
		Task.FromResult(this.effectiveBits > 8 ? BitmapFormat.Bgra64 : BitmapFormat.Bgra32);
}
