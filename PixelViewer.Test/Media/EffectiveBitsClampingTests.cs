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
/// Tests of clamping the values beyond effective bits when rendering image.
/// </summary>
[TestFixture]
class EffectiveBitsClampingTests : BaseTests
{
	// Constants.
	const int TestImageHeight = 16;
	const int TestImageWidth = 16;


	/// <summary>
	/// Test for rendering the values beyond effective bits as the maximum value.
	/// </summary>
	/// <param name="formatName">Name of format to render.</param>
	/// <param name="effectiveBits">Effective bits.</param>
	/// <param name="blackLevel">Black level, or 0 with white level of 0 to render without black and white levels.</param>
	/// <param name="whiteLevel">White level, or 0 to render without black and white levels.</param>
	[TestCase("L16", 10, 0u, 0u)]
	[TestCase("Bayer_Pattern_16", 12, 0u, 0u)]
	[TestCase("Bayer_Pattern_16", 14, 1024u, 16368u)]
	[TestCase("Bayer_Pattern_8", 6, 0u, 0u)]
	[TestCase("Bayer_Pattern_8", 7, 4u, 120u)]
	public void ClampingTest(string formatName, int effectiveBits, uint blackLevel, uint whiteLevel) => this.TestOnApplicationThread(async () =>
	{
		// create data whose values cover the whole range of each pixel, and the same data clamped to effective bits
		await this.InitializeSubSystemsAsync();
		var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
		var pixelStride = renderer.Format.PlaneDescriptors[0].PixelStride;
		var maxValue = (1 << effectiveBits) - 1;
		var values = new int[TestImageWidth * TestImageHeight];
		for (var i = values.Length - 1; i >= 0; --i)
			values[i] = this.Random.Next(0, 1 << (pixelStride << 3));
		values[0] = maxValue + 1;
		var clampedValues = values.Select(it => Math.Min(it, maxValue)).ToArray();

		// render both data
		var colors = await this.RenderAsync(renderer, this.CreateCacheFile(values, pixelStride), effectiveBits, pixelStride, blackLevel, whiteLevel);
		var clampedColors = await this.RenderAsync(renderer, this.CreateCacheFile(clampedValues, pixelStride), effectiveBits, pixelStride, blackLevel, whiteLevel);
		Assert.That(colors, Is.EqualTo(clampedColors), "Values beyond effective bits should be rendered as the maximum value.");
	});


	// Create cache file which contains the values in little-endian, and return its name.
	string CreateCacheFile(int[] values, int pixelStride)
	{
		// the file must be closed before it is opened again as a data source
		using var stream = this.CreateCacheFile();
		var data = new byte[values.Length * pixelStride];
		for (var i = values.Length - 1; i >= 0; --i)
		{
			data[i * pixelStride] = (byte)values[i];
			if (pixelStride > 1)
				data[i * pixelStride + 1] = (byte)(values[i] >> 8);
		}
		stream.Write(data);
		return stream.Name;
	}


	// Render the file by the renderer and return the rendered bitmap.
	async Task<byte[]> RenderAsync(IImageRenderer renderer, string fileName, int effectiveBits, int pixelStride, uint blackLevel, uint whiteLevel)
	{
		// prepare options
		using var source = new FileImageDataSource(this.Application, fileName, true);
		var planeOptions = renderer.CreateDefaultPlaneOptions(TestImageWidth, TestImageHeight);
		planeOptions[0] = whiteLevel > 0
			? new ImagePlaneOptions(effectiveBits, blackLevel, whiteLevel, pixelStride, TestImageWidth * pixelStride)
			: new ImagePlaneOptions(effectiveBits, pixelStride, TestImageWidth * pixelStride);
		var renderingOptions = new ImageRenderingOptions
		{
			BayerPattern = BayerPattern.RGGB_2x2,
			BlueGain = 1.0,
			ByteOrdering = ByteOrdering.LittleEndian,
			Demosaicing = DemosaicingAlgorithms.Bypass,
			GreenGain = 1.0,
			RedGain = 1.0,
		};

		// render
		var format = await renderer.SelectRenderedFormatAsync(source, renderingOptions, planeOptions, CancellationToken.None);
		using var buffer = new BitmapBuffer(format, ColorSpace.Default, TestImageWidth, TestImageHeight);
		await renderer.RenderAsync(source, buffer, renderingOptions, planeOptions, CancellationToken.None);

		// copy rendered bitmap
		var bitmap = new byte[buffer.RowBytes * buffer.Height];
		unsafe
		{
			buffer.Memory.Pin(address => new ReadOnlySpan<byte>((void*)address, bitmap.Length).CopyTo(bitmap));
		}
		return bitmap;
	}
}
