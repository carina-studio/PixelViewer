using Carina.PixelViewer.Media;
using NUnit.Framework;
using System;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="ColorTable"/>.
/// </summary>
[TestFixture]
class ColorTableTests : BaseTests
{
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
}
