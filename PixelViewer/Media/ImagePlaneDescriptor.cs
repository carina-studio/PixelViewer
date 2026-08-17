using System;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Descriptor for image plane for specific <see cref="ImageFormat"/>.
/// </summary>
class ImagePlaneDescriptor : IEquatable<ImagePlaneDescriptor>
{
	/// <summary>
	/// Initialize new <see cref="ImagePlaneDescriptor"/> instance.
	/// </summary>
	/// <param name="pixelStride">Pixel stride.</param>
	public ImagePlaneDescriptor(int pixelStride) : this(pixelStride, pixelStride << 3, pixelStride << 3)
	{ }


	/// <summary>
	/// Initialize new <see cref="ImagePlaneDescriptor"/> instance.
	/// </summary>
	/// <param name="pixelStride">Pixel stride.</param>
	/// <param name="minEffectiveBits">Minimum effective bits for each pixel.</param>
	/// <param name="maxEffectiveBits">Maximum effective bits for each pixel.</param>
	/// <param name="adjustableBlackWhiteLevels">Whether black/white levels are adjustable or not.</param>
	/// <param name="horizontalSubsampling">Number of pixels sharing one sample of the plane in horizontal direction.</param>
	/// <param name="verticalSubsampling">Number of pixels sharing one sample of the plane in vertical direction.</param>
	public ImagePlaneDescriptor(int pixelStride, int minEffectiveBits, int maxEffectiveBits, bool adjustableBlackWhiteLevels = false, int horizontalSubsampling = 1, int verticalSubsampling = 1)
	{
		if (pixelStride < 0)
			throw new ArgumentOutOfRangeException(nameof(pixelStride));
		if (minEffectiveBits < 0 || (pixelStride > 0 && minEffectiveBits > pixelStride << 3))
			throw new ArgumentOutOfRangeException(nameof(minEffectiveBits));
		if (maxEffectiveBits < minEffectiveBits || (pixelStride > 0 && maxEffectiveBits > pixelStride << 3))
			throw new ArgumentOutOfRangeException(nameof(maxEffectiveBits));
		if (horizontalSubsampling < 1)
			throw new ArgumentOutOfRangeException(nameof(horizontalSubsampling));
		if (verticalSubsampling < 1)
			throw new ArgumentOutOfRangeException(nameof(verticalSubsampling));
		this.AreAdjustableBlackWhiteLevels = adjustableBlackWhiteLevels;
		this.PixelStride = pixelStride;
		this.MinEffectiveBits = minEffectiveBits;
		this.MaxEffectiveBits = maxEffectiveBits;
		this.HorizontalSubsampling = horizontalSubsampling;
		this.VerticalSubsampling = verticalSubsampling;
	}


	/// <summary>
	/// Check whether black/white levels are adjustable or not.
	/// </summary>
	public bool AreAdjustableBlackWhiteLevels { get; }


	/// <summary>
	/// Number of pixels sharing one sample of the plane in horizontal direction.
	/// </summary>
	/// <remarks>The value is 1 if the plane is not subsampled horizontally, which means that each pixel has its own sample. For example, the value is 2 for the chroma plane of a YUV 4:2:0 format.</remarks>
	public int HorizontalSubsampling { get; }


	/// <summary>
	/// Check whether effective bits is adjustable or not.
	/// </summary>
	public bool IsAdjustableEffectiveBits => this.MinEffectiveBits < this.MaxEffectiveBits;


	/// <summary>
	/// Check whether pixel stride is adjustable or not.
	/// </summary>
	public bool IsAdjustablePixelStride => this.PixelStride > 0;


	/// <summary>
	/// Check whether data of pixels are packed into bits instead of bytes.
	/// </summary>
	public bool IsPackedBits => this.PixelStride == 0;


	/// <summary>
	/// Maximum effective bits for each pixel.
	/// </summary>
	public int MaxEffectiveBits { get; }


	/// <summary>
	/// Minimum effective bits for each pixel.
	/// </summary>
	public int MinEffectiveBits { get; }


	/// <summary>
	/// Pixel stride.
	/// </summary>
	public int PixelStride { get; }


	/// <summary>
	/// Number of pixels sharing one sample of the plane in vertical direction.
	/// </summary>
	/// <remarks>The value is 1 if the plane is not subsampled vertically, which means that each row of pixels has its own row of samples. For example, the value is 2 for the chroma plane of a YUV 4:2:0 format.</remarks>
	public int VerticalSubsampling { get; }


	// Implementations
	public bool Equals(ImagePlaneDescriptor? other) => other != null
		&& this.AreAdjustableBlackWhiteLevels == other.AreAdjustableBlackWhiteLevels
		&& this.MinEffectiveBits == other.MinEffectiveBits
		&& this.MaxEffectiveBits == other.MaxEffectiveBits
		&& this.PixelStride == other.PixelStride
		&& this.HorizontalSubsampling == other.HorizontalSubsampling
		&& this.VerticalSubsampling == other.VerticalSubsampling;
	public override bool Equals(object? obj)
	{
		if (obj is ImagePlaneDescriptor descriptor)
			return this.Equals(descriptor);
		return false;
	}
	public override int GetHashCode() => HashCode.Combine(this.AreAdjustableBlackWhiteLevels, this.HorizontalSubsampling, this.MaxEffectiveBits, this.MinEffectiveBits, this.PixelStride, this.VerticalSubsampling);
}