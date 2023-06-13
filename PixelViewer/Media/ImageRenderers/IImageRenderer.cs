using Avalonia;
using Avalonia.Media.Imaging;
using CarinaStudio;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

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
	Task<ImageRenderingResult> RenderAsync(IImageDataSource source, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


	/// <summary>
	/// Get the format rendered by this renderer.
	/// </summary>
	BitmapFormat RenderedFormat { get; }
}


/// <summary>
/// Options of single plane for rendering image.
/// </summary>
struct ImagePlaneOptions : IEquatable<ImagePlaneOptions>
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
		this.BlackLevel = null;
		this.EffectiveBits = effectiveBits;
		this.PixelStride = pixelStride;
		this.RowStride = rowStride;
		this.WhiteLevel = null;
	}


	/// <summary>
	/// Initialize fields in <see cref="ImagePlaneOptions"/> structure.
	/// </summary>
	/// <param name="effectiveBits">Effective bits to render color in each pixel.</param>
	/// <param name="blackLevel">Black level.</param>
	/// <param name="whiteLevel">White level.</param>
	/// <param name="pixelStride">Pixel stride in bytes.</param>
	/// <param name="rowStride">Row stride in bytes.</param>
	public ImagePlaneOptions(int effectiveBits, uint blackLevel, uint whiteLevel, int pixelStride, int rowStride)
	{
		this.BlackLevel = blackLevel;
		this.EffectiveBits = effectiveBits;
		this.PixelStride = pixelStride;
		this.RowStride = rowStride;
		this.WhiteLevel = whiteLevel;
	}


	/// <summary>
	/// Black level.
	/// </summary>
	public uint? BlackLevel { get; set; }


	/// <inheritdoc/>
	public bool Equals(ImagePlaneOptions options) =>
		this.BlackLevel == options.BlackLevel
		&& this.EffectiveBits == options.EffectiveBits
		&& this.PixelStride == options.PixelStride
		&& this.RowStride == options.RowStride
		&& this.WhiteLevel == options.WhiteLevel;


	/// <inheritdoc/>
	public override bool Equals([NotNullWhen(true)] object? obj) =>
		obj is ImagePlaneOptions options && this.Equals(options);


    /// <summary>
    /// Effective bits to render color in each pixel.
    /// </summary>
    public int EffectiveBits { get; set; }


	/// <inheritdoc/>
	public override int GetHashCode() =>
		this.RowStride;


    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(ImagePlaneOptions left, ImagePlaneOptions right) =>
		left.Equals(right);


	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator !=(ImagePlaneOptions left, ImagePlaneOptions right) =>
		!left.Equals(right);


	/// <summary>
	/// Pixel stride in bytes.
	/// </summary>
	public int PixelStride { get; set; }


	/// <summary>
	/// Row stride in bytes.
	/// </summary>
	public int RowStride { get; set; }


	/// <summary>
	/// White level.
	/// </summary>
	public uint? WhiteLevel { get; set; }
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
		int width;
		int height;
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
struct ImageRenderingOptions : IEquatable<ImageRenderingOptions>
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
	/// Gain of blue color when rendering.
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


	/// <inheritdoc/>
	public bool Equals(ImageRenderingOptions options) =>
		this.BayerPattern == options.BayerPattern
		&& Math.Abs(this.BlueGain - options.BlueGain) <= 0.001
		&& this.ByteOrdering == options.ByteOrdering
		&& this.DataOffset == options.DataOffset
		&& this.Demosaicing == options.Demosaicing
		&& Math.Abs(this.GreenGain - options.GreenGain) <= 0.001
		&& Math.Abs(this.RedGain - options.RedGain) <= 0.001
		&& this.YuvToBgraConverter == options.YuvToBgraConverter;


	/// <inheritdoc/>
	public override bool Equals([NotNullWhen(true)] object? obj) =>
		obj is ImageRenderingOptions options && this.Equals(options);


	/// <inheritdoc/>
	public override int GetHashCode() =>
		(((int)this.ByteOrdering & 0xf) << 28) | (((this.YuvToBgraConverter?.GetHashCode() ?? 0) & 0xfff) << 16) | (int)(this.DataOffset & 0xffff);


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
	/// Gain of green color when rendering.
	/// </summary>
	public double GreenGain { get; set; }


	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator ==(ImageRenderingOptions x, ImageRenderingOptions y) =>
		x.Equals(y);


	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator !=(ImageRenderingOptions x, ImageRenderingOptions y) =>
		!x.Equals(y);


	/// <summary>
	/// Gain of red color when rendering.
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
	public bool HasMaxOfRgb => this.MaxOfBlue > 0 || this.MaxOfGreen > 0 || this.MaxOfRed > 0;


	/// <summary>
	/// Check whether mean of RGB is valid or not.
	/// </summary>
	public bool HasMeanOfRgb => double.IsFinite(this.MeanOfBlue) && double.IsFinite(this.MeanOfGreen) && double.IsFinite(this.MeanOfRed);


	/// <summary>
	/// Check whether minimum of RGB is valid or not.
	/// </summary>
	public bool HasMinOfRgb => this.MinOfBlue > 0 || this.MinOfGreen > 0 || this.MinOfRed > 0;


	/// <summary>
	/// Check whether weighted mean of RGB is valid or not.
	/// </summary>
	public bool HasWeightedMeanOfRgb => double.IsFinite(this.WeightedMeanOfBlue) && double.IsFinite(this.WeightedMeanOfGreen) && double.IsFinite(this.WeightedMeanOfRed);


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
