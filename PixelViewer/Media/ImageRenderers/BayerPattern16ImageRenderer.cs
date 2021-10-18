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


		/// <summary>
		/// Initialize new <see cref="BayerPattern16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		protected BayerPattern16ImageRenderer(ImageFormat format) : base(format)
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
