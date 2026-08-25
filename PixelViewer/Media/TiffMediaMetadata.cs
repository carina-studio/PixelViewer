using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Metadata of media which is parsed from TIFF-based data.
/// </summary>
/// <param name="ifdIndex">Index of IFD which the metadata is parsed from.</param>
/// <remarks>The instance is not thread-safe, it should be completely set-up before being shared with other threads.</remarks>
class TiffMediaMetadata(int ifdIndex = 0) : IMediaMetadata
{
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
    const double MaxApertureValue = 20;
    const int MaxEntryCountToRead = 4096;
    const double MaxExposureTimeSeconds = 86400;
    const int MaxStringDataSize = 4096;
    const int MaxXmpDataSize = 2 << 20;
    const ushort ModelTagId = 0x0110;
    const ushort OffsetTimeOriginalTagId = 0x9011;
    const ushort OffsetTimeTagId = 0x9010;
    const ushort PhotographicSensitivityTagId = 0x8827;
    const ushort SoftwareTagId = 0x0131;
    const ushort SubSecTimeOriginalTagId = 0x9291;
    const ushort UniqueCameraModelTagId = 0xc614;
    const ushort XmpTagId = 0x02bc;


    // Fields.
    readonly Dictionary<(string IfdName, ushort TagId), object> entryValues = new();


    /// <inheritdoc/>
    public string? CameraManufacturer => this.GetStringEntryData(IfdNames.Default, MakeTagId);


    /// <inheritdoc/>
    public string? CameraModel =>
        this.GetStringEntryData(IfdNames.Default, ModelTagId)
        ?? this.GetStringEntryData(IfdNames.Default, UniqueCameraModelTagId);


    /// <inheritdoc/>
    public DateTimeOffset? CreationTime
    {
        get
        {
            // get the time when the media was captured, or the time when the media was changed
            var timeString = this.GetStringEntryData(IfdNames.Exif, DateTimeOriginalTagId);
            string? offsetString;
            string? subSecString = null;
            if (timeString is not null)
            {
                offsetString = this.GetStringEntryData(IfdNames.Exif, OffsetTimeOriginalTagId);
                subSecString = this.GetStringEntryData(IfdNames.Exif, SubSecTimeOriginalTagId);
            }
            else
            {
                timeString = this.GetStringEntryData(IfdNames.Default, DateTimeTagId);
                if (timeString is null)
                    return null;
                offsetString = this.GetStringEntryData(IfdNames.Exif, OffsetTimeTagId);
            }

            // parse the time which is represented in the format defined by TIFF
            if (!DateTime.TryParseExact(timeString, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return null;

            // add the fraction of second which is kept by another entry
            if (subSecString is not null
                && double.TryParse($"0.{subSecString}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var subSec))
            {
                time = time.AddSeconds(subSec);
            }

            // use the offset to UTC provided by the source of media, or treat the time as UTC if it is unavailable
            var offset = TimeSpan.Zero;
            if (offsetString is not null && TryParseUtcOffset(offsetString, out var parsedOffset))
                offset = parsedOffset;

            // combine the time and the offset, the time may be out of range after applying the offset
            try
            {
                return new DateTimeOffset(time, offset);
            }
            catch
            {
                return null;
            }
        }
    }


    /// <inheritdoc/>
    public TimeSpan? ExposureTime
    {
        get
        {
            // get the exposure time in seconds
            if (!this.TryGetEntryValue<double[]>(IfdNames.Exif, ExposureTimeTagId, out var data))
                return null;

            // convert to time span, the value is bounded to keep it convertible
            var seconds = data[0];
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
            // use the F-number provided by the source of media
            if (this.TryGetEntryValue<double[]>(IfdNames.Exif, FNumberTagId, out var data) && data[0] > 0)
                return data[0];

            // convert from the aperture value which is represented in APEX
            if (this.TryGetEntryValue(IfdNames.Exif, ApertureValueTagId, out data) && data[0] >= 0 && data[0] <= MaxApertureValue)
                return Math.Pow(2, data[0] / 2);

            // no F-number is available
            return null;
        }
    }


    /// <inheritdoc/>
    public double? FocalLength
    {
        get
        {
            if (this.TryGetEntryValue<double[]>(IfdNames.Exif, FocalLengthTagId, out var data) && data[0] > 0)
                return data[0];
            return null;
        }
    }


    /// <inheritdoc/>
    public int? FocalLengthIn35mmFilm
    {
        get
        {
            if (this.TryGetEntryValue<uint[]>(IfdNames.Exif, FocalLengthIn35mmFilmTagId, out var data) && data[0] > 0 && data[0] <= int.MaxValue)
                return (int)data[0];
            return null;
        }
    }


    // Get data of the entry with the given tag as string, or Null if the data is unavailable.
    string? GetStringEntryData(string ifdName, ushort tagId) =>
        this.TryGetEntryValue<string>(ifdName, tagId, out var data) ? data : null;


    /// <summary>
    /// Get index of IFD which the metadata is parsed from.
    /// </summary>
    public int IfdIndex { get; } = ifdIndex;


    /// <summary>
    /// Check whether no data of entry is kept by the metadata or not.
    /// </summary>
    public bool IsEmpty => this.entryValues.IsEmpty();


    /// <inheritdoc/>
    public int? IsoSpeed
    {
        get
        {
            if (this.TryGetEntryValue<uint[]>(IfdNames.Exif, PhotographicSensitivityTagId, out var data) && data[0] > 0 && data[0] <= int.MaxValue)
                return (int)data[0];
            return null;
        }
    }


    /// <inheritdoc/>
    public string? LensManufacturer => this.GetStringEntryData(IfdNames.Exif, LensMakeTagId);


    /// <inheritdoc/>
    public string? LensModel => this.GetStringEntryData(IfdNames.Exif, LensModelTagId);


    /// <summary>
    /// Set data of the entry with the given tag, the entry will be removed if the data is empty.
    /// </summary>
    /// <param name="ifdName">Name of IFD which the entry belongs to.</param>
    /// <param name="tagId">ID of tag of the entry.</param>
    /// <param name="data">Data of the entry.</param>
    public void SetEntry(string ifdName, ushort tagId, string data)
    {
        // trim the data, the string kept by an entry may be padded with null characters or spaces
        var trimmedData = data.Trim('\0', ' ', '\t', '\r', '\n');

        // set or remove the entry
        if (trimmedData.Length == 0)
            this.entryValues.Remove((ifdName, tagId));
        else
            this.entryValues[(ifdName, tagId)] = trimmedData;
    }


    /// <summary>
    /// Set data of the entry with the given tag, the entry will be removed if the data is empty.
    /// </summary>
    /// <param name="ifdName">Name of IFD which the entry belongs to.</param>
    /// <param name="tagId">ID of tag of the entry.</param>
    /// <param name="data">Data of the entry.</param>
    public void SetEntry(string ifdName, ushort tagId, byte[] data)
    {
        if (data.IsEmpty())
            this.entryValues.Remove((ifdName, tagId));
        else
            this.entryValues[(ifdName, tagId)] = data;
    }


    /// <summary>
    /// Set data of the entry with the given tag, the entry will be removed if the data is empty.
    /// </summary>
    /// <param name="ifdName">Name of IFD which the entry belongs to.</param>
    /// <param name="tagId">ID of tag of the entry.</param>
    /// <param name="data">Data of the entry.</param>
    public void SetEntry(string ifdName, ushort tagId, uint[] data)
    {
        if (data.IsEmpty())
            this.entryValues.Remove((ifdName, tagId));
        else
            this.entryValues[(ifdName, tagId)] = data;
    }


    /// <summary>
    /// Set data of the entry with the given tag, the entry will be removed if the data is empty.
    /// </summary>
    /// <param name="ifdName">Name of IFD which the entry belongs to.</param>
    /// <param name="tagId">ID of tag of the entry.</param>
    /// <param name="data">Data of the entry, the values which are not finite are dropped.</param>
    public void SetEntry(string ifdName, ushort tagId, double[] data)
    {
        // drop the values which are not finite, the denominator of a rational number kept by an entry may be zero
        var finiteData = data.Where(double.IsFinite).ToArray();

        // set or remove the entry
        if (finiteData.IsEmpty())
            this.entryValues.Remove((ifdName, tagId));
        else
            this.entryValues[(ifdName, tagId)] = finiteData;
    }


    /// <summary>
    /// Set data of the current entry of the given reader if the entry is needed by the metadata.
    /// </summary>
    /// <param name="reader">Reader of entries of IFDs.</param>
    /// <remarks>The method doesn't enqueue the sub IFD referenced by the current entry, the caller decides which sub IFDs to read.</remarks>
    public void SetEntry(IfdEntryReader reader)
    {
        // check whether the entry belongs to the IFD which the metadata is parsed from or not, IFDs with the same name are chained for different images
        var ifdName = reader.CurrentIfdName;
        if (ifdName is null)
            return;
        if (ifdName == IfdNames.Default && reader.CurrentIfdIndex != this.IfdIndex)
            return;

        // set data of the entry which is needed by the metadata
        var tagId = reader.CurrentEntryId;
        switch (tagId)
        {
            case DateTimeOriginalTagId:
            case DateTimeTagId:
            case LensMakeTagId:
            case LensModelTagId:
            case MakeTagId:
            case ModelTagId:
            case OffsetTimeOriginalTagId:
            case OffsetTimeTagId:
            case SoftwareTagId:
            case SubSecTimeOriginalTagId:
            case UniqueCameraModelTagId:
                if (reader.CurrentEntryType == IfdEntryType.AsciiString
                    && reader.CurrentEntryDataSize <= MaxStringDataSize
                    && reader.TryGetEntryData(out string? stringData))
                {
                    this.SetEntry(ifdName, tagId, stringData);
                }
                break;
            case ApertureValueTagId:
            case ExposureTimeTagId:
            case FNumberTagId:
            case FocalLengthTagId:
                if (reader.TryGetEntryData(out double[]? doubleData))
                    this.SetEntry(ifdName, tagId, doubleData);
                break;
            case FocalLengthIn35mmFilmTagId:
            case PhotographicSensitivityTagId:
                switch (reader.CurrentEntryType)
                {
                    case IfdEntryType.UInt16:
                        if (reader.TryGetEntryData(out ushort[]? ushortData))
                            this.SetEntry(ifdName, tagId, Array.ConvertAll(ushortData, it => (uint)it));
                        break;
                    case IfdEntryType.UInt32:
                        if (reader.TryGetEntryData(out uint[]? uintData))
                            this.SetEntry(ifdName, tagId, uintData);
                        break;
                }
                break;
            case XmpTagId:
                if (reader.CurrentEntryDataSize <= MaxXmpDataSize
                    && reader.TryGetEntryData(out byte[]? byteData))
                {
                    this.SetEntry(ifdName, tagId, byteData);
                }
                break;
        }
    }


    /// <inheritdoc/>
    public string? Software => this.GetStringEntryData(IfdNames.Default, SoftwareTagId);


    /// <summary>
    /// Try creating <see cref="TiffMediaMetadata"/> from the TIFF-based data in the given stream.
    /// </summary>
    /// <param name="stream">Seekable <see cref="Stream"/> which is positioned at the header of TIFF-based data.</param>
    /// <param name="metadata">Created metadata.</param>
    /// <returns>True if the metadata is created successfully.</returns>
    /// <remarks>The metadata is parsed from the first IFD and the Exif IFD referenced by it.</remarks>
    public static bool TryCreate(Stream stream, [NotNullWhen(true)] out TiffMediaMetadata? metadata)
    {
        // create reader of entries, the header of TIFF-based data is checked by the reader
        IfdEntryReader reader;
        try
        {
            reader = new IfdEntryReader(stream);
        }
        catch
        {
            metadata = null;
            return false;
        }

        // read entries of the first IFD and the Exif IFD referenced by it, the number of entries is bounded because IFDs may refer to each other
        var createdMetadata = new TiffMediaMetadata();
        try
        {
            for (var i = MaxEntryCountToRead; i > 0 && reader.Read(); --i)
            {
                if (reader.CurrentEntryId == ExifIfdPointerTagId
                    && reader.CurrentIfdName == IfdNames.Default
                    && reader.CurrentIfdIndex == 0
                    && reader.TryGetEntryData(out uint[]? ifdOffsets)
                    && ifdOffsets.IsNotEmpty())
                {
                    reader.EnqueueIfdToRead(reader.InitialStreamPosition + ifdOffsets[0], IfdNames.Exif);
                }
                else
                    createdMetadata.SetEntry(reader);
            }
        }
        catch
        {
            // the data may be malformed, keep the entries which were read before the failure
        }

        // no metadata is available if no entry needed by the metadata was read
        if (createdMetadata.IsEmpty)
        {
            metadata = null;
            return false;
        }

        // complete
        metadata = createdMetadata;
        return true;
    }


    /// <summary>
    /// Try creating <see cref="TiffMediaMetadata"/> from the TIFF-based data in the given memory.
    /// </summary>
    /// <param name="data">Data which contains TIFF-based data.</param>
    /// <param name="offset">Offset to the header of TIFF-based data in <paramref name="data"/>.</param>
    /// <param name="metadata">Created metadata.</param>
    /// <returns>True if the metadata is created successfully.</returns>
    /// <remarks>The metadata is parsed from the first IFD and the Exif IFD referenced by it.</remarks>
    public static bool TryCreate(byte[] data, int offset, [NotNullWhen(true)] out TiffMediaMetadata? metadata)
    {
        // check offset to the header of TIFF-based data
        if (offset < 0 || offset >= data.Length)
        {
            metadata = null;
            return false;
        }

        // create metadata from the data in memory
        using var stream = new MemoryStream(data);
        stream.Position = offset;
        return TryCreate(stream, out metadata);
    }


    /// <summary>
    /// Try getting data of the entry with the given tag.
    /// </summary>
    /// <param name="ifdName">Name of IFD which the entry belongs to.</param>
    /// <param name="tagId">ID of tag of the entry.</param>
    /// <param name="data">Data of the entry.</param>
    /// <returns>True if the data is got successfully.</returns>
    public bool TryGetEntryData(string ifdName, ushort tagId, [NotNullWhen(true)] out string? data) =>
        this.TryGetEntryValue(ifdName, tagId, out data);


    /// <summary>
    /// Try getting data of the entry with the given tag.
    /// </summary>
    /// <param name="ifdName">Name of IFD which the entry belongs to.</param>
    /// <param name="tagId">ID of tag of the entry.</param>
    /// <param name="data">Data of the entry.</param>
    /// <returns>True if the data is got successfully.</returns>
    public bool TryGetEntryData(string ifdName, ushort tagId, [NotNullWhen(true)] out byte[]? data) =>
        this.TryGetEntryValue(ifdName, tagId, out data);


    /// <summary>
    /// Try getting data of the entry with the given tag.
    /// </summary>
    /// <param name="ifdName">Name of IFD which the entry belongs to.</param>
    /// <param name="tagId">ID of tag of the entry.</param>
    /// <param name="data">Data of the entry, the data of an entry which keeps unsigned 16-bit integers is widened to unsigned 32-bit integers.</param>
    /// <returns>True if the data is got successfully.</returns>
    public bool TryGetEntryData(string ifdName, ushort tagId, [NotNullWhen(true)] out uint[]? data) =>
        this.TryGetEntryValue(ifdName, tagId, out data);


    /// <summary>
    /// Try getting data of the entry with the given tag.
    /// </summary>
    /// <param name="ifdName">Name of IFD which the entry belongs to.</param>
    /// <param name="tagId">ID of tag of the entry.</param>
    /// <param name="data">Data of the entry.</param>
    /// <returns>True if the data is got successfully.</returns>
    public bool TryGetEntryData(string ifdName, ushort tagId, [NotNullWhen(true)] out double[]? data) =>
        this.TryGetEntryValue(ifdName, tagId, out data);


    // Try getting data of the entry with the given tag as the given type.
    bool TryGetEntryValue<T>(string ifdName, ushort tagId, [NotNullWhen(true)] out T? data) where T : class
    {
        // get data of the entry
        if (this.entryValues.TryGetValue((ifdName, tagId), out var value) && value is T typedValue)
        {
            data = typedValue;
            return true;
        }

        // no data of the entry is available
        data = null;
        return false;
    }


    // Try parsing the offset to UTC which is represented in the format defined by Exif, ex. "+08:00".
    static bool TryParseUtcOffset(string s, out TimeSpan offset)
    {
        // check format
        offset = TimeSpan.Zero;
        if (s.Length != 6 || s[3] != ':')
            return false;
        if (s[0] != '+' && s[0] != '-')
            return false;

        // parse hours and minutes
        if (!int.TryParse(s.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(s.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || hours > 23
            || minutes > 59)
        {
            return false;
        }

        // complete
        offset = new TimeSpan(hours, minutes, 0);
        if (s[0] == '-')
            offset = -offset;
        return true;
    }
}
