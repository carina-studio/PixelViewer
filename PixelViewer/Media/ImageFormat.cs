using CarinaStudio;
using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Image format.
	/// </summary>
	class ImageFormat : IEquatable<ImageFormat>
	{
		/// <summary>
		/// Maximum number of planes defined for each format.
		/// </summary>
		public const int MaxPlaneCount = 4;


		/// <summary>
		/// Initialize new <see cref="ImageFormat"/> instance.
		/// </summary>
		/// <param name="category">Category of format.</param>
		/// <param name="name">Name.</param>
		/// <param name="planeDescriptor">Plane descriptor.</param>
		public ImageFormat(ImageFormatCategory category, string name, ImagePlaneDescriptor planeDescriptor) : this(category, name, name, new ImagePlaneDescriptor[] { planeDescriptor })
		{ }


		/// <summary>
		/// Initialize new <see cref="ImageFormat"/> instance.
		/// </summary>
		/// <param name="category">Category of format.</param>
		/// <param name="name">Name.</param>
		/// <param name="displayName">Displayed name.</param>
		/// <param name="planeDescriptor">Plane descriptor.</param>
		public ImageFormat(ImageFormatCategory category, string name, string displayName, ImagePlaneDescriptor planeDescriptor) : this(category, name, displayName, new ImagePlaneDescriptor[] { planeDescriptor })
		{ }


		/// <summary>
		/// Initialize new <see cref="ImageFormat"/> instance.
		/// </summary>
		/// <param name="category">Category of format.</param>
		/// <param name="name">Name.</param>
		/// <param name="planeDescriptors">Plane descriptors.</param>
		public ImageFormat(ImageFormatCategory category, string name, IList<ImagePlaneDescriptor> planeDescriptors) : this(category, name, name, planeDescriptors)
		{ }


		/// <summary>
		/// Initialize new <see cref="ImageFormat"/> instance.
		/// </summary>
		/// <param name="category">Category of format.</param>
		/// <param name="name">Name.</param>
		/// <param name="displayName">Displayed name.</param>
		/// <param name="planeDescriptors">Plane descriptors.</param>
		public ImageFormat(ImageFormatCategory category, string name, string displayName, IList<ImagePlaneDescriptor> planeDescriptors)
		{
			if (planeDescriptors.IsEmpty())
				throw new ArgumentException("Empty image plane descriptor.");
			this.Category = category;
			this.Name = name;
			this.DisplayName = displayName;
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
		/// Display name of format.
		/// </summary>
		public string DisplayName { get; }


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
		public int PlaneCount { get => this.PlaneDescriptors.Count; }


		// Implementations.
		public bool Equals(ImageFormat? other)
		{
			if (other == null)
				return false;
			if (other == this)
				return true;
			return (this.Name == other.Name
				&& this.DisplayName == other.DisplayName
				&& this.Category == other.Category
				&& this.PlaneDescriptors.Equals(other.PlaneDescriptors));
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
}
