using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// Base implementation of <see cref="IImageRenderer"/> which renders image with bayer filter pattern.
    /// </summary>
    abstract class BayerPatternImageRenderer : SinglePlaneImageRenderer
    {
        // Constants.
        const int BlueColorComponent = 0;
        const int GreenColorComponent = 1;
        const int RedColorComponent = 2;


        // Static fields.
        static readonly Dictionary<BayerPattern, int[][]> ColorPatternMap = new Dictionary<BayerPattern, int[][]>()
        {
            { BayerPattern.BGGR_2x2, new int[][]{
                new int[]{ BlueColorComponent, GreenColorComponent },
                new int[]{ GreenColorComponent, RedColorComponent },
            } },
            { BayerPattern.GBRG_2x2, new int[][]{
                new int[]{ GreenColorComponent, BlueColorComponent },
                new int[]{ RedColorComponent, GreenColorComponent },
            } },
            { BayerPattern.GRBG_2x2, new int[][]{
                new int[]{ GreenColorComponent, RedColorComponent },
                new int[]{ BlueColorComponent, GreenColorComponent },
            } },
            { BayerPattern.RGGB_2x2, new int[][]{
                new int[]{ RedColorComponent, GreenColorComponent },
                new int[]{ GreenColorComponent, BlueColorComponent },
            } },
        };


        /// <summary>
        /// Initialize new <see cref="BayerPatternImageRenderer"/> instance.
        /// </summary>
        /// <param name="format">Format.</param>
        protected BayerPatternImageRenderer(ImageFormat format) : base(format)
        { }


		/// <inheritdoc/>
		protected override unsafe void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			if (width <= 0 || height <= 0)
				throw new ArgumentException($"Invalid size: {width}x{height}.");

			// select color pattern
			var colorPattern = ColorPatternMap[renderingOptions.BayerPattern];
			var colorPatternWidth = colorPattern[0].Length;
			var colorPatternHeight = colorPattern.Length;
			var colorComponentSelector = Global.Run(() =>
			{
				var xMask = colorPatternWidth switch
				{
					2 => 0x1,
					4 => 0x3,
					8 => 0x7,
					_ => 0,
				};
				var yMask = colorPatternHeight switch
				{
					2 => 0x1,
					4 => 0x3,
					8 => 0x7,
					_ => 0,
				};
				if (xMask != 0)
				{
					if (yMask != 0)
						return new Func<int, int, int>((x, y) => colorPattern[y & yMask][x & xMask]);
					return new Func<int, int, int>((x, y) => colorPattern[y % colorPatternHeight][x & xMask]);
				}
				else
				{
					if (yMask != 0)
						return new Func<int, int, int>((x, y) => colorPattern[y & yMask][x % colorPatternWidth]);
					return new Func<int, int, int>((x, y) => colorPattern[y % colorPatternHeight][x % colorPatternWidth]);
				}
			});

			// render
			this.OnRender(source, imageStream, bitmapBuffer, colorComponentSelector, renderingOptions, planeOptions, cancellationToken);
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
						var centerComponent = colorComponentSelector(x, y);

						// collect colors around current pixel
						if (x > 0)
						{
							var neighborComponent = colorComponentSelector(x - 1, y);
							if (neighborComponent != centerComponent)
							{
								accumColors[neighborComponent] += leftBitmapPixelPtr[neighborComponent];
								++colorCounts[neighborComponent];
							}
						}
						if (x < width - 1)
						{
							var neighborComponent = colorComponentSelector(x + 1, y);
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
						var centerComponent = colorComponentSelector(x, y);

						// collect colors around current pixel
						if (y > 0)
						{
							var neighborComponent = colorComponentSelector(x, y - 1);
							if (neighborComponent != centerComponent)
							{
								accumColors[neighborComponent] += ((ushort*)topBitmapPixelPtr)[neighborComponent];
								++colorCounts[neighborComponent];
							}
						}
						if (y < height - 1)
						{
							var neighborComponent = colorComponentSelector(x, y + 1);
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
		/// Called to render image.
		/// </summary>
		/// <param name="source">Source of image data.</param>
		/// <param name="imageStream">Stream to read image data.</param>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> to put rendered bayer pattern image.</param>
		/// <param name="colorComponentSelector">Function to select color component for given pixel poxition.</param>
		/// <param name="renderingOptions">Rendering options.</param>
		/// <param name="planeOptions">Plane options.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		protected abstract void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int,int,int> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


        /// <inheritdoc/>
        public override BitmapFormat RenderedFormat => BitmapFormat.Bgra64;
    }
}
