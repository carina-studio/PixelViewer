using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.Demosaicing;
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
/// Tests of applying <see cref="ColorTable"/> when rendering image.
/// </summary>
[TestFixture]
class ColorTableRenderingTests : BaseTests
{
	// Constants.
	const int TestImageHeight = 8;
	const int TestImageWidth = 8;


	// Create a color table whose colors are selected by the given function.
	static ColorTable CreateColorTable(int count, int colorBitDepth, Func<int, uint> colorSelector) =>
		new ColorTable(count, colorBitDepth).Also(it =>
		{
			var colors = it.Memory.Span;
			for (var i = count - 1; i >= 0; --i)
				colors[i] = colorSelector(i);
		});


	// Create a data source which provides an 8-bit bayer pattern image whose value of each pixel is its index.
	IImageDataSource CreateTestImageDataSource()
	{
		// the file must be closed before it is opened again as a data source
		string fileName;
		using (var stream = this.CreateCacheFile())
		{
			fileName = stream.Name;
			var data = new byte[TestImageWidth * TestImageHeight];
			for (var i = data.Length - 1; i >= 0; --i)
				data[i] = (byte)i;
			stream.Write(data);
		}
		return new FileImageDataSource(this.Application, fileName);
	}


	// Render the given source with the given options and return the rendered colors of B/G/R of each pixel.
	async Task<(int Bits, ushort[] Colors)> RenderAsync(IImageDataSource source, ImageRenderingOptions renderingOptions)
	{
		var renderer = ImageRenderers.All.First(it => it.Format.Name == "Bayer_Pattern_8");
		var planeOptions = new List<ImagePlaneOptions> { new(8, 1, TestImageWidth) };
		var format = await renderer.SelectRenderedFormatAsync(source, renderingOptions, planeOptions, CancellationToken.None);
		using var buffer = new BitmapBuffer(format, ColorSpace.Default, TestImageWidth, TestImageHeight);
		await renderer.RenderAsync(source, buffer, renderingOptions, planeOptions, CancellationToken.None);

		// read every color component back as a 16-bit value so that both formats can be compared directly
		var colors = new ushort[TestImageWidth * TestImageHeight * 4];
		unsafe
		{
			buffer.Memory.Pin(baseAddress =>
			{
				var index = 0;
				for (var y = 0; y < TestImageHeight; ++y)
				{
					var rowPtr = (byte*)baseAddress + (long)y * buffer.RowBytes;
					for (var x = 0; x < TestImageWidth * 4; ++x, ++index)
					{
						colors[index] = format == BitmapFormat.Bgra64
							? ((ushort*)rowPtr)[x]
							: (ushort)(rowPtr[x] * 257);
					}
				}
			});
		}
		return (format == BitmapFormat.Bgra64 ? 16 : 8, colors);
	}


	// Create a data source which provides a 16-bit bayer pattern image whose value of each pixel is its index.
	IImageDataSource CreateTest16BitImageDataSource(ByteOrdering byteOrdering)
	{
		// the file must be closed before it is opened again as a data source
		string fileName;
		using (var stream = this.CreateCacheFile())
		{
			fileName = stream.Name;
			var data = new byte[TestImageWidth * TestImageHeight * 2];
			for (var i = TestImageWidth * TestImageHeight - 1; i >= 0; --i)
			{
				if (byteOrdering == ByteOrdering.LittleEndian)
				{
					data[i * 2] = (byte)i;
					data[i * 2 + 1] = 0;
				}
				else
				{
					data[i * 2] = 0;
					data[i * 2 + 1] = (byte)i;
				}
			}
			stream.Write(data);
		}
		return new FileImageDataSource(this.Application, fileName);
	}


	// Render the given source through the 16-bit bayer renderer and return the rendered colors of each pixel.
	async Task<ushort[]> Render16BitAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, uint whiteLevel)
	{
		var renderer = ImageRenderers.All.First(it => it.Format.Name == "Bayer_Pattern_16");
		var planeOptions = new List<ImagePlaneOptions> { new(16, 2, TestImageWidth * 2) { WhiteLevel = whiteLevel } };
		var format = await renderer.SelectRenderedFormatAsync(source, renderingOptions, planeOptions, CancellationToken.None);
		using var buffer = new BitmapBuffer(format, ColorSpace.Default, TestImageWidth, TestImageHeight);
		await renderer.RenderAsync(source, buffer, renderingOptions, planeOptions, CancellationToken.None);
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
	/// Test for rendering a 16-bit image through a color table which is shorter than the range of its values.
	/// </summary>
	[Test]
	public void ShortColorTableRenderingTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			await this.InitializeSubSystemsAsync();
			using var source = this.CreateTest16BitImageDataSource(ByteOrdering.LittleEndian);
			using var colorTable = CreateColorTable(256, 14, i => (uint)(i * 64));

			// every value of the image is below 64, so a table of 256 colors is enough to render it
			var colors = await this.Render16BitAsync(source, new()
			{
				BayerPattern = BayerPattern.RGGB_2x2,
				BlueColorTable = colorTable,
				BlueGain = 1.0,
				ByteOrdering = ByteOrdering.LittleEndian,
				Demosaicing = DemosaicingAlgorithms.Bypass,
				GreenColorTable = colorTable,
				GreenGain = 1.0,
				RedColorTable = colorTable,
				RedGain = 1.0,
			}, 16383u);
			Assert.That(colors[6 * 4 + 2], Is.EqualTo((ushort)(6u * 64 * 65535.0 / 16383 + 0.5)));

			// a table which does not cover every value of the image fails while the image is being rendered
			using var shortColorTable = CreateColorTable(4, 14, i => (uint)(i * 64));
			try
			{
				await this.Render16BitAsync(source, new()
				{
					BayerPattern = BayerPattern.RGGB_2x2,
					BlueColorTable = shortColorTable,
					BlueGain = 1.0,
					ByteOrdering = ByteOrdering.LittleEndian,
					Demosaicing = DemosaicingAlgorithms.Bypass,
					GreenColorTable = shortColorTable,
					GreenGain = 1.0,
					RedColorTable = shortColorTable,
					RedGain = 1.0,
				}, 16383u);
				Assert.Fail("Rendering with a color table which is too short should fail.");
			}
			catch (IndexOutOfRangeException)
			{ }
		});
	}


	/// <summary>
	/// Test for reading the value of each pixel of a 16-bit image by the given byte ordering before it is mapped.
	/// </summary>
	[Test]
	public void ByteOrderingWithColorTableTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			await this.InitializeSubSystemsAsync();
			using var colorTable = CreateColorTable(65536, 16, i => (uint)i);

			// the same value is stored by both byte orderings, so both must be mapped to the same color
			foreach (var byteOrdering in new[] { ByteOrdering.LittleEndian, ByteOrdering.BigEndian })
			{
				using var source = this.CreateTest16BitImageDataSource(byteOrdering);
				var colors = await this.Render16BitAsync(source, new()
				{
					BayerPattern = BayerPattern.RGGB_2x2,
					BlueColorTable = colorTable,
					BlueGain = 1.0,
					ByteOrdering = byteOrdering,
					Demosaicing = DemosaicingAlgorithms.Bypass,
					GreenColorTable = colorTable,
					GreenGain = 1.0,
					RedColorTable = colorTable,
					RedGain = 1.0,
				}, 65535u);
				Assert.That(colors[6 * 4 + 2], Is.EqualTo(6), $"Incorrect color rendered with {byteOrdering} byte ordering.");
			}
		});
	}


	/// <summary>
	/// Test for selecting the format of rendered image according to the bit depth of color tables.
	/// </summary>
	[Test]
	public void RenderedFormatSelectionTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			await this.InitializeSubSystemsAsync();
			var renderer = ImageRenderers.All.First(it => it.Format.Name == "Bayer_Pattern_8");
			Assert.That(renderer.IsColorTableSupported);
			using var source = this.CreateTestImageDataSource();
			var planeOptions = new List<ImagePlaneOptions> { new(8, 1, TestImageWidth) };

			// an 8-bit image without color table keeps being rendered as a 32-bit bitmap
			var format = await renderer.SelectRenderedFormatAsync(source, new(), planeOptions, CancellationToken.None);
			Assert.That(format, Is.EqualTo(BitmapFormat.Bgra32));

			// a color table which needs no more than 8 bits does not widen the rendered bitmap
			using var colorTable8 = CreateColorTable(256, 8, i => (uint)i);
			format = await renderer.SelectRenderedFormatAsync(source, new() { GreenColorTable = colorTable8 }, planeOptions, CancellationToken.None);
			Assert.That(format, Is.EqualTo(BitmapFormat.Bgra32));

			// a color table which needs more than 8 bits widens the rendered bitmap
			using var colorTable14 = CreateColorTable(256, 14, i => (uint)(i * i));
			format = await renderer.SelectRenderedFormatAsync(source, new() { GreenColorTable = colorTable14 }, planeOptions, CancellationToken.None);
			Assert.That(format, Is.EqualTo(BitmapFormat.Bgra64));
		});
	}


	/// <summary>
	/// Test for rendering image with color tables which map the values of color channels linearly.
	/// </summary>
	[Test]
	public void LinearColorTableRenderingTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			await this.InitializeSubSystemsAsync();
			using var source = this.CreateTestImageDataSource();

			// render without color table
			var (bitsWithoutTable, colorsWithoutTable) = await this.RenderAsync(source, new()
			{
				BayerPattern = BayerPattern.RGGB_2x2,
				BlueGain = 1.0,
				Demosaicing = DemosaicingAlgorithms.Bypass,
				GreenGain = 1.0,
				RedGain = 1.0,
			});
			Assert.That(bitsWithoutTable, Is.EqualTo(8));

			// render with color tables which map each value to itself, the result should be the same as rendering
			// without color table because such tables define exactly the mapping which is applied by default
			using var identityColorTable = CreateColorTable(256, 8, i => (uint)i);
			var (bitsWithTable, colorsWithTable) = await this.RenderAsync(source, new()
			{
				BayerPattern = BayerPattern.RGGB_2x2,
				BlueColorTable = identityColorTable,
				BlueGain = 1.0,
				Demosaicing = DemosaicingAlgorithms.Bypass,
				GreenColorTable = identityColorTable,
				GreenGain = 1.0,
				RedColorTable = identityColorTable,
				RedGain = 1.0,
			});
			Assert.That(bitsWithTable, Is.EqualTo(8));
			Assert.That(colorsWithTable, Is.EqualTo(colorsWithoutTable));
		});
	}


	/// <summary>
	/// Test for rendering image with 8-bit color tables which differ per color component, as an indexed color image does.
	/// </summary>
	[Test]
	public void PaletteColorTableRenderingTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			await this.InitializeSubSystemsAsync();
			using var source = this.CreateTestImageDataSource();

			// the three tables share the index but map it to different colors, which is exactly an indexed color image
			using var blueColorTable = CreateColorTable(256, 8, i => (uint)(255 - i));
			using var greenColorTable = CreateColorTable(256, 8, i => (uint)(i / 2));
			using var redColorTable = CreateColorTable(256, 8, i => (uint)Math.Min(255, i * 3));
			var (bits, colors) = await this.RenderAsync(source, new()
			{
				BayerPattern = BayerPattern.RGGB_2x2,
				BlueColorTable = blueColorTable,
				BlueGain = 1.0,
				Demosaicing = DemosaicingAlgorithms.Bypass,
				GreenColorTable = greenColorTable,
				GreenGain = 1.0,
				RedColorTable = redColorTable,
				RedGain = 1.0,
			});

			// an 8-bit palette needs no more than an 8-bit bitmap
			Assert.That(bits, Is.EqualTo(8));

			// the pixel at (6, 0) is red and carries the value 6, so the red table must have been applied to it
			Assert.That(colors[6 * 4 + 2], Is.EqualTo((ushort)(Math.Min(255, 6 * 3) * 257)));

			// the pixel at (7, 0) is green and carries the value 7
			Assert.That(colors[7 * 4 + 1], Is.EqualTo((ushort)(7 / 2 * 257)));

			// the pixel at (1, 1) is blue and carries the value 9
			Assert.That(colors[TestImageWidth * 4 + 1 * 4], Is.EqualTo((ushort)((255 - 9) * 257)));
		});
	}


	/// <summary>
	/// Test for rendering image with a color table which maps the values of color channels to more than 8-bit colors.
	/// </summary>
	[Test]
	public void WideColorTableRenderingTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			await this.InitializeSubSystemsAsync();
			using var source = this.CreateTestImageDataSource();

			// map each value by a square law, as the DNG files which pack linear colors into fewer bits do
			using var colorTable = CreateColorTable(256, 14, i => (uint)(i * i * 16383 / 65025));
			var (bits, colors) = await this.RenderAsync(source, new()
			{
				BayerPattern = BayerPattern.RGGB_2x2,
				BlueColorTable = colorTable,
				BlueGain = 1.0,
				Demosaicing = DemosaicingAlgorithms.Bypass,
				GreenColorTable = colorTable,
				GreenGain = 1.0,
				RedColorTable = colorTable,
				RedGain = 1.0,
			});
			Assert.That(bits, Is.EqualTo(16));

			// the first pixel is red and its value is 0, so it is mapped to the darkest color
			Assert.That(colors[2], Is.EqualTo(0));

			// only the pixels at even positions of the first row are red in the RGGB pattern, so the pixel at (6, 0)
			// carries the value 6 and its color is expected to be the entry of the table scaled to 16-bit
			var colorInTable = (uint)(6 * 6 * 16383 / 65025);
			var expected = (ushort)(colorInTable * 65535.0 / 16383 + 0.5);
			Assert.That(colors[6 * 4 + 2], Is.EqualTo(expected));

			// a square law must not be rendered as a linear ramp, otherwise the table has not been applied
			Assert.That(colors[6 * 4 + 2], Is.LessThan(6 * 65535 / 255));
		});
	}
}
