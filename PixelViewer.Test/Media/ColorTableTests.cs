using Carina.PixelViewer.Media;
using NUnit.Framework;
using System;
using System.IO;
using System.Text.Json;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="ColorTable"/>.
/// </summary>
[TestFixture]
class ColorTableTests : BaseTests
{
	/// <summary>
	/// Test for reporting whether the colors are shared between instances or not.
	/// </summary>
	[Test]
	public void ContentSharingTest()
	{
		this.TestOnApplicationThread(() =>
		{
			// an instance always shares its colors with itself and with the instances shared from it
			using var colorTable = CreateColorTable(256, 14, i => (uint)(i * i));
			using var sharedColorTable = colorTable.Share();
			using var sharedColorTable2 = sharedColorTable.Share();
			Assert.That(colorTable.IsContentSharedWith(colorTable));
			Assert.That(colorTable.IsContentSharedWith(sharedColorTable));
			Assert.That(sharedColorTable.IsContentSharedWith(colorTable));
			Assert.That(sharedColorTable.IsContentSharedWith(sharedColorTable2));

			// sharing must not be reported by reference equality, otherwise a shared table is treated as a different table
			Assert.That(ReferenceEquals(colorTable, sharedColorTable), Is.False);

			// an independent table with identical colors is a different table
			using var equivalentColorTable = CreateColorTable(256, 14, i => (uint)(i * i));
			Assert.That(colorTable.IsContentSharedWith(equivalentColorTable), Is.False);
		});
	}


	// Create a color table whose colors are filled with the given function.
	static ColorTable CreateColorTable(int count, int colorBitDepth, Func<int, uint> colorSelector)
	{
		var colorTable = new ColorTable(count, colorBitDepth);
		var colors = colorTable.Memory.Span;
		for (var i = count - 1; i >= 0; --i)
			colors[i] = colorSelector(i);
		return colorTable;
	}


	/// <summary>
	/// Test for writing the table to JSON and loading it back.
	/// </summary>
	[Test]
	public void JsonSerializationTest()
	{
		this.TestOnApplicationThread(() =>
		{
			// the colors cover the full range of 32-bit so that the encoding of wide colors is checked as well
			using var colorTable = CreateColorTable(1024, 32, i => i switch
			{
				0 => 0u,
				1 => uint.MaxValue,
				2 => 65536u,
				_ => (uint)(i * 4177),
			});
			using var json = WriteToJson(colorTable);

			// load the table back and check that every color survived
			Assert.That(ColorTable.TryLoadFromJson(json.RootElement, out var decodedColorTable));
			using (decodedColorTable)
			{
				Assert.That(decodedColorTable, Is.Not.Null);
				Assert.That(decodedColorTable!.Count, Is.EqualTo(colorTable.Count));
				Assert.That(decodedColorTable.ColorBitDepth, Is.EqualTo(32));
				Assert.That(decodedColorTable.Memory.Span.SequenceEqual(colorTable.Memory.Span));
			}

			// a JSON value which was not written by the table must be rejected instead of throwing
			using var invalidJson = JsonDocument.Parse("{ \"ColorBitDepth\": 8, \"Colors\": \"not a color table\" }");
			Assert.That(ColorTable.TryLoadFromJson(invalidJson.RootElement, out var invalidColorTable), Is.False);
			Assert.That(invalidColorTable, Is.Null);
			using var emptyJson = JsonDocument.Parse("{ }");
			Assert.That(ColorTable.TryLoadFromJson(emptyJson.RootElement, out invalidColorTable), Is.False);

			// the tables which are carried by real files are smooth, so compressing them should pay for the 33% which
			// is added by converting the compressed bytes into a Base64 string
			using var smoothColorTable = CreateColorTable(256, 14, i => (uint)(i * i * 16383 / 65025));
			using var smoothJson = WriteToJson(smoothColorTable);
			Assert.That(smoothJson.RootElement.GetProperty("Colors").GetString()!.Length, Is.LessThan(256 * sizeof(uint)));
		});
	}


	/// <summary>
	/// Test for keeping the colors alive until every instance which shares them has been disposed.
	/// </summary>
	[Test]
	public void SharingLifetimeTest()
	{
		this.TestOnApplicationThread(() =>
		{
			// share the table and then release the instance which created it
			var colorTable = CreateColorTable(256, 14, i => (uint)(i * 4));
			var sharedColorTable = colorTable.Share();
			colorTable.Dispose();

			// the colors are still accessible through the shared instance
			Assert.That(sharedColorTable.Count, Is.EqualTo(256));
			Assert.That(sharedColorTable.ColorBitDepth, Is.EqualTo(14));
			Assert.That(sharedColorTable.Memory.Span[255], Is.EqualTo(1020u));

			// complete
			sharedColorTable.Dispose();
		});
	}


	/// <summary>
	/// Test for validating the parameters to create instance.
	/// </summary>
	[Test]
	public void ValidationTest()
	{
		this.TestOnApplicationThread(() =>
		{
			// the number of colors and the bit depth must be positive and within the supported range
			Assert.Throws<ArgumentOutOfRangeException>(() => new ColorTable(0, 8).Dispose());
			Assert.Throws<ArgumentOutOfRangeException>(() => new ColorTable(ColorTable.MaxCount + 1, 8).Dispose());
			Assert.Throws<ArgumentOutOfRangeException>(() => new ColorTable(256, 0).Dispose());
			Assert.Throws<ArgumentOutOfRangeException>(() => new ColorTable(256, ColorTable.MaxColorBitDepth + 1).Dispose());

			// the number of colors is not required to be a power of 2
			using var colorTable = new ColorTable(200, 14);
			Assert.That(colorTable.Count, Is.EqualTo(200));
		});
	}


	// Write the given color table to JSON and read the written value back.
	static JsonDocument WriteToJson(ColorTable colorTable)
	{
		using var stream = new MemoryStream();
		using (var jsonWriter = new Utf8JsonWriter(stream))
		{
			colorTable.WriteToJson(jsonWriter);
			jsonWriter.Flush();
		}
		stream.Position = 0;
		return JsonDocument.Parse(stream);
	}
}
