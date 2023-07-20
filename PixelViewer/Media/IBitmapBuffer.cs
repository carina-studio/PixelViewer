using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Carina.PixelViewer.Runtime.InteropServices;
using CarinaStudio;
using CarinaStudio.AppSuite;
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
// ReSharper disable AccessToDisposedClosure

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Data buffer of <see cref="IBitmap"/>.
	/// </summary>
	interface IBitmapBuffer : IShareableDisposable<IBitmapBuffer>, IMemoryOwner<byte>
	{
		/// <summary>
		/// Color space of bitmap.
		/// </summary>
		ColorSpace ColorSpace { get; }


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
		// Fields.
		static readonly ILogger? Logger = App.CurrentOrNull?.LoggerFactory.CreateLogger(nameof(BitmapBufferExtensions));


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
					var stopWatch = IAppSuiteApplication.CurrentOrNull?.IsDebugMode == true
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
										ImageProcessing.ParallelFor(0, sharedBitmapBuffer.Height, (y) =>
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
						Logger?.LogTrace("Take {duration} ms to convert format of {width}x{height} bitmap buffer from {format} to Bgra32", stopWatch.ElapsedMilliseconds, width, sharedBitmapBuffer.Height, sharedBitmapBuffer.Format);
					}
				}
			}, cancellationToken);
		}


		/// <summary>
		/// Convert color space of <paramref name="bitmapBuffer"/> to the color space of <paramref name="resultBitmapBuffer"/>.
		/// </summary>
		/// <param name="bitmapBuffer">Source <see cref="IBitmapBuffer"/>.</param>
		/// <param name="resultBitmapBuffer"><see cref="IBitmapBuffer"/> to receive converted data.</param>
		/// <param name="useLinearSourceColorSpace">Whether color space of <paramref name="bitmapBuffer"/> should be treat as linear color space or not.</param>
		/// <param name="useLinearTargetColorSpace">Whether color space of <paramref name="resultBitmapBuffer"/> should be treat as linear color space or not.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of conversion.</returns>
		public static async Task ConvertToColorSpaceAsync(this IBitmapBuffer bitmapBuffer, IBitmapBuffer resultBitmapBuffer, bool useLinearSourceColorSpace, bool useLinearTargetColorSpace, CancellationToken cancellationToken)
		{
			// check parameters
			if (resultBitmapBuffer == bitmapBuffer)
				throw new ArgumentException("Cannot convert color space in same bitmap buffer.");
			if (bitmapBuffer.Format != resultBitmapBuffer.Format)
				throw new ArgumentException("Cannot convert to bitmap with different formats.");
			if (bitmapBuffer.Width != resultBitmapBuffer.Width || bitmapBuffer.Height != resultBitmapBuffer.Height)
				throw new ArgumentException("Cannot convert to bitmap with different dimensions.");
			
			// check color space
			if (bitmapBuffer.ColorSpace.Equals(resultBitmapBuffer.ColorSpace))
			{
				await Task.Run(() => CopyTo(bitmapBuffer, resultBitmapBuffer), cancellationToken);
				return;
			}

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
					var converter = new ColorSpace.Converter(srcColorSpace, useLinearSourceColorSpace, targetColorSpace, useLinearTargetColorSpace);

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
					var stopWatch = IAppSuiteApplication.CurrentOrNull?.IsDebugMode == true
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
										var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
										var packFunc = ImageProcessing.SelectBgra32Packing();
										ImageProcessing.ParallelFor(0, sharedBitmapBuffer.Height, (y) =>
										{
											var b = (byte)0;
											var g = (byte)0;
											var r = (byte)0;
											var a = (byte)0;
											var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (uint*)((byte*)destBaseAddr + (y * destRowStride));
											for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												(r, g, b) = converter.Convert(r, g, b);
												*destPixelPtr = packFunc(b, g, r, a);
											}
										});
									}
									break;
								case BitmapFormat.Bgra64:
									{
										var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
										var packFunc = ImageProcessing.SelectBgra64Packing();
										ImageProcessing.ParallelFor(0, sharedBitmapBuffer.Height, (y) =>
										{
											var b = (ushort)0;
											var g = (ushort)0;
											var r = (ushort)0;
											var a = (ushort)0;
											var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
											var destPixelPtr = (ulong*)((byte*)destBaseAddr + (y * destRowStride));
											for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
											{
												unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
												(r, g, b) = converter.Convert(r, g, b);
												*destPixelPtr = packFunc(b, g, r, a);
											}
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
						Logger?.LogTrace("Take {duration} ms to convert color space of {width}x{height} bitmap buffer from {srcColorSpace} to sRGB", stopWatch.ElapsedMilliseconds, width, sharedBitmapBuffer.Height, srcColorSpace);
					}
				}
			}, cancellationToken);
		}


		/// <summary>
		/// Copy data as new bitmap buffer.
		/// </summary>
		/// <param name="source">Source <see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="IBitmapBuffer"/> with copied data.</returns>
		public static IBitmapBuffer Copy(this IBitmapBuffer source) => new BitmapBuffer(source.Format, source.ColorSpace, source.Width, source.Height).Also(source.CopyTo);


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


		// Copy bitmap data with same format and size including rotated size.
		static unsafe void CopyTo(IntPtr srcBaseAddress, BitmapFormat format, int width, int height, int srcRowStride, IntPtr destBaseAddress, int destRowStride, int orientation, CancellationToken cancellationToken)
		{
			switch (format)
			{
				case BitmapFormat.Bgra32:
					switch (orientation)
					{
						case 0:
							var minRowStride = Math.Min(srcRowStride, destRowStride);
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var srcRowPtr = ((byte*)srcBaseAddress + (y * srcRowStride));
								var destRowPtr = ((byte*)destBaseAddress + (y * destRowStride));
								Marshal.Copy(srcRowPtr, destRowPtr, minRowStride);
							});
							break;
						case 90:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var srcPixelPtr = (uint*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((height - y - 1) * sizeof(uint)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
									*(uint*)destPixelPtr = *srcPixelPtr;
							});
							break;
						case 180:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var srcPixelPtr = (uint*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = (uint*)((byte*)destBaseAddress + ((height - y - 1) * destRowStride) + ((width - 1) * sizeof(uint)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
									*destPixelPtr = *srcPixelPtr;
							});
							break;
						case 270:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var srcPixelPtr = (uint*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((width - 1) * destRowStride) + (y * sizeof(uint)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
									*(uint*)destPixelPtr = *srcPixelPtr;
							});
							break;
						default:
							throw new ArgumentException();
					}
					break;

				case BitmapFormat.Bgra64:
					switch (orientation)
					{
						case 0:
							var minRowStride = Math.Min(srcRowStride, destRowStride);
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var srcRowPtr = ((byte*)srcBaseAddress + (y * srcRowStride));
								var destRowPtr = ((byte*)destBaseAddress + (y * destRowStride));
								Marshal.Copy(srcRowPtr, destRowPtr, minRowStride);
							});
							break;
						case 90:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((height - y - 1) * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
									*(ulong*)destPixelPtr = *srcPixelPtr;
							});
							break;
						case 180:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = (ulong*)((byte*)destBaseAddress + ((height - y - 1) * destRowStride) + ((width - 1) * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
									*destPixelPtr = *srcPixelPtr;
							});
							break;
						case 270:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((width - 1) * destRowStride) + (y * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
									*(ulong*)destPixelPtr = *srcPixelPtr;
							});
							break;
						default:
							throw new ArgumentException();
					}
					break;

				default:
					throw new NotSupportedException();
			}
		}


		/// <summary>
		/// Copy data to given <see cref="WriteableBitmap"/>.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="bitmap"><see cref="WriteableBitmap"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of copying data.</returns>
		public static async Task CopyToAvaloniaBitmapAsync(this IBitmapBuffer bitmapBuffer, WriteableBitmap bitmap, CancellationToken cancellationToken = default)
		{
			// check size
			if (bitmapBuffer.Width != bitmap.PixelSize.Width || bitmapBuffer.Height != bitmap.PixelSize.Height)
				throw new ArgumentException("Size of bitmaps are different.");

			// copy
			using var destBitmapBuffer = bitmap.Lock();
			using var srcBitmapBuffer = bitmapBuffer.Share();
			await Task.Run(() =>
			{
				srcBitmapBuffer.Memory.Pin(srcBaseAddress =>
				{
					var bitmapFormat = bitmap.Format.GetValueOrDefault();
					if (bitmapFormat == PixelFormats.Bgra8888)
						CopyToBgra32(srcBaseAddress, srcBitmapBuffer.Format, srcBitmapBuffer.Width, srcBitmapBuffer.Height, srcBitmapBuffer.RowBytes, destBitmapBuffer.Address, destBitmapBuffer.RowBytes, 0, cancellationToken); 
					else if (bitmapFormat == PixelFormats.Rgba64)
						CopyToRgba64(srcBaseAddress, srcBitmapBuffer.Format, srcBitmapBuffer.Width, srcBitmapBuffer.Height, srcBitmapBuffer.RowBytes, destBitmapBuffer.Address, destBitmapBuffer.RowBytes, 0, cancellationToken);
					else
						throw new NotSupportedException();
				});
			}, cancellationToken);
		}


		// Copy bitmap data to BGRA32 with same size including rotated size.
		static unsafe void CopyToBgra32(IntPtr srcBaseAddress, BitmapFormat srcFormat, int width, int height, int srcRowStride, IntPtr destBaseAddress, int destRowStride, int orientation, CancellationToken cancellationToken)
		{
			switch (srcFormat)
			{
				case BitmapFormat.Bgra32:
					CopyTo(srcBaseAddress, srcFormat, width, height, srcRowStride, destBaseAddress, destRowStride, orientation, cancellationToken);
					break;

				case BitmapFormat.Bgra64:
					var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
					var packFunc = ImageProcessing.SelectBgra32Packing();
					switch (orientation)
					{
						case 0:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (ushort)0;
								var g = (ushort)0;
								var r = (ushort)0;
								var a = (ushort)0;
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = (uint*)((byte*)destBaseAddress + (y * destRowStride));
								for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
								}
							});
							break;
						case 90:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (ushort)0;
								var g = (ushort)0;
								var r = (ushort)0;
								var a = (ushort)0;
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((height - y - 1) * sizeof(uint)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*(uint*)destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
								}
							});
							break;
						case 180:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (ushort)0;
								var g = (ushort)0;
								var r = (ushort)0;
								var a = (ushort)0;
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = (uint*)((byte*)destBaseAddress + ((height - y - 1) * destRowStride) + ((width - 1) * sizeof(uint)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
								}
							});
							break;
						case 270:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (ushort)0;
								var g = (ushort)0;
								var r = (ushort)0;
								var a = (ushort)0;
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((width - 1) * destRowStride) + (y * sizeof(uint)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*(uint*)destPixelPtr = packFunc((byte)(b >> 8), (byte)(g >> 8), (byte)(r >> 8), (byte)(a >> 8));
								}
							});
							break;
					}
					break;

				default:
					throw new NotSupportedException();
			}
		}


		/// <summary>
		/// Copy data to given quarter-size <see cref="WriteableBitmap"/>.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="bitmap"><see cref="WriteableBitmap"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of copying data.</returns>
		public static async Task CopyToQuarterSizeAvaloniaBitmapAsync(this IBitmapBuffer bitmapBuffer, WriteableBitmap bitmap, CancellationToken cancellationToken = default)
		{
			// check size
			if ((bitmapBuffer.Width >> 1) != bitmap.PixelSize.Width || (bitmapBuffer.Height >> 1) != bitmap.PixelSize.Height)
				throw new ArgumentException("Invalid bitmap size.");

			// copy
			using var destBitmapBuffer = bitmap.Lock();
			using var srcBitmapBuffer = bitmapBuffer.Share();
			await Task.Run(() =>
			{
				srcBitmapBuffer.Memory.Pin(srcBaseAddress =>
				{
					var bitmapFormat = bitmap.Format;
					if (bitmapFormat == PixelFormats.Bgra8888)
						CopyToQuarterBgra32(srcBaseAddress, srcBitmapBuffer.Format, srcBitmapBuffer.Width, srcBitmapBuffer.Height, srcBitmapBuffer.RowBytes, destBitmapBuffer.Address, destBitmapBuffer.RowBytes, cancellationToken);
					else if (bitmapFormat == PixelFormats.Rgba64)
						CopyToQuarterRgba64(srcBaseAddress, srcBitmapBuffer.Format, srcBitmapBuffer.Width, srcBitmapBuffer.Height, srcBitmapBuffer.RowBytes, destBitmapBuffer.Address, destBitmapBuffer.RowBytes, cancellationToken);
					else
						throw new NotSupportedException();
				});
			}, cancellationToken);
		}


		// Copy bitmap data to BGRA32 with quarter size.
		static unsafe void CopyToQuarterBgra32(IntPtr srcBaseAddress, BitmapFormat srcFormat, int width, int height, int srcRowStride, IntPtr destBaseAddress, int destRowStride, CancellationToken cancellationToken)
		{
			width >>= 1;
			height >>= 1;
			switch (srcFormat)
			{
				case BitmapFormat.Bgra32:
				{
					var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
					var packFunc = ImageProcessing.SelectBgra32Packing();
					ImageProcessing.ParallelFor(0, height, (y) =>
					{
						if (cancellationToken.IsCancellationRequested)
							throw new TaskCanceledException();
						var r1 = (byte)0;
						var r2 = (byte)0;
						var g1 = (byte)0;
						var g2 = (byte)0;
						var b1 = (byte)0;
						var b2 = (byte)0;
						var a1 = (byte)0;
						var a2 = (byte)0;
						var selection = y;
						var srcRowPtr = (uint*)((byte*)srcBaseAddress + (y * 2 * srcRowStride));
						var srcNextRowPtr = (uint*)((byte*)srcRowPtr + srcRowStride);
						var destPixelPtr = (uint*)((byte*)destBaseAddress + (y * destRowStride));
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
					break;
				}
				case BitmapFormat.Bgra64:
				{
					var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
					var packFunc = ImageProcessing.SelectBgra32Packing();
					ImageProcessing.ParallelFor(0, height, (y) =>
					{
						if (cancellationToken.IsCancellationRequested)
							throw new TaskCanceledException();
						var r1 = (ushort)0;
						var r2 = (ushort)0;
						var g1 = (ushort)0;
						var g2 = (ushort)0;
						var b1 = (ushort)0;
						var b2 = (ushort)0;
						var a1 = (ushort)0;
						var a2 = (ushort)0;
						var selection = y;
						var srcRowPtr = (ulong*)((byte*)srcBaseAddress + (y * 2 * srcRowStride));
						var srcNextRowPtr = (ulong*)((byte*)srcRowPtr + srcRowStride);
						var destPixelPtr = (uint*)((byte*)destBaseAddress + (y * destRowStride));
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
					break;
				}
				default:
					throw new NotSupportedException($"Unsupported bitmap format: {srcFormat}");
			}
		}
		
		
		// Copy bitmap data to RGBA64 with quarter size.
		static unsafe void CopyToQuarterRgba64(IntPtr srcBaseAddress, BitmapFormat srcFormat, int width, int height, int srcRowStride, IntPtr destBaseAddress, int destRowStride, CancellationToken cancellationToken)
		{
			width >>= 1;
			height >>= 1;
			switch (srcFormat)
			{
				case BitmapFormat.Bgra32:
				{
					ushort ExtendToUInt16(byte n) => (ushort)((n << 8) | n);
					var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
					var packFunc = ImageProcessing.SelectRgba64Packing();
					ImageProcessing.ParallelFor(0, height, (y) =>
					{
						if (cancellationToken.IsCancellationRequested)
							throw new TaskCanceledException();
						var r1 = (byte)0;
						var r2 = (byte)0;
						var g1 = (byte)0;
						var g2 = (byte)0;
						var b1 = (byte)0;
						var b2 = (byte)0;
						var a1 = (byte)0;
						var a2 = (byte)0;
						var selection = y;
						var srcRowPtr = (uint*)((byte*)srcBaseAddress + (y * 2 * srcRowStride));
						var srcNextRowPtr = (uint*)((byte*)srcRowPtr + srcRowStride);
						var destPixelPtr = (ulong*)((byte*)destBaseAddress + (y * destRowStride));
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
							*destPixelPtr = packFunc(ExtendToUInt16((byte)((r1 + r2) >> 1)), ExtendToUInt16((byte)((g1 + g2) >> 1)), ExtendToUInt16((byte)((b1 + b2) >> 1)), ExtendToUInt16((byte)((a1 + a2) >> 1)));
						}
					});
					break;
				}
				case BitmapFormat.Bgra64:
				{
					var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
					var packFunc = ImageProcessing.SelectRgba64Packing();
					ImageProcessing.ParallelFor(0, height, (y) =>
					{
						if (cancellationToken.IsCancellationRequested)
							throw new TaskCanceledException();
						var r1 = (ushort)0;
						var r2 = (ushort)0;
						var g1 = (ushort)0;
						var g2 = (ushort)0;
						var b1 = (ushort)0;
						var b2 = (ushort)0;
						var a1 = (ushort)0;
						var a2 = (ushort)0;
						var selection = y;
						var srcRowPtr = (ulong*)((byte*)srcBaseAddress + (y * 2 * srcRowStride));
						var srcNextRowPtr = (ulong*)((byte*)srcRowPtr + srcRowStride);
						var destPixelPtr = (ulong*)((byte*)destBaseAddress + (y * destRowStride));
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
							*destPixelPtr = packFunc((ushort)((r1 + r2) >> 1), (ushort)((g1 + g2) >> 1), (ushort)((b1 + b2) >> 1), (ushort)((a1 + a2) >> 1));
						}
					});
					break;
				}
				default:
					throw new NotSupportedException($"Unsupported bitmap format: {srcFormat}");
			}
		}
		
		
		// Copy bitmap data to RGBA64 with same size including rotated size.
		static unsafe void CopyToRgba64(IntPtr srcBaseAddress, BitmapFormat srcFormat, int width, int height, int srcRowStride, IntPtr destBaseAddress, int destRowStride, int orientation, CancellationToken cancellationToken)
		{
			switch (srcFormat)
			{
				case BitmapFormat.Bgra32:
				{
					ushort ExtendToUInt16(byte n) => (ushort)((n << 8) | n);
					var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
					var packFunc = ImageProcessing.SelectRgba64Packing();
					switch (orientation)
					{
						case 0:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (byte)0;
								var g = (byte)0;
								var r = (byte)0;
								var a = (byte)0;
								var srcPixelPtr = (uint*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = (ulong*)((byte*)destBaseAddress + (y * destRowStride));
								for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*destPixelPtr = packFunc(ExtendToUInt16(r), ExtendToUInt16(g), ExtendToUInt16(b), ExtendToUInt16(a));
								}
							});
							break;
						case 90:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (byte)0;
								var g = (byte)0;
								var r = (byte)0;
								var a = (byte)0;
								var srcPixelPtr = (uint*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((height - y - 1) * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*(ulong*)destPixelPtr = packFunc(ExtendToUInt16(r), ExtendToUInt16(g), ExtendToUInt16(b), ExtendToUInt16(a));
								}
							});
							break;
						case 180:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (byte)0;
								var g = (byte)0;
								var r = (byte)0;
								var a = (byte)0;
								var srcPixelPtr = (uint*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = (ulong*)((byte*)destBaseAddress + ((height - y - 1) * destRowStride) + ((width - 1) * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*destPixelPtr = packFunc(ExtendToUInt16(r), ExtendToUInt16(g), ExtendToUInt16(b), ExtendToUInt16(a));
								}
							});
							break;
						case 270:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (byte)0;
								var g = (byte)0;
								var r = (byte)0;
								var a = (byte)0;
								var srcPixelPtr = (uint*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((width - 1) * destRowStride) + (y * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*(ulong*)destPixelPtr = packFunc(ExtendToUInt16(r), ExtendToUInt16(g), ExtendToUInt16(b), ExtendToUInt16(a));
								}
							});
							break;
					}
					break;
				}
				case BitmapFormat.Bgra64:
				{
					var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
					var packFunc = ImageProcessing.SelectRgba64Packing();
					switch (orientation)
					{
						case 0:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (ushort)0;
								var g = (ushort)0;
								var r = (ushort)0;
								var a = (ushort)0;
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = (ulong*)((byte*)destBaseAddress + (y * destRowStride));
								for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*destPixelPtr = packFunc(r, g, b, a);
								}
							});
							break;
						case 90:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (ushort)0;
								var g = (ushort)0;
								var r = (ushort)0;
								var a = (ushort)0;
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((height - y - 1) * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr += destRowStride)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*(ulong*)destPixelPtr = packFunc(r, g, b, a);
								}
							});
							break;
						case 180:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (ushort)0;
								var g = (ushort)0;
								var r = (ushort)0;
								var a = (ushort)0;
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = (ulong*)((byte*)destBaseAddress + ((height - y - 1) * destRowStride) + ((width - 1) * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, --destPixelPtr)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*destPixelPtr = packFunc(r, g, b, a);
								}
							});
							break;
						case 270:
							ImageProcessing.ParallelFor(0, height, (y) =>
							{
								if (cancellationToken.IsCancellationRequested)
									throw new TaskCanceledException();
								var b = (ushort)0;
								var g = (ushort)0;
								var r = (ushort)0;
								var a = (ushort)0;
								var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (y * srcRowStride));
								var destPixelPtr = ((byte*)destBaseAddress + ((width - 1) * destRowStride) + (y * sizeof(ulong)));
								for (var x = width; x > 0; --x, ++srcPixelPtr, destPixelPtr -= destRowStride)
								{
									unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
									*(ulong*)destPixelPtr = packFunc(r, g, b, a);
								}
							});
							break;
					}
					break;
				}
				default:
					throw new NotSupportedException();
			}
		}
		

		/// <summary>
		/// Create <see cref="IBitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="orientation">Orientation.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns><see cref="IBitmap"/>.</returns>
		public static Bitmap CreateAvaloniaBitmap(this IBitmapBuffer buffer, int orientation = 0, CancellationToken cancellationToken = default)
		{
			if (cancellationToken.IsCancellationRequested)
				throw new TaskCanceledException();
			return buffer.Memory.Pin(srcBaseAddr =>
			{
				var srcWidth = buffer.Width;
				var srcHeight = buffer.Height;
				var avaloniaPixelFormat = buffer.Format switch
				{
					BitmapFormat.Bgra32 => PixelFormats.Bgra8888,
					BitmapFormat.Bgra64 => PixelFormats.Rgba64,
					_ => throw new NotSupportedException(),
				};
				var avaloniaBitmap = orientation switch
				{
					0 or 180 => new WriteableBitmap(new PixelSize(srcWidth, srcHeight), new Vector(96, 96), avaloniaPixelFormat, AlphaFormat.Unpremul),
					90 or 270 => new WriteableBitmap(new PixelSize(srcHeight, srcWidth), new Vector(96, 96), avaloniaPixelFormat, AlphaFormat.Unpremul),
					_ => throw new ArgumentException(),
				};
				using var avaloniaBitmapBuffer = avaloniaBitmap.Lock();
				var stopWatch = IAppSuiteApplication.CurrentOrNull?.IsDebugMode == true
					? new Stopwatch().Also(it => it.Start())
					: null;
				switch (buffer.Format)
				{
					case BitmapFormat.Bgra32:
						CopyTo(srcBaseAddr, buffer.Format, srcWidth, srcHeight, buffer.RowBytes, avaloniaBitmapBuffer.Address, avaloniaBitmapBuffer.RowBytes, orientation, cancellationToken);
						break;
					case BitmapFormat.Bgra64:
						CopyToRgba64(srcBaseAddr, buffer.Format, srcWidth, srcHeight, buffer.RowBytes, avaloniaBitmapBuffer.Address, avaloniaBitmapBuffer.RowBytes, orientation, cancellationToken);
						break;
					default:
						throw new NotSupportedException($"Unsupported bitmap format: {buffer.Format}");
				}
				if (stopWatch != null)
				{
					stopWatch.Stop();
					Logger?.LogTrace("Take {duration} ms to convert from {srcWidth}x{srcHeight} {format} bitmap buffer to Avalonia bitmap", stopWatch.ElapsedMilliseconds, srcWidth, buffer.Height, buffer.Format);
				}
				return avaloniaBitmap;
			});
		}


		/// <summary>
		/// Create <see cref="IBitmap"/> which copied data from this <see cref="IBitmapBuffer"/> asynchronously.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="orientation">Orientation.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of creating <see cref="IBitmap"/>.</returns>
		public static async Task<Bitmap> CreateAvaloniaBitmapAsync(this IBitmapBuffer buffer, int orientation = 0, CancellationToken cancellationToken = default)
		{
			using var sharedBuffer = buffer.Share();
			return await Task.Run(() => CreateAvaloniaBitmap(sharedBuffer, orientation, cancellationToken), cancellationToken);
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
				switch (orientation)
				{
					case 0:
						return new System.Drawing.Bitmap(srcWidth, srcHeight, srcRowStride, buffer.Format.ToSystemDrawingPixelFormat(), srcBaseAddr);
					case 90:
					case 270:
						return new System.Drawing.Bitmap(srcHeight, srcWidth, buffer.Format.ToSystemDrawingPixelFormat()).Also(bitmap =>
						{
							var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
							try
							{
								CopyTo(srcBaseAddr, buffer.Format, srcWidth, srcHeight, srcRowStride, bitmapData.Scan0, bitmapData.Stride, orientation);
							}
							finally
							{
								bitmap.UnlockBits(bitmapData);
							}
						});
					case 180:
						return new System.Drawing.Bitmap(srcWidth, srcHeight, buffer.Format.ToSystemDrawingPixelFormat()).Also(bitmap =>
						{
							var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
							try
							{
								CopyTo(srcBaseAddr, buffer.Format, srcWidth, srcHeight, srcRowStride, bitmapData.Scan0, bitmapData.Stride, orientation);
							}
							finally
							{
								bitmap.UnlockBits(bitmapData);
							}
						});
					default:
						throw new ArgumentException();
				}
			});
		}
#endif


		/// <summary>
		/// Create <see cref="IBitmap"/> with quarter size which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns><see cref="IBitmap"/>.</returns>
		public static Bitmap CreateQuarterSizeAvaloniaBitmap(this IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken = default)
        {
			// check size
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			if (width <= 2 || height <= 2)
				return bitmapBuffer.CreateAvaloniaBitmap();

			// create bitmap
			var pixelFormat = bitmapBuffer.Format switch
			{
				BitmapFormat.Bgra32 => PixelFormats.Bgra8888,
				BitmapFormat.Bgra64 => PixelFormats.Rgba64,
				_ => throw new NotSupportedException(),
			};
			width >>= 1;
			height >>= 1;
			return new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), pixelFormat, AlphaFormat.Unpremul).Also(bitmap =>
			{
				using var bitmapFrame = bitmap.Lock();
				var srcRowStride = bitmapBuffer.RowBytes;
				var destRowStride = bitmapFrame.RowBytes;
				var stopWatch = IAppSuiteApplication.CurrentOrNull?.IsDebugMode == true
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
								ImageProcessing.ParallelFor(0, height, (y) =>
								{
									if (cancellationToken.IsCancellationRequested)
										throw new TaskCanceledException();
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
								var packFunc = ImageProcessing.SelectRgba64Packing();
								ImageProcessing.ParallelFor(0, height, (y) =>
								{
									if (cancellationToken.IsCancellationRequested)
										throw new TaskCanceledException();
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
									var destPixelPtr = (ulong*)((byte*)destBaseAddr + (y * destRowStride));
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
										*destPixelPtr = packFunc((ushort)((r1 + r2) >> 1), (ushort)((g1 + g2) >> 1), (ushort)((b1 + b2) >> 1), (ushort)((a1 + a2) >> 1));
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
					Logger?.LogTrace("Take {duration} ms to convert from {width}x{height} {format} bitmap buffer to quarter size Avalonia bitmap", stopWatch.ElapsedMilliseconds, bitmapBuffer.Width, bitmapBuffer.Height, bitmapBuffer.Format);
				}
			});
        }


		/// <summary>
		/// Create <see cref="IBitmap"/> with quarter size which copied data from this <see cref="IBitmapBuffer"/> asynchronously.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Task of creating <see cref="IBitmap"/>.</returns>
		public static async Task<Bitmap> CreateQuarterSizeAvaloniaBitmapAsync(this IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken)
		{
			using var sharedBitmapBuffer = bitmapBuffer.Share();
			var bitmap = await Task.Run(() => CreateQuarterSizeAvaloniaBitmap(sharedBitmapBuffer), cancellationToken);
			if (cancellationToken.IsCancellationRequested)
				throw new TaskCanceledException();
			return bitmap;
		}


		/// <summary>
		/// Create <see cref="SKBitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="orientation">Orientation.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns><see cref="SKBitmap"/>.</returns>
		public static SKBitmap CreateSkiaBitmap(this IBitmapBuffer bitmapBuffer, int orientation = 0, CancellationToken cancellationToken = default)
		{
			var srcWidth = bitmapBuffer.Width;
			var srcHeight = bitmapBuffer.Height;
			var skiaColorType = bitmapBuffer.Format switch
			{
				BitmapFormat.Bgra32
				or BitmapFormat.Bgra64 => SKColorType.Bgra8888,
				_ => throw new NotSupportedException($"Unsupported bitmap format: {bitmapBuffer.Format}"),
			};
			var skiaImageInfo = orientation switch
			{ 
				0 or 180 => new SKImageInfo(srcWidth, srcHeight, skiaColorType, SKAlphaType.Unpremul),
				90 or 270 => new SKImageInfo(srcHeight, srcWidth, skiaColorType, SKAlphaType.Unpremul),
				_ => throw new ArgumentException(),
			};
			return new SKBitmap(skiaImageInfo).Also(skiaBitmap =>
			{
				using var skiaPixels = skiaBitmap.PeekPixels().AsNonNull();
				var stopWatch = IAppSuiteApplication.CurrentOrNull?.IsDebugMode == true
					? new Stopwatch().Also(it => it.Start())
					: null;
				bitmapBuffer.Memory.Pin(srcBaseAddr =>
				{
					switch (bitmapBuffer.Format)
					{
						case BitmapFormat.Bgra32:
							CopyTo(srcBaseAddr, bitmapBuffer.Format, srcWidth, srcHeight, bitmapBuffer.RowBytes, skiaPixels.GetPixels(), skiaPixels.RowBytes, orientation, cancellationToken);
							break;
						case BitmapFormat.Bgra64:
							CopyToBgra32(srcBaseAddr, bitmapBuffer.Format, srcWidth, srcHeight, bitmapBuffer.RowBytes, skiaPixels.GetPixels(), skiaPixels.RowBytes, orientation, cancellationToken);
							break;
					}
				});
				if (stopWatch != null)
				{
					stopWatch.Stop();
					Logger?.LogTrace("Take {duration} ms to convert from {width}x{height} {format} bitmap buffer to Skia bitmap", stopWatch.ElapsedMilliseconds, bitmapBuffer.Width, bitmapBuffer.Height, bitmapBuffer.Format);
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


		/// <summary>
		/// Rotate <see cref="IBitmapBuffer"/> with given orientation to new <see cref="IBitmapBuffer"/> instance.
		/// </summary>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="orientation">Orientation, must be one of 0, 90, 180 and 270.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Rotated <see cref="IBitmapBuffer"/>.</returns>
		public static IBitmapBuffer Rotate(this IBitmapBuffer bitmapBuffer, int orientation, CancellationToken cancellationToken = default) => orientation switch
		{
			0 => bitmapBuffer.Copy(),
			90 or 270 => new BitmapBuffer(bitmapBuffer.Format, bitmapBuffer.ColorSpace, bitmapBuffer.Height, bitmapBuffer.Width).Also(rotatedBitmapBuffer=>
            {
				bitmapBuffer.Memory.Pin(srcBaseAddr =>
				{
					rotatedBitmapBuffer.Memory.Pin(destBaseAddr =>
					{
						CopyTo(srcBaseAddr, bitmapBuffer.Format, bitmapBuffer.Width, bitmapBuffer.Height, bitmapBuffer.RowBytes, destBaseAddr, rotatedBitmapBuffer.RowBytes, orientation, cancellationToken);
					});
				});
            }),
			180 => new BitmapBuffer(bitmapBuffer.Format, bitmapBuffer.ColorSpace, bitmapBuffer.Width, bitmapBuffer.Height).Also(rotatedBitmapBuffer =>
			{
				bitmapBuffer.Memory.Pin(srcBaseAddr =>
				{
					rotatedBitmapBuffer.Memory.Pin(destBaseAddr =>
					{
						CopyTo(srcBaseAddr, bitmapBuffer.Format, bitmapBuffer.Width, bitmapBuffer.Height, bitmapBuffer.RowBytes, destBaseAddr, rotatedBitmapBuffer.RowBytes, orientation, cancellationToken);
					});
				});
			}),
			_ => throw new ArgumentException(),
		};
	}
}
