using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Metadata of media which is parsed from XMP data.
/// </summary>
/// <remarks>The instance is not thread-safe, it should be completely set-up before being shared with other threads.</remarks>
class XmpMediaMetadata : IMediaMetadata
{
    // Constants.
    const string DefaultLanguage = "x-default";
    const int MaxDataSize = 2 << 20;
    const double MaxExposureTimeSeconds = 86400;


    // Static fields.
    static readonly XName RdfAltName = XName.Get("Alt", XmpNamespaces.Rdf);
    static readonly XName RdfBagName = XName.Get("Bag", XmpNamespaces.Rdf);
    static readonly XName RdfDescriptionName = XName.Get("Description", XmpNamespaces.Rdf);
    static readonly XName RdfLiName = XName.Get("li", XmpNamespaces.Rdf);
    static readonly XName RdfName = XName.Get("RDF", XmpNamespaces.Rdf);
    static readonly XName RdfResourceName = XName.Get("resource", XmpNamespaces.Rdf);
    static readonly XName RdfSeqName = XName.Get("Seq", XmpNamespaces.Rdf);
    static readonly XName XmlLangName = XName.Get("lang", XNamespace.Xml.NamespaceName);


    // Fields.
    readonly Dictionary<(string NamespaceUri, string Name), string[]> propertyValues = new();


    /// <inheritdoc/>
    public string? CameraManufacturer => this.GetPropertyValue(XmpNamespaces.Tiff, "Make");


    /// <inheritdoc/>
    public string? CameraModel => this.GetPropertyValue(XmpNamespaces.Tiff, "Model");


    // Create settings to read the document of XMP data, the data is provided by the source of media so no entity is allowed to be resolved.
    static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };


    /// <inheritdoc/>
    public DateTimeOffset? CreationTime
    {
        get
        {
            // get the time when the media was created, the time may be provided by different properties
            var timeString = this.GetPropertyValue(XmpNamespaces.Exif, "DateTimeOriginal")
                ?? this.GetPropertyValue(XmpNamespaces.Xmp, "CreateDate")
                ?? this.GetPropertyValue(XmpNamespaces.Photoshop, "DateCreated");
            if (timeString is null)
                return null;

            // parse the time which is represented in the format defined by ISO 8601
            if (TryParseDateTime(timeString, out var time))
                return time;
            return null;
        }
    }


    /// <inheritdoc/>
    public TimeSpan? ExposureTime
    {
        get
        {
            // get the exposure time in seconds
            var valueString = this.GetPropertyValue(XmpNamespaces.Exif, "ExposureTime");
            if (valueString is null || !TryParseRational(valueString, out var seconds))
                return null;

            // convert to time span, the value is bounded to keep it convertible
            if (seconds < 0 || seconds > MaxExposureTimeSeconds)
                return null;
            return TimeSpan.FromSeconds(seconds);
        }
    }


    /// <inheritdoc/>
    public double? FNumber
    {
        get
        {
            var valueString = this.GetPropertyValue(XmpNamespaces.Exif, "FNumber");
            if (valueString is not null && TryParseRational(valueString, out var value) && value > 0)
                return value;
            return null;
        }
    }


    /// <inheritdoc/>
    public double? FocalLength
    {
        get
        {
            var valueString = this.GetPropertyValue(XmpNamespaces.Exif, "FocalLength");
            if (valueString is not null && TryParseRational(valueString, out var value) && value > 0)
                return value;
            return null;
        }
    }


    /// <inheritdoc/>
    public int? FocalLengthIn35mmFilm
    {
        get
        {
            var valueString = this.GetPropertyValue(XmpNamespaces.Exif, "FocalLengthIn35mmFilm");
            if (valueString is not null
                && int.TryParse(valueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && value > 0)
            {
                return value;
            }
            return null;
        }
    }


    // Get the first value of the property with the given name, or Null if the property is unavailable.
    string? GetPropertyValue(string namespaceUri, string name) =>
        this.TryGetPropertyValue(namespaceUri, name, out var value) ? value : null;


    // Get values which are kept by the given element of property.
    static string[] GetPropertyValues(XElement element)
    {
        // the values may be kept by an array, the value for the default language is preferred by an alternative array
        var arrayElement = element.Element(RdfAltName) ?? element.Element(RdfBagName) ?? element.Element(RdfSeqName);
        if (arrayElement is not null)
        {
            var items = arrayElement.Elements(RdfLiName).ToList();
            if (arrayElement.Name == RdfAltName)
            {
                var defaultItemIndex = items.FindIndex(it => it.Attribute(XmlLangName)?.Value == DefaultLanguage);
                if (defaultItemIndex > 0)
                {
                    var defaultItem = items[defaultItemIndex];
                    items.RemoveAt(defaultItemIndex);
                    items.Insert(0, defaultItem);
                }
            }
            return items.Select(it => it.Value).ToArray();
        }

        // the value may be a reference to a resource
        var resourceAttribute = element.Attribute(RdfResourceName);
        if (resourceAttribute is not null)
            return [ resourceAttribute.Value ];

        // the value is kept by the element itself, a structured value is unsupported
        if (element.HasElements)
            return [];
        return [ element.Value ];
    }


    // Check whether the given string carries the offset to UTC or not.
    static bool HasUtcOffset(string s)
    {
        // the offset is placed after the time, a string which carries no time carries no offset
        var timeIndex = s.IndexOf('T');
        if (timeIndex < 0)
            return false;

        // find the designator of the offset
        for (var i = s.Length - 1; i > timeIndex; --i)
        {
            var c = s[i];
            if (c == 'Z' || c == 'z' || c == '+' || c == '-')
                return true;
        }
        return false;
    }


    /// <inheritdoc/>
    public int? IsoSpeed
    {
        get
        {
            var valueString = this.GetPropertyValue(XmpNamespaces.Exif, "ISOSpeedRatings");
            if (valueString is not null
                && int.TryParse(valueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && value > 0)
            {
                return value;
            }
            return null;
        }
    }


    // Check whether the given name is the name of a property or not.
    static bool IsPropertyName(XName name)
    {
        var namespaceName = name.NamespaceName;
        return namespaceName.Length > 0
            && namespaceName != XmpNamespaces.Rdf
            && namespaceName != XNamespace.Xml.NamespaceName
            && namespaceName != XNamespace.Xmlns.NamespaceName;
    }


    /// <inheritdoc/>
    public string? LensManufacturer => this.GetPropertyValue(XmpNamespaces.ExifEx, "LensMake");


    /// <inheritdoc/>
    public string? LensModel =>
        this.GetPropertyValue(XmpNamespaces.ExifEx, "LensModel")
        ?? this.GetPropertyValue(XmpNamespaces.Aux, "Lens");


    /// <summary>
    /// Get URIs of namespaces of the properties which are kept by the metadata.
    /// </summary>
    public IReadOnlyList<string> NamespaceUris =>
        this.propertyValues.Keys.Select(it => it.NamespaceUri).Distinct().ToArray();


    /// <summary>
    /// Set values of the property with the given name, the property will be removed if no value is given.
    /// </summary>
    /// <param name="namespaceUri">URI of namespace of the property.</param>
    /// <param name="name">Name of the property in its namespace.</param>
    /// <param name="values">Values of the property, the first value is the preferred one.</param>
    public void SetProperty(string namespaceUri, string name, params string[] values)
    {
        // trim the values, a value may be surrounded by the whitespaces which format the document
        var trimmedValues = values.Select(it => it.Trim()).Where(it => it.Length > 0).ToArray();

        // set or remove the property
        if (trimmedValues.IsEmpty())
            this.propertyValues.Remove((namespaceUri, name));
        else
            this.propertyValues[(namespaceUri, name)] = trimmedValues;
    }


    /// <inheritdoc/>
    public string? Software => this.GetPropertyValue(XmpNamespaces.Xmp, "CreatorTool");


    /// <summary>
    /// Try creating <see cref="XmpMediaMetadata"/> from the given XMP data.
    /// </summary>
    /// <param name="xml">Document of XMP data.</param>
    /// <param name="metadata">Created metadata.</param>
    /// <returns>True if the metadata is created successfully.</returns>
    public static bool TryCreate(string xml, [NotNullWhen(true)] out XmpMediaMetadata? metadata)
    {
        // check size of data
        if (xml.Length > MaxDataSize)
        {
            metadata = null;
            return false;
        }

        // load the document of XMP data
        XDocument document;
        try
        {
            using var textReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(textReader, CreateReaderSettings());
            document = XDocument.Load(xmlReader);
        }
        catch
        {
            metadata = null;
            return false;
        }

        // parse properties from the document
        return TryCreateFromDocument(document, out metadata);
    }


    /// <summary>
    /// Try creating <see cref="XmpMediaMetadata"/> from the XMP data in the given memory.
    /// </summary>
    /// <param name="data">Data which contains XMP data.</param>
    /// <param name="offset">Offset to XMP data in <paramref name="data"/>.</param>
    /// <param name="length">Size of XMP data in bytes.</param>
    /// <param name="metadata">Created metadata.</param>
    /// <returns>True if the metadata is created successfully.</returns>
    /// <remarks>The data which is placed before the document, such as the identifier of the container of XMP data, is skipped.</remarks>
    public static bool TryCreate(byte[] data, int offset, int length, [NotNullWhen(true)] out XmpMediaMetadata? metadata)
    {
        // check the range of data
        if (offset < 0 || length < 0 || offset > data.Length - length || length > MaxDataSize)
        {
            metadata = null;
            return false;
        }

        // skip the data which is placed before the document, the byte-order mark is kept for detecting the encoding
        var start = offset;
        var end = offset + length;
        if (start < end && data[start] != '<' && data[start] != 0xef && data[start] != 0xff && data[start] != 0xfe)
        {
            while (start < end && data[start] != '<')
                ++start;
        }
        if (start >= end)
        {
            metadata = null;
            return false;
        }

        // load the document of XMP data, the encoding of the document is detected by the reader
        XDocument document;
        try
        {
            using var stream = new MemoryStream(data, start, end - start);
            using var xmlReader = XmlReader.Create(stream, CreateReaderSettings());
            document = XDocument.Load(xmlReader);
        }
        catch
        {
            metadata = null;
            return false;
        }

        // parse properties from the document
        return TryCreateFromDocument(document, out metadata);
    }


    // Try creating metadata from the given document of XMP data.
    static bool TryCreateFromDocument(XDocument document, [NotNullWhen(true)] out XmpMediaMetadata? metadata)
    {
        // parse properties from each description of resource, a description which is nested in a structured value describes another resource
        var createdMetadata = new XmpMediaMetadata();
        foreach (var description in document.Descendants(RdfName).Elements(RdfDescriptionName))
        {
            foreach (var attribute in description.Attributes())
            {
                if (IsPropertyName(attribute.Name))
                    createdMetadata.SetProperty(attribute.Name.NamespaceName, attribute.Name.LocalName, attribute.Value);
            }
            foreach (var element in description.Elements())
            {
                if (IsPropertyName(element.Name))
                    createdMetadata.SetProperty(element.Name.NamespaceName, element.Name.LocalName, GetPropertyValues(element));
            }
        }

        // no metadata is available if no property was parsed
        if (createdMetadata.propertyValues.IsEmpty())
        {
            metadata = null;
            return false;
        }

        // complete
        metadata = createdMetadata;
        return true;
    }


    /// <summary>
    /// Try getting the preferred value of the property with the given name.
    /// </summary>
    /// <param name="namespaceUri">URI of namespace of the property.</param>
    /// <param name="name">Name of the property in its namespace.</param>
    /// <param name="value">Preferred value of the property.</param>
    /// <returns>True if the value is got successfully.</returns>
    public bool TryGetPropertyValue(string namespaceUri, string name, [NotNullWhen(true)] out string? value)
    {
        if (this.propertyValues.TryGetValue((namespaceUri, name), out var values))
        {
            value = values[0];
            return true;
        }
        value = null;
        return false;
    }


    /// <summary>
    /// Try getting all values of the property with the given name.
    /// </summary>
    /// <param name="namespaceUri">URI of namespace of the property.</param>
    /// <param name="name">Name of the property in its namespace.</param>
    /// <param name="values">Values of the property, the first value is the preferred one.</param>
    /// <returns>True if the values are got successfully.</returns>
    public bool TryGetPropertyValues(string namespaceUri, string name, [NotNullWhen(true)] out string[]? values) =>
        this.propertyValues.TryGetValue((namespaceUri, name), out values);


    // Try parsing the time which is represented in the format defined by ISO 8601.
    static bool TryParseDateTime(string s, out DateTimeOffset time)
    {
        // parse the time with the offset to UTC which is provided by the source of media
        if (HasUtcOffset(s))
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

        // parse the time without an offset, the time is treated as UTC without being changed
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeWithoutOffset))
        {
            time = new DateTimeOffset(timeWithoutOffset, TimeSpan.Zero);
            return true;
        }
        time = DateTimeOffset.MinValue;
        return false;
    }


    // Try parsing the real number which may be represented as a rational number.
    static bool TryParseRational(string s, out double value)
    {
        // parse the rational number
        var separatorIndex = s.IndexOf('/');
        if (separatorIndex >= 0)
        {
            if (double.TryParse(s.AsSpan(0, separatorIndex), NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
                && double.TryParse(s.AsSpan(separatorIndex + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
                && denominator != 0)
            {
                value = numerator / denominator;
                return true;
            }
            value = 0;
            return false;
        }

        // parse the real number
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
