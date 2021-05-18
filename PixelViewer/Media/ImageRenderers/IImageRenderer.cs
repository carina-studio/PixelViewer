using Avalonia;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Object to render image into <see cref="Bitmap"/>.
	/// </summary>
	interface IImageRenderer
	{
		/// <summary>
		/// Create default rendering options for each plane.
		/// </summary>
		/// <param name="width">Width of image in pixels.</param>
		/// <param name="height">Height of pixel in pixels.</param>
		/// <returns>List of <see cref="ImagePlaneOptions"/>.</returns>
		IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height);


		/// <summary>
		/// Evaluate pixel count for data provided by given <see cref="IImageDataSource"/>.
		/// </summary>
		/// <param name="source"><see cref="IImageDataSource"/>.</param>
		/// <returns>Pixel count.</returns>
		int EvaluatePixelCount(IImageDataSource source);


		/// <summary>
		/// Format supported by this renderer.
		/// </summary>
		ImageFormat Format { get; }


		/// <summary>
		/// Start rendering.
		/// </summary>
		/// <param name="source"><see cref="IImageDataSource"/>.</param>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> to receive rendered image. The format of buffer should be same as <see cref="RenderedFormat"/>.</param>
		/// <param name="renderingOptions">Rendering options.</param>
		/// <param name="planeOptions">Rendering options for each plane.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task Render(IImageDataSource source, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


		/// <summary>
		/// Get the format rendered by this renderer.
		/// </summary>
		BitmapFormat RenderedFormat { get; }
	}


	/// <summary>
	/// Options of single plane for rendering image.
	/// </summary>
	struct ImagePlaneOptions
	{
		/// <summary>
		/// Initialize fields in <see cref="ImagePlaneOptions"/> structure.
		/// </summary>
		/// <param name="pixelStride">Pixel stride in bytes.</param>
		/// <param name="rowStride">Row stride in bytes.</param>
		public ImagePlaneOptions(int pixelStride, int rowStride) : this(pixelStride << 3, pixelStride, rowStride)
		{ }


		/// <summary>
		/// Initialize fields in <see cref="ImagePlaneOptions"/> structure.
		/// </summary>
		/// <param name="effectiveBits">Effective bits to render color in each pixel.</param>
		/// <param name="pixelStride">Pixel stride in bytes.</param>
		/// <param name="rowStride">Row stride in bytes.</param>
		public ImagePlaneOptions(int effectiveBits, int pixelStride, int rowStride)
		{
			this.EffectiveBits = effectiveBits;
			this.PixelStride = pixelStride;
			this.RowStride = rowStride;
		}


		/// <summary>
		/// Effective bits to render color in each pixel.
		/// </summary>
		public int EffectiveBits { get; set; }


		/// <summary>
		/// Pixel stride in bytes.
		/// </summary>
		public int PixelStride { get; set; }


		/// <summary>
		/// Row stride in bytes.
		/// </summary>
		public int RowStride { get; set; }
	}


	/// <summary>
	/// Extensions for <see cref="IImageRenderer"/>.
	/// </summary>
	static class ImageRendererExtensions
	{
		// Static fields.
		static readonly Regex NumberRegex = new Regex("[\\d]+");


		/// <summary>
		/// Evaluate rendering dimensions for given <see cref="IImageDataSource"/>.
		/// </summary>
		/// <param name="renderer"><see cref="IImageRenderer"/>.</param>
		/// <param name="source"><see cref="IImageDataSource"/>.</param>
		/// <param name="aspectRatio">Preferred aspect ratio.</param>
		/// <param name="alignment">Size alignment.</param>
		/// <returns>Evaluated dimensions, or null if unable to evaluate.</returns>
		public static PixelSize? EvaluateDimensions(this IImageRenderer renderer, IImageDataSource source, AspectRatio aspectRatio, int alignment = 16)
		{
			// check pixel count
			int maxPixelCount = renderer.EvaluatePixelCount(source);
			if (maxPixelCount <= 0)
				return null;

			// evaluate by file name
			var width = 0;
			var height = 0;
			if (aspectRatio == AspectRatio.Unknown)
			{
				if (source is FileImageDataSource fileImageDataSource)
				{
					var match = NumberRegex.Match(fileImageDataSource.FileName);
					if (match.Success && int.TryParse(match.Value, out width))
					{
						var minPixelCountDiff = int.MaxValue;
						PixelSize? nearestDimension = null;
						match = match.NextMatch();
						while (match.Success && int.TryParse(match.Value, out height))
						{
							try
							{
								var pixelCount = (width * height);
								if (pixelCount <= 0 || pixelCount > maxPixelCount || (double)pixelCount / maxPixelCount < 0.8)
									continue;
								var diff = (maxPixelCount - pixelCount);
								if (diff < minPixelCountDiff)
								{
									nearestDimension = new PixelSize(width, height);
									minPixelCountDiff = diff;
								}
							}
							finally
							{
								match = match.NextMatch();
								width = height;
							}
						}
						return nearestDimension;
					}
				}
				return null;
			}

			// evaluate by aspect ratio
			if (alignment < 0)
				throw new ArgumentOutOfRangeException(nameof(alignment));
			var ratio = aspectRatio.CalculateRatio();
			if (double.IsNaN(ratio))
				return null;
			height = (int)Math.Sqrt(maxPixelCount / ratio);
			width = (int)(height * ratio);
			return new PixelSize(
				width.Let((it) =>
				{
					if (alignment == 0)
						return it;
					return Math.Max(1, it - (it % alignment));
				}),
				height.Let((it) =>
				{
					if (alignment == 0)
						return it;
					return Math.Max(1, it - (it % alignment));
				})
			);
		}
	}


	/// <summary>
	/// Options for rendering image.
	/// </summary>
	public struct ImageRenderingOptions
	{ }
}
