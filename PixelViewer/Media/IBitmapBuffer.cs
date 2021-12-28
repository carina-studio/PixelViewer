using Avalonia;
using Avalonia.Media.Imaging;
using Carina.PixelViewer.Runtime.InteropServices;
using CarinaStudio;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.Buffers;
using System.Diagnostics;
#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Data buffer of <see cref="IBitmap"/>.
	/// </summary>
	unsafe interface IBitmapBuffer : IShareableDisposable<IBitmapBuffer>, IMemoryOwner<byte>
	{
		/// <summary>
		/// Color space of bitmap.
		/// </summary>
		BitmapColorSpace ColorSpace { get; }


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
		// Function to convert color space.
		unsafe delegate void ColorSpaceConversion(double* r, double* g, double* b);


		// Fields.
		static readonly ILogger? Logger = App.CurrentOrNull?.LoggerFactory?.CreateLogger(nameof(BitmapBufferExtensions));


		/// <summary>
		/// Convert format of <paramref name="bitmapBuffer"/> to <see cref="BitmapFormat.Bgra32"/>.
		/// </summary>
		/// <param name="bitmapBuffer">Source <see cref="IBitmapBuffer"/>.</param>
		/// <param name="resultBitmapBuffer"><see cref="IBitmapBuffer"/> to receive converted data.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of conversion.</returns>
		public static async Task ConvertToBgra32Async(this IBitmapBuffer bitmapBuffer, IBitmapBuffer resultBitmapBuffer, CancellationToken cancellationToken)
        {
			// check parameters
			if (resultBitmapBuffer == bitmapBuffer)
				throw new ArgumentException("Cannot convert color space in same bitmap buffer.");
			if (bitmapBuffer.ColorSpace != resultBitmapBuffer.ColorSpace)
				throw new ArgumentException("Cannot convert to bitmap with different color spaces.");
			if (resultBitmapBuffer.Format != BitmapFormat.Bgra32)
				throw new ArgumentException("Format of result bitmap buffer is not Bgra32.");
			if (bitmapBuffer.Width != resultBitmapBuffer.Width || bitmapBuffer.Height != resultBitmapBuffer.Height)
				throw new ArgumentException("Cannot convert to bitmap with different dimensions.");

			// convert
			using var sharedBitmapBuffer = bitmapBuffer.Share();
			using var sharedResultBitmapBuffer = resultBitmapBuffer.Share();
			await Task.Run(() =>
			{
				unsafe
				{
					// copy directly
					if (sharedBitmapBuffer.Format == BitmapFormat.Bgra32)
					{
						sharedBitmapBuffer.CopyTo(sharedResultBitmapBuffer);
						return;
					}

					// convert to BGRA32
					var width = sharedBitmapBuffer.Width;
					var srcRowStride = sharedBitmapBuffer.RowBytes;
					var destRowStride = sharedResultBitmapBuffer.RowBytes;
					var stopWatch = App.CurrentOrNull?.IsDebugMode == true
						? new Stopwatch().Also(it => it.Start())
						: null;
					sharedBitmapBuffer.Memory.Pin(srcBaseAddr =>
					{
						sharedResultBitmapBuffer.Memory.Pin(destBaseAddr =>
						{
							switch (sharedBitmapBuffer.Format)
							{
								case BitmapFormat.Bgra64:
									{
										var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
										var packFunc = ImageProcessing.SelectBgra32Packing();
										Parallel.For(0, sharedBitmapBuffer.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var b = (ushort)0;
											var g = (ushort)0;
											var r = (ushort)0;
											var a = (ushort)0;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (uint*)((byte*)destBaseAddr + (y * destRowStride));
											for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												*destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
											}
											if (cancellationToken.IsCancellationRequested)
												return;
										});
									}
									break;
							}
						});
					});
					if (cancellationToken.IsCancellationRequested)
						throw new TaskCanceledException();
					if (stopWatch != null)
					{
						stopWatch.Stop();
						Logger?.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to convert format of {width}x{sharedBitmapBuffer.Height} bitmap buffer from {sharedBitmapBuffer.Format} to Bgra32");
					}
				}
			});
		}


		/// <summary>
		/// Convert color space of <paramref name="bitmapBuffer"/> to the color space of <paramref name="resultBitmapBuffer"/>.
		/// </summary>
		/// <param name="bitmapBuffer">Source <see cref="IBitmapBuffer"/>.</param>
		/// <param name="resultBitmapBuffer"><see cref="IBitmapBuffer"/> to receive converted data.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of conversion.</returns>
		public static async Task ConvertToColorSpaceAsync(this IBitmapBuffer bitmapBuffer, IBitmapBuffer resultBitmapBuffer, CancellationToken cancellationToken)
		{
			// check parameters
			if (resultBitmapBuffer == bitmapBuffer)
				throw new ArgumentException("Cannot convert color space in same bitmap buffer.");
			if (bitmapBuffer.Format != resultBitmapBuffer.Format)
				throw new ArgumentException("Cannot convert to bitmap with different formats.");
			if (bitmapBuffer.Width != resultBitmapBuffer.Width || bitmapBuffer.Height != resultBitmapBuffer.Height)
				throw new ArgumentException("Cannot convert to bitmap with different dimensions.");

			// convert
			using var sharedBitmapBuffer = bitmapBuffer.Share();
			using var sharedResultBitmapBuffer = resultBitmapBuffer.Share();
			await Task.Run(() =>
			{
				unsafe
				{
					// select color space converter
					var srcColorSpace = sharedBitmapBuffer.ColorSpace;
					var targetColorSpace = resultBitmapBuffer.ColorSpace;
					var convertFunc = Global.Run(() =>
					{
						if (targetColorSpace == BitmapColorSpace.DCI_P3)
							return (ColorSpaceConversion)srcColorSpace.ConvertToDciP3ColorSpace;
						else if (targetColorSpace == BitmapColorSpace.Srgb)
							return srcColorSpace.ConvertToSrgbColorSpace;
						else
							throw new NotSupportedException($"Unsupported target color space: {resultBitmapBuffer.ColorSpace}");
					});

					// copy directly
					if (srcColorSpace == resultBitmapBuffer.ColorSpace)
					{
						sharedBitmapBuffer.CopyTo(sharedResultBitmapBuffer);
						return;
					}

					// convert to target color space
					var width = sharedBitmapBuffer.Width;
					var srcRowStride = sharedBitmapBuffer.RowBytes;
					var destRowStride = sharedResultBitmapBuffer.RowBytes;
					var stopWatch = App.CurrentOrNull?.IsDebugMode == true
						? new Stopwatch().Also(it => it.Start())
						: null;
					sharedBitmapBuffer.Memory.Pin(srcBaseAddr =>
					{
						sharedResultBitmapBuffer.Memory.Pin(destBaseAddr =>
						{
							switch (sharedBitmapBuffer.Format)
							{
								case BitmapFormat.Bgra32:
									{
										var unpackFunc = ImageProcessing.SelectBgrx32UnpackingAndNormalizing();
										var packFunc = ImageProcessing.SelectBgrx32DenormalizingAndPacking();
										Parallel.For(0, sharedBitmapBuffer.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var b = 0.0;
											var g = 0.0;
											var r = 0.0;
											var a = (byte)0;
											var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (uint*)((byte*)destBaseAddr + (y * destRowStride));
											for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												convertFunc(&r, &g, &b);
												*destPixelPtr = packFunc(b, g, r, a);
											}
											if (cancellationToken.IsCancellationRequested)
												return;
										});
									}
									break;
								case BitmapFormat.Bgra64:
									{
										var unpackFunc = ImageProcessing.SelectBgrx64UnpackingAndNormalizing();
										var packFunc = ImageProcessing.SelectBgrx64DenormalizingAndPacking();
										Parallel.For(0, sharedBitmapBuffer.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var b = 0.0;
											var g = 0.0;
											var r = 0.0;
											var a = (ushort)0;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (ulong*)((byte*)destBaseAddr + (y * destRowStride));
											for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												convertFunc(&r, &g, &b);
												*destPixelPtr = packFunc(b, g, r, a);
											}
											if (cancellationToken.IsCancellationRequested)
												return;
										});
									}
									break;
							}
						});
					});
					if (cancellationToken.IsCancellationRequested)
						throw new TaskCanceledException();
					if (stopWatch != null)
					{
						stopWatch.Stop();
						Logger?.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to convert color space of {width}x{sharedBitmapBuffer.Height} bitmap buffer from {srcColorSpace} to sRGB");
					}
				}
			});
		}


		/// <summary>
		/// Copy data as new bitmap buffer.
		/// </summary>
		/// <param name="source">Source <see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="IBitmapBuffer"/> with copied data.</returns>
		public static IBitmapBuffer Copy(this IBitmapBuffer source) => new BitmapBuffer(source.Format, source.ColorSpace, source.Width, source.Height).Also(it =>
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
				throw new ArgumentException("Cannot copy to bitmap with different formats.");
			if (source.ColorSpace != dest.ColorSpace)
				throw new ArgumentException("Cannot copy to bitmap with different color spaces.");
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
		/// <param name="orientation">Orientation.</param>
		/// <returns><see cref="IBitmap"/>.</returns>
		public static IBitmap CreateAvaloniaBitmap(this IBitmapBuffer buffer, int orientation = 0)
		{
			return buffer.Memory.Pin((srcBaseAddr) =>
			{
				unsafe
				{
					var srcWidth = buffer.Width;
					var srcHeight = buffer.Height;
					var avaloniaBitmap = orientation switch
					{
						0 or 180 => new WriteableBitmap(new PixelSize(srcWidth, srcHeight), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul),
						90 or 270 => new WriteableBitmap(new PixelSize(srcHeight, srcWidth), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul),
						_ => throw new ArgumentException(),
					};
					using var avaloniaBitmapBuffer = avaloniaBitmap.Lock();
					var srcRowStride = buffer.RowBytes;
					var destRowStride = avaloniaBitmapBuffer.RowBytes;
					var stopWatch = App.CurrentOrNull?.IsDebugMode == true
						? new Stopwatch().Also(it => it.Start())
						: null;
					switch (buffer.Format)
					{
						case BitmapFormat.Bgra32:
							{
								switch (orientation)
								{
									case 0:
										{
											var minRowStride = Math.Min(srcRowStride, destRowStride);
											Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
											{
												var srcRowPtr = ((byte*)srcBaseAddr + (y * srcRowStride));
												var destRowPtr = ((byte*)avaloniaBitmapBuffer.Address + (y * destRowStride));
												Marshal.Copy(srcRowPtr, destRowPtr, minRowStride);
											});
										}
										break;
									case 90:
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = ((byte*)avaloniaBitmapBuffer.Address + ((srcHeight - y - 1) * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
												*(uint*)destPixelPtr = *srcPixelPtr;
										});
										break;
									case 180:
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (uint*)((byte*)avaloniaBitmapBuffer.Address + ((srcHeight - y - 1) * destRowStride) + ((srcWidth - 1) * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
												*destPixelPtr = *srcPixelPtr;
										});
										break;
									case 270:
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = ((byte*)avaloniaBitmapBuffer.Address + ((srcWidth - 1) * destRowStride) + (y * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
												*(uint*)destPixelPtr = *srcPixelPtr;
										});
										break;
								}
							}
							break;
						case BitmapFormat.Bgra64:
							{
								var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
								var packFunc = ImageProcessing.SelectBgra32Packing();
								switch (orientation)
								{
									case 0:
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var b = (ushort)0;
											var g = (ushort)0;
											var r = (ushort)0;
											var a = (ushort)0;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (uint*)((byte*)avaloniaBitmapBuffer.Address + (y * destRowStride));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												*destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
											}
										});
										break;
									case 90:
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var b = (ushort)0;
											var g = (ushort)0;
											var r = (ushort)0;
											var a = (ushort)0;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = ((byte*)avaloniaBitmapBuffer.Address + ((srcHeight - y - 1) * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												*(uint*)destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
											}
										});
										break;
									case 180:
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var b = (ushort)0;
											var g = (ushort)0;
											var r = (ushort)0;
											var a = (ushort)0;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (uint*)((byte*)avaloniaBitmapBuffer.Address + ((srcHeight - y - 1) * destRowStride) + ((srcWidth - 1) * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												*destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
											}
										});
										break;
									case 270:
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var b = (ushort)0;
											var g = (ushort)0;
											var r = (ushort)0;
											var a = (ushort)0;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = ((byte*)avaloniaBitmapBuffer.Address + ((srcWidth - 1) * destRowStride) + (y * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												*(uint*)destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
											}
										});
										break;
								}
							}
							break;
						default:
							throw new NotSupportedException($"Unsupported bitmap format: {buffer.Format}");
					}
					if (stopWatch != null)
					{
						stopWatch.Stop();
						Logger?.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to convert from {srcWidth}x{buffer.Height} {buffer.Format} bitmap buffer to Avalonia bitmap");
					}
					return avaloniaBitmap;
				}
			});
		}


		/// <summary>
		/// Create <see cref="IBitmap"/> which copied data from this <see cref="IBitmapBuffer"/> asynchronously.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="orientation">Orientation.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of creating <see cref="IBitmap"/>.</returns>
		public static async Task<IBitmap> CreateAvaloniaBitmapAsync(this IBitmapBuffer buffer, int orientation = 0, CancellationToken cancellationToken = default)
		{
			using var sharedBuffer = buffer.Share();
			var bitmap = await Task.Run(() => CreateAvaloniaBitmap(sharedBuffer));
			if (cancellationToken.IsCancellationRequested)
				throw new TaskCanceledException();
			return bitmap;
		}


#if WINDOWS
		/// <summary>
		/// Create <see cref="System.Drawing.Bitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="orientation">Orientation.</param>
		/// <returns><see cref="System.Drawing.Bitmap"/>.</returns>
		public static unsafe System.Drawing.Bitmap CreateSystemDrawingBitmap(this IBitmapBuffer buffer, int orientation = 0)
		{
			return buffer.Memory.Pin((srcBaseAddr) =>
			{
				var srcWidth = buffer.Width;
				var srcHeight = buffer.Height;
				var srcRowStride = buffer.RowBytes;
				switch (buffer.Format)
				{
					case BitmapFormat.Bgra32:
						switch (orientation)
						{
							case 0:
								return new System.Drawing.Bitmap(srcWidth, srcHeight, srcRowStride, PixelFormat.Format32bppArgb, srcBaseAddr);
							case 90:
								return new System.Drawing.Bitmap(srcHeight, srcWidth, PixelFormat.Format32bppArgb).Also(bitmap =>
								{
									var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
									try
									{
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var destRowStride = bitmapData.Stride;
											var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = ((byte*)bitmapData.Scan0 + ((srcHeight - y - 1) * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
												*(uint*)destPixelPtr = *srcPixelPtr;
										});
									}
									finally
									{
										bitmap.UnlockBits(bitmapData);
									}
								});
							case 180:
								return new System.Drawing.Bitmap(srcWidth, srcHeight, PixelFormat.Format32bppArgb).Also(bitmap =>
								{
									var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
									try
									{
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (uint*)((byte*)bitmapData.Scan0 + ((srcHeight - y - 1) * bitmapData.Stride) + ((srcWidth - 1) * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
												*destPixelPtr = *srcPixelPtr;
										});
									}
									finally
									{
										bitmap.UnlockBits(bitmapData);
									}
								});
							case 270:
								return new System.Drawing.Bitmap(srcHeight, srcWidth, PixelFormat.Format32bppArgb).Also(bitmap =>
								{
									var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
									try
									{
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var destRowStride = bitmapData.Stride;
											var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = ((byte*)bitmapData.Scan0 + ((srcWidth - 1) * destRowStride) + (y * sizeof(uint)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
												*(uint*)destPixelPtr = *srcPixelPtr;
										});
									}
									finally
									{
										bitmap.UnlockBits(bitmapData);
									}
								});
							default:
								throw new ArgumentException();
						}
					case BitmapFormat.Bgra64:
						switch (orientation)
						{
							case 0:
								return new System.Drawing.Bitmap(srcWidth, srcHeight, srcRowStride, PixelFormat.Format64bppArgb, srcBaseAddr);
							case 90:
								return new System.Drawing.Bitmap(srcHeight, srcWidth, PixelFormat.Format64bppArgb).Also(bitmap =>
								{
									var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
									try
									{
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var destRowStride = bitmapData.Stride;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = ((byte*)bitmapData.Scan0 + ((srcHeight - y - 1) * sizeof(ulong)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
												*(ulong*)destPixelPtr = *srcPixelPtr;
										});
									}
									finally
									{
										bitmap.UnlockBits(bitmapData);
									}
								});
							case 180:
								return new System.Drawing.Bitmap(srcWidth, srcHeight, PixelFormat.Format64bppArgb).Also(bitmap =>
								{
									var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
									try
									{
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (ulong*)((byte*)bitmapData.Scan0 + ((srcHeight - y - 1) * bitmapData.Stride) + ((srcWidth - 1) * sizeof(ulong)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
												*destPixelPtr = *srcPixelPtr;
										});
									}
									finally
									{
										bitmap.UnlockBits(bitmapData);
									}
								});
							case 270:
								return new System.Drawing.Bitmap(srcHeight, srcWidth, PixelFormat.Format64bppArgb).Also(bitmap =>
								{
									var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
									try
									{
										Parallel.For(0, srcHeight, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
										{
											var destRowStride = bitmapData.Stride;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = ((byte*)bitmapData.Scan0 + ((srcWidth - 1) * destRowStride) + (y * sizeof(ulong)));
											for (var x = srcWidth; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
												*(ulong*)destPixelPtr = *srcPixelPtr;
										});
									}
									finally
									{
										bitmap.UnlockBits(bitmapData);
									}
								});
							default:
								throw new ArgumentException();
						}
					default:
						throw new NotSupportedException();
				}
			});
		}
#endif


		/// <summary>
		/// Create <see cref="IBitmap"/> with quarter size which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="IBitmap"/>.</returns>
		public static IBitmap CreateQuarterSizeAvaloniaBitmap(this IBitmapBuffer bitmapBuffer)
        {
			// check size
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			if (width <= 2 || height <= 2)
				return bitmapBuffer.CreateAvaloniaBitmap();

			// create bitmap
			width >>= 1;
			height >>= 1;
			return new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul).Also(bitmap =>
			{
				using var bitmapFrame = bitmap.Lock();
				var srcRowStride = bitmapBuffer.RowBytes;
				var destRowStride = bitmapFrame.RowBytes;
				var stopWatch = App.CurrentOrNull?.IsDebugMode == true
					? new Stopwatch().Also(it => it.Start())
					: null;
				bitmapBuffer.Memory.Pin(srcBaseAddr =>
				{
					var destBaseAddr = bitmapFrame.Address;
					switch (bitmapBuffer.Format)
					{
						case BitmapFormat.Bgra32:
							unsafe
							{
								var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
								var packFunc = ImageProcessing.SelectBgra32Packing();
								Parallel.For(0, height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
								{
									var r1 = (byte)0;
									var r2 = (byte)0;
									var g1 = (byte)0;
									var g2 = (byte)0;
									var b1 = (byte)0;
									var b2 = (byte)0;
									var a1 = (byte)0;
									var a2 = (byte)0;
									var selection = y;
									var srcRowPtr = (uint*)((byte*)srcBaseAddr + (y * 2 * srcRowStride));
									var srcNextRowPtr = (uint*)((byte*)srcRowPtr + srcRowStride);
									var destPixelPtr = (uint*)((byte*)destBaseAddr + (y * destRowStride));
									for (var x = width; x > 0; --x, srcRowPtr += 2, srcNextRowPtr += 2, ++destPixelPtr, ++selection)
									{
										if ((selection & 0x1) == 0)
										{
											unpackFunc(srcRowPtr[0], &b1, &g1, &r1, &a1);
											unpackFunc(srcNextRowPtr[1], &b2, &g2, &r2, &a2);
										}
										else
										{
											unpackFunc(srcRowPtr[1], &b1, &g1, &r1, &a1);
											unpackFunc(srcNextRowPtr[0], &b2, &g2, &r2, &a2);
										}
										*destPixelPtr = packFunc((byte)((b1 + b2) >> 1), (byte)((g1 + g2) >> 1), (byte)((r1 + r2) >> 1), (byte)((a1 + a2) >> 1));
									}
								});
							}
							break;
						case BitmapFormat.Bgra64:
							unsafe
							{
								var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
								var packFunc = ImageProcessing.SelectBgra32Packing();
								Parallel.For(0, height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
								{
									var r1 = (ushort)0;
									var r2 = (ushort)0;
									var g1 = (ushort)0;
									var g2 = (ushort)0;
									var b1 = (ushort)0;
									var b2 = (ushort)0;
									var a1 = (ushort)0;
									var a2 = (ushort)0;
									var selection = y;
									var srcRowPtr = (ulong*)((byte*)srcBaseAddr + (y * 2 * srcRowStride));
									var srcNextRowPtr = (ulong*)((byte*)srcRowPtr + srcRowStride);
									var destPixelPtr = (uint*)((byte*)destBaseAddr + (y * destRowStride));
									for (var x = width; x > 0; --x, srcRowPtr += 2, srcNextRowPtr += 2, ++destPixelPtr, ++selection)
									{
										if ((selection & 0x1) == 0)
										{
											unpackFunc(srcRowPtr[0], &b1, &g1, &r1, &a1);
											unpackFunc(srcNextRowPtr[1], &b2, &g2, &r2, &a2);
										}
										else
										{
											unpackFunc(srcRowPtr[1], &b1, &g1, &r1, &a1);
											unpackFunc(srcNextRowPtr[0], &b2, &g2, &r2, &a2);
										}
										var b = (byte)(((b1 + b2) >> 9) & 0xff);
										var g = (byte)(((g1 + g2) >> 9) & 0xff);
										var r = (byte)(((r1 + r2) >> 9) & 0xff);
										var a = (byte)(((a1 + a2) >> 9) & 0xff);
										*destPixelPtr = packFunc(b, g, r, a);
									}
								});
							}
							break;
						default:
							throw new NotSupportedException($"Unsupported bitmap format: {bitmapBuffer.Format}");
					}
				});
				if (stopWatch != null)
				{
					stopWatch.Stop();
					Logger?.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to convert from {bitmapBuffer.Width}x{bitmapBuffer.Height} {bitmapBuffer.Format} bitmap buffer to quarter size Avalonia bitmap");
				}
			});
        }


		/// <summary>
		/// Create <see cref="IBitmap"/> with quarter size which copied data from this <see cref="IBitmapBuffer"/> asynchronously.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of creating <see cref="IBitmap"/>.</returns>
		public static async Task<IBitmap> CreateQuarterSizeAvaloniaBitmapAsync(this IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken)
		{
			using var sharedBitmapBuffer = bitmapBuffer.Share();
			var bitmap = await Task.Run(() => CreateQuarterSizeAvaloniaBitmap(sharedBitmapBuffer));
			if (cancellationToken.IsCancellationRequested)
				throw new TaskCanceledException();
			return bitmap;
		}


		/// <summary>
		/// Create <see cref="SKBitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="SKBitmap"/>.</returns>
		public static SKBitmap CreateSkiaBitmap(this IBitmapBuffer bitmapBuffer)
		{
			var skiaColorType = bitmapBuffer.Format switch
			{
				BitmapFormat.Bgra32
				or BitmapFormat.Bgra64 => SKColorType.Bgra8888,
				_ => throw new NotSupportedException($"Unsupported bitmap format: {bitmapBuffer.Format}"),
			};
			var skiaImageInfo = new SKImageInfo(bitmapBuffer.Width, bitmapBuffer.Height, skiaColorType, SKAlphaType.Unpremul);
			return new SKBitmap(skiaImageInfo).Also(skiaBitmap =>
			{
				unsafe
				{
					using var skiaPixels = skiaBitmap.PeekPixels().AsNonNull();
					var srcRowStride = bitmapBuffer.RowBytes;
					var destRowStride = skiaPixels.RowBytes;
					var stopWatch = App.CurrentOrNull?.IsDebugMode == true
						? new Stopwatch().Also(it => it.Start())
						: null;
					bitmapBuffer.Memory.Pin(srcBaseAddr =>
					{
						var destBaseAddr = (byte*)skiaPixels.GetPixels();
						switch (bitmapBuffer.Format)
						{
							case BitmapFormat.Bgra32:
								{
									var minRowStride = Math.Min(srcRowStride, destRowStride);
									Parallel.For(0, bitmapBuffer.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
									{
										var srcRowPtr = (byte*)srcBaseAddr + (y * srcRowStride);
										var destRowPtr = destBaseAddr + (y * destRowStride);
										Marshal.Copy(srcRowPtr, destRowPtr, minRowStride);
									});
								}
								break;
							case BitmapFormat.Bgra64:
                                {
									var width = bitmapBuffer.Width;
									var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
									var packFunc = ImageProcessing.SelectBgra32Packing();
									Parallel.For(0, bitmapBuffer.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
									{
										var r = (ushort)0;
										var g = (ushort)0;
										var b = (ushort)0;
										var a = (ushort)0;
										var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
										var destPixelPtr = (uint*)(destBaseAddr + (y * destRowStride));
										for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
										{
											unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
											*destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
										}
									});
								}
								break;
						}
					});
					if (stopWatch != null)
					{
						stopWatch.Stop();
						Logger?.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to convert from {bitmapBuffer.Width}x{bitmapBuffer.Height} {bitmapBuffer.Format} bitmap buffer to Skia bitmap");
					}
				}
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
