using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which renders image with MIPI CSI-2 10-bit YUV422 format.
/// </summary>
/// <remarks>Each line carries U, Y, V, Y of every 2 pixels in 5 bytes: the 8 most significant bits of each component in the first 4 bytes, followed by 1 byte which carries the 2 least significant bits of each component, the first component in the lowest bits.</remarks>
class Yuv422Mipi10ImageRenderer() : BaseImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "YUV422_10_MIPI", new ImagePlaneDescriptor(0, 10, 10), [ "YUV422_10_MIPI" ]))
{
	/// <inheritdoc/>
	public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) =>
		[ new(10, 0, ((width & 0x7ffffffe) >> 1) * 5) ];


	/// <inheritdoc/>
	public override int EvaluatePixelCount(IImageDataSource source) =>
		(int)(source.Size * 2 / 5);


	/// <inheritdoc/>
	public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
	{
		width &= 0x7ffffffe;
		if (width <= 0 || height <= 0)
			return 0;
		var rowStride = Math.Max((width >> 1) * 5, planeOptions[0].RowStride);
		return (long)rowStride * height;
	}


	/// <inheritdoc/>
	protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// get state
		var width = bitmapBuffer.Width & 0x7ffffffe;
		var height = bitmapBuffer.Height;
		var rowStride = planeOptions[0].RowStride;
		if (width <= 0 || height <= 0)
			throw new ArgumentException($"Invalid size: {width}x{height}.");
		if ((width >> 1) * 5 > rowStride)
			throw new ArgumentException($"Invalid row stride: {rowStride}.");

		// select color converter
		var converter = renderingOptions.YuvToBgraConverter ?? YuvToBgraConverter.Default;

		// render
		bitmapBuffer.Memory.Pin(bitmapBaseAddress =>
		{
			var row = new byte[rowStride];
			var yComponents = new ushort[width];
			var uComponents = new ushort[width >> 1];
			var vComponents = new ushort[width >> 1];
			fixed (byte* rowPtr = row)
			fixed (ushort* yPtr = yComponents, uPtr = uComponents, vPtr = vComponents)
			{
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				for (var y = 0; y < height; ++y, bitmapRowPtr += bitmapBuffer.RowBytes)
				{
					// read line, missing data is treated as zero
					if (imageStream.ReadAtLeast(row, rowStride, throwOnEndOfStream: false) < rowStride)
						Array.Clear(row);

					// convert line
					ReadPackedUyvyRow(rowPtr, width, yPtr, uPtr, vPtr);
					var bitmapPixelPtr = bitmapRowPtr;
					for (var x = 0; x < width; x += 2, bitmapPixelPtr += 16)
						converter.ConvertFromYuv422ToBgra64(yPtr[x], yPtr[x + 1], uPtr[x >> 1], vPtr[x >> 1], (ulong*)bitmapPixelPtr, (ulong*)(bitmapPixelPtr + 8));

					// check cancellation
					cancellationToken.ThrowIfCancellationRequested();
				}
			}
		});

		// complete
		return new ImageRenderingResult();
	}


	/// <summary>
	/// Read Y, U and V components from a line packed in the MIPI CSI-2 10-bit YUV422 format.
	/// </summary>
	/// <param name="row">Pointer to data of the line.</param>
	/// <param name="width">Width of image, which is a multiple of 2.</param>
	/// <param name="y">Pointer to buffer to receive <paramref name="width"/> Y components.</param>
	/// <param name="u">Pointer to buffer to receive <paramref name="width"/>/2 U components.</param>
	/// <param name="v">Pointer to buffer to receive <paramref name="width"/>/2 V components.</param>
	/// <remarks>Components are scaled to 16-bit. The line which carries U, Y, V, Y in the MIPI CSI-2 10-bit YUV420 format is packed the same way.</remarks>
	internal static unsafe void ReadPackedUyvyRow(byte* row, int width, ushort* y, ushort* u, ushort* v)
	{
		for (var x = 0; x < width; x += 2, row += 5, y += 2, ++u, ++v)
		{
			var lsbs = row[4];
			*u = ScaleTo16Bit((row[0] << 2) | (lsbs & 0x3));
			y[0] = ScaleTo16Bit((row[1] << 2) | ((lsbs >> 2) & 0x3));
			*v = ScaleTo16Bit((row[2] << 2) | ((lsbs >> 4) & 0x3));
			y[1] = ScaleTo16Bit((row[3] << 2) | (lsbs >> 6));
		}
	}


	/// <summary>
	/// Scale 10-bit component to 16-bit.
	/// </summary>
	/// <param name="value">10-bit component.</param>
	/// <returns>16-bit component.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static ushort ScaleTo16Bit(int value) =>
		(ushort)((value << 6) | (value >> 4));


	/// <inheritdoc/>
	public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
		Task.FromResult(BitmapFormat.Bgra64);
}
