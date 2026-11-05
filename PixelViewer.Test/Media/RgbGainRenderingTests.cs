using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.Demosaicing;
using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of applying RGB gains to linear colors when rendering image.
/// </summary>
[TestFixture]
class RgbGainRenderingTests : BaseTests
{
	/// <summary>
	/// Color transfer whose linear color is the square of color.
	/// </summary>
	class SquareColorTransfer
	{
		/// <summary>
		/// Transfer linear color to color.
		/// </summary>
		/// <param name="color">Normalized linear color.</param>
		/// <returns>Normalized color.</returns>
		public double FromLinear(double color) => Math.Sqrt(color);

		/// <summary>
		/// Transfer color to linear color.
		/// </summary>
		/// <param name="color">Normalized color.</param>
		/// <returns>Normalized linear color.</returns>
		public double ToLinear(double color) => color * color;
	}


	// Constants.
	const int TestImageHeight = 16;
	const int TestImageWidth = 16;


	/// <summary>
	/// Test for comparing the color transfer functions of rendering options.
	/// </summary>
	[Test]
	public void ColorTransferFuncEqualityTest()
	{
		// create options with the functions of the same transfer
		var transfer = new SquareColorTransfer();
		var options = CreateRenderingOptions(transfer);
		Assert.That(options.HasColorTransferFunctions, Is.True, "Color transfer functions should be defined.");
		Assert.That(CreateRenderingOptions(transfer), Is.EqualTo(options), "Functions of the same transfer should be treated as the same.");

		// compare with options with the functions of another transfer or without functions
		Assert.That(CreateRenderingOptions(new SquareColorTransfer()), Is.Not.EqualTo(options), "Functions of another transfer should be treated as different.");
		Assert.That(CreateRenderingOptions(null), Is.Not.EqualTo(options), "Options without functions should be treated as different.");
		Assert.That(CreateRenderingOptions(null).HasColorTransferFunctions, Is.False, "Color transfer functions should not be defined.");
	}


	// Create cache file which contains the data, and return its name.
	string CreateCacheFile(byte[] data)
	{
		// the file must be closed before it is opened again as a data source
		using var stream = this.CreateCacheFile();
		stream.Write(data);
		return stream.Name;
	}


	// Create rendering options with color transfer functions of given transfer.
	static ImageRenderingOptions CreateRenderingOptions(SquareColorTransfer? transfer, double blueGain = 1.0, double greenGain = 1.0, double redGain = 1.0) => new()
	{
		BayerPattern = BayerPattern.RGGB_2x2,
		BlueGain = blueGain,
		ByteOrdering = ByteOrdering.LittleEndian,
		ColorTransferFuncFromLinear = transfer is not null ? transfer.FromLinear : null,
		ColorTransferFuncToLinear = transfer is not null ? transfer.ToLinear : null,
		Demosaicing = DemosaicingAlgorithms.Bypass,
		GreenGain = greenGain,
		RedGain = redGain,
	};


	// Create source which contains random data to be rendered by the renderer.
	FileImageDataSource CreateSource(IImageRenderer renderer)
	{
		// create data, half-float colors are kept in [0, 1] so that they are not clipped
		var planeOptions = renderer.CreateDefaultPlaneOptions(TestImageWidth, TestImageHeight);
		var data = new byte[renderer.EvaluateSourceDataSize(TestImageWidth, TestImageHeight, new ImageRenderingOptions(), planeOptions)];
		if (IsHalfFloatFormat(renderer))
		{
			for (var i = data.Length - 2; i >= 0; i -= 2)
			{
				var bits = BitConverter.HalfToUInt16Bits((Half)this.Random.NextDouble());
				data[i] = (byte)bits;
				data[i + 1] = (byte)(bits >> 8);
			}
		}
		else
			this.Random.NextBytes(data);

		// create source
		return new FileImageDataSource(this.Application, this.CreateCacheFile(data), true);
	}


	/// <summary>
	/// Test for applying RGB gains to linear colors.
	/// </summary>
	/// <param name="formatName">Name of format to render.</param>
	[TestCase("Bayer_Pattern_8")]
	[TestCase("Bayer_Pattern_16")]
	[TestCase("Bayer_Pattern_10_MIPI")]
	[TestCase("Bayer_Pattern_12_MIPI")]
	[TestCase("Bayer_Pattern_14_MIPI")]
	[TestCase("Bayer_Pattern_10_Packed")]
	[TestCase("Bayer_Pattern_12_Packed")]
	[TestCase("Bayer_Pattern_14_Packed")]
	[TestCase("ABGR_F16")]
	[TestCase("ARGB_16161616")]
	[TestCase("ARGB_2101010")]
	[TestCase("ARGB_8888")]
	[TestCase("RGB_161616")]
	[TestCase("RGB_565")]
	[TestCase("RGB_888")]
	public void GainTest(string formatName) => this.TestOnApplicationThread(async () =>
	{
		// render without gains and with gains applied to linear colors
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
		using var source = this.CreateSource(renderer);
		var transfer = new SquareColorTransfer();
		var gains = new[] { 0.5, 1.0, 2.0 };
		var (format, colors, _) = await RenderAsync(renderer, source, CreateRenderingOptions(null));
		var (_, transferredColors, _) = await RenderAsync(renderer, source, CreateRenderingOptions(transfer));
		var (_, gainedColors, _) = await RenderAsync(renderer, source, CreateRenderingOptions(transfer, gains[0], gains[1], gains[2]));
		Assert.That(transferredColors, Is.EqualTo(colors), "Colors should not be changed by color transfer functions without gains.");

		// check that the gains are applied to linear colors, half-float colors are rendered from the exact colors instead of the truncated ones so they may differ by 2
		var maxColor = format == BitmapFormat.Bgra64 ? 65535 : 255;
		var tolerance = IsHalfFloatFormat(renderer) ? 2 : 1;
		for (var y = TestImageHeight - 1; y >= 0; --y)
		{
			for (var x = TestImageWidth - 1; x >= 0; --x)
			{
				foreach (var colorComponent in SelectColorComponents(renderer, x, y))
				{
					var index = (y * TestImageWidth + x) * 4 + colorComponent;
					var linearColor = Math.Min(1, transfer.ToLinear(colors[index] / (double)maxColor) * gains[colorComponent]);
					var expectedColor = (int)Math.Min(maxColor, transfer.FromLinear(linearColor) * maxColor + 0.5);
					Assert.That(gainedColors[index], Is.EqualTo(expectedColor).Within(tolerance), $"Gain should be applied to linear color {colorComponent} at ({x}, {y}).");
				}
			}
		}
	});


	/// <summary>
	/// Test for checking whether RGB gains change the colors or not, and whether the smallest step of gains is treated as a change.
	/// </summary>
	/// <param name="gain">Gain.</param>
	/// <param name="expectedResult">Expected result.</param>
	[TestCase(1.0, false)]
	[TestCase(1.000005, false)]
	[TestCase(0.999995, false)]
	[TestCase(1.0001, true)]
	[TestCase(0.9999, true)]
	[TestCase(double.NaN, false)]
	[TestCase(0.0, true)]
	public void HasRgbGainTest(double gain, bool expectedResult)
	{
		// check each gain
		Assert.That(CreateRenderingOptions(null, blueGain: gain).HasRgbGain, Is.EqualTo(expectedResult), $"Incorrect result for blue gain {gain}.");
		Assert.That(CreateRenderingOptions(null, greenGain: gain).HasRgbGain, Is.EqualTo(expectedResult), $"Incorrect result for green gain {gain}.");
		Assert.That(CreateRenderingOptions(null, redGain: gain).HasRgbGain, Is.EqualTo(expectedResult), $"Incorrect result for red gain {gain}.");

		// check that the options are different from the ones without gain
		if (double.IsFinite(gain))
			Assert.That(CreateRenderingOptions(null, redGain: gain) == CreateRenderingOptions(null), Is.EqualTo(!expectedResult), $"Incorrect equality for red gain {gain}.");
	}


	// Check whether colors of the format rendered by the renderer are half-float or not.
	static bool IsHalfFloatFormat(IImageRenderer renderer) =>
		renderer.Format.Name.EndsWith("_F16");


	/// <summary>
	/// Test for calculating means of linear colors.
	/// </summary>
	/// <param name="formatName">Name of format to render.</param>
	[TestCase("Bayer_Pattern_8")]
	[TestCase("Bayer_Pattern_16")]
	[TestCase("ABGR_F16")]
	[TestCase("ARGB_2101010")]
	[TestCase("RGB_565")]
	[TestCase("RGB_888")]
	public void LinearMeansTest(string formatName) => this.TestOnApplicationThread(async () =>
	{
		// render with color transfer functions
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
		using var source = this.CreateSource(renderer);
		var transfer = new SquareColorTransfer();
		var (format, colors, result) = await RenderAsync(renderer, source, CreateRenderingOptions(transfer));

		// calculate means of linear colors, a pixel in the central area is counted twice by the weighted means
		var maxColor = format == BitmapFormat.Bgra64 ? 65535 : 255;
		var sums = new double[3];
		var counts = new int[3];
		var weightedSums = new double[3];
		var weightedCounts = new int[3];
		var left = TestImageWidth / 3;
		var right = TestImageWidth - left;
		var top = TestImageHeight / 3;
		var bottom = TestImageHeight - top;
		for (var y = TestImageHeight - 1; y >= 0; --y)
		{
			for (var x = TestImageWidth - 1; x >= 0; --x)
			{
				var weight = (x >= left && x <= right && y >= top && y <= bottom) ? 2 : 1;
				foreach (var colorComponent in SelectColorComponents(renderer, x, y))
				{
					var linearColor = (int)(transfer.ToLinear(colors[(y * TestImageWidth + x) * 4 + colorComponent] / (double)maxColor) * 65535 + 0.5);
					sums[colorComponent] += linearColor;
					++counts[colorComponent];
					weightedSums[colorComponent] += linearColor * weight;
					weightedCounts[colorComponent] += weight;
				}
			}
		}

		// check means
		Assert.That(result.MeanOfBlue, Is.EqualTo(sums[0] / counts[0]).Within(0.001), "Incorrect mean of blue.");
		Assert.That(result.MeanOfGreen, Is.EqualTo(sums[1] / counts[1]).Within(0.001), "Incorrect mean of green.");
		Assert.That(result.MeanOfRed, Is.EqualTo(sums[2] / counts[2]).Within(0.001), "Incorrect mean of red.");
		Assert.That(result.WeightedMeanOfBlue, Is.EqualTo(weightedSums[0] / weightedCounts[0]).Within(0.001), "Incorrect weighted mean of blue.");
		Assert.That(result.WeightedMeanOfGreen, Is.EqualTo(weightedSums[1] / weightedCounts[1]).Within(0.001), "Incorrect weighted mean of green.");
		Assert.That(result.WeightedMeanOfRed, Is.EqualTo(weightedSums[2] / weightedCounts[2]).Within(0.001), "Incorrect weighted mean of red.");
	});


	// Render the source by the renderer and return the rendered format, the value of each channel of the rendered bitmap and the rendering result.
	static async Task<(BitmapFormat, int[], ImageRenderingResult)> RenderAsync(IImageRenderer renderer, IImageDataSource source, ImageRenderingOptions renderingOptions)
	{
		// render
		var planeOptions = renderer.CreateDefaultPlaneOptions(TestImageWidth, TestImageHeight);
		var format = await renderer.SelectRenderedFormatAsync(source, renderingOptions, planeOptions, CancellationToken.None);
		using var buffer = new BitmapBuffer(format, ColorSpace.Default, TestImageWidth, TestImageHeight);
		var result = await renderer.RenderAsync(source, buffer, renderingOptions, planeOptions, CancellationToken.None);

		// copy the value of each channel of rendered bitmap
		var channelCount = TestImageWidth * TestImageHeight * 4;
		var colors = new int[channelCount];
		unsafe
		{
			buffer.Memory.Pin(address =>
			{
				if (format == BitmapFormat.Bgra64)
				{
					var pixels = new ReadOnlySpan<ushort>((void*)address, channelCount);
					for (var i = channelCount - 1; i >= 0; --i)
						colors[i] = pixels[i];
				}
				else
				{
					var pixels = new ReadOnlySpan<byte>((void*)address, channelCount);
					for (var i = channelCount - 1; i >= 0; --i)
						colors[i] = pixels[i];
				}
			});
		}
		return (format, colors, result);
	}


	// Select the offsets of color channels in a BGRA pixel rendered by the renderer which carry colors of source.
	static int[] SelectColorComponents(IImageRenderer renderer, int x, int y) => renderer.Format.Category == ImageFormatCategory.Bayer
		? [ (int)BayerPattern.RGGB_2x2.CreateColorComponentSelector()(x, y) ]
		: [ 0, 1, 2 ];


	/// <summary>
	/// Test for checking which built-in renderers apply RGB gains.
	/// </summary>
	[Test]
	public void SupportTest() => this.TestOnApplicationThread(async () =>
	{
		await this.InitializeSubSystemsAsync();
		foreach (var renderer in ImageRenderers.All.Where(it => it.IsBuiltIn))
		{
			var expectedResult = renderer.Format.Category is ImageFormatCategory.ARGB
				or ImageFormatCategory.Bayer
				or ImageFormatCategory.RGB;
			Assert.That(renderer.IsRgbGainSupported, Is.EqualTo(expectedResult), $"Incorrect support of RGB gains of {renderer.Format.Name}.");
		}
	});
}
