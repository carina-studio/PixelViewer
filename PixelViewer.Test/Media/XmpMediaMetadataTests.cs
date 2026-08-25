using Carina.PixelViewer.Media;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Text;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="XmpMediaMetadata"/>.
/// </summary>
[TestFixture]
class XmpMediaMetadataTests
{
	// Constants.
	const string NamespaceDeclarations =
		"xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" "
		+ "xmlns:aux=\"http://ns.adobe.com/exif/1.0/aux/\" "
		+ "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" "
		+ "xmlns:exif=\"http://ns.adobe.com/exif/1.0/\" "
		+ "xmlns:exifEX=\"http://cipa.jp/exif/1.0/\" "
		+ "xmlns:photoshop=\"http://ns.adobe.com/photoshop/1.0/\" "
		+ "xmlns:tiff=\"http://ns.adobe.com/tiff/1.0/\" "
		+ "xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" "
		+ "xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\"";
	const string XmpDataIdentifier = "http://ns.adobe.com/xap/1.0/\0";


	/// <summary>
	/// Test for parsing the property which keeps an array of values.
	/// </summary>
	[Test]
	public void ArrayValueParsingTest()
	{
		// check that the value for the default language is preferred by an alternative array no matter where it is placed
		var data = BuildXmpData("", "<dc:title><rdf:Alt><rdf:li xml:lang=\"de\">Titel</rdf:li><rdf:li xml:lang=\"x-default\">Title</rdf:li></rdf:Alt></dc:title>");
		var metadata = CreateMetadata(data);
		Assert.That(metadata.TryGetPropertyValue(XmpNamespaces.DC, "title", out var value), Is.True);
		Assert.That(value, Is.EqualTo("Title"));
		Assert.That(metadata.TryGetPropertyValues(XmpNamespaces.DC, "title", out var values), Is.True);
		Assert.That(values, Is.EqualTo(new[] { "Title", "Titel" }));

		// check that an ordered array keeps its own order
		data = BuildXmpData("", "<dc:creator><rdf:Seq><rdf:li>Alice</rdf:li><rdf:li>Bob</rdf:li></rdf:Seq></dc:creator>");
		metadata = CreateMetadata(data);
		Assert.That(metadata.TryGetPropertyValue(XmpNamespaces.DC, "creator", out value), Is.True);
		Assert.That(value, Is.EqualTo("Alice"));
		Assert.That(metadata.TryGetPropertyValues(XmpNamespaces.DC, "creator", out values), Is.True);
		Assert.That(values, Is.EqualTo(new[] { "Alice", "Bob" }));

		// check that an unordered array is parsed
		data = BuildXmpData("", "<dc:subject><rdf:Bag><rdf:li>Landscape</rdf:li></rdf:Bag></dc:subject>");
		Assert.That(CreateMetadata(data).TryGetPropertyValues(XmpNamespaces.DC, "subject", out values), Is.True);
		Assert.That(values, Is.EqualTo(new[] { "Landscape" }));
	}


	/// <summary>
	/// Test for parsing the properties which are kept by attributes or child elements.
	/// </summary>
	[Test]
	public void AttributeAndElementFormParsingTest()
	{
		// check that the properties kept by attributes are parsed
		var data = BuildXmpData("tiff:Make=\"Nikon\" tiff:Model=\"D850\"", "");
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Nikon"));
		Assert.That(metadata.CameraModel, Is.EqualTo("D850"));

		// check that the properties kept by child elements are parsed in the same way
		data = BuildXmpData("", "<tiff:Make>Nikon</tiff:Make><tiff:Model>D850</tiff:Model>");
		metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Nikon"));
		Assert.That(metadata.CameraModel, Is.EqualTo("D850"));

		// check that the whitespaces which format the document are trimmed
		data = BuildXmpData("", "<tiff:Make>\n\t\tNikon\n\t</tiff:Make>");
		Assert.That(CreateMetadata(data).CameraManufacturer, Is.EqualTo("Nikon"));
	}


	// Build XMP data which contains the given properties.
	static string BuildXmpData(string attributes, string elements) =>
		"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
		+ $"<rdf:RDF {NamespaceDeclarations}>"
		+ $"<rdf:Description rdf:about=\"\" {attributes}>{elements}</rdf:Description>"
		+ "</rdf:RDF></x:xmpmeta>";


	/// <summary>
	/// Test for parsing XMP data which is kept in memory.
	/// </summary>
	[Test]
	public void ByteDataParsingTest()
	{
		// check that data which contains the document only is parsed
		var xml = BuildXmpData("tiff:Make=\"Nikon\"", "");
		var data = Encoding.UTF8.GetBytes(xml);
		Assert.That(XmpMediaMetadata.TryCreate(data, 0, data.Length, out var metadata), Is.True);
		Assert.That(metadata.AsNonNull().CameraManufacturer, Is.EqualTo("Nikon"));

		// check that the identifier of the container of XMP data is skipped
		data = Encoding.UTF8.GetBytes(XmpDataIdentifier + xml);
		Assert.That(XmpMediaMetadata.TryCreate(data, 0, data.Length, out metadata), Is.True);
		Assert.That(metadata.AsNonNull().CameraManufacturer, Is.EqualTo("Nikon"));

		// check that the byte-order mark is kept for detecting the encoding of the document
		var preamble = Encoding.UTF8.GetPreamble();
		var xmlData = Encoding.UTF8.GetBytes(xml);
		data = new byte[preamble.Length + xmlData.Length];
		Array.Copy(preamble, data, preamble.Length);
		Array.Copy(xmlData, 0, data, preamble.Length, xmlData.Length);
		Assert.That(XmpMediaMetadata.TryCreate(data, 0, data.Length, out metadata), Is.True);
		Assert.That(metadata.AsNonNull().CameraManufacturer, Is.EqualTo("Nikon"));

		// check that the data is parsed from the given range
		data = Encoding.UTF8.GetBytes($"____{xml}");
		Assert.That(XmpMediaMetadata.TryCreate(data, 4, data.Length - 4, out metadata), Is.True);
		Assert.That(metadata.AsNonNull().CameraManufacturer, Is.EqualTo("Nikon"));

		// check that an invalid range of data is rejected
		Assert.That(XmpMediaMetadata.TryCreate(data, -1, data.Length, out _), Is.False);
		Assert.That(XmpMediaMetadata.TryCreate(data, 0, data.Length + 1, out _), Is.False);
		Assert.That(XmpMediaMetadata.TryCreate(data, 0, -1, out _), Is.False);
		Assert.That(XmpMediaMetadata.TryCreate([], 0, 0, out _), Is.False);
	}


	// Create metadata from the given XMP data.
	static XmpMediaMetadata CreateMetadata(string xml)
	{
		var isCreated = XmpMediaMetadata.TryCreate(xml, out var metadata);
		Assert.That(isCreated, Is.True);
		return metadata.AsNonNull();
	}


	/// <summary>
	/// Test for parsing the time when the media was created.
	/// </summary>
	[Test]
	public void CreationTimeParsingTest()
	{
		// check that the time in UTC is parsed
		var data = BuildXmpData("", "<exif:DateTimeOriginal>2026-08-24T13:45:30Z</exif:DateTimeOriginal>");
		var creationTime = CreateMetadata(data).CreationTime;
		Assert.That(creationTime, Is.Not.Null);
		Assert.That(creationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30)));
		Assert.That(creationTime.GetValueOrDefault().Offset, Is.EqualTo(TimeSpan.Zero));

		// check that the offset to UTC provided by the source of media is used without changing the time itself
		data = BuildXmpData("", "<exif:DateTimeOriginal>2026-08-24T13:45:30+08:00</exif:DateTimeOriginal>");
		creationTime = CreateMetadata(data).CreationTime;
		Assert.That(creationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30)));
		Assert.That(creationTime.GetValueOrDefault().Offset, Is.EqualTo(TimeSpan.FromHours(8)));

		// check that a negative offset to UTC is parsed
		data = BuildXmpData("", "<exif:DateTimeOriginal>2026-08-24T13:45:30-05:30</exif:DateTimeOriginal>");
		creationTime = CreateMetadata(data).CreationTime;
		Assert.That(creationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30)));
		Assert.That(creationTime.GetValueOrDefault().Offset, Is.EqualTo(TimeSpan.FromHours(-5.5)));

		// check that the time is treated as UTC when the source of media provides no offset to UTC, the time itself is kept no matter which time zone the test runs in
		data = BuildXmpData("", "<exif:DateTimeOriginal>2026-08-24T13:45:30</exif:DateTimeOriginal>");
		creationTime = CreateMetadata(data).CreationTime;
		Assert.That(creationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30)));
		Assert.That(creationTime.GetValueOrDefault().Offset, Is.EqualTo(TimeSpan.Zero));

		// check that a date without a time is parsed
		data = BuildXmpData("", "<exif:DateTimeOriginal>2026-08-24</exif:DateTimeOriginal>");
		creationTime = CreateMetadata(data).CreationTime;
		Assert.That(creationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24)));
		Assert.That(creationTime.GetValueOrDefault().Offset, Is.EqualTo(TimeSpan.Zero));

		// check that the time provided by the properties of XMP and Photoshop is used when the time of Exif is unavailable
		data = BuildXmpData("", "<xmp:CreateDate>2026-01-02T03:04:05Z</xmp:CreateDate>");
		Assert.That(CreateMetadata(data).CreationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 1, 2, 3, 4, 5)));
		data = BuildXmpData("", "<photoshop:DateCreated>2026-02-03T04:05:06Z</photoshop:DateCreated>");
		Assert.That(CreateMetadata(data).CreationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 2, 3, 4, 5, 6)));

		// check that the time of Exif is preferred
		data = BuildXmpData("", "<exif:DateTimeOriginal>2026-08-24T13:45:30Z</exif:DateTimeOriginal><xmp:CreateDate>2026-01-02T03:04:05Z</xmp:CreateDate>");
		Assert.That(CreateMetadata(data).CreationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30)));

		// check that a malformed time is dropped
		data = BuildXmpData("", "<tiff:Make>Nikon</tiff:Make><exif:DateTimeOriginal>yesterday</exif:DateTimeOriginal>");
		Assert.That(CreateMetadata(data).CreationTime, Is.Null);
	}


	/// <summary>
	/// Test for rejecting the document which declares a document type.
	/// </summary>
	[Test]
	public void DtdRejectionTest()
	{
		// check that a document which declares an internal entity is rejected, the entity may be expanded repeatedly to exhaust the memory
		var data = "<!DOCTYPE rdf:RDF [<!ENTITY payload \"expanded\">]>" + BuildXmpData("", "<tiff:Make>&payload;</tiff:Make>");
		Assert.That(XmpMediaMetadata.TryCreate(data, out _), Is.False);

		// check that a document which refers to an external entity is rejected without resolving the entity
		data = "<!DOCTYPE rdf:RDF [<!ENTITY payload SYSTEM \"file:///etc/passwd\">]>" + BuildXmpData("", "<tiff:Make>&payload;</tiff:Make>");
		Assert.That(XmpMediaMetadata.TryCreate(data, out _), Is.False);
	}


	/// <summary>
	/// Test for creating metadata from invalid data.
	/// </summary>
	[Test]
	public void InvalidDataParsingTest()
	{
		// check that data which is not a document is rejected
		Assert.That(XmpMediaMetadata.TryCreate("", out _), Is.False);
		Assert.That(XmpMediaMetadata.TryCreate("Nikon D850", out _), Is.False);

		// check that a malformed document is rejected
		Assert.That(XmpMediaMetadata.TryCreate("<rdf:RDF><rdf:Description", out _), Is.False);

		// check that a document which describes no resource is rejected
		Assert.That(XmpMediaMetadata.TryCreate("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>", out _), Is.False);

		// check that a document which contains no property is rejected
		Assert.That(XmpMediaMetadata.TryCreate(BuildXmpData("", ""), out _), Is.False);

		// check that an oversized document is rejected before being parsed
		var oversizedData = BuildXmpData("", $"<tiff:Make>{new string('a', 2 << 20)}</tiff:Make>");
		Assert.That(XmpMediaMetadata.TryCreate(oversizedData, out _), Is.False);
	}


	/// <summary>
	/// Test for parsing the properties which are declared with different prefixes of namespaces.
	/// </summary>
	[Test]
	public void NamespacePrefixIndependenceTest()
	{
		// check that a property is identified by the URI of its namespace instead of the prefix which is chosen by the source of media
		var data = "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:whatever=\"http://ns.adobe.com/tiff/1.0/\">"
			+ "<rdf:Description whatever:Make=\"Nikon\"><whatever:Model>D850</whatever:Model></rdf:Description>"
			+ "</rdf:RDF>";
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Nikon"));
		Assert.That(metadata.CameraModel, Is.EqualTo("D850"));
	}


	/// <summary>
	/// Test for parsing each property which is needed by the metadata.
	/// </summary>
	[Test]
	public void PropertyParsingTest()
	{
		// build data which contains every property needed by the metadata
		var data = BuildXmpData("",
			"<tiff:Make>Nikon</tiff:Make>"
			+ "<tiff:Model>D850</tiff:Model>"
			+ "<exif:DateTimeOriginal>2026-08-24T13:45:30+08:00</exif:DateTimeOriginal>"
			+ "<exif:ExposureTime>1/250</exif:ExposureTime>"
			+ "<exif:FNumber>28/10</exif:FNumber>"
			+ "<exif:FocalLength>500/10</exif:FocalLength>"
			+ "<exif:FocalLengthIn35mmFilm>75</exif:FocalLengthIn35mmFilm>"
			+ "<exif:ISOSpeedRatings><rdf:Seq><rdf:li>400</rdf:li></rdf:Seq></exif:ISOSpeedRatings>"
			+ "<exifEX:LensMake>Nikkor</exifEX:LensMake>"
			+ "<exifEX:LensModel>50mm f/1.8</exifEX:LensModel>"
			+ "<xmp:CreatorTool>Lightroom</xmp:CreatorTool>");

		// check that every property is parsed
		var metadata = CreateMetadata(data);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Nikon"));
		Assert.That(metadata.CameraModel, Is.EqualTo("D850"));
		Assert.That(metadata.CreationTime.GetValueOrDefault().DateTime, Is.EqualTo(new DateTime(2026, 8, 24, 13, 45, 30)));
		Assert.That(metadata.ExposureTime, Is.EqualTo(TimeSpan.FromSeconds(1 / 250.0)));
		Assert.That(metadata.FNumber, Is.EqualTo(2.8).Within(0.0001));
		Assert.That(metadata.FocalLength, Is.EqualTo(50.0).Within(0.0001));
		Assert.That(metadata.FocalLengthIn35mmFilm, Is.EqualTo(75));
		Assert.That(metadata.IsoSpeed, Is.EqualTo(400));
		Assert.That(metadata.LensManufacturer, Is.EqualTo("Nikkor"));
		Assert.That(metadata.LensModel, Is.EqualTo("50mm f/1.8"));
		Assert.That(metadata.Software, Is.EqualTo("Lightroom"));

		// check that the model of lens defined by Adobe is used when the model of lens is unavailable
		data = BuildXmpData("", "<aux:Lens>50mm f/1.8</aux:Lens>");
		Assert.That(CreateMetadata(data).LensModel, Is.EqualTo("50mm f/1.8"));
	}


	/// <summary>
	/// Test for parsing the value which is represented as a rational number.
	/// </summary>
	[Test]
	public void RationalValueParsingTest()
	{
		// check that a rational number is converted to a real number
		var data = BuildXmpData("", "<exif:FNumber>28/10</exif:FNumber>");
		Assert.That(CreateMetadata(data).FNumber, Is.EqualTo(2.8).Within(0.0001));

		// check that a real number is parsed directly
		data = BuildXmpData("", "<exif:FocalLength>50</exif:FocalLength>");
		Assert.That(CreateMetadata(data).FocalLength, Is.EqualTo(50.0).Within(0.0001));

		// check that a rational number with a zero denominator is dropped
		data = BuildXmpData("", "<tiff:Make>Nikon</tiff:Make><exif:ExposureTime>1/0</exif:ExposureTime>");
		Assert.That(CreateMetadata(data).ExposureTime, Is.Null);

		// check that a malformed number is dropped
		data = BuildXmpData("", "<tiff:Make>Nikon</tiff:Make><exif:FNumber>wide open</exif:FNumber>");
		Assert.That(CreateMetadata(data).FNumber, Is.Null);
	}


	/// <summary>
	/// Test for setting and getting values of properties directly.
	/// </summary>
	[Test]
	public void SettingPropertyTest()
	{
		// check that every value of a property is kept and the first one is preferred
		var metadata = new XmpMediaMetadata();
		metadata.SetProperty(XmpNamespaces.DC, "creator", "Alice", "Bob");
		Assert.That(metadata.TryGetPropertyValue(XmpNamespaces.DC, "creator", out var value), Is.True);
		Assert.That(value, Is.EqualTo("Alice"));
		Assert.That(metadata.TryGetPropertyValues(XmpNamespaces.DC, "creator", out var values), Is.True);
		Assert.That(values, Is.EqualTo(new[] { "Alice", "Bob" }));

		// check that the value of a property is trimmed and the property which is needed by the metadata is reported
		metadata.SetProperty(XmpNamespaces.Tiff, "Make", "  Nikon  ");
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Nikon"));

		// check that the URI of namespace of every property is reported
		Assert.That(metadata.NamespaceUris, Is.EquivalentTo(new[] { XmpNamespaces.DC, XmpNamespaces.Tiff }));

		// check that a property which was not set cannot be got
		Assert.That(metadata.TryGetPropertyValue(XmpNamespaces.Tiff, "Model", out value), Is.False);
		Assert.That(value, Is.Null);
		Assert.That(metadata.TryGetPropertyValues(XmpNamespaces.Exif, "FNumber", out values), Is.False);
		Assert.That(values, Is.Null);

		// check that a property is removed when it keeps no value
		metadata.SetProperty(XmpNamespaces.Tiff, "Make");
		Assert.That(metadata.CameraManufacturer, Is.Null);
		metadata.SetProperty(XmpNamespaces.DC, "creator", " ");
		Assert.That(metadata.TryGetPropertyValues(XmpNamespaces.DC, "creator", out values), Is.False);
	}


	/// <summary>
	/// Test for parsing the property which keeps a structured value.
	/// </summary>
	[Test]
	public void StructuredValueParsingTest()
	{
		// build data with a property which keeps a structured value
		var data = BuildXmpData("",
			"<xmpMM:DerivedFrom rdf:parseType=\"Resource\"><tiff:Make>Structured</tiff:Make></xmpMM:DerivedFrom>"
			+ "<tiff:Make>Nikon</tiff:Make>");

		// check that the structured value is dropped and the other properties are still parsed
		var metadata = CreateMetadata(data);
		Assert.That(metadata.TryGetPropertyValue("http://ns.adobe.com/xap/1.0/mm/", "DerivedFrom", out _), Is.False);
		Assert.That(metadata.CameraManufacturer, Is.EqualTo("Nikon"));

		// check that a description which is nested in a structured value describes another resource instead of this one
		data = BuildXmpData("",
			"<xmpMM:DerivedFrom><rdf:Description tiff:Make=\"Structured\"/></xmpMM:DerivedFrom>"
			+ "<tiff:Make>Nikon</tiff:Make>");
		Assert.That(CreateMetadata(data).CameraManufacturer, Is.EqualTo("Nikon"));
	}


	/// <summary>
	/// Test for parsing the property in a namespace which is unknown to the metadata.
	/// </summary>
	[Test]
	public void UnknownNamespaceParsingTest()
	{
		// check that a property in an unknown namespace is kept and can be got
		var data = "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:sensor=\"https://carinastudio.azurewebsites.net/ns/sensor/\">"
			+ "<rdf:Description sensor:Temperature=\"36.5\"/>"
			+ "</rdf:RDF>";
		var metadata = CreateMetadata(data);
		Assert.That(metadata.TryGetPropertyValue("https://carinastudio.azurewebsites.net/ns/sensor/", "Temperature", out var value), Is.True);
		Assert.That(value, Is.EqualTo("36.5"));
		Assert.That(metadata.CameraManufacturer, Is.Null);
	}
}
