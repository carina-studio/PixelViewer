using Carina.PixelViewer.Runtime.InteropServices;
using NLog;
using System;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Utility functions for image processing.
	/// </summary>
	static unsafe class ImageProcessing
	{
		/// <summary>
		/// Method to convert YUV422 to packed BGRA (unsafe).
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		public delegate void Yuv422ToBgraUnsafe(int y1, int y2, int u, int v, int* bgra1, int* bgra2);


		/// <summary>
		/// Method to convert YUV444 to packed BGRA (unsafe).
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		public delegate void Yuv444ToBgraUnsafe(int y, int u, int v, int* bgra);


		// Static fields.
		static readonly ILogger logger = LogManager.GetCurrentClassLogger();


		/// <summary>
		/// Clip given number to the range of <see cref="byte"/>.
		/// </summary>
		/// <param name="value">Given number.</param>
		/// <returns>Clipped number.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte ClipToByte(int value)
		{
			if (value < 0)
				return 0;
			if (value > 255)
				return 255;
			return (byte)value;
		}


		/// <summary>
		/// Copy data between image planes.
		/// </summary>
		/// <param name="width">Width of plane in pixels.</param>
		/// <param name="height">Height of plane in pixels.</param>
		/// <param name="bytesPerPixel">Bytes per pixel.</param>
		/// <param name="srcBuffer">Address of buffer of source plane.</param>
		/// <param name="srcPixelStride">Pixel stride in source plane.</param>
		/// <param name="srcRowStride">Row stride in source plane.</param>
		/// <param name="destBuffer">Address of buffer of destination plane.</param>
		/// <param name="destPixelStride">Pixel stride in destination plane.</param>
		/// <param name="destRowStride">Row stride in destination plane.</param>
		public static void CopyPlane(int width, int height, int bytesPerPixel, void* srcBuffer, int srcPixelStride, int srcRowStride, void* destBuffer, int destPixelStride, int destRowStride)
		{
			// check parameters
			if (width <= 0 || height <= 0)
				throw new ArgumentOutOfRangeException($"Invalid size of plane: {width}x{height}.", (Exception?)null);
			if (srcBuffer == null || destBuffer == null)
				throw new ArgumentOutOfRangeException($"Either source or destination buffer is null.", (Exception?)null);
			if (bytesPerPixel <= 0)
				throw new ArgumentOutOfRangeException(nameof(bytesPerPixel));
			if (srcBuffer == destBuffer)
				return;

			// copy
			if (srcPixelStride != bytesPerPixel || destPixelStride != bytesPerPixel)
			{
				switch (bytesPerPixel)
				{
					case 1:
						{
							var srcRowPtr = (byte*)srcBuffer;
							var destRowPtr = (byte*)destBuffer;
							while (height > 0)
							{
								var srcPixelPtr = srcRowPtr;
								var destPixelPtr = destRowPtr;
								for (var i = width; i > 0; --i, srcPixelPtr += srcPixelStride, destPixelPtr += destPixelStride)
									*destPixelPtr = *srcPixelPtr;
								srcRowPtr += srcRowStride;
								destRowPtr += destRowStride;
								--height;
							}
							break;
						}
					case 2:
						{
							var srcRowPtr = (byte*)srcBuffer;
							var destRowPtr = (byte*)destBuffer;
							while (height > 0)
							{
								var srcPixelPtr = srcRowPtr;
								var destPixelPtr = destRowPtr;
								for (var i = width; i > 0; --i, srcPixelPtr += srcPixelStride, destPixelPtr += destPixelStride)
									*(short*)destPixelPtr = *(short*)srcPixelPtr;
								srcRowPtr += srcRowStride;
								destRowPtr += destRowStride;
								--height;
							}
							break;
						}
					case 4:
						{
							var srcRowPtr = (byte*)srcBuffer;
							var destRowPtr = (byte*)destBuffer;
							while (height > 0)
							{
								var srcPixelPtr = srcRowPtr;
								var destPixelPtr = destRowPtr;
								for (var i = width; i > 0; --i, srcPixelPtr += srcPixelStride, destPixelPtr += destPixelStride)
									*(int*)destPixelPtr = *(int*)srcPixelPtr;
								srcRowPtr += srcRowStride;
								destRowPtr += destRowStride;
								--height;
							}
							break;
						}
					case 8:
						{
							var srcRowPtr = (byte*)srcBuffer;
							var destRowPtr = (byte*)destBuffer;
							while (height > 0)
							{
								var srcPixelPtr = srcRowPtr;
								var destPixelPtr = destRowPtr;
								for (var i = width; i > 0; --i, srcPixelPtr += srcPixelStride, destPixelPtr += destPixelStride)
									*(long*)destPixelPtr = *(long*)srcPixelPtr;
								srcRowPtr += srcRowStride;
								destRowPtr += destRowStride;
								--height;
							}
							break;
						}
					default:
						{
							var srcRowPtr = (byte*)srcBuffer;
							var destRowPtr = (byte*)destBuffer;
							while (height > 0)
							{
								var srcPixelPtr = srcRowPtr;
								var destPixelPtr = destRowPtr;
								for (var i = width; i > 0; --i, srcPixelPtr += srcPixelStride, destPixelPtr += destPixelStride)
									Marshal.Copy(srcPixelPtr, destPixelPtr, bytesPerPixel);
								srcRowPtr += srcRowStride;
								destRowPtr += destRowStride;
								--height;
							}
							break;
						}
				}
			}
			else if (srcRowStride != destRowStride)
			{
				var srcRowPtr = (byte*)srcBuffer;
				var destRowPtr = (byte*)destBuffer;
				var rowSize = Math.Min(srcRowStride, destRowStride);
				while (height > 0)
				{
					Marshal.Copy(srcRowPtr, destRowPtr, rowSize);
					srcRowPtr += srcRowStride;
					destRowPtr += destRowStride;
					--height;
				}
			}
			else
			{
				var size = (srcRowStride * (height - 1) + bytesPerPixel * width);
				Marshal.Copy(srcBuffer, destBuffer, size);
			}
		}


		/// <summary>
		/// Select proper function for YUV422 to BGRA conversion (unsafe).
		/// </summary>
		/// <returns>YUV422 to BGRA conversion function.</returns>
		public static Yuv422ToBgraUnsafe SelectYuv422ToBgraConversionUnsafe() => App.Current.Settings.YuvConversionMode switch
		{
			YuvConversionMode.ITU_R => Yuv422ToBgraUnsafeITUR,
			_ => Yuv422ToBgraUnsafeNTSC,
		};


		/// <summary>
		/// Select proper function for YUV to BGRA conversion.
		/// </summary>
		/// <returns>YUV to BGRA conversion function.</returns>
		public static Yuv444ToBgraUnsafe SelectYuv444ToBgraConversionUnsafe() => App.Current.Settings.YuvConversionMode switch
		{
			YuvConversionMode.ITU_R => Yuv444ToBgraUnsafeITUR,
			_ => Yuv444ToBgraUnsafeNTSC,
		};


		/// <summary>
		/// Convert YUV422 color to packed BGRA color based-on ITU-R standard.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv422ToBgraUnsafeITUR(int y1, int y2, int u, int v, int* bgra1, int* bgra2)
		{
			var pixel1 = (byte*)bgra1;
			var pixel2 = (byte*)bgra2;
			u -= 128;
			v -= 128;
			var bCoeff = (u + (u >> 1) + (u >> 2) + (u >> 6));
			var gCoeff = (-((u >> 2) + (u >> 4) + (u >> 5)) - ((u >> 1) + (v >> 3) + (v >> 4) + (v >> 5)));
			var rCoeff = (v + (v >> 2) + (v >> 3) + (v >> 5));
			pixel1[0] = ClipToByte(y1 + bCoeff);
			pixel1[1] = ClipToByte(y1 + gCoeff);
			pixel1[2] = ClipToByte(y1 + rCoeff);
			pixel1[3] = 255;
			pixel2[0] = ClipToByte(y2 + bCoeff);
			pixel2[1] = ClipToByte(y2 + gCoeff);
			pixel2[2] = ClipToByte(y2 + rCoeff);
			pixel2[3] = 255;
		}


		/// <summary>
		/// Convert YUV422 color to packed BGRA color based-on NTSC standard.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv422ToBgraUnsafeNTSC(int y1, int y2, int u, int v, int* bgra1, int* bgra2)
		{
			var pixel1 = (byte*)bgra1;
			var pixel2 = (byte*)bgra2;
			y1 = 298 * (y1 - 16);
			y2 = 298 * (y2 - 16);
			u -= 128;
			v -= 128;
			var bCoeff = (516 * u + 128);
			var gCoeff = (-100 * u - 208 * v + 128);
			var rCoeff = (409 * v + 128);
			pixel1[0] = ClipToByte((y1 + bCoeff) >> 8);
			pixel1[1] = ClipToByte((y1 + gCoeff) >> 8);
			pixel1[2] = ClipToByte((y1 + rCoeff) >> 8);
			pixel1[3] = 255;
			pixel2[0] = ClipToByte((y2 + bCoeff) >> 8);
			pixel2[1] = ClipToByte((y2 + gCoeff) >> 8);
			pixel2[2] = ClipToByte((y2 + rCoeff) >> 8);
			pixel2[3] = 255;
		}


		/// <summary>
		/// Convert YUV444 color to packed BGRA color based-on ITU-R standard.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv444ToBgraUnsafeITUR(int y, int u, int v, int* bgra)
		{
			/*
			 * ITU-R formula:
			 * R = clip(Y + 1.402(Cr - 128))
			 * G = clip(Y - 0.344(Cb - 128) - 0.714(Cr - 128))
			 * B = clip(Y + 1.772(Cb - 128))
			 * 
			 * Quantized:
			 * Cb = Cb - 128
			 * Cr = Cr - 128
			 * R = Y + Cr + (Cr >> 2) + (Cr >> 3) + (Cr >> 5)
			 * G = Y - ((Cb >> 2) + (Cb >> 4) + (Cb >> 5)) - ((Cr >> 1) + (Cr >> 3) + (Cr >> 4) + (Cr >> 5))
			 * B = Y + Cb + (Cb >> 1) + (Cb >> 2) + (Cb >> 6)
			 */
			var pixel = (byte*)bgra;
			u -= 128;
			v -= 128;
			pixel[0] = ClipToByte(y + u + (u >> 1) + (u >> 2) + (u >> 6));
			pixel[1] = ClipToByte(y - ((u >> 2) + (u >> 4) + (u >> 5)) - ((v >> 1) + (v >> 3) + (v >> 4) + (v >> 5)));
			pixel[2] = ClipToByte(y + v + (v >> 2) + (v >> 3) + (v >> 5));
			pixel[3] = 255;
		}


		/// <summary>
		/// Convert YUV444 color to packed BGRA color based-on NTSC standard.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv444ToBgraUnsafeNTSC(int y, int u, int v, int* bgra)
		{
			/*
			 * NTSC formula:
			 * Y = 298(Y - 16)
			 * U = U - 128
			 * V = V - 128
			 * R = (Y + 409V + 128) >> 8
			 * G = (Y - 100U - 208V + 128) >> 8
			 * B = (Y + 516U + 128) >> 8
			 */
			var pixel = (byte*)bgra;
			y = 298 * (y - 16);
			u -= 128;
			v -= 128;
			pixel[0] = ClipToByte((y + 516 * u + 128) >> 8);
			pixel[1] = ClipToByte((y - 100 * u - 208 * v + 128) >> 8);
			pixel[2] = ClipToByte((y + 409 * v + 128) >> 8);
			pixel[3] = 255;
		}
	}
}
