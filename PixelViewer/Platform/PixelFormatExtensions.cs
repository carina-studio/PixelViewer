using Avalonia.Platform;
using System;

namespace Carina.PixelViewer.Platform
{
	/// <summary>
	/// Extensions for <see cref="PixelFormat"/>.
	/// </summary>
	static class PixelFormatExtensions
	{
		/// <summary>
		/// Get number of bytes for given format.
		/// </summary>
		/// <param name="format">Format.</param>
		/// <returns>Size in bytes.</returns>
		public static int GetByteSize(this PixelFormat format)
		{
			if (format == PixelFormats.Bgra8888
			    || format == PixelFormats.Rgba8888)
			{
				return 4;
			}
			if (format == PixelFormats.Rgb565)
				return 2;
			throw new ArgumentException();
		}
	}
}
