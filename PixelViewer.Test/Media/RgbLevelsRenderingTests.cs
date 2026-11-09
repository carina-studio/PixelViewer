using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of applying black and white levels when rendering 16-bit RGB image.
/// </summary>
[TestFixture]
class RgbLevelsRenderingTests : BaseTests
{
	// Constants.
	const int TestImageHeight = 4;
	const int TestImageWidth = 16;


	// Create source whose components carry increasing values in the range of given effective bits, and return the values of the components.
	FileImageDataSource CreateSource(int effectiveBits, out int[] values)
	{
		// generate values
		var componentCount = TestImageWidth * TestImageHeight * 3;
		var maxValue = (1 << effectiveBits) - 1;
		values = new int[componentCount];
		for (var i = componentCount - 1; i >= 0; --i)
			values[i] = (int)((long)i * maxValue / (componentCount - 1));

		// write data
		var data = new byte[componentCount * 2];
		for (var i = componentCount - 1; i >= 0; --i)
		{
			data[i * 2] = (byte)values[i];
			data[i * 2 + 1] = (byte)(values[i] >> 8);
		}
		string fileName;
		using (var stream = this.CreateCacheFile())
		{
			fileName = stream.Name;
			stream.Write(data);
		}
		return new FileImageDataSource(this.Application, fileName, true);
	}


	/// <summary>
	/// Test for refusing invalid black and white levels.
	/// </summary>
	/// <param name="formatName">Name of format to render.</param>
	[Test]
	public void InvalidLevelsTest([Values("RGB_161616", "BGR_161616")] string formatName) => this.TestOnApplicationThread(async () =>
	{
		// prepare
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
		using var source = this.CreateSource(14, out _);

		// check levels
		foreach (var (blackLevel, whiteLevel) in new (uint, uint)[] { (8000, 8000), (9000, 8000), (0, 16384) })
		{
			var isThrown = false;
			try
			{
				await RenderAsync(renderer, source, 14, blackLevel, whiteLevel);
			}
			catch (ArgumentException)
			{
				isThrown = true;
			}
			Assert.That(isThrown, Is.True, $"Black level {blackLevel} and white level {whiteLevel} should be refused.");
		}
	});


	/// <summary>
	/// Test for mapping the black level to 0 and the white level to the maximum color, and for clipping the components beyond them.
	/// </summary>
	/// <param name="formatName">Name of format to render.</param>
	/// <param name="effectiveBits">Effective bits of components.</param>
	[Test]
	public void LevelsTest([Values("RGB_161616", "BGR_161616")] string formatName, [Values(14, 16)] int effectiveBits) => this.TestOnApplicationThread(async () =>
	{
		// render with levels
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
		Assert.That(renderer.Format.PlaneDescriptors[0].AreAdjustableBlackWhiteLevels, Is.True, $"Levels of {formatName} should be adjustable.");
		using var source = this.CreateSource(effectiveBits, out var values);
		var maxValue = (1u << effectiveBits) - 1;
		var blackLevel = maxValue / 16;
		var whiteLevel = maxValue - maxValue / 8;
		var colors = await RenderAsync(renderer, source, effectiveBits, blackLevel, whiteLevel);

		// check colors, the colors of fewer effective bits are expanded from the scaled values so they differ by the step of the bits
		var tolerance = 65536 >> effectiveBits;
		var isRgb = formatName == "RGB_161616";
		for (var i = TestImageWidth * TestImageHeight - 1; i >= 0; --i)
		{
			for (var c = 0; c < 3; ++c)
			{
				var value = values[i * 3 + c];
				var channel = isRgb ? 2 - c : c;
				var color = colors[i * 4 + channel];
				if (value <= blackLevel)
					Assert.That(color, Is.Zero, $"Component {c} of pixel {i} below black level should be black.");
				else if (value >= whiteLevel)
					Assert.That(color, Is.EqualTo(65535), $"Component {c} of pixel {i} above white level should be white.");
				else
					Assert.That(color, Is.EqualTo((value - blackLevel) * 65535.0 / (whiteLevel - blackLevel)).Within(tolerance), $"Component {c} of pixel {i} should be scaled by levels.");
			}
		}
	});


	/// <summary>
	/// Test for rendering without levels as with the levels of the full range of effective bits.
	/// </summary>
	/// <param name="formatName">Name of format to render.</param>
	/// <param name="effectiveBits">Effective bits of components.</param>
	[Test]
	public void NoLevelsTest([Values("RGB_161616", "BGR_161616")] string formatName, [Values(10, 14, 16)] int effectiveBits) => this.TestOnApplicationThread(async () =>
	{
		// render without levels and with the levels of full range
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
		using var source = this.CreateSource(effectiveBits, out _);
		var colors = await RenderAsync(renderer, source, effectiveBits, null, null);
		var fullRangeColors = await RenderAsync(renderer, source, effectiveBits, 0, (1u << effectiveBits) - 1);

		// compare
		Assert.That(colors, Is.EqualTo(fullRangeColors), "Rendering without levels should be the same as rendering with the levels of full range.");
		Assert.That(colors.Where((_, i) => (i & 3) != 3).Max(), Is.EqualTo(65535), "The largest value of effective bits should be rendered as white.");
	});


	// Render the source with given effective bits and levels, and return the value of each channel of the rendered bitmap.
	static async Task<int[]> RenderAsync(IImageRenderer renderer, IImageDataSource source, int effectiveBits, uint? blackLevel, uint? whiteLevel)
	{
		// render
		var renderingOptions = new ImageRenderingOptions
		{
			BlueGain = 1.0,
			ByteOrdering = ByteOrdering.LittleEndian,
			GreenGain = 1.0,
			RedGain = 1.0,
		};
		var planeOptions = new List<ImagePlaneOptions>
		{
			new(effectiveBits, 6, TestImageWidth * 6)
			{
				BlackLevel = blackLevel,
				WhiteLevel = whiteLevel,
			},
		};
		var format = await renderer.SelectRenderedFormatAsync(source, renderingOptions, planeOptions, CancellationToken.None);
		using var buffer = new BitmapBuffer(format, ColorSpace.Default, TestImageWidth, TestImageHeight);
		await renderer.RenderAsync(source, buffer, renderingOptions, planeOptions, CancellationToken.None);

		// copy the value of each channel of rendered bitmap
		var channelCount = TestImageWidth * TestImageHeight * 4;
		var colors = new int[channelCount];
		unsafe
		{
			buffer.Memory.Pin(address =>
			{
				var pixels = new ReadOnlySpan<ushort>((void*)address, channelCount);
				for (var i = channelCount - 1; i >= 0; --i)
					colors[i] = pixels[i];
			});
		}
		return colors;
	}
}
