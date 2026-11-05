using Avalonia;
using Carina.PixelViewer.Media;
using NUnit.Framework;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="PixelInsets"/> and clipping <see cref="IBitmapBuffer"/> by them.
/// </summary>
[TestFixture]
class PixelInsetsTests
{
	/// <summary>
	/// Test for exposing a region of bitmap without copying its data.
	/// </summary>
	[Test]
	public void ClipTest()
	{
		// prepare bitmap whose each pixel carries its position
		const int width = 7;
		const int height = 5;
		using var bitmapBuffer = new BitmapBuffer(BitmapFormat.Bgra32, ColorSpace.Default, width, height);
		var pixels = bitmapBuffer.Memory.Span;
		for (var y = 0; y < height; ++y)
		{
			for (var x = 0; x < width; ++x)
			{
				var offset = y * bitmapBuffer.RowBytes + x * 4;
				pixels[offset] = (byte)x;
				pixels[offset + 1] = (byte)y;
			}
		}

		// clip and check the pixels of region
		var region = new PixelRect(2, 1, 3, 2);
		using var clippedBuffer = bitmapBuffer.Clip(region);
		Assert.That((clippedBuffer.Width, clippedBuffer.Height), Is.EqualTo((3, 2)));
		Assert.That(clippedBuffer.RowBytes, Is.EqualTo(bitmapBuffer.RowBytes), "Rows of region should keep the row stride of source.");
		Assert.That(clippedBuffer.IsBufferSharedWith(bitmapBuffer), Is.True, "Data of source should not be copied.");
		var clippedPixels = clippedBuffer.Memory.Span;
		for (var y = 0; y < region.Height; ++y)
		{
			for (var x = 0; x < region.Width; ++x)
			{
				var offset = y * clippedBuffer.RowBytes + x * 4;
				Assert.That((clippedPixels[offset], clippedPixels[offset + 1]), Is.EqualTo(((byte)(x + region.X), (byte)(y + region.Y))), $"Incorrect pixel ({x}, {y}) of region.");
			}
		}

		// copy the region, whose rows are packed
		using (var copiedBuffer = clippedBuffer.Copy())
		{
			Assert.That(copiedBuffer.RowBytes, Is.EqualTo(region.Width * 4), "Rows of copied region should be packed.");
			Assert.That(copiedBuffer.Memory.Span[(copiedBuffer.RowBytes + 4)..][..2].ToArray(), Is.EqualTo(new byte[] { 3, 2 }), "Copied region should keep the pixels.");
		}

		// share the clipped buffer
		using (var sharedBuffer = clippedBuffer.Share())
			Assert.That((sharedBuffer.Width, sharedBuffer.Height, sharedBuffer.Memory.Span[0]), Is.EqualTo((3, 2, (byte)2)), "Shared buffer should expose the same region.");

		// clip the whole bitmap
		using (var wholeBuffer = bitmapBuffer.Clip(new PixelRect(0, 0, width, height)))
			Assert.That(wholeBuffer, Is.InstanceOf<BitmapBuffer>(), "Clipping the whole bitmap should share the source.");
	}


	/// <summary>
	/// Test for writing and loading insets as JSON.
	/// </summary>
	[Test]
	public void JsonTest()
	{
		// write and load
		var insets = new PixelInsets(1, 2, 3, 4);
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
			insets.WriteToJson(writer);
		using (var document = JsonDocument.Parse(stream.ToArray()))
		{
			Assert.That(PixelInsets.TryLoadFromJson(document.RootElement, out var loadedInsets), Is.True);
			Assert.That(loadedInsets, Is.EqualTo(insets));
		}

		// load insets whose some edges are not defined
		using (var document = JsonDocument.Parse(Encoding.UTF8.GetBytes("{ \"Right\": 14, \"Bottom\": -2 }")))
		{
			Assert.That(PixelInsets.TryLoadFromJson(document.RootElement, out var loadedInsets), Is.True);
			Assert.That(loadedInsets, Is.EqualTo(new PixelInsets(0, 0, 14, -2)), "Undefined insets should be 0 and negative ones should be kept.");
		}

		// load invalid values
		using (var document = JsonDocument.Parse(Encoding.UTF8.GetBytes("{ \"Right\": \"14\" }")))
			Assert.That(PixelInsets.TryLoadFromJson(document.RootElement, out _), Is.False, "Inset which is not an integer should fail loading.");
		using (var document = JsonDocument.Parse(Encoding.UTF8.GetBytes("[ 1, 2, 3, 4 ]")))
			Assert.That(PixelInsets.TryLoadFromJson(document.RootElement, out _), Is.False, "Value which is not an object should fail loading.");
	}


	/// <summary>
	/// Test for getting the region left by insets.
	/// </summary>
	[Test]
	public void RegionTest()
	{
		// insets which leave a region
		Assert.That(new PixelInsets(144, 114, 17, 15).GetRegion(5344, 3584), Is.EqualTo(new PixelRect(144, 114, 5183, 3455)));
		Assert.That(new PixelInsets(0, 0, 14, 0).GetRegion(2496, 1648), Is.EqualTo(new PixelRect(0, 0, 2482, 1648)));
		Assert.That(PixelInsets.Empty.GetRegion(200, 100), Is.EqualTo(new PixelRect(0, 0, 200, 100)), "Empty insets should leave the whole image.");

		// the region is not checked, it is not positive when the insets leave nothing and beyond the image when an inset is negative
		Assert.That(new PixelInsets(100, 0, 100, 0).GetRegion(200, 100), Is.EqualTo(new PixelRect(100, 0, 0, 100)));
		var insets = new PixelInsets(-1, 2, -3, 4);
		Assert.That((insets.Left, insets.Top, insets.Right, insets.Bottom), Is.EqualTo((-1, 2, -3, 4)), "Negative insets should be kept.");
		Assert.That(insets.IsEmpty, Is.False);
		Assert.That(insets.GetRegion(200, 100), Is.EqualTo(new PixelRect(-1, 2, 204, 94)));
	}
}
