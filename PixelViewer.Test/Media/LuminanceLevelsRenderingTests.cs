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
/// Tests of applying black and white levels when rendering 16-bit luminance image.
/// </summary>
[TestFixture]
class LuminanceLevelsRenderingTests : BaseTests
{
	// Constants.
	const string FormatName = "L16";
	const int TestImageHeight = 4;
	const int TestImageWidth = 16;


	// Create source whose samples carry increasing values in the range of given effective bits, and return the values of the samples.
	FileImageDataSource CreateSource(int effectiveBits, out int[] values)
	{
		// generate values
		var sampleCount = TestImageWidth * TestImageHeight;
		var maxValue = (1 << effectiveBits) - 1;
		values = new int[sampleCount];
		for (var i = sampleCount - 1; i >= 0; --i)
			values[i] = (int)((long)i * maxValue / (sampleCount - 1));

		// write data
		var data = new byte[sampleCount * 2];
		for (var i = sampleCount - 1; i >= 0; --i)
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
	[Test]
	public void InvalidLevelsTest() => this.TestOnApplicationThread(async () =>
	{
		// prepare
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == FormatName);
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
	/// Test for mapping the black level to 0 and the white level to the maximum luminance, and for clipping the samples beyond them.
	/// </summary>
	/// <param name="effectiveBits">Effective bits of samples.</param>
	[Test]
	public void LevelsTest([Values(12, 14, 16)] int effectiveBits) => this.TestOnApplicationThread(async () =>
	{
		// render with levels
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == FormatName);
		Assert.That(renderer.Format.PlaneDescriptors[0].AreAdjustableBlackWhiteLevels, Is.True, $"Levels of {FormatName} should be adjustable.");
		using var source = this.CreateSource(effectiveBits, out var values);
		var maxValue = (1u << effectiveBits) - 1;
		var blackLevel = maxValue / 16;
		var whiteLevel = maxValue - maxValue / 8;
		var colors = await RenderAsync(renderer, source, effectiveBits, blackLevel, whiteLevel);

		// check colors, the colors of fewer effective bits are expanded from the scaled values so they differ by the step of the bits
		var tolerance = 65536 >> effectiveBits;
		for (var i = TestImageWidth * TestImageHeight - 1; i >= 0; --i)
		{
			var value = values[i];
			for (var c = 0; c < 3; ++c)
			{
				var color = colors[i * 4 + c];
				if (value <= blackLevel)
					Assert.That(color, Is.Zero, $"Channel {c} of pixel {i} below black level should be black.");
				else if (value >= whiteLevel)
					Assert.That(color, Is.EqualTo(65535), $"Channel {c} of pixel {i} above white level should be white.");
				else
					Assert.That(color, Is.EqualTo((value - blackLevel) * 65535.0 / (whiteLevel - blackLevel)).Within(tolerance), $"Channel {c} of pixel {i} should be scaled by levels.");
			}
		}
	});


	/// <summary>
	/// Test for rendering without levels as with the levels of the full range of effective bits.
	/// </summary>
	/// <param name="effectiveBits">Effective bits of samples.</param>
	[Test]
	public void NoLevelsTest([Values(10, 14, 16)] int effectiveBits) => this.TestOnApplicationThread(async () =>
	{
		// render without levels and with the levels of full range
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == FormatName);
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
			ByteOrdering = ByteOrdering.LittleEndian,
		};
		var planeOptions = new List<ImagePlaneOptions>
		{
			new(effectiveBits, 2, TestImageWidth * 2)
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
