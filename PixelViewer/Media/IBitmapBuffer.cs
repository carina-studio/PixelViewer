using Avalonia;
using Avalonia.Media.Imaging;
using CarinaStudio;
using System.Buffers;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Data buffer of <see cref="IBitmap"/>.
	/// </summary>
	unsafe interface IBitmapBuffer : IShareableDisposable<IBitmapBuffer>, IMemoryOwner<byte>
	{
		/// <summary>
		/// Format of bitmap.
		/// </summary>
		BitmapFormat Format { get; }


		/// <summary>
		/// Height of bitmap in pixels.
		/// </summary>
		int Height { get; }


		/// <summary>
		/// Bytes per row.
		/// </summary>
		int RowBytes { get; }


		/// <summary>
		/// Width of bitmap in pixels.
		/// </summary>
		int Width { get; }
	}


	/// <summary>
	/// Extensions for <see cref="IBitmapBuffer"/>.
	/// </summary>
	static class BitmapBufferExtensions
	{
		/// <summary>
		/// Create <see cref="IBitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="IBitmap"/>.</returns>
		public static IBitmap CreateAvaloniaBitmap(this IBitmapBuffer buffer)
		{
			return buffer.Memory.UnsafeAccess((address) =>
			{
				return new Bitmap(buffer.Format.ToAvaloniaPixelFormat(), Avalonia.Platform.AlphaFormat.Unpremul, address, new PixelSize(buffer.Width, buffer.Height), new Vector(96, 96), buffer.RowBytes);
			});
		}


		/// <summary>
		/// Create <see cref="System.Drawing.Bitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="System.Drawing.Bitmap"/>.</returns>
		public static System.Drawing.Bitmap CreateSystemDrawingBitmap(this IBitmapBuffer buffer)
		{
			return buffer.Memory.UnsafeAccess((address) =>
			{
				return new System.Drawing.Bitmap(buffer.Width, buffer.Height, buffer.RowBytes, buffer.Format.ToSystemDrawingPixelFormat(), address);
			});
		}


		/// <summary>
		/// Get byte offset to pixel on given position.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="x">Horizontal position of pixel.</param>
		/// <param name="y">Vertical position of pixel.</param>
		/// <returns>Byte offset to pixel.</returns>
		public static int GetPixelOffset(this IBitmapBuffer buffer, int x, int y) => (y * buffer.RowBytes) + (x * buffer.Format.GetByteSize());
	}
}
