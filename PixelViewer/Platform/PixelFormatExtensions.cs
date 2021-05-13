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
		public static int GetByteSize(this PixelFormat format) => format switch
		{
			PixelFormat.Bgra8888 => 4,
			PixelFormat.Rgba8888 => 4,
			PixelFormat.Rgb565 => 2,
			_ => throw new ArgumentException(),
		};
	}
}
