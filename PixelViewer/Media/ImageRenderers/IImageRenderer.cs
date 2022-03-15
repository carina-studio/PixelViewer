using Avalonia;
using Avalonia.Media.Imaging;
using CarinaStudio;
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
		/// Evaluate size of data need to be consumed from source.
		/// </summary>
		/// <param name="width">Width of image in pixels.</param>
		/// <param name="height">Height of image in pixels.</param>
		/// <param name="renderingOptions">Rendering options.</param>
		/// <param name="planeOptions">Rendering options for each plane.</param>
		/// <returns>Data size in bytes.</returns>
		long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions);


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
		Task<ImageRenderingResult> Render(IImageDataSource source, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


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
								if (pixelCount <= 0 || pixelCount > maxPixelCount)
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
	struct ImageRenderingOptions
	{
		/// <summary>
		/// Maximum value of <see cref="BlueGain"/>, <see cref="GreenGain"/> and <see cref="RedGain"/>.
		/// </summary>
		public const double MaxRgbGain = 100;


		/// <summary>
		/// Minimum value of <see cref="BlueGain"/>, <see cref="GreenGain"/> and <see cref="RedGain"/>.
		/// </summary>
		public const double MinRgbGain = 0.01;


		/// <summary>
		/// Pattern of Bayer Filter.
		/// </summary>
		public BayerPattern BayerPattern { get; set; }


		/// <summary>
		/// Gain of blue color when renderering.
		/// </summary>
		public double BlueGain { get; set; }


		/// <summary>
		/// Byte ordering.
		/// </summary>
		public ByteOrdering ByteOrdering { get; set; }


		/// <summary>
		/// Offset to first byte of data provided by <see cref="IImageDataSource"/>.
		/// </summary>
		public long DataOffset { get; set; }


		/// <summary>
		/// Whether demosaicing is needed to be performed or not.
		/// </summary>
		public bool Demosaicing { get; set; }


		/// <summary>
		/// Get valid gain of RGB color.
		/// </summary>
		/// <param name="gain">Original gain of RGB color.</param>
		/// <returns>Valid gain of RGB color.</returns>
		public static double GetValidRgbGain(double gain)
		{
			if (!double.IsFinite(gain))
				return 1;
			if (gain < MinRgbGain)
				return MinRgbGain;
			if (gain > MaxRgbGain)
				return MinRgbGain;
			return gain;
		}


		/// <summary>
		/// Gain of green color when renderering.
		/// </summary>
		public double GreenGain { get; set; }


		/// <summary>
		/// Gain of red color when renderering.
		/// </summary>
		public double RedGain { get; set; }


		/// <summary>
		/// YUV to RGB converter.
		/// </summary>
		public YuvToBgraConverter? YuvToBgraConverter { get; set; }
	}


	/// <summary>
	/// Result of image rendering.
	/// </summary>
	struct ImageRenderingResult
	{
		/// <summary>
		/// Initialize fields of <see cref="ImageRenderingResult"/> structure.
		/// </summary>
		public ImageRenderingResult()
		{ }


		/// <summary>
		/// Check whether maximum of RGB is valid or not.
		/// </summary>
		public bool HasMaxOfRgb 
		{
			get => this.MaxOfBlue > 0 || this.MaxOfGreen > 0 || this.MaxOfRed > 0;
		}


		/// <summary>
		/// Check whether mean of RGB is valid or not.
		/// </summary>
		public bool HasMeanOfRgb 
		{
			get => double.IsFinite(this.MeanOfBlue) && double.IsFinite(this.MeanOfGreen) && double.IsFinite(this.MeanOfRed);
		}


		/// <summary>
		/// Check whether minimum of RGB is valid or not.
		/// </summary>
		public bool HasMinOfRgb 
		{
			get => this.MinOfBlue > 0 || this.MinOfGreen > 0 || this.MinOfRed > 0;
		}


		/// <summary>
		/// Check whether weighted mean of RGB is valid or not.
		/// </summary>
		public bool HasWeightedMeanOfRgb 
		{
			get => double.IsFinite(this.WeightedMeanOfBlue) && double.IsFinite(this.WeightedMeanOfGreen) && double.IsFinite(this.WeightedMeanOfRed);
		}


		/// <summary>
		/// Maximum color of blue.
		/// </summary>
		public int MaxOfBlue { get; set; } = 0;


		/// <summary>
		/// Maximum color of green.
		/// </summary>
		public int MaxOfGreen { get; set; } = 0;


		/// <summary>
		/// Maximum color of red.
		/// </summary>
		public int MaxOfRed { get; set; } = 0;


		/// <summary>
		/// Mean of blue color.
		/// </summary>
		public double MeanOfBlue { get; set; } = double.NaN;


		/// <summary>
		/// Mean of green color.
		/// </summary>
		public double MeanOfGreen { get; set; } = double.NaN;


		/// <summary>
		/// Mean of red color.
		/// </summary>
		public double MeanOfRed { get; set; } = double.NaN;


		/// <summary>
		/// Minimum color of blue.
		/// </summary>
		public int MinOfBlue { get; set; } = 0;


		/// <summary>
		/// Minimum color of green.
		/// </summary>
		public int MinOfGreen { get; set; } = 0;


		/// <summary>
		/// Minimum color of red.
		/// </summary>
		public int MinOfRed { get; set; } = 0;


		/// <summary>
		/// Weighted mean of blue color.
		/// </summary>
		public double WeightedMeanOfBlue { get; set; } = double.NaN;


		/// <summary>
		/// Weighted mean of green color.
		/// </summary>
		public double WeightedMeanOfGreen { get; set; } = double.NaN;


		/// <summary>
		/// Weighted mean of red color.
		/// </summary>
		public double WeightedMeanOfRed { get; set; } = double.NaN;
	}
}
