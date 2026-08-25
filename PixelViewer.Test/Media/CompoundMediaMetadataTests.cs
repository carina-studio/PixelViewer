using Carina.PixelViewer.Media;
using NUnit.Framework;
using System;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="CompoundMediaMetadata"/>.
/// </summary>
[TestFixture]
class CompoundMediaMetadataTests
{
	// Compound metadata which selects the name of manufacturer of camera by itself for testing.
	class OverridingCompoundMediaMetadata(params IMediaMetadata?[] elements) : CompoundMediaMetadata(elements)
	{
		public override string? CameraManufacturer => "Overridden";
	}


	// Compound metadata which combines the given metadata for testing.
	class TestCompoundMediaMetadata(params IMediaMetadata?[] elements) : CompoundMediaMetadata(elements);


	// Constants.
	const ushort MakeTagId = 0x010f;


	// Create metadata which provides every value needed by the metadata, the values are identified by the given number.
	static XmpMediaMetadata CreateMetadata(int id)
	{
		var metadata = new XmpMediaMetadata();
		metadata.SetProperty(XmpNamespaces.Tiff, "Make", $"Make{id}");
		metadata.SetProperty(XmpNamespaces.Tiff, "Model", $"Model{id}");
		metadata.SetProperty(XmpNamespaces.Exif, "DateTimeOriginal", $"2026-08-2{id}T13:45:30Z");
		metadata.SetProperty(XmpNamespaces.Exif, "ExposureTime", $"1/{id}00");
		metadata.SetProperty(XmpNamespaces.Exif, "FNumber", $"{id}0/10");
		metadata.SetProperty(XmpNamespaces.Exif, "FocalLength", $"{id}00/10");
		metadata.SetProperty(XmpNamespaces.Exif, "FocalLengthIn35mmFilm", $"{id}0");
		metadata.SetProperty(XmpNamespaces.Exif, "ISOSpeedRatings", $"{id}00");
		metadata.SetProperty(XmpNamespaces.ExifEx, "LensMake", $"LensMake{id}");
		metadata.SetProperty(XmpNamespaces.ExifEx, "LensModel", $"LensModel{id}");
		metadata.SetProperty(XmpNamespaces.Xmp, "CreatorTool", $"Software{id}");
		return metadata;
	}


	// Create metadata which provides the name of manufacturer of camera only, the value is identified by the given number.
	static XmpMediaMetadata CreatePartialMetadata(int id)
	{
		var metadata = new XmpMediaMetadata();
		metadata.SetProperty(XmpNamespaces.Tiff, "Make", $"Make{id}");
		return metadata;
	}


	/// <summary>
	/// Test for selecting the values according to the order of the combined metadata.
	/// </summary>
	[Test]
	public void ElementOrderTest()
	{
		// check that the values are provided by the metadata which is placed before the other
		var metadata = new TestCompoundMediaMetadata(CreateMetadata(1), CreateMetadata(2));
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Make1"));
		Assert.That(metadata.IsoSpeed, Is.EqualTo(100));

		// check that reversing the order of the combined metadata reverses the selection
		metadata = new TestCompoundMediaMetadata(CreateMetadata(2), CreateMetadata(1));
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Make2"));
		Assert.That(metadata.IsoSpeed, Is.EqualTo(200));
	}


	/// <summary>
	/// Test for the metadata which combines nothing.
	/// </summary>
	[Test]
	public void EmptyMetadataTest()
	{
		// check that no value is provided when nothing is combined
		var metadata = new TestCompoundMediaMetadata();
		Assert.That(metadata.Elements, Is.Empty);
		Assert.That(metadata.CameraManufacturer, Is.Null);
		Assert.That(metadata.CameraModel, Is.Null);
		Assert.That(metadata.CreationTime, Is.Null);
		Assert.That(metadata.ExposureTime, Is.Null);
		Assert.That(metadata.FNumber, Is.Null);
		Assert.That(metadata.FocalLength, Is.Null);
		Assert.That(metadata.FocalLengthIn35mmFilm, Is.Null);
		Assert.That(metadata.IsoSpeed, Is.Null);
		Assert.That(metadata.LensManufacturer, Is.Null);
		Assert.That(metadata.LensModel, Is.Null);
		Assert.That(metadata.Software, Is.Null);

		// check that combining nothing but null is the same as combining nothing
		metadata = new TestCompoundMediaMetadata(null, null);
		Assert.That(metadata.Elements, Is.Empty);
		Assert.That(metadata.CameraManufacturer, Is.Null);
	}


	/// <summary>
	/// Test for finding the combined metadata with a specific type.
	/// </summary>
	[Test]
	public void FindingMetadataTest()
	{
		// check that the metadata itself is found
		var xmpMetadata = CreateMetadata(1);
		Assert.That(xmpMetadata.Find<XmpMediaMetadata>(), Is.SameAs(xmpMetadata));
		Assert.That(xmpMetadata.Find<TiffMediaMetadata>(), Is.Null);

		// check that the combined metadata is found
		var compoundMetadata = new TestCompoundMediaMetadata(xmpMetadata);
		Assert.That(compoundMetadata.Find<XmpMediaMetadata>(), Is.SameAs(xmpMetadata));
		Assert.That(compoundMetadata.Find<CompoundMediaMetadata>(), Is.SameAs(compoundMetadata));
		Assert.That(compoundMetadata.Find<TiffMediaMetadata>(), Is.Null);

		// check that the metadata which is combined by the combined metadata is found
		var tiffMetadata = new TiffMediaMetadata();
		tiffMetadata.SetEntry(IfdNames.Default, MakeTagId, "Nikon");
		var nestedCompoundMetadata = new TestCompoundMediaMetadata(new TestCompoundMediaMetadata(tiffMetadata), xmpMetadata);
		Assert.That(nestedCompoundMetadata.Find<TiffMediaMetadata>(), Is.SameAs(tiffMetadata));
		Assert.That(nestedCompoundMetadata.Find<XmpMediaMetadata>(), Is.SameAs(xmpMetadata));
	}


	/// <summary>
	/// Test for the metadata of each format of file.
	/// </summary>
	[Test]
	public void FormatCompoundMetadataTest()
	{
		// build the metadata parsed from the Exif data and the XMP data which provide different values
		var exifMetadata = new TiffMediaMetadata();
		exifMetadata.SetEntry(IfdNames.Default, MakeTagId, "Nikon");
		var xmpMetadata = CreateMetadata(1);

		// check that the metadata parsed from the Exif data is preferred by every format, and the value which it doesn't provide is still available
		Assert.That(new JpegCompoundMediaMetadata(exifMetadata, xmpMetadata).CameraManufacturer, Is.EqualTo("Nikon"));
		Assert.That(new JpegCompoundMediaMetadata(exifMetadata, xmpMetadata).Software, Is.EqualTo("Software1"));
		Assert.That(new TiffCompoundMediaMetadata(exifMetadata, xmpMetadata).CameraManufacturer, Is.EqualTo("Nikon"));
		Assert.That(new TiffCompoundMediaMetadata(exifMetadata, xmpMetadata).Software, Is.EqualTo("Software1"));
		Assert.That(new PngCompoundMediaMetadata(exifMetadata, xmpMetadata).CameraManufacturer, Is.EqualTo("Nikon"));
		Assert.That(new PngCompoundMediaMetadata(exifMetadata, xmpMetadata).Software, Is.EqualTo("Software1"));

		// check that the metadata which was not parsed is dropped
		var metadata = new JpegCompoundMediaMetadata(null, xmpMetadata);
		Assert.That(metadata.Elements, Is.EqualTo(new IMediaMetadata[] { xmpMetadata }));
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Make1"));
	}


	/// <summary>
	/// Test for dropping the metadata which is Null.
	/// </summary>
	[Test]
	public void NullElementTest()
	{
		// check that only the metadata which is not null is combined, and the order of the rest is kept
		var firstMetadata = CreateMetadata(1);
		var secondMetadata = CreateMetadata(2);
		var metadata = new TestCompoundMediaMetadata(null, firstMetadata, null, secondMetadata, null);
		Assert.That(metadata.Elements, Is.EqualTo(new IMediaMetadata[] { firstMetadata, secondMetadata }));
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Make1"));
	}


	/// <summary>
	/// Test for selecting the value by the metadata itself instead of the combined metadata.
	/// </summary>
	[Test]
	public void OverridingValueSelectionTest()
	{
		// check that the value selected by the metadata itself is used, and the other values are still selected from the combined metadata
		var metadata = new OverridingCompoundMediaMetadata(CreateMetadata(1));
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Overridden"));
		Assert.That(metadata.CameraModel, Is.EqualTo("Model1"));
	}


	/// <summary>
	/// Test for selecting each value from the combined metadata.
	/// </summary>
	[Test]
	public void ValueSelectionTest()
	{
		// combine the metadata which provides the name of manufacturer of camera only with the metadata which provides every value
		var metadata = new TestCompoundMediaMetadata(CreatePartialMetadata(1), CreateMetadata(2));

		// check that the value provided by the preferred metadata is selected, and the rest are selected from the other metadata
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Make1"));
		Assert.That(metadata.CameraModel, Is.EqualTo("Model2"));
		Assert.That(metadata.CreationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 22, 13, 45, 30)));
		Assert.That(metadata.ExposureTime, Is.EqualTo(TimeSpan.FromSeconds(1 / 200.0)));
		Assert.That(metadata.FNumber, Is.EqualTo(2.0).Within(0.0001));
		Assert.That(metadata.FocalLength, Is.EqualTo(20.0).Within(0.0001));
		Assert.That(metadata.FocalLengthIn35mmFilm, Is.EqualTo(20));
		Assert.That(metadata.IsoSpeed, Is.EqualTo(200));
		Assert.That(metadata.LensManufacturer, Is.EqualTo("LensMake2"));
		Assert.That(metadata.LensModel, Is.EqualTo("LensModel2"));
		Assert.That(metadata.Software, Is.EqualTo("Software2"));
	}
}
