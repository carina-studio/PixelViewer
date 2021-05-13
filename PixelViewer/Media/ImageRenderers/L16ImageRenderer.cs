using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> to rendering L16 format image.
	/// </summary>
	abstract class L16ImageRenderer : SinglePlaneImageRenderer
	{
		// Fields.
		readonly bool isLittleEndian;


		/// <summary>
		/// Initiaize new <see cref="L16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Supported format.</param>
		/// <param name="isLittleEndian">True to use little-endian for byte ordering.</param>
		protected L16ImageRenderer(ImageFormat format, bool isLittleEndian) : base(format)
		{
			this.isLittleEndian = isLittleEndian;
		}


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
			if (width <= 0 || height <= 0 || pixelStride <= 0 || (pixelStride * width) > rowStride || effectiveBits <= 8 || effectiveBits > 16)
				return;

			// select byte ordering
			var effectiveBitsShiftCount = (effectiveBits - 8);
			var effectiveBitsMask = (effectiveBits == 16) switch
			{
				true => 0,
				_ => 0xff << effectiveBitsShiftCount,
			};
			Func<byte, byte, byte> pixelConversionFunc = (effectiveBits == 16) switch
			{
				true => this.isLittleEndian switch
				{
					true => (b1, b2) => b2,
					_ => (b1, b2) => b1,
				},
				_ => this.isLittleEndian switch
				{
					true => (b1, b2) => (byte)((((b2 << 8) | b1) & effectiveBitsMask) >> effectiveBitsShiftCount),
					_ => (b1, b2) => (byte)((((b1 << 8) | b2) & effectiveBitsMask) >> effectiveBitsShiftCount),
				},
			};

			// render
			bitmapBuffer.Memory.UnsafeAccess((bitmapBaseAddress) =>
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
