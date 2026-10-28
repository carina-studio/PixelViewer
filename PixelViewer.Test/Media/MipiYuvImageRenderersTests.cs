using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of rendering images packed in the MIPI CSI-2 YUV420 and YUV422 layouts.
/// </summary>
[TestFixture]
class MipiYuvImageRenderersTests : BaseTests
{
	// Constants.
	const int TestImageHeight = 4;
	const int TestImageWidth = 8;


	// Create a data source which provides the given components packed in the layout of the given format, with the given number of padding bytes at the end of each line.
	IImageDataSource CreateTestImageDataSource(string formatName, ushort[,] yComponents, ushort[,] uComponents, ushort[,] vComponents, int[] paddingSizes)
	{
		// pack lines, a YUV420 format alternates a line of Y with a line of U, Y, V, Y, while the legacy one alternates a line of U, Y, Y with a line of V, Y, Y
		var is10Bit = formatName.Contains("_10_");
		var isLegacy = formatName.Contains("_LEGACY_");
		var data = new MemoryStream();
		for (var y = 0; y < TestImageHeight; ++y)
		{
			// collect components of the line
			var isLumaOnlyLine = formatName.StartsWith("YUV420") && !isLegacy && (y & 1) == 0;
			var chromaRow = formatName.StartsWith("YUV420") ? (y >> 1) : y;
			var components = new List<ushort>();
			for (var x = 0; x < TestImageWidth; x += 2)
			{
				if (isLegacy)
					components.AddRange([ (y & 1) == 0 ? uComponents[chromaRow, x >> 1] : vComponents[chromaRow, x >> 1], yComponents[y, x], yComponents[y, x + 1] ]);
				else if (isLumaOnlyLine)
					components.AddRange([ yComponents[y, x], yComponents[y, x + 1] ]);
				else
					components.AddRange([ uComponents[chromaRow, x >> 1], yComponents[y, x], vComponents[chromaRow, x >> 1], yComponents[y, x + 1] ]);
			}

			// pack components, a 10-bit format carries the 8 most significant bits of 4 components followed by their 2 least significant bits
			if (is10Bit)
			{
				for (var i = 0; i < components.Count; i += 4)
				{
					var lsbs = 0;
					for (var j = 0; j < 4; ++j)
					{
						data.WriteByte((byte)(components[i + j] >> 2));
						lsbs |= (components[i + j] & 0x3) << (j * 2);
					}
					data.WriteByte((byte)lsbs);
				}
			}
			else
			{
				foreach (var component in components)
					data.WriteByte((byte)component);
			}

			// add padding
			var paddingSize = paddingSizes[isLumaOnlyLine || paddingSizes.Length == 1 ? 0 : 1];
			for (var i = 0; i < paddingSize; ++i)
				data.WriteByte(0xa5);
		}

		// the file must be closed before it is opened again as a data source
		string fileName;
		using (var stream = this.CreateCacheFile())
		{
			fileName = stream.Name;
			stream.Write(data.ToArray());
		}
		return new FileImageDataSource(this.Application, fileName);
	}


	/// <summary>
	/// Test for evaluating size of source data and number of pixels.
	/// </summary>
	[Test]
	public void EvaluatingSizeTest([Values("YUV420_8_MIPI", "YUV420_8_LEGACY_MIPI", "YUV420_10_MIPI", "YUV422_10_MIPI")] string formatName)
	{
		this.TestOnApplicationThread(async () =>
		{
			// evaluate size of a 320x240 image, which matches the size of samples captured from a CSI-2 receiver
			await this.InitializeSubSystemsAsync();
			var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
			var planeOptions = renderer.CreateDefaultPlaneOptions(320, 240);
			var expectedSize = formatName switch
			{
				"YUV420_8_MIPI" or "YUV420_8_LEGACY_MIPI" => 115200,
				"YUV420_10_MIPI" => 144000,
				_ => 192000,
			};
			Assert.That(renderer.EvaluateSourceDataSize(320, 240, new ImageRenderingOptions(), planeOptions), Is.EqualTo(expectedSize));

			// evaluate number of pixels
			string fileName;
			using (var stream = this.CreateCacheFile())
			{
				fileName = stream.Name;
				stream.Write(new byte[expectedSize]);
			}
			using var source = new FileImageDataSource(this.Application, fileName);
			Assert.That(renderer.EvaluatePixelCount(source), Is.EqualTo(320 * 240));
		});
	}


	/// <summary>
	/// Test for reading each component from the packed bytes.
	/// </summary>
	[Test]
	public void RenderingTest([Values("YUV420_8_MIPI", "YUV420_8_LEGACY_MIPI", "YUV420_10_MIPI", "YUV422_10_MIPI")] string formatName, [Values] bool hasPadding)
	{
		this.TestOnApplicationThread(async () =>
		{
			// prepare components which use every bit
			await this.InitializeSubSystemsAsync();
			var bits = formatName.Contains("_10_") ? 10 : 8;
			var is420 = formatName.StartsWith("YUV420");
			var random = new Random((bits << 1) | (is420 ? 1 : 0));
			var yComponents = new ushort[TestImageHeight, TestImageWidth];
			var chromaRowCount = is420 ? TestImageHeight / 2 : TestImageHeight;
			var uComponents = new ushort[chromaRowCount, TestImageWidth / 2];
			var vComponents = new ushort[chromaRowCount, TestImageWidth / 2];
			ushort[][,] componentPlanes = [ yComponents, uComponents, vComponents ];
			foreach (var components in componentPlanes)
			{
				for (var y = components.GetLength(0) - 1; y >= 0; --y)
				{
					for (var x = components.GetLength(1) - 1; x >= 0; --x)
						components[y, x] = (ushort)random.Next(1 << bits);
				}
			}

			// prepare plane options, padding makes row strides of the 2 planes of a non-legacy YUV420 format differ from each other
			var renderer = ImageRenderers.All.First(it => it.Format.Name == formatName);
			var defaultPlaneOptions = renderer.CreateDefaultPlaneOptions(TestImageWidth, TestImageHeight);
			int[] paddingSizes = defaultPlaneOptions.Count > 1
				? (hasPadding ? [ 3, 7 ] : [ 0, 0 ])
				: (hasPadding ? [ 5 ] : [ 0 ]);
			var planeOptions = defaultPlaneOptions.Select((it, index) => new ImagePlaneOptions(it.EffectiveBits, it.PixelStride, it.RowStride + paddingSizes[index])).ToArray();

			// render
			using var source = this.CreateTestImageDataSource(formatName, yComponents, uComponents, vComponents, paddingSizes);
			var renderingOptions = new ImageRenderingOptions();
			var format = await renderer.SelectRenderedFormatAsync(source, renderingOptions, planeOptions, CancellationToken.None);
			Assert.That(format, Is.EqualTo(bits > 8 ? BitmapFormat.Bgra64 : BitmapFormat.Bgra32));
			using var buffer = new BitmapBuffer(format, ColorSpace.Default, TestImageWidth, TestImageHeight);
			await renderer.RenderAsync(source, buffer, renderingOptions, planeOptions, CancellationToken.None);

			// check each pixel against the color converted from its components, which share U and V with the pixels of the same 2x1 (YUV422) or 2x2 (YUV420) block
			var converter = YuvToBgraConverter.Default;
			unsafe
			{
				buffer.Memory.Pin(baseAddress =>
				{
					for (var y = 0; y < TestImageHeight; ++y)
					{
						var rowPtr = (byte*)baseAddress + (long)y * buffer.RowBytes;
						var chromaRow = is420 ? (y >> 1) : y;
						for (var x = 0; x < TestImageWidth; x += 2)
						{
							var y1 = Scale(yComponents[y, x], bits);
							var y2 = Scale(yComponents[y, x + 1], bits);
							var u = Scale(uComponents[chromaRow, x >> 1], bits);
							var v = Scale(vComponents[chromaRow, x >> 1], bits);
							if (bits > 8)
							{
								ulong expectedColor1, expectedColor2;
								converter.ConvertFromYuv422ToBgra64(y1, y2, u, v, &expectedColor1, &expectedColor2);
								var colorPtr = (ulong*)rowPtr + x;
								Assert.That(colorPtr[0], Is.EqualTo(expectedColor1), $"Incorrect color of pixel ({x}, {y}).");
								Assert.That(colorPtr[1], Is.EqualTo(expectedColor2), $"Incorrect color of pixel ({x + 1}, {y}).");
							}
							else
							{
								uint expectedColor1, expectedColor2;
								converter.ConvertFromYuv422ToBgra32((byte)y1, (byte)y2, (byte)u, (byte)v, &expectedColor1, &expectedColor2);
								var colorPtr = (uint*)rowPtr + x;
								Assert.That(colorPtr[0], Is.EqualTo(expectedColor1), $"Incorrect color of pixel ({x}, {y}).");
								Assert.That(colorPtr[1], Is.EqualTo(expectedColor2), $"Incorrect color of pixel ({x + 1}, {y}).");
							}
						}
					}
				});
			}
		});
	}


	// Scale component to the depth of rendered color, which is 16-bit for 10-bit formats.
	static ushort Scale(ushort component, int bits) => bits > 8
		? (ushort)((component << 6) | (component >> 4))
		: component;
}
