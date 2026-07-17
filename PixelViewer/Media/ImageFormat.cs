using System.IO;
using CarinaStudio;
using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Image format.
/// </summary>
class ImageFormat : IEquatable<ImageFormat>
{
	/// <summary>
	/// Maximum number of planes defined for each format.
	/// </summary>
	public const int MaxPlaneCount = 4;


	// Static fields.
	static readonly SortedList<string, ImageFormat> formatsByKeyword = new(Comparer<string>.Create((x, y) =>
	{
		var result = y.Length - x.Length;
		if (result != 0)
			return result;
		return string.Compare(x, y, StringComparison.InvariantCultureIgnoreCase);
	}));
	static readonly Dictionary<string, ImageFormat> formatsByName = new();


	// Fields.
	readonly string? displayName;


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance which is defined by user.
	/// </summary>
	/// <param name="id">Identifier of format, which is also its <see cref="Name"/>.</param>
	/// <param name="displayName">Name of format for displaying to user.</param>
	/// <param name="hasMultiByteOrderings">Whether multiple byte orderings are supported by this format or not.</param>
	/// <param name="planeDescriptors">Plane descriptors.</param>
	/// <remarks>The identifier is stable across renaming, so it is the value persisted by profiles and sessions to refer to the format. A user-defined format registers no keyword, it never takes part in detecting format by file name.</remarks>
	public ImageFormat(string id, string displayName, bool hasMultiByteOrderings, IList<ImagePlaneDescriptor> planeDescriptors) : this(ImageFormatCategory.UserDefined, id, hasMultiByteOrderings, planeDescriptors, [])
	{
		this.displayName = displayName;
	}


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="planeDescriptor">Plane descriptor.</param>
	public ImageFormat(ImageFormatCategory category, string name, ImagePlaneDescriptor planeDescriptor) : this(category, name, [ planeDescriptor ], Array.Empty<string>())
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="planeDescriptor">Plane descriptor.</param>
	/// <param name="keywords">Keywords.</param>
	public ImageFormat(ImageFormatCategory category, string name, ImagePlaneDescriptor planeDescriptor, IEnumerable<string> keywords) : this(category, name, [ planeDescriptor ], keywords)
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="hasMultiByteOrderings">Whether multiple byte orderings are supported by this format or not.</param>
	/// <param name="planeDescriptor">Plane descriptor.</param>
	public ImageFormat(ImageFormatCategory category, string name, bool hasMultiByteOrderings, ImagePlaneDescriptor planeDescriptor) : this(category, name, hasMultiByteOrderings, [ planeDescriptor ], Array.Empty<string>())
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="hasMultiByteOrderings">Whether multiple byte orderings are supported by this format or not.</param>
	/// <param name="planeDescriptor">Plane descriptor.</param>
	/// <param name="keywords">Keywords.</param>
	public ImageFormat(ImageFormatCategory category, string name, bool hasMultiByteOrderings, ImagePlaneDescriptor planeDescriptor, IEnumerable<string> keywords) : this(category, name, hasMultiByteOrderings, [ planeDescriptor ], keywords)
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="planeDescriptors">Plane descriptors.</param>
	public ImageFormat(ImageFormatCategory category, string name, IList<ImagePlaneDescriptor> planeDescriptors) : this(category, name, false, planeDescriptors, Array.Empty<string>())
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="planeDescriptors">Plane descriptors.</param>
	/// <param name="keywords">Keywords.</param>
	public ImageFormat(ImageFormatCategory category, string name, IList<ImagePlaneDescriptor> planeDescriptors, IEnumerable<string> keywords) : this(category, name, false, planeDescriptors, keywords)
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="hasMultiByteOrderings">Whether multiple byte orderings are supported by this format or not.</param>
	/// <param name="planeDescriptors">Plane descriptors.</param>
	public ImageFormat(ImageFormatCategory category, string name, bool hasMultiByteOrderings, IList<ImagePlaneDescriptor> planeDescriptors) : this(category, name, hasMultiByteOrderings, planeDescriptors, Array.Empty<string>())
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="hasMultiByteOrderings">Whether multiple byte orderings are supported by this format or not.</param>
	/// <param name="planeDescriptors">Plane descriptors.</param>
	/// <param name="keywords">Keywords.</param>
	public ImageFormat(ImageFormatCategory category, string name, bool hasMultiByteOrderings, IList<ImagePlaneDescriptor> planeDescriptors, IEnumerable<string> keywords)
	{
		// check parameters
		if (planeDescriptors.IsEmpty())
			throw new ArgumentException("Empty image plane descriptor.");

		// register format, the name must be unique so editing a user-defined format needs to unregister the previous one carrying the same identifier first
		formatsByName.Add(name, this);
		foreach (var keyword in keywords)
			formatsByKeyword.Add(keyword, this);

		// setup properties
		this.Category = category;
		this.Name = name;
		this.HasMultipleByteOrderings = hasMultiByteOrderings;
		this.PlaneDescriptors = planeDescriptors.Let((it) =>
		{
			if (it.IsReadOnly)
				return it;
			return new ReadOnlyCollection<ImagePlaneDescriptor>(planeDescriptors);
		});
	}


	/// <summary>
	/// Get category of format.
	/// </summary>
	public ImageFormatCategory Category { get; }


	/// <summary>
	/// Get name of format for displaying to user.
	/// </summary>
	/// <remarks>The name given by user is returned for a user-defined format, whose <see cref="Name"/> is an identifier which is meaningless to user. The name defined in string resource is returned for a built-in format, or its <see cref="Name"/> if no string resource is defined for it.</remarks>
	public string DisplayName
	{
		get
		{
			if (this.Category == ImageFormatCategory.UserDefined)
				return this.displayName ?? this.Name;
			return Application.CurrentOrNull?.GetStringNonNull($"ImageFormat.{this.Name}", this.Name) ?? this.Name;
		}
	}


	/// <summary>
	/// Whether multiple byte orderings are supported by this format or not.
	/// </summary>
	public bool HasMultipleByteOrderings { get; }


	/// <summary>
	/// Get descriptors of each plane.
	/// </summary>
	public IList<ImagePlaneDescriptor> PlaneDescriptors { get; }


	/// <summary>
	/// NAme of format.
	/// </summary>
	public string Name { get; }


	/// <summary>
	/// Get number of planes of this format.
	/// </summary>
	public int PlaneCount => this.PlaneDescriptors.Count;


	/// <summary>
	/// Try getting <see cref="ImageFormat"/> by given file name.
	/// </summary>
	/// <param name="fileName">File name.</param>
	/// <param name="format">Format found by file name.</param>
	/// <returns>True if format found.</returns>
	public static bool TryGetByFileName(string fileName, out ImageFormat? format)
	{
		fileName = Path.GetFileNameWithoutExtension(fileName);
		foreach (var (keyword, candidate) in formatsByKeyword)
		{
			if (fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
			{
				format = candidate;
				return true;
			}
		}
		format = null;
		return false;
	}


	/// <summary>
	/// Try getting <see cref="ImageFormat"/> by given name.
	/// </summary>
	/// <param name="name">Name of format.</param>
	/// <param name="format">Format found by name.</param>
	/// <returns>True if format found.</returns>
	public static bool TryGetByName(string name, out ImageFormat? format) =>
		formatsByName.TryGetValue(name, out format);


	/// <summary>
	/// Unregister the given user-defined format so that it can no longer be found by its name.
	/// </summary>
	/// <param name="format">User-defined format to unregister.</param>
	/// <returns>True if the format has been unregistered.</returns>
	/// <remarks>A built-in format cannot be unregistered. The registration is only dropped if it still refers to <paramref name="format"/>, so unregistering a format twice is a no-op. A user-defined format registers no keyword, so there is no keyword to drop.</remarks>
	public static bool Unregister(ImageFormat format)
	{
		// reject built-in format
		if (format.Category != ImageFormatCategory.UserDefined)
			return false;

		// unregister from name
		if (!formatsByName.TryGetValue(format.Name, out var registeredFormat) || !ReferenceEquals(registeredFormat, format))
			return false;
		formatsByName.Remove(format.Name);
		return true;
	}


	// Implementations.
	public bool Equals(ImageFormat? other)
	{
		if (other is null)
			return false;
		if (ReferenceEquals(other, this))
			return true;
		return this.Name == other.Name
			&& this.Category == other.Category
			&& this.PlaneDescriptors.Equals(other.PlaneDescriptors);
	}
	public override bool Equals(object? obj)
	{
		if (obj is ImageFormat imageFormat)
			return this.Equals(imageFormat);
		return false;
	}
	public override int GetHashCode() => this.Name.GetHashCode();
	public override string ToString() => this.Name;
}


/// <summary>
/// Category of <see cref="ImageFormat"/>.
/// </summary>
enum ImageFormatCategory
{
	/// <summary>
	/// Unclassified.
	/// </summary>
	Unclassified,
	/// <summary>
	/// Compressed.
	/// </summary>
	Compressed,
	/// <summary>
	/// Luminance.
	/// </summary>
	Luminance,
	/// <summary>
	/// RGB.
	/// </summary>
	RGB,
	/// <summary>
	/// ARGB.
	/// </summary>
	ARGB,
	/// <summary>
	/// YUV.
	/// </summary>
	YUV,
	/// <summary>
	/// Bayer.
	/// </summary>
	Bayer,
	/// <summary>
	/// Defined by user.
	/// </summary>
	UserDefined,
}
