using Avalonia;
using Avalonia.Media.Imaging;
using Carina.PixelViewer.Runtime.InteropServices;
using CarinaStudio;
using System;
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
		/// Copy data as new bitmap buffer.
		/// </summary>
		/// <param name="source">Source <see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="IBitmapBuffer"/> with copied data.</returns>
		public static IBitmapBuffer Copy(this IBitmapBuffer source) => new BitmapBuffer(source.Format, source.Width, source.Height).Also(it =>
		{
			source.CopyTo(it);
		});


		/// <summary>
		/// Copy data to given bitmap buffer.
		/// </summary>
		/// <param name="source">Source <see cref="IBitmapBuffer"/>.</param>
		/// <param name="dest">Destination <see cref="IBitmapBuffer"/>.</param>
		public static unsafe void CopyTo(this IBitmapBuffer source, IBitmapBuffer dest)
		{
			if (source == dest)
				return;
			if (source.Format != dest.Format)
				throw new ArgumentException("Cannot copy to bitmap with different format.");
			if (source.Width != dest.Width || source.Height != dest.Height)
				throw new ArgumentException("Cannot copy to bitmap with different dimensions.");
			source.Memory.Pin(sourceBaseAddr =>
			{
				dest.Memory.Pin(destBaseAddr =>
				{
					var sourceRowStride = source.RowBytes;
					var destRowStride = dest.RowBytes;
					if (sourceRowStride == destRowStride)
						Marshal.Copy((void*)sourceBaseAddr, (void*)destBaseAddr, sourceRowStride * source.Height);
					else
					{
						var sourceRowPtr = (byte*)sourceBaseAddr;
						var destRowPtr = (byte*)destBaseAddr;
						var minRowStride = Math.Min(sourceRowStride, destRowStride);
						for (var y = source.Height; y > 0; --y, sourceRowPtr += sourceRowStride, destRowPtr += destRowStride)
							Marshal.Copy(sourceRowPtr, destRowPtr, minRowStride);
					}
				});
			});
		}


		/// <summary>
		/// Create <see cref="IBitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="IBitmap"/>.</returns>
		public static IBitmap CreateAvaloniaBitmap(this IBitmapBuffer buffer)
		{
			return buffer.Memory.Pin((address) =>
			{
				return new Bitmap(buffer.Format.ToAvaloniaPixelFormat(), Avalonia.Platform.AlphaFormat.Unpremul, address, new PixelSize(buffer.Width, buffer.Height), new Vector(96, 96), buffer.RowBytes);
			});
		}


#if WINDOWS10_0_17763_0_OR_GREATER
		/// <summary>
		/// Create <see cref="System.Drawing.Bitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="System.Drawing.Bitmap"/>.</returns>
		public static System.Drawing.Bitmap CreateSystemDrawingBitmap(this IBitmapBuffer buffer)
		{
			return buffer.Memory.Pin((address) =>
			{
				return new System.Drawing.Bitmap(buffer.Width, buffer.Height, buffer.RowBytes, buffer.Format.ToSystemDrawingPixelFormat(), address);
			});
		}
#endif


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
