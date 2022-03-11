using System;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Pixel format of bitmap.
	/// </summary>
	enum BitmapFormat
	{
		/// <summary>
		/// 32-bit BGRA.
		/// </summary>
		Bgra32,
		/// <summary>
		/// 64-bit BGRA.
		/// </summary>
		Bgra64,
	}


	/// <summary>
	/// Extensions for <see cref="BitmapFormat"/>.
	/// </summary>
	static class BitmapFormatExtensions
	{
		/// <summary>
		/// Calculate size of bitmap row in bytes.
		/// </summary>
		/// <param name="format"><see cref="BitmapFormat"/>.</param>
		/// <param name="width">Width of bitmap in pixels.</param>
		/// <returns>Bytes per row.</returns>
		public static int CalculateRowBytes(this BitmapFormat format, int width) => format.GetByteSize() * width;


		/// <summary>
		/// Get size of pixel for given <see cref="BitmapFormat"/> in bytes.
		/// </summary>
		/// <param name="format"><see cref="BitmapFormat"/>.</param>
		/// <returns>Bytes per pixel.</returns>
		public static int GetByteSize(this BitmapFormat format) => format switch
		{
			BitmapFormat.Bgra32 => 4,
			BitmapFormat.Bgra64 => 8,
			_ => throw new ArgumentException($"Unknown format: {format}"),
		};


		/// <summary>
		/// Get number of colors supported by given <see cref="BitmapFormat"/>.
		/// </summary>
		/// <param name="format"><see cref="BitmapFormat"/>.</param>
		/// <returns>Number of colors.</returns>
		public static int GetColorCount(this BitmapFormat format) => format switch
		{
			BitmapFormat.Bgra32 => 256,
			BitmapFormat.Bgra64 => 65536,
			_ => throw new ArgumentException($"Unknown format: {format}"),
		};


		/// <summary>
		/// Convert <see cref="BitmapFormat"/> to <see cref="Avalonia.Platform.PixelFormat"/>.
		/// </summary>
		/// <param name="format"><see cref="BitmapFormat"/>.</param>
		/// <returns><see cref="Avalonia.Platform.PixelFormat"/>.</returns>
		public static Avalonia.Platform.PixelFormat ToAvaloniaPixelFormat(this BitmapFormat format) => format switch
		{
			BitmapFormat.Bgra32 => Avalonia.Platform.PixelFormat.Bgra8888,
			_ => throw new ArgumentException($"Unknown format: {format}"),
		};


		/// <summary>
		/// Convert <see cref="BitmapFormat"/> to <see cref="System.Drawing.Imaging.PixelFormat"/>.
		/// </summary>
		/// <param name="format"><see cref="BitmapFormat"/>.</param>
		/// <returns><see cref="System.Drawing.Imaging.PixelFormat"/>.</returns>
		public static System.Drawing.Imaging.PixelFormat ToSystemDrawingPixelFormat(this BitmapFormat format) => format switch
		{
			BitmapFormat.Bgra32 => System.Drawing.Imaging.PixelFormat.Format32bppArgb,
			BitmapFormat.Bgra64 => System.Drawing.Imaging.PixelFormat.Format64bppArgb,
			_ => throw new ArgumentException($"Unknown format: {format}"),
		};
	}
}
