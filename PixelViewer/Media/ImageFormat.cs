﻿using System.IO;
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


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="planeDescriptor">Plane descriptor.</param>
	public ImageFormat(ImageFormatCategory category, string name, ImagePlaneDescriptor planeDescriptor) : this(category, name, new[] { planeDescriptor }, Array.Empty<string>())
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="planeDescriptor">Plane descriptor.</param>
	/// <param name="keywords">Keywords.</param>
	public ImageFormat(ImageFormatCategory category, string name, ImagePlaneDescriptor planeDescriptor, IEnumerable<string> keywords) : this(category, name, new[] { planeDescriptor }, keywords)
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="hasMultiByteOrderings">Whether multiple byte orderings are supported by this format or not.</param>
	/// <param name="planeDescriptor">Plane descriptor.</param>
	public ImageFormat(ImageFormatCategory category, string name, bool hasMultiByteOrderings, ImagePlaneDescriptor planeDescriptor) : this(category, name, hasMultiByteOrderings, new[] { planeDescriptor }, Array.Empty<string>())
	{ }


	/// <summary>
	/// Initialize new <see cref="ImageFormat"/> instance.
	/// </summary>
	/// <param name="category">Category of format.</param>
	/// <param name="name">Name.</param>
	/// <param name="hasMultiByteOrderings">Whether multiple byte orderings are supported by this format or not.</param>
	/// <param name="planeDescriptor">Plane descriptor.</param>
	/// <param name="keywords">Keywords.</param>
	public ImageFormat(ImageFormatCategory category, string name, bool hasMultiByteOrderings, ImagePlaneDescriptor planeDescriptor, IEnumerable<string> keywords) : this(category, name, hasMultiByteOrderings, new[] { planeDescriptor }, keywords)
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
		if (planeDescriptors.IsEmpty())
			throw new ArgumentException("Empty image plane descriptor.");
		formatsByName.Add(name, this);
		foreach (var keyword in keywords)
			formatsByKeyword.Add(keyword, this);
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
}
