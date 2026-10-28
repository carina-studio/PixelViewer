using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which renders image with MIPI CSI-2 legacy 8-bit YUV420 format.
/// </summary>
/// <remarks>Every line carries a chroma component followed by Y of 2 pixels for every 2 pixels: U in lines 1, 3, 5… and V in lines 2, 4, 6…. The U and V of a pair of lines are shared by the 2x2 pixels of both lines.</remarks>
class Yuv420Mipi8LegacyImageRenderer() : BaseImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "YUV420_8_LEGACY_MIPI", new ImagePlaneDescriptor(0, 8, 8), [ "YUV420_8_LEGACY_MIPI" ]))
{
	/// <inheritdoc/>
	public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) =>
		[ new(8, 0, ((width & 0x7ffffffe) >> 1) * 3) ];


	/// <inheritdoc/>
	public override int EvaluatePixelCount(IImageDataSource source) =>
		(int)(source.Size * 2 / 3);


	/// <inheritdoc/>
	public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
	{
		width &= 0x7ffffffe;
		height &= 0x7ffffffe;
		if (width <= 0 || height <= 0)
			return 0;
		var rowStride = Math.Max((width >> 1) * 3, planeOptions[0].RowStride);
		return (long)rowStride * height;
	}


	/// <inheritdoc/>
	protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// get state
		var width = bitmapBuffer.Width & 0x7ffffffe;
		var height = bitmapBuffer.Height & 0x7ffffffe;
		var rowStride = planeOptions[0].RowStride;
		if (width <= 0 || height <= 0)
			throw new ArgumentException($"Invalid size: {width}x{height}.");
		if ((width >> 1) * 3 > rowStride)
			throw new ArgumentException($"Invalid row stride: {rowStride}.");

		// select color converter
		var converter = renderingOptions.YuvToBgraConverter ?? YuvToBgraConverter.Default;

		// render
		bitmapBuffer.Memory.Pin(bitmapBaseAddress =>
		{
			var uRow = new byte[rowStride];
			var vRow = new byte[rowStride];
			fixed (byte* uRowPtr = uRow, vRowPtr = vRow)
			{
				var bitmapRowStride = bitmapBuffer.RowBytes;
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				for (var y = 0; y < height; y += 2, bitmapRowPtr += bitmapRowStride * 2)
				{
					// read the pair of lines, missing data is treated as zero
					if (imageStream.ReadAtLeast(uRow, rowStride, throwOnEndOfStream: false) < rowStride)
						Array.Clear(uRow);
					if (imageStream.ReadAtLeast(vRow, rowStride, throwOnEndOfStream: false) < rowStride)
						Array.Clear(vRow);

					// convert both lines with U of the 1st line and V of the 2nd line
					var uGroupPtr = uRowPtr;
					var vGroupPtr = vRowPtr;
					var bitmapPixelPtr1 = bitmapRowPtr;
					var bitmapPixelPtr2 = bitmapRowPtr + bitmapRowStride;
					for (var x = 0; x < width; x += 2, uGroupPtr += 3, vGroupPtr += 3, bitmapPixelPtr1 += 8, bitmapPixelPtr2 += 8)
					{
						var u = uGroupPtr[0];
						var v = vGroupPtr[0];
						converter.ConvertFromYuv422ToBgra32(uGroupPtr[1], uGroupPtr[2], u, v, (uint*)bitmapPixelPtr1, (uint*)(bitmapPixelPtr1 + 4));
						converter.ConvertFromYuv422ToBgra32(vGroupPtr[1], vGroupPtr[2], u, v, (uint*)bitmapPixelPtr2, (uint*)(bitmapPixelPtr2 + 4));
					}

					// check cancellation
					cancellationToken.ThrowIfCancellationRequested();
				}
			}
		});

		// complete
		return new ImageRenderingResult();
	}
}
