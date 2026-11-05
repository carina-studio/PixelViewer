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
/// Tests of rendering Bayer pattern images whose samples are packed into bits.
/// </summary>
[TestFixture]
class PackedImageRenderersTests : BaseTests
{
	// Constants.
	const uint LevelMargin = 64;
	const int RowPadding = 3;
	const int TestImageHeight = 4;
	const int TestImageWidth = 6;


	// Create a data source which provides the given values packed into bits, the most significant bit first for big-endian or the least
	// significant bit first for little-endian, each row of which starts at a byte boundary and is followed by the given padding.
	IImageDataSource CreateTestImageDataSource(int[] values, int bits, ByteOrdering byteOrdering, int rowStride)
	{
		// pack values of each row
		var data = new byte[rowStride * TestImageHeight];
		for (var y = 0; y < TestImageHeight; ++y)
		{
			var bitPosition = 0;
			for (var x = 0; x < TestImageWidth; ++x)
			{
				var value = values[y * TestImageWidth + x];
				for (var i = 0; i < bits; ++i, ++bitPosition)
				{
					var bit = byteOrdering == ByteOrdering.BigEndian
						? (value >> (bits - 1 - i)) & 1
						: (value >> i) & 1;
					if (bit == 0)
						continue;
					var offset = y * rowStride + (bitPosition >> 3);
					data[offset] |= byteOrdering == ByteOrdering.BigEndian
						? (byte)(0x80 >> (bitPosition & 7))
						: (byte)(1 << (bitPosition & 7));
				}
			}
		}

		// fill padding which should be ignored
		var minRowStride = (TestImageWidth * bits + 7) >> 3;
		for (var y = 0; y < TestImageHeight; ++y)
			data.AsSpan(y * rowStride + minRowStride, rowStride - minRowStride).Fill(0xff);

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
	/// Test for evaluating the layout of packed samples.
	/// </summary>
	[Test]
	public void LayoutTest([Values(10, 12, 14)] int bits) => this.TestOnApplicationThread(() =>
	{
		var renderer = ImageRenderers.All.First(it => it.Format.Name == $"Bayer_Pattern_{bits}_Packed");
		var planeOptions = renderer.CreateDefaultPlaneOptions(TestImageWidth, TestImageHeight);
		var minRowStride = (TestImageWidth * bits + 7) >> 3;
		Assert.That(planeOptions[0].RowStride, Is.EqualTo(minRowStride), "Default row stride should be the bytes of samples rounded up.");
		Assert.That(renderer.EvaluateSourceDataSize(TestImageWidth, TestImageHeight, new ImageRenderingOptions(), planeOptions), Is.EqualTo(minRowStride * TestImageHeight));
		ImagePlaneOptions[] paddedPlaneOptions = [ new(bits, 0, minRowStride + RowPadding) ];
		Assert.That(renderer.EvaluateSourceDataSize(TestImageWidth, TestImageHeight, new ImageRenderingOptions(), paddedPlaneOptions), Is.EqualTo((minRowStride + RowPadding) * TestImageHeight), "Padding of rows should be counted.");
	});


	// Render the given source through the renderer of the given format and return the rendered colors of each pixel.
	async Task<ushort[]> RenderAsync(IImageDataSource source, string formatName, ByteOrdering byteOrdering, uint blackLevel, uint whiteLevel, int rowStride)
	{
		// render
		var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
		var defaultPlaneOptions = renderer.CreateDefaultPlaneOptions(TestImageWidth, TestImageHeight)[0];
		ImagePlaneOptions[] planeOptions = [ new(defaultPlaneOptions.EffectiveBits, blackLevel, whiteLevel, 0, rowStride) ];
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


	/// <summary>
	/// Test for reading the value of each pixel from the packed bits.
	/// </summary>
	[Test]
	public void RenderingTest([Values(10, 12, 14)] int bits, [Values] ByteOrdering byteOrdering, [Values] bool hasLevels, [Values] bool hasRowPadding) => this.TestOnApplicationThread(async () =>
	{
		// prepare values which use every bit of a pixel
		await this.InitializeSubSystemsAsync();
		var maxValue = (1 << bits) - 1;
		var random = new Random(bits);
		var values = new int[TestImageWidth * TestImageHeight];
		for (var i = values.Length - 1; i >= 0; --i)
			values[i] = random.Next(maxValue + 1);

		// render
		var rowStride = ((TestImageWidth * bits + 7) >> 3) + (hasRowPadding ? RowPadding : 0);
		using var source = this.CreateTestImageDataSource(values, bits, byteOrdering, rowStride);
		var blackLevel = hasLevels ? LevelMargin : 0u;
		var whiteLevel = hasLevels ? (uint)maxValue - LevelMargin : (uint)maxValue;
		var colors = await this.RenderAsync(source, $"Bayer_Pattern_{bits}_Packed", byteOrdering, blackLevel, whiteLevel, rowStride);

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
