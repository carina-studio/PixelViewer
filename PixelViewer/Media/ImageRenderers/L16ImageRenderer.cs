using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> to rendering L16 format image.
	/// </summary>
	class L16ImageRenderer : SinglePlaneImageRenderer
	{
		/// <summary>
		/// Initiaize new <see cref="L16ImageRenderer"/> instance.
		/// </summary>
		public L16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Luminance, "L16", true, new ImagePlaneDescriptor(2, 9, 16)))
		{ }


		// Create default plane options.
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also((it) =>
		{
			it.Add(new ImagePlaneOptions(16, 2, width * 2));
		});


		// Render.
		protected override unsafe void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
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
			if (effectiveBits <= 8 || effectiveBits > 16)
				throw new ArgumentException($"Invalid effective bits: {effectiveBits}.");

			// select byte ordering
			var pixelConversionFunc = this.Create16BitsTo8BitsConversion(renderingOptions.ByteOrdering, effectiveBits);

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
							var l8 = pixelConversionFunc(pixelPtr[0], pixelPtr[1]);
							bitmapPixelPtr[0] = l8;
							bitmapPixelPtr[1] = l8;
							bitmapPixelPtr[2] = l8;
							bitmapPixelPtr[3] = 255;
						}
						if (isLastRow || cancellationToken.IsCancellationRequested)
							break;
						if (!isLastRow)
							Array.Clear(row, 0, rowStride);
					}
				}
			});
		}
	}
}
