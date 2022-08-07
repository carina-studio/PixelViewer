using System;

namespace Carina.PixelViewer.Media
{
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
		public ImagePlaneDescriptor(int pixelStride, int minEffectiveBits, int maxEffectiveBits) : this(pixelStride, minEffectiveBits, maxEffectiveBits, false)
		{ }


		/// <summary>
		/// Initialize new <see cref="ImagePlaneDescriptor"/> instance.
		/// </summary>
		/// <param name="pixelStride">Pixel stride.</param>
		/// <param name="minEffectiveBits">Minimum effective bits for each pixel.</param>
		/// <param name="maxEffectiveBits">Maximum effective bits for each pixel.</param>
		/// <param name="adjustableBlackWhiteLevels">Whether black/white levels are adjustable or not.</param>
		public ImagePlaneDescriptor(int pixelStride, int minEffectiveBits, int maxEffectiveBits, bool adjustableBlackWhiteLevels)
		{
			if (pixelStride < 0)
				throw new ArgumentOutOfRangeException(nameof(pixelStride));
			if (minEffectiveBits < 0 || (pixelStride > 0 && minEffectiveBits > pixelStride << 3))
				throw new ArgumentOutOfRangeException(nameof(minEffectiveBits));
			if (maxEffectiveBits < minEffectiveBits || (pixelStride > 0 && maxEffectiveBits > pixelStride << 3))
				throw new ArgumentOutOfRangeException(nameof(maxEffectiveBits));
			this.AreAdjustableBlackWhiteLevels = adjustableBlackWhiteLevels;
			this.PixelStride = pixelStride;
			this.MinEffectiveBits = minEffectiveBits;
			this.MaxEffectiveBits = maxEffectiveBits;
		}


		/// <summary>
		/// Check whether black/white levels are adjustable or not.
		/// </summary>
		public bool AreAdjustableBlackWhiteLevels { get; }


		/// <summary>
		/// Check whether effective bits is adjustable or not.
		/// </summary>
		public bool IsAdjustableEffectiveBits { get => this.MinEffectiveBits < this.MaxEffectiveBits; }


		/// <summary>
		/// Check whether pixel stride is adjustable or not.
		/// </summary>
		public bool IsAdjustablePixelStride { get => this.PixelStride > 0; }


		/// <summary>
		/// Check whether data of pixels are packed into bits instead of bytes.
		/// </summary>
		public bool IsPackedBits { get => this.PixelStride == 0; }


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


		// Implementations
		public bool Equals(ImagePlaneDescriptor? other) => other != null
			&& this.MinEffectiveBits == other.MinEffectiveBits
			&& this.MaxEffectiveBits == other.MaxEffectiveBits
			&& this.PixelStride == other.PixelStride;
		public override bool Equals(object? obj)
		{
			if (obj is ImagePlaneDescriptor descriptor)
				return this.Equals(descriptor);
			return false;
		}
		public override int GetHashCode() => this.PixelStride << 16 | this.MaxEffectiveBits << 8 | this.MinEffectiveBits;
	}
}
