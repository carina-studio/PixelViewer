using Carina.PixelViewer.Media;
using NUnit.Framework;
using System.IO;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="IfdEntryReader"/>.
/// </summary>
[TestFixture]
class IfdEntryReaderTests
{
	/// <summary>
	/// Test for getting position of data of entry, which is in the entry itself or pointed by it.
	/// </summary>
	[Test]
	public void EntryDataPositionTest([Values(0, 5)] int prefixSize)
	{
		// write TIFF data after a prefix, so that offsets are relative to the start of TIFF data rather than of the stream
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write(new byte[prefixSize]);
		writer.Write("II"u8);
		writer.Write((ushort)42);
		writer.Write(8u);
		writer.Write((ushort)2);
		writer.Write((ushort)0x0100);
		writer.Write((ushort)IfdEntryType.UInt16);
		writer.Write(1u);
		writer.Write(640u);
		writer.Write((ushort)0x927c);
		writer.Write((ushort)IfdEntryType.Undefined);
		writer.Write(6u);
		writer.Write(38u);
		writer.Write(0u);
		writer.Write("Nikon\0"u8);

		// read entries
		stream.Position = prefixSize;
		var reader = new IfdEntryReader(stream);
		Assert.That(reader.Read(), Is.True);
		Assert.That(reader.CurrentEntryId, Is.EqualTo(0x0100));
		Assert.That(reader.CurrentEntryDataPosition, Is.EqualTo(prefixSize + 18), "Position of data stored in entry should be the value field of entry.");
		Assert.That(reader.Read(), Is.True);
		Assert.That(reader.CurrentEntryId, Is.EqualTo(0x927c));
		Assert.That(reader.CurrentEntryDataPosition, Is.EqualTo(prefixSize + 38), "Position of data pointed by entry should be relative to the start of stream.");
		Assert.That(reader.TryGetEntryData(out byte[]? data), Is.True);
		Assert.That(data, Is.EqualTo("Nikon\0"u8.ToArray()));
	}
}
