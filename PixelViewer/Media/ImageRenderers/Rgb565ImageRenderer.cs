﻿using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> to rendering RGB_565 format image.
	/// </summary>
	class Rgb565ImageRenderer : SinglePlaneImageRenderer
	{
		/// <summary>
		/// Initiaize new <see cref="Rgb565ImageRenderer"/> instance.
		/// </summary>
		public Rgb565ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "RGB_565", true, new ImagePlaneDescriptor(2), new string[]{ "RGB565", "RGB_565", "RGB16" }))
		{ }


		// Render.
		protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			var pixelStride = planeOptions[0].PixelStride;
			var rowStride = planeOptions[0].RowStride;
			if (width <= 0 || height <= 0 || pixelStride <= 0 || (pixelStride * width) > rowStride)
				throw new ArgumentException("Invalid dimensions, pixel-stride or row-stride.");

			// select byte ordering
			Func<byte, byte, int> pixelConversionFunc = renderingOptions.ByteOrdering switch
			{
				ByteOrdering.LittleEndian => (b1, b2) => (b2 << 8) | b1,
				_ => (b1, b2) => (b1 << 8) | b2,
			};

			// render
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				byte[] row = new byte[rowStride];
				fixed (byte* rowPtr = row)
				{
					var bitmapRowPtr = (byte*)bitmapBaseAddress;
					for (var y = height; y > 0; --y, bitmapRowPtr += bitmapBuffer.RowBytes)
					{
						var isLastRow = (imageStream.Read(row, 0, rowStride) < rowStride || y == 1);
						var pixelPtr = rowPtr;
						var bitmapPixelPtr = bitmapRowPtr;
						for (var x = width; x > 0; --x, pixelPtr += pixelStride, bitmapPixelPtr += 4)
						{
							var rgb565 = pixelConversionFunc(pixelPtr[0], pixelPtr[1]);
							var r = (rgb565 >> 11) & 0x1f;
							var g = (rgb565 >> 5) & 0x3f;
							var b = rgb565 & 0x1f;
							bitmapPixelPtr[0] = (byte)((b << 3) | (b >> 2)); // extend from 5 bits to 8 bits
							bitmapPixelPtr[1] = (byte)((g << 2) | (g >> 4)); // extend from 6 bits to 8 bits
							bitmapPixelPtr[2] = (byte)((r << 3) | (r >> 2)); // extend from 5 bits to 8 bits
							bitmapPixelPtr[3] = 255;
						}
						if (isLastRow || cancellationToken.IsCancellationRequested)
							break;
						if (!isLastRow)
							Array.Clear(row, 0, rowStride);
					}
				}
			});

			// complete
			return new ImageRenderingResult();
		}
	}
}
