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
/// Tests of rendering Bayer pattern images packed in the MIPI RAW10, RAW12 and RAW14 layouts.
/// </summary>
[TestFixture]
class MipiImageRenderersTests : BaseTests
{
	// Constants.
	const uint LevelMargin = 64;
	const int TestImageHeight = 4;
	const int TestImageWidth = 8;


	// Create a data source which provides the given values packed in the layout of Android RAW10, RAW12 or RAW14.
	IImageDataSource CreateTestImageDataSource(int[] values, int bits, ByteOrdering byteOrdering)
	{
		// each group of pixels takes one byte per pixel, followed by the extra bits of every pixel concatenated from the least significant bit
		var extraBitCount = bits - 8;
		var pixelsPerGroup = bits == 12 ? 2 : 4;
		var groupSize = pixelsPerGroup + ((pixelsPerGroup * extraBitCount) >> 3);
		var data = new byte[values.Length / pixelsPerGroup * groupSize];
		for (var group = values.Length / pixelsPerGroup - 1; group >= 0; --group)
		{
			// pack the byte of each pixel and collect its extra bits
			var offset = group * groupSize;
			var extraBits = 0u;
			for (var i = 0; i < pixelsPerGroup; ++i)
			{
				var value = values[group * pixelsPerGroup + i];
				var (pixelByte, pixelExtraBits) = byteOrdering == ByteOrdering.BigEndian
					? (value >> extraBitCount, value & ((1 << extraBitCount) - 1))
					: (value & 0xff, value >> 8);
				data[offset + i] = (byte)pixelByte;
				extraBits |= (uint)pixelExtraBits << (i * extraBitCount);
			}

			// pack the extra bits
			for (var i = pixelsPerGroup; i < groupSize; ++i)
			{
				data[offset + i] = (byte)extraBits;
				extraBits >>= 8;
			}
		}

		// the file must be closed before it is opened again as a data source
		string fileName;
		using (var stream = this.CreateCacheFile())
		{
			fileName = stream.Name;
			stream.Write(data);
		}
		return new FileImageDataSource(this.Application, fileName);
	}


	/// <summary>
	/// Test for reading the value of each pixel from the packed bytes.
	/// </summary>
	[Test]
	public void RenderingTest([Values(10, 12, 14)] int bits, [Values] ByteOrdering byteOrdering, [Values] bool hasLevels)
	{
		this.TestOnApplicationThread(async () =>
		{
			// prepare values which use every bit of a pixel
			await this.InitializeSubSystemsAsync();
			var maxValue = (1 << bits) - 1;
			var random = new Random(bits);
			var values = new int[TestImageWidth * TestImageHeight];
			for (var i = values.Length - 1; i >= 0; --i)
				values[i] = random.Next(maxValue + 1);

			// render
			using var source = this.CreateTestImageDataSource(values, bits, byteOrdering);
			var blackLevel = hasLevels ? LevelMargin : 0u;
			var whiteLevel = hasLevels ? (uint)maxValue - LevelMargin : (uint)maxValue;
			var colors = await this.RenderAsync(source, $"Bayer_Pattern_{bits}_MIPI", byteOrdering, blackLevel, whiteLevel);

			// check the color component of each pixel, which carries its value in the most significant bits
			var scale = (double)maxValue / (whiteLevel - blackLevel);
			for (var y = 0; y < TestImageHeight; ++y)
			{
				for (var x = 0; x < TestImageWidth; ++x)
				{
					var value = values[y * TestImageWidth + x];
					var expectedValue = value <= blackLevel ? 0
						: value >= whiteLevel ? maxValue
						: (int)((value - blackLevel) * scale + 0.5);
					var colorComponent = ((x & 1) == 0, (y & 1) == 0) switch
					{
						(true, true) => BayerPatternColorComponent.Red,
						(false, false) => BayerPatternColorComponent.Blue,
						_ => BayerPatternColorComponent.Green,
					};
					var color = colors[(y * TestImageWidth + x) * 4 + (int)colorComponent];
					Assert.That(color >> (16 - bits), Is.EqualTo(expectedValue), $"Incorrect value of pixel ({x}, {y}) rendered with {byteOrdering} byte ordering.");
				}
			}
		});
	}


	// Render the given source through the renderer of the given format and return the rendered colors of each pixel.
	async Task<ushort[]> RenderAsync(IImageDataSource source, string formatName, ByteOrdering byteOrdering, uint blackLevel, uint whiteLevel)
	{
		// render
		var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
		var defaultPlaneOptions = renderer.CreateDefaultPlaneOptions(TestImageWidth, TestImageHeight)[0];
		ImagePlaneOptions[] planeOptions = [ new(defaultPlaneOptions.EffectiveBits, blackLevel, whiteLevel, 0, defaultPlaneOptions.RowStride) ];
		var renderingOptions = new ImageRenderingOptions
		{
			BayerPattern = BayerPattern.RGGB_2x2,
			BlueGain = 1.0,
			ByteOrdering = byteOrdering,
			Demosaicing = DemosaicingAlgorithms.Bypass,
			GreenGain = 1.0,
			RedGain = 1.0,
		};
		var format = await renderer.SelectRenderedFormatAsync(source, renderingOptions, planeOptions, CancellationToken.None);
		using var buffer = new BitmapBuffer(format, ColorSpace.Default, TestImageWidth, TestImageHeight);
		await renderer.RenderAsync(source, buffer, renderingOptions, planeOptions, CancellationToken.None);

		// read the colors back
		var colors = new ushort[TestImageWidth * TestImageHeight * 4];
		unsafe
		{
			buffer.Memory.Pin(baseAddress =>
			{
				var index = 0;
				for (var y = 0; y < TestImageHeight; ++y)
				{
					var rowPtr = (ushort*)((byte*)baseAddress + (long)y * buffer.RowBytes);
					for (var x = 0; x < TestImageWidth * 4; ++x, ++index)
						colors[index] = rowPtr[x];
				}
			});
		}
		return colors;
	}
}
