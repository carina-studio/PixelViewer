using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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

			// prepare conversion
			var extractFunc = this.Create16BitColorExtraction(renderingOptions.ByteOrdering, effectiveBits);

			// render
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				// render to 8-bit R/G/B
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				var bitmapRowStride = bitmapBuffer.RowBytes;
				byte[] row = new byte[rowStride];
				fixed (byte* rowPtr = row)
				{
					for (var y = 0; y < height; ++y, bitmapRowPtr += bitmapRowStride)
					{
						imageStream.Read(row, 0, rowStride);
						var pixelPtr = rowPtr;
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						for (var x = 0; x < width; ++x, pixelPtr += pixelStride, bitmapPixelPtr += 4)
						{
							bitmapPixelPtr[(int)this.SelectColorComponent(x, y)] = extractFunc(pixelPtr[0], pixelPtr[1]);
							bitmapPixelPtr[3] = 65535;
						}
						if (cancellationToken.IsCancellationRequested)
							break;
						if (y < height - 1)
							Array.Clear(row, 0, rowStride);
					}
				}

				// horizontal demosaicing
				if (cancellationToken.IsCancellationRequested || !renderingOptions.Demosaicing)
					return;
				Parallel.For(0, height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
				{
					var accumColors = stackalloc int[3];
					var colorCounts = stackalloc int[3];
					var bitmapPixelPtr = (ushort*)((byte*)bitmapBaseAddress + bitmapRowStride * y);
					var leftBitmapPixelPtr = (ushort*)null;
					var rightBitmapPixelPtr = (bitmapPixelPtr + 4);
					for (var x = 0; x < width; ++x, leftBitmapPixelPtr = bitmapPixelPtr, bitmapPixelPtr = rightBitmapPixelPtr, rightBitmapPixelPtr += 4)
					{
						// get component at current pixel
						var centerComponent = (int)this.SelectColorComponent(x, y);

						// collect colors around current pixel
						if (x > 0)
						{
							var neighborComponent = (int)this.SelectColorComponent(x - 1, y);
							if (neighborComponent != centerComponent)
							{
								accumColors[neighborComponent] += leftBitmapPixelPtr[neighborComponent];
								++colorCounts[neighborComponent];
							}
						}
						if (x < width - 1)
						{
							var neighborComponent = (int)this.SelectColorComponent(x + 1, y);
							if (neighborComponent != centerComponent)
							{
								accumColors[neighborComponent] += rightBitmapPixelPtr[neighborComponent];
								++colorCounts[neighborComponent];
							}
						}

						// combine to full RGB color
						for (var i = 2; i >= 0; --i)
						{
							if (i != centerComponent && colorCounts[i] > 0)
								bitmapPixelPtr[i] = (ushort)(accumColors[i] / colorCounts[i]);
							accumColors[i] = 0;
							colorCounts[i] = 0;
						}
					}
					if (cancellationToken.IsCancellationRequested)
						return;
				});

				// vertical demosaicing
				if (cancellationToken.IsCancellationRequested)
					return;
				Parallel.For(0, width, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (x) =>
				{
					var accumColors = stackalloc int[3];
					var colorCounts = stackalloc int[3];
					var bitmapPixelPtr = ((byte*)bitmapBaseAddress + x * sizeof(ulong));
					var topBitmapPixelPtr = (bitmapPixelPtr - bitmapRowStride);
					var bottomBitmapPixelPtr = (bitmapPixelPtr + bitmapRowStride);
					for (var y = 0; y < height; ++y, bitmapPixelPtr += bitmapRowStride, topBitmapPixelPtr += bitmapRowStride, bottomBitmapPixelPtr += bitmapRowStride)
					{
						// get component at current pixel
						var centerComponent = (int)this.SelectColorComponent(x, y);

						// collect colors around current pixel
						if (y > 0)
						{
							var neighborComponent = (int)this.SelectColorComponent(x, y - 1);
							if (neighborComponent != centerComponent)
							{
								accumColors[neighborComponent] += ((ushort*)topBitmapPixelPtr)[neighborComponent];
								++colorCounts[neighborComponent];
							}
						}
						if (y < height - 1)
						{
							var neighborComponent = (int)this.SelectColorComponent(x, y + 1);
							if (neighborComponent != centerComponent)
							{
								accumColors[neighborComponent] += ((ushort*)bottomBitmapPixelPtr)[neighborComponent];
								++colorCounts[neighborComponent];
							}
						}

						// combine to full RGB color
						for (var i = 2; i >= 0; --i)
						{
							if (i != centerComponent && colorCounts[i] > 0)
								((ushort*)bitmapPixelPtr)[i] = (ushort)(accumColors[i] / colorCounts[i]);
							accumColors[i] = 0;
							colorCounts[i] = 0;
						}
					}
					if (cancellationToken.IsCancellationRequested)
						return;
				});
			});
		}


		// Rendered format.
		public override BitmapFormat RenderedFormat => BitmapFormat.Bgra64;


        /// <summary>
        /// Select color component for given pixel.
        /// </summary>
        /// <param name="x">Horizontal position of pixel.</param>
        /// <param name="y">Vertical position of pixel.</param>
        /// <returns>Color component.</returns>
        protected abstract ColorComponent SelectColorComponent(int x, int y);
	}
}
