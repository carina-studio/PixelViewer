using Carina.PixelViewer.Media;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="TiffMediaMetadata"/>.
/// </summary>
[TestFixture]
class TiffMediaMetadataTests
{
	// Entry of IFD to build TIFF-based data for testing.
	record class TestEntry(ushort TagId, IfdEntryType Type, uint Count, object Values);


	// Constants.
	const ushort ApertureValueTagId = 0x9202;
	const ushort DateTimeOriginalTagId = 0x9003;
	const ushort DateTimeTagId = 0x0132;
	const ushort ExifIfdPointerTagId = 0x8769;
	const ushort ExposureTimeTagId = 0x829a;
	const ushort FNumberTagId = 0x829d;
	const ushort FocalLengthIn35mmFilmTagId = 0xa405;
	const ushort FocalLengthTagId = 0x920a;
	const ushort LensMakeTagId = 0xa433;
	const ushort LensModelTagId = 0xa434;
	const ushort MakeTagId = 0x010f;
	const ushort ModelTagId = 0x0110;
	const ushort OffsetTimeOriginalTagId = 0x9011;
	const ushort OrientationTagId = 0x0112;
	const ushort PhotographicSensitivityTagId = 0x8827;
	const ushort SoftwareTagId = 0x0131;
	const ushort SubSecTimeOriginalTagId = 0x9291;
	const ushort UniqueCameraModelTagId = 0xc614;
	const ushort XmpTagId = 0x02bc;


	/// <summary>
	/// Test for parsing the F-number which is kept as the aperture value in APEX.
	/// </summary>
	[Test]
	public void ApertureValueParsingTest()
	{
		// check that the F-number is used directly when it is available
		var data = BuildTiffData(true, [ [] ],
		[
			URationalEntry(FNumberTagId, 40, 10),
			URationalEntry(ApertureValueTagId, 30, 10),
		]);
		Assert.That(CreateMetadata(data).FNumber, Is.EqualTo(4.0).Within(0.0001));

		// check that the F-number is converted from the aperture value when it is unavailable, the aperture value 3 in APEX is F/2.828
		data = BuildTiffData(true, [ [] ], [ URationalEntry(ApertureValueTagId, 30, 10) ]);
		Assert.That(CreateMetadata(data).FNumber, Is.EqualTo(Math.Sqrt(8)).Within(0.0001));
	}


	// Create an entry which keeps a string.
	static TestEntry AsciiEntry(ushort tagId, string value) =>
		new(tagId, IfdEntryType.AsciiString, (uint)(value.Length + 1), value);


	// Build TIFF-based data which contains the given IFDs.
	static byte[] BuildTiffData(bool isLittleEndian, IList<IList<TestEntry>> chainedIfds, IList<TestEntry>? exifEntries = null, int exifIfdOwnerIndex = 0)
	{
		// collect the IFDs to write, the Exif IFD is referred to by one of the chained IFDs
		var ifds = new List<IList<TestEntry>>(chainedIfds);
		var exifIfdPointerIndex = -1;
		if (exifEntries is not null)
		{
			var owner = new List<TestEntry>(ifds[exifIfdOwnerIndex]);
			exifIfdPointerIndex = owner.Count;
			owner.Add(UInt32Entry(ExifIfdPointerTagId, 0));
			ifds[exifIfdOwnerIndex] = owner;
			ifds.Add(exifEntries);
		}

		// calculate the position of each IFD, the data which cannot be kept by entries is placed after all IFDs
		var ifdPositions = new uint[ifds.Count];
		var dataPosition = 8u;
		for (var i = 0; i < ifds.Count; ++i)
		{
			ifdPositions[i] = dataPosition;
			dataPosition += (uint)(2 + (12 * ifds[i].Count) + 4);
		}

		// correct the entry which refers to the Exif IFD
		if (exifIfdPointerIndex >= 0)
		{
			var owner = (List<TestEntry>)ifds[exifIfdOwnerIndex];
			owner[exifIfdPointerIndex] = UInt32Entry(ExifIfdPointerTagId, ifdPositions[^1]);
		}

		// write the header
		using var stream = new MemoryStream();
		using var dataStream = new MemoryStream();
		stream.Write(isLittleEndian ? "II"u8 : "MM"u8);
		WriteUInt16(stream, isLittleEndian, 0x2a);
		WriteUInt32(stream, isLittleEndian, 8);

		// write each IFD, the last IFD of the chain and the Exif IFD refer to no further IFD
		for (var i = 0; i < ifds.Count; ++i)
		{
			var entries = ifds[i];
			WriteUInt16(stream, isLittleEndian, (ushort)entries.Count);
			foreach (var entry in entries)
			{
				WriteUInt16(stream, isLittleEndian, entry.TagId);
				WriteUInt16(stream, isLittleEndian, (ushort)entry.Type);
				WriteUInt32(stream, isLittleEndian, entry.Count);
				var values = SerializeEntryValues(entry, isLittleEndian);
				if (values.Length <= 4)
				{
					stream.Write(values);
					for (var j = 4 - values.Length; j > 0; --j)
						stream.WriteByte(0);
				}
				else
				{
					WriteUInt32(stream, isLittleEndian, (uint)(dataPosition + dataStream.Length));
					dataStream.Write(values);
				}
			}
			WriteUInt32(stream, isLittleEndian, i + 1 < chainedIfds.Count ? ifdPositions[i + 1] : 0);
		}

		// complete
		dataStream.Position = 0;
		dataStream.CopyTo(stream);
		return stream.ToArray();
	}


	/// <summary>
	/// Test for parsing data of entries which are kept by the IFDs of different images.
	/// </summary>
	[Test]
	public void ChainedIfdParsingTest()
	{
		// build data with a chain of IFDs which describe the main image and its thumbnail
		var data = BuildTiffData(true,
		[
			[ AsciiEntry(MakeTagId, "Canon"), AsciiEntry(ModelTagId, "EOS R5") ],
			[ AsciiEntry(MakeTagId, "Thumbnail"), AsciiEntry(ModelTagId, "Thumbnail Model") ],
		]);

		// check that only the entries of the main image are parsed
		var metadata = CreateMetadata(data);
		Assert.That(metadata.IfdIndex, Is.EqualTo(0));
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Canon"));
		Assert.That(metadata.CameraModel, Is.EqualTo("EOS R5"));

		// check that the metadata of the thumbnail keeps the entries of the thumbnail only
		metadata = CreateMetadata(data, 1);
		Assert.That(metadata.IfdIndex, Is.EqualTo(1));
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Thumbnail"));
		Assert.That(metadata.CameraModel, Is.EqualTo("Thumbnail Model"));
	}


	// Create metadata of the main image from the given TIFF-based data.
	static TiffMediaMetadata CreateMetadata(byte[] data)
	{
		var isCreated = TiffMediaMetadata.TryCreate(data, 0, out var metadata);
		Assert.That(isCreated, Is.True);
		return metadata.AsNonNull();
	}


	// Create metadata of the image described by the IFD with the given index from the given TIFF-based data.
	static TiffMediaMetadata CreateMetadata(byte[] data, int ifdIndex)
	{
		using var stream = new MemoryStream(data);
		var reader = new IfdEntryReader(stream);
		var metadata = new TiffMediaMetadata(ifdIndex);
		while (reader.Read())
			metadata.SetEntry(reader);
		return metadata;
	}


	/// <summary>
	/// Test for parsing the time when the media was created.
	/// </summary>
	[Test]
	public void CreationTimeParsingTest()
	{
		// check that the time is treated as UTC when the source of media provides no time zone
		var data = BuildTiffData(true, [ [] ], [ AsciiEntry(DateTimeOriginalTagId, "2026:08:24 13:45:30") ]);
		var creationTime = CreateMetadata(data).CreationTime;
		Assert.That(creationTime, Is.Not.Null);
		Assert.That(creationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30)));
		Assert.That(creationTime.GetValueOrDefault().Offset, Is.EqualTo(TimeSpan.Zero));

		// check that the time zone provided by the source of media is used without changing the time itself
		data = BuildTiffData(true, [ [] ],
		[
			AsciiEntry(DateTimeOriginalTagId, "2026:08:24 13:45:30"),
			AsciiEntry(OffsetTimeOriginalTagId, "+08:00"),
		]);
		creationTime = CreateMetadata(data).CreationTime;
		Assert.That(creationTime, Is.Not.Null);
		Assert.That(creationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30)));
		Assert.That(creationTime.GetValueOrDefault().Offset, Is.EqualTo(TimeSpan.FromHours(8)));

		// check that a negative offset to UTC is parsed
		data = BuildTiffData(true, [ [] ],
		[
			AsciiEntry(DateTimeOriginalTagId, "2026:08:24 13:45:30"),
			AsciiEntry(OffsetTimeOriginalTagId, "-05:30"),
		]);
		Assert.That(CreateMetadata(data).CreationTime.GetValueOrDefault().Offset, Is.EqualTo(TimeSpan.FromHours(-5.5)));

		// check that the fraction of second is added to the time
		data = BuildTiffData(true, [ [] ],
		[
			AsciiEntry(DateTimeOriginalTagId, "2026:08:24 13:45:30"),
			AsciiEntry(SubSecTimeOriginalTagId, "25"),
		]);
		Assert.That(CreateMetadata(data).CreationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30, 250)));

		// check that the time when the media was changed is used when the time when the media was captured is unavailable
		data = BuildTiffData(true, [ [ AsciiEntry(DateTimeTagId, "2026:01:02 03:04:05") ] ]);
		Assert.That(CreateMetadata(data).CreationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 1, 2, 3, 4, 5)));

		// check that a malformed time is dropped
		data = BuildTiffData(true, [ [ AsciiEntry(MakeTagId, "Nikon") ] ], [ AsciiEntry(DateTimeOriginalTagId, "yesterday") ]);
		Assert.That(CreateMetadata(data).CreationTime, Is.Null);
	}


	/// <summary>
	/// Test for parsing data of entries which are kept by the first IFD.
	/// </summary>
	[TestCase(true)]
	[TestCase(false)]
	public void DefaultIfdParsingTest(bool isLittleEndian)
	{
		// build data with entries which describe the camera, the same expectations are applied to both byte orderings
		var data = BuildTiffData(isLittleEndian,
		[
			[
				AsciiEntry(MakeTagId, "NIKON CORPORATION"),
				AsciiEntry(ModelTagId, "NIKON D850"),
				AsciiEntry(SoftwareTagId, "Ver.1.01"),
			],
		]);

		// check that every entry is parsed, the name of manufacturer which is repeated by the name of model is kept
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("NIKON CORPORATION"));
		Assert.That(metadata.CameraModel, Is.EqualTo("NIKON D850"));
		Assert.That(metadata.Software, Is.EqualTo("Ver.1.01"));
	}


	/// <summary>
	/// Test for parsing data which are kept by entries themselves or placed after IFDs.
	/// </summary>
	[Test]
	public void EntryDataPlacementParsingTest()
	{
		// build data with a string which is short enough to be kept by its entry and a string which is not
		var data = BuildTiffData(true,
		[
			[
				AsciiEntry(MakeTagId, "ABC"),
				AsciiEntry(ModelTagId, "A Very Long Name of Model"),
			],
		]);

		// check that both strings are parsed
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("ABC"));
		Assert.That(metadata.CameraModel, Is.EqualTo("A Very Long Name of Model"));
	}


	/// <summary>
	/// Test for parsing data of entries which are kept by the Exif IFD.
	/// </summary>
	[Test]
	public void ExifIfdParsingTest()
	{
		// build data with an Exif IFD which is referred to by the first IFD
		var data = BuildTiffData(true, [ [ AsciiEntry(MakeTagId, "Nikon") ] ],
		[
			URationalEntry(ExposureTimeTagId, 1, 250),
			URationalEntry(FNumberTagId, 28, 10),
			UInt16Entry(PhotographicSensitivityTagId, 400),
			URationalEntry(FocalLengthTagId, 50, 1),
			UInt16Entry(FocalLengthIn35mmFilmTagId, 75),
			AsciiEntry(LensMakeTagId, "Nikkor"),
			AsciiEntry(LensModelTagId, "50mm f/1.8"),
		]);

		// check that every entry of the Exif IFD is parsed
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Nikon"));
		Assert.That(metadata.ExposureTime, Is.EqualTo(TimeSpan.FromSeconds(1 / 250.0)));
		Assert.That(metadata.FNumber, Is.EqualTo(2.8).Within(0.0001));
		Assert.That(metadata.IsoSpeed, Is.EqualTo(400));
		Assert.That(metadata.FocalLength, Is.EqualTo(50.0).Within(0.0001));
		Assert.That(metadata.FocalLengthIn35mmFilm, Is.EqualTo(75));
		Assert.That(metadata.LensManufacturer, Is.EqualTo("Nikkor"));
		Assert.That(metadata.LensModel, Is.EqualTo("50mm f/1.8"));
	}


	/// <summary>
	/// Test for creating metadata from invalid data.
	/// </summary>
	[Test]
	public void InvalidDataParsingTest()
	{
		// check that data with an invalid header is rejected
		byte[] invalidData = [ 0x1, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8 ];
		Assert.That(TiffMediaMetadata.TryCreate(invalidData, 0, out _), Is.False);
		Assert.That(TiffMediaMetadata.TryCreate([], 0, out _), Is.False);

		// check that an invalid offset to the header of data is rejected
		var data = BuildTiffData(true, [ [ AsciiEntry(MakeTagId, "Nikon") ] ]);
		Assert.That(TiffMediaMetadata.TryCreate(data, -1, out _), Is.False);
		Assert.That(TiffMediaMetadata.TryCreate(data, data.Length, out _), Is.False);

		// check that truncated data is handled without throwing exception
		for (var length = 1; length < data.Length; ++length)
		{
			var truncatedData = new byte[length];
			Array.Copy(data, truncatedData, length);
			Assert.DoesNotThrow(() => TiffMediaMetadata.TryCreate(truncatedData, 0, out _));
		}

		// check that data which contains no entry needed by the metadata is rejected
		data = BuildTiffData(true, [ [ UInt16Entry(OrientationTagId, 1) ] ]);
		Assert.That(TiffMediaMetadata.TryCreate(data, 0, out _), Is.False);
	}


	/// <summary>
	/// Test for the entry which declares an oversized size of data.
	/// </summary>
	[Test]
	public void OversizedEntryParsingTest()
	{
		// build data with an entry which declares a size of data much larger than the data it keeps
		var data = BuildTiffData(true,
		[
			[
				new TestEntry(MakeTagId, IfdEntryType.AsciiString, 1 << 20, "Malformed"),
				AsciiEntry(ModelTagId, "EOS R5"),
			],
		]);

		// check that the size of data declared by the entry is reported before the data is read
		using var stream = new MemoryStream(data);
		var reader = new IfdEntryReader(stream);
		var declaredDataSize = 0L;
		while (reader.Read())
		{
			if (reader.CurrentEntryId == MakeTagId)
				declaredDataSize = reader.CurrentEntryDataSize;
		}
		Assert.That(declaredDataSize, Is.EqualTo(1 << 20));

		// check that the oversized entry is dropped and the other entries are still parsed
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.Null);
		Assert.That(metadata.CameraModel, Is.EqualTo("EOS R5"));
	}


	// Serialize values of the given entry with the given byte ordering.
	static byte[] SerializeEntryValues(TestEntry entry, bool isLittleEndian)
	{
		using var stream = new MemoryStream();
		switch (entry.Values)
		{
			case string stringValue:
				stream.Write(Encoding.ASCII.GetBytes(stringValue + '\0'));
				break;
			case byte[] byteValues:
				stream.Write(byteValues);
				break;
			case ushort[] ushortValues:
				foreach (var value in ushortValues)
					WriteUInt16(stream, isLittleEndian, value);
				break;
			case uint[] uintValues:
				foreach (var value in uintValues)
					WriteUInt32(stream, isLittleEndian, value);
				break;
			default:
				throw new NotSupportedException();
		}
		return stream.ToArray();
	}


	/// <summary>
	/// Test for setting and getting data of entries directly.
	/// </summary>
	[Test]
	public void SettingEntryDataTest()
	{
		// set data of entries with each type of data
		byte[] xmpData = [ 1, 2 ];
		uint[] isoSpeedData = [ 100u ];
		double[] fNumberData = [ 1.8 ];
		var metadata = new TiffMediaMetadata();
		metadata.SetEntry(IfdNames.Default, MakeTagId, "Sony");
		metadata.SetEntry(IfdNames.Default, XmpTagId, xmpData);
		metadata.SetEntry(IfdNames.Exif, PhotographicSensitivityTagId, isoSpeedData);
		metadata.SetEntry(IfdNames.Exif, FNumberTagId, fNumberData);

		// check that data of every entry is got with the type it was set with
		Assert.That(metadata.TryGetEntryData(IfdNames.Default, MakeTagId, out string? stringData), Is.True);
		Assert.That(stringData, Is.EqualTo("Sony"));
		Assert.That(metadata.TryGetEntryData(IfdNames.Default, XmpTagId, out byte[]? byteData), Is.True);
		Assert.That(byteData, Is.EqualTo(xmpData));
		Assert.That(metadata.TryGetEntryData(IfdNames.Exif, PhotographicSensitivityTagId, out uint[]? uintData), Is.True);
		Assert.That(uintData, Is.EqualTo(isoSpeedData));
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Sony"));
		Assert.That(metadata.IsoSpeed, Is.EqualTo(100));
		Assert.That(metadata.FNumber, Is.EqualTo(1.8).Within(0.0001));

		// check that data of an entry cannot be got with a different type
		Assert.That(metadata.TryGetEntryData(IfdNames.Default, MakeTagId, out uintData), Is.False);
		Assert.That(uintData, Is.Null);
		Assert.That(metadata.TryGetEntryData(IfdNames.Exif, FNumberTagId, out stringData), Is.False);

		// check that data of an entry which was not set cannot be got
		Assert.That(metadata.TryGetEntryData(IfdNames.Exif, MakeTagId, out stringData), Is.False);
		Assert.That(metadata.TryGetEntryData(IfdNames.Default, ModelTagId, out stringData), Is.False);

		// check that the entry is removed when its data is empty
		metadata.SetEntry(IfdNames.Default, MakeTagId, "");
		Assert.That(metadata.TryGetEntryData(IfdNames.Default, MakeTagId, out stringData), Is.False);
		Assert.That(metadata.CameraManufacturer, Is.Null);
	}


	/// <summary>
	/// Test for the entry which refers to the Exif IFD of a thumbnail.
	/// </summary>
	[Test]
	public void ThumbnailExifIfdParsingTest()
	{
		// build data with a chain of IFDs where the Exif IFD is referred to by the thumbnail
		var data = BuildTiffData(true,
		[
			[ AsciiEntry(MakeTagId, "Canon") ],
			[ AsciiEntry(ModelTagId, "Thumbnail Model") ],
		], [ AsciiEntry(LensModelTagId, "RF 50mm F1.2") ], 1);

		// check that the Exif IFD of the thumbnail is not read
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Canon"));
		Assert.That(metadata.LensModel, Is.Null);
	}


	/// <summary>
	/// Test for trimming the string which is kept by an entry.
	/// </summary>
	[Test]
	public void TrimmingStringDataTest()
	{
		// build data with strings which are padded with null characters and spaces
		var data = BuildTiffData(true,
		[
			[
				AsciiEntry(MakeTagId, "  Fujifilm \0\0"),
				AsciiEntry(ModelTagId, "X-T5\0\0\0\0"),
				AsciiEntry(SoftwareTagId, " \0 "),
			],
		]);

		// check that the padding is trimmed and the entry which keeps nothing else is dropped
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Fujifilm"));
		Assert.That(metadata.CameraModel, Is.EqualTo("X-T5"));
		Assert.That(metadata.Software, Is.Null);
	}


	// Create an entry which keeps unsigned 16-bit integers.
	static TestEntry UInt16Entry(ushort tagId, params ushort[] values) =>
		new(tagId, IfdEntryType.UInt16, (uint)values.Length, values);


	// Create an entry which keeps unsigned 32-bit integers.
	static TestEntry UInt32Entry(ushort tagId, params uint[] values) =>
		new(tagId, IfdEntryType.UInt32, (uint)values.Length, values);


	/// <summary>
	/// Test for parsing the model of camera which is kept by the entry defined by DNG.
	/// </summary>
	[Test]
	public void UniqueCameraModelParsingTest()
	{
		// check that the model of camera defined by DNG is used when the model of camera is unavailable
		var data = BuildTiffData(true,
		[
			[
				AsciiEntry(MakeTagId, "Leica"),
				AsciiEntry(UniqueCameraModelTagId, "Leica M11"),
			],
		]);
		Assert.That(CreateMetadata(data).CameraModel, Is.EqualTo("Leica M11"));

		// check that the model of camera is used when both of the entries are available
		data = BuildTiffData(true,
		[
			[
				AsciiEntry(ModelTagId, "M11"),
				AsciiEntry(UniqueCameraModelTagId, "Leica M11"),
			],
		]);
		Assert.That(CreateMetadata(data).CameraModel, Is.EqualTo("M11"));
	}


	// Create an entry which keeps an unsigned rational number.
	static TestEntry URationalEntry(ushort tagId, uint numerator, uint denominator)
	{
		uint[] values = [ numerator, denominator ];
		return new(tagId, IfdEntryType.URational, 1, values);
	}


	// Write an unsigned 16-bit integer with the given byte ordering.
	static void WriteUInt16(Stream stream, bool isLittleEndian, ushort value)
	{
		var buffer = new byte[sizeof(ushort)];
		if (isLittleEndian)
			BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
		else
			BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
		stream.Write(buffer);
	}


	// Write an unsigned 32-bit integer with the given byte ordering.
	static void WriteUInt32(Stream stream, bool isLittleEndian, uint value)
	{
		var buffer = new byte[sizeof(uint)];
		if (isLittleEndian)
			BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
		else
			BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
		stream.Write(buffer);
	}


	/// <summary>
	/// Test for the entry which keeps a rational number with a zero denominator.
	/// </summary>
	[Test]
	public void ZeroDenominatorParsingTest()
	{
		// build data with a rational number which cannot be converted to a real number
		var data = BuildTiffData(true, [ [ AsciiEntry(MakeTagId, "Nikon") ] ],
		[
			URationalEntry(ExposureTimeTagId, 1, 0),
			URationalEntry(FNumberTagId, 28, 10),
		]);

		// check that the entry is dropped and the other entries are still parsed
		var metadata = CreateMetadata(data);
		Assert.That(metadata.ExposureTime, Is.Null);
		Assert.That(metadata.FNumber, Is.EqualTo(2.8).Within(0.0001));
	}
}
