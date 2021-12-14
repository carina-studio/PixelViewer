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
	abstract class BayerPatternImageRenderer : SinglePlaneImageRenderer
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
		/// Initialize new <see cref="BayerPatternImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		protected BayerPatternImageRenderer(ImageFormat format) : base(format)
		{ }


		// Render.
		protected override unsafe void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			if (width <= 0 || height <= 0)
				throw new ArgumentException($"Invalid size: {width}x{height}.");

			// render
			this.OnRenderBayerPatternImage(source, imageStream, bitmapBuffer, renderingOptions, planeOptions, cancellationToken);
			if (cancellationToken.IsCancellationRequested)
				return;

			// demosaicing
			if (!renderingOptions.Demosaicing)
				return;
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				// horizontal demosaicing
				var bitmapRowStride = bitmapBuffer.RowBytes;
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


		/// <summary>
		/// Called to render image in bayer pattern.
		/// </summary>
		/// <param name="source">Source of image data.</param>
		/// <param name="imageStream">Stream to read image data.</param>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> to put rendered bayer pattern image.</param>
		/// <param name="renderingOptions">Rendering options.</param>
		/// <param name="planeOptions">Plane options.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		protected abstract void OnRenderBayerPatternImage(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


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
