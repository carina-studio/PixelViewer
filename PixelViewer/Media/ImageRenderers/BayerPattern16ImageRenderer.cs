using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> which renders image with bayer pattern.
	/// </summary>
	abstract class BayerPattern16ImageRenderer : SinglePlaneImageRenderer
	{
		/// <summary>
		/// Color component.
		/// </summary>
		protected enum ColorComponent
		{
			/// <summary>
			/// Red.
			/// </summary>
			Red = 2,
			/// <summary>
			/// Green.
			/// </summary>
			Green = 1,
			/// <summary>
			/// Blue.
			/// </summary>
			Blue = 0,
		}


		// Fields.
		readonly bool isLittleEndian;


		/// <summary>
		/// Initialize new <see cref="BayerPattern16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		/// <param name="isLittleEndian">True to use little-endian for byte ordering.</param>
		protected BayerPattern16ImageRenderer(ImageFormat format, bool isLittleEndian) : base(format)
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
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				byte[] row = new byte[rowStride];
				fixed (byte* rowPtr = row)
				{
					var bitmapRowPtr = (byte*)bitmapBaseAddress;
					for (var y = 0; y < height; ++y, bitmapRowPtr += bitmapBuffer.RowBytes)
					{
						imageStream.Read(row, 0, rowStride);
						var pixelPtr = rowPtr;
						var bitmapPixelPtr = bitmapRowPtr;
						for (var x = 0; x < width; ++x, pixelPtr += pixelStride, bitmapPixelPtr += 4)
						{
							bitmapPixelPtr[(int)this.SelectColorComponent(x, y)] = pixelConversionFunc(pixelPtr[0], pixelPtr[1]);
							bitmapPixelPtr[3] = 255;
						}
						if (cancellationToken.IsCancellationRequested)
							break;
						if (y < height - 1)
							Array.Clear(row, 0, rowStride);
					}
				}
			});
		}


		/// <summary>
		/// Select color component for given pixel.
		/// </summary>
		/// <param name="x">Horizontal position of pixel.</param>
		/// <param name="y">Vertical position of pixel.</param>
		/// <returns>Color component.</returns>
		protected abstract ColorComponent SelectColorComponent(int x, int y);
	}
}
