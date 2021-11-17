using Carina.PixelViewer.Runtime.InteropServices;
using CarinaStudio.Configuration;
using Microsoft.Extensions.Logging;
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
		/// Whether system uses Big-Endian or not.
		/// </summary>
		public static readonly bool IsBigEndian = CarinaStudio.Global.Run(() =>
		{
			var n = 1;
			return ((*((byte*)&n) & 1) == 0);
		});
		/// <summary>
		/// Whether system uses Little-Endian or not.
		/// </summary>
		public static readonly bool IsLittleEndian = !IsBigEndian;


		// Static fields.
		static readonly ILogger logger = App.Current.LoggerFactory.CreateLogger(nameof(ImageProcessing));


		/// <summary>
		/// Clip given number to the range of <see cref="byte"/>.
		/// </summary>
		/// <param name="value">Given number.</param>
		/// <returns>Clipped number.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte ClipToByte(double value)
		{
			if (value < 0)
				return 0;
			if (value > 255)
				return 255;
			return (byte)(value + 0.5);
		}


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
		/// Clip given number to the range of <see cref="ushort"/>.
		/// </summary>
		/// <param name="value">Given number.</param>
		/// <returns>Clipped number.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort ClipToUInt16(double value)
		{
			if (value < 0)
				return 0;
			if (value > 65535)
				return 65535;
			return (ushort)value;
		}


		/// <summary>
		/// Clip given number to the range of <see cref="ushort"/>.
		/// </summary>
		/// <param name="value">Given number.</param>
		/// <returns>Clipped number.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort ClipToUInt16(long value)
		{
			if (value < 0)
				return 0;
			if (value > 65535)
				return 65535;
			return (ushort)value;
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
		/// Pack B/G/R/A to 32-bit BGRA for Big-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="a">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint PackBgra32BE(byte b, byte g, byte r, byte a) =>
			(uint)((b << 24) | (g << 16) | (r << 8) | a);


		/// <summary>
		/// Pack B/G/R/A to 32-bit BGRA for Little-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="a">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint PackBgra32LE(byte b, byte g, byte r, byte a) =>
			(uint)((a << 24) | (r << 16) | (g << 8) | b);


		/// <summary>
		/// Pack B/G/R/A to 62-bit BGRA for Big-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="a">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong PackBgra64BE(ushort b, ushort g, ushort r, ushort a) =>
			((ulong)b << 48) | ((ulong)g << 32) | ((ulong)r << 16) | a;


		/// <summary>
		/// Pack B/G/R/A to 62-bit BGRA for Little-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="a">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong PackBgra64LE(ushort b, ushort g, ushort r, ushort a) =>
			((ulong)a << 48) | ((ulong)r << 32) | ((ulong)g << 16) | b;


		// Convert from RGB24 to Luminance based-on ITU-R BT.709.
		static byte Rgb24ToLuminanceBT709(byte r, byte g, byte b)
		{
			// L = 0.2126 R + 0.7152 G + 0.0722 B
			return ClipToByte((13933 * r + 46871 * g + 4732 * b) >> 16);
		}


		// Convert from RGB48 to Luminance based-on ITU-R BT.709.
		static ushort Rgb48ToLuminanceBT709(ushort r, ushort g, ushort b)
		{
			// L = 0.2126 R + 0.7152 G + 0.0722 B
			return ClipToUInt16((13933L * r + 46871L * g + 4732L * b) >> 16);
		}


		/// <summary>
		/// Select property function to pack B/G/R/A into 32-bit integer.
		/// </summary>
		/// <returns>Pointer to packing function.</returns>
		public static delegate*<byte, byte, byte, byte, uint> SelectBgra32Packing()
        {
			if (IsLittleEndian)
				return &PackBgra32LE;
			return &PackBgra32BE;
        }


		/// <summary>
		/// Select property function to unpack 32-bit integer into B/G/R/A.
		/// </summary>
		/// <returns>Pointer to unpacking function.</returns>
		public static delegate*<uint, byte*, byte*, byte*, byte*, void> SelectBgra32Unpacking()
		{
			if (IsLittleEndian)
				return &UnpackBgra32LE;
			return &UnpackBgra32BE;
		}


		/// <summary>
		/// Select property function to pack B/G/R/A into 64-bit integer.
		/// </summary>
		/// <returns>Pointer to packing function.</returns>
		public static delegate*<ushort, ushort, ushort, ushort, ulong> SelectBgra64Packing()
		{
			if (IsLittleEndian)
				return &PackBgra64LE;
			return &PackBgra64BE;
		}


		/// <summary>
		/// Select property function to unpack 64-bit integer into B/G/R/A.
		/// </summary>
		/// <returns>Pointer to unpacking function.</returns>
		public static delegate*<ulong, ushort*, ushort*, ushort*, ushort*, void> SelectBgra64Unpacking()
		{
			if (IsLittleEndian)
				return &UnpackBgra64LE;
			return &UnpackBgra64BE;
		}


		/// <summary>
		/// Select proper maximum degree of parallel image processing.
		/// </summary>
		/// <returns>Maximum degree of parallel image processing.</returns>
		public static int SelectMaxDegreeOfParallelism() => Math.Max(1, Environment.ProcessorCount / 2);


		/// <summary>
		/// Select proper function to convert from 24-bit RGB to 8-bit luminance.
		/// </summary>
		/// <returns>RGB to luminance conversion function.</returns>
		public static delegate*<byte, byte, byte, byte> SelectRgb24ToLuminanceConversion() => &Rgb24ToLuminanceBT709;


		/// <summary>
		/// Select proper function to convert from 48-bit RGB to 16-bit luminance.
		/// </summary>
		/// <returns>RGB to luminance conversion function.</returns>
		public static delegate*<ushort, ushort, ushort, ushort> SelectRgb48ToLuminanceConversion() => &Rgb48ToLuminanceBT709;


		/// <summary>
		/// Select proper function for YUV422 to 32-bit BGRA conversion (unsafe).
		/// </summary>
		/// <returns>YUV422 to BGRA conversion function.</returns>
		public static delegate*<int, int, int, int, uint*, uint*, void> SelectYuv422ToBgra32Conversion() =>
			SelectYuv422ToBgra32Conversion(App.Current.Settings.GetValueOrDefault(SettingKeys.DefaultYuvConversionMode));


		/// <summary>
		/// Select proper function for YUV422 to 32-bit BGRA conversion (unsafe).
		/// </summary>
		/// <param name="mode">Conversion mode.</param>
		/// <returns>YUV422 to BGRA conversion function.</returns>
		public static delegate*<int, int, int, int, uint*, uint*, void> SelectYuv422ToBgra32Conversion(YuvConversionMode mode) => mode switch
		{
			YuvConversionMode.BT_601 => &Yuv422ToBgra32BT601,
			YuvConversionMode.BT_709 => &Yuv422ToBgra32BT709,
			_ => &Yuv422ToBgra32BT656,
		};


		/// <summary>
		/// Select proper function for YUV422 to 64-bit BGRA conversion (unsafe).
		/// </summary>
		/// <returns>YUV422 to BGRA conversion function.</returns>
		public static delegate*<int, int, int, int, ulong*, ulong*, void> SelectYuv422ToBgra64Conversion() =>
			SelectYuv422ToBgra64Conversion(App.Current.Settings.GetValueOrDefault(SettingKeys.DefaultYuvConversionMode));


		/// <summary>
		/// Select proper function for YUV422 to 64-bit BGRA conversion (unsafe).
		/// </summary>
		/// <param name="mode">Conversion mode.</param>
		/// <returns>YUV422 to BGRA conversion function.</returns>
		public static delegate*<int, int, int, int, ulong*, ulong*, void> SelectYuv422ToBgra64Conversion(YuvConversionMode mode) => mode switch
		{
			YuvConversionMode.BT_601 => &Yuv422ToBgra64BT601,
			YuvConversionMode.BT_709 => &Yuv422ToBgra64BT709,
			_ => &Yuv422ToBgra64BT656,
		};


		/// <summary>
		/// Select proper function for YUV to 32-bit BGRA conversion.
		/// </summary>
		/// <returns>YUV to BGRA conversion function.</returns>
		public static delegate*<int, int, int, uint*, void> SelectYuv444ToBgra32Conversion() =>
			SelectYuv444ToBgra32Conversion(App.Current.Settings.GetValueOrDefault(SettingKeys.DefaultYuvConversionMode));


		/// <summary>
		/// Select proper function for YUV to 32-bit BGRA conversion.
		/// </summary>
		/// <param name="mode">Conversion mode.</param>
		/// <returns>YUV to BGRA conversion function.</returns>
		public static delegate*<int, int, int, uint*, void> SelectYuv444ToBgra32Conversion(YuvConversionMode mode) => mode switch
		{
			YuvConversionMode.BT_601 => &Yuv444ToBgra32BT601,
			YuvConversionMode.BT_709 => &Yuv444ToBgra32BT709,
			_ => &Yuv444ToBgra32BT656,
		};


		/// <summary>
		/// Select proper function for YUV to 64-bit BGRA conversion.
		/// </summary>
		/// <returns>YUV to BGRA conversion function.</returns>
		public static delegate*<int, int, int, ulong*, void> SelectYuv444ToBgra64Conversion() =>
			SelectYuv444ToBgra64Conversion(App.Current.Settings.GetValueOrDefault(SettingKeys.DefaultYuvConversionMode));


		/// <summary>
		/// Select proper function for YUV to 64-bit BGRA conversion.
		/// </summary>
		/// <param name="mode">Conversion mode.</param>
		/// <returns>YUV to BGRA conversion function.</returns>
		public static delegate*<int, int, int, ulong*, void> SelectYuv444ToBgra64Conversion(YuvConversionMode mode) => mode switch
		{
			YuvConversionMode.BT_601 => &Yuv444ToBgra64BT601,
			YuvConversionMode.BT_709 => &Yuv444ToBgra64BT709,
			_ => &Yuv444ToBgra64BT656,
		};


		/// <summary>
		/// Unpack 32-bit integer into B/G/R/A for Big-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked B.</param>
		/// <param name="g">Unpacked G.</param>
		/// <param name="r">Unpacked R.</param>
		/// <param name="a">Unpacked A.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackBgra32BE(uint bgra, byte* b, byte* g, byte* r, byte* a)
		{
			*b = (byte)(bgra >> 24);
			*g = (byte)((bgra >> 16) & 0xff);
			*r = (byte)((bgra >> 8) & 0xff);
			*a = (byte)(bgra & 0xff);
		}


		/// <summary>
		/// Unpack 32-bit integer into B/G/R/A for Little-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked B.</param>
		/// <param name="g">Unpacked G.</param>
		/// <param name="r">Unpacked R.</param>
		/// <param name="a">Unpacked A.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackBgra32LE(uint bgra, byte* b, byte* g, byte* r, byte* a)
		{
			*a = (byte)(bgra >> 24);
			*r = (byte)((bgra >> 16) & 0xff);
			*g = (byte)((bgra >> 8) & 0xff);
			*b = (byte)(bgra & 0xff);
		}


		/// <summary>
		/// Unpack 64-bit integer into B/G/R/A for Big-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked B.</param>
		/// <param name="g">Unpacked G.</param>
		/// <param name="r">Unpacked R.</param>
		/// <param name="a">Unpacked A.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackBgra64BE(ulong bgra, ushort* b, ushort* g, ushort* r, ushort* a)
		{
			*b = (ushort)(bgra >> 48);
			*g = (ushort)((bgra >> 32) & 0xffff);
			*r = (ushort)((bgra >> 16) & 0xffff);
			*a = (ushort)(bgra & 0xffff);
		}


		/// <summary>
		/// Unpack 64-bit integer into B/G/R/A for Little-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked B.</param>
		/// <param name="g">Unpacked G.</param>
		/// <param name="r">Unpacked R.</param>
		/// <param name="a">Unpacked A.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackBgra64LE(ulong bgra, ushort* b, ushort* g, ushort* r, ushort* a)
		{
			*a = (ushort)(bgra >> 48);
			*r = (ushort)((bgra >> 32) & 0xffff);
			*g = (ushort)((bgra >> 16) & 0xffff);
			*b = (ushort)(bgra & 0xffff);
		}


		/// <summary>
		/// Convert YUV422 color to packed 32-bit BGRA color based-on BT.601.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv422ToBgra32BT601(int y1, int y2, int u, int v, uint* bgra1, uint* bgra2)
		{
			var pixel1 = (byte*)bgra1;
			var pixel2 = (byte*)bgra2;
			u -= 128;
			v -= 128;
			var bCoeff = (u + (u >> 1) + (u >> 2) + (u >> 6));
			var gCoeff = (-((u >> 2) + (u >> 4) + (u >> 5)) - ((v >> 1) + (v >> 3) + (v >> 4) + (v >> 5)));
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
		/// Convert YUV422 color to packed 32-bit BGRA color based-on BT.656.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv422ToBgra32BT656(int y1, int y2, int u, int v, uint* bgra1, uint* bgra2)
		{
			/*
			 * R = 1.164 * (y - 16) + 1.596 * (Cr - 128)
			 * G = 1.164 * (y - 16) - 0.391 * (Cb - 128) - 0.813 * (Cr - 128)
			 * B = 1.164 * (y - 16) + 2.018 * (Cb - 128)
			 * 
			 * [Quantized by 256]
			 * y -= 16
			 * u -= 128
			 * v -= 128
			 * R = (298 * y + 409 * v) >> 8
			 * G = (298 * y - 100 * u - 208 * v) >> 8
			 * B = (298 * y + 516 * u) >> 8
			 * 
			 * Decompose 298 = 256 + 32 + 8 + 2
			 * Decompose 409 = 256 + 128 + 16 + 8 + 1
			 * Decompose 100 = 64 + 32 + 4
			 * Decompose 208 = 128 + 64 + 16
			 * Decompose 516 = 512 + 4
			 * 
			 * y -= 16
			 * u -= 128
			 * v -= 128
			 * R = ((y << 8) + (y << 5) + (y << 3) + y + (v << 8) + (v << 7) + (v << 4) + (v << 3) + v) >> 8
			 * G = ((y << 8) + (y << 5) + (y << 3) + y - (u << 6) - (u << 5) - (u << 2) - (v << 7) - (v << 6) - (v << 4)) >> 8
			 * B = ((y << 8) + (y << 5) + (y << 3) + y + (u << 9) + (u << 2)) >> 8
			 */
			var pixel1 = (byte*)bgra1;
			var pixel2 = (byte*)bgra2;
			y1 -= 16;
			y2 -= 16;
			y1 = (y1 << 8) + (y1 << 5) + (y1 << 3) + y1;
			y2 = (y2 << 8) + (y2 << 5) + (y2 << 3) + y2;
			u -= 128;
			v -= 128;
			var rCoeff = (v << 8) + (v << 7) + (v << 4) + (v << 3) + v;
			var gCoeff = (u << 6) + (u << 5) + (u << 2) + (v << 7) + (v << 6) + (v << 4);
			var bCoeff = (u << 9) + (u << 2);
			pixel1[0] = ClipToByte((y1 + bCoeff) >> 8);
			pixel1[1] = ClipToByte((y1 - gCoeff) >> 8);
			pixel1[2] = ClipToByte((y1 + rCoeff) >> 8);
			pixel1[3] = 255;
			pixel2[0] = ClipToByte((y2 + bCoeff) >> 8);
			pixel2[1] = ClipToByte((y2 - gCoeff) >> 8);
			pixel2[2] = ClipToByte((y2 + rCoeff) >> 8);
			pixel2[3] = 255;
		}


		/// <summary>
		/// Convert YUV422 color to packed 32-bit BGRA color based-on BT.709.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv422ToBgra32BT709(int y1, int y2, int u, int v, uint* bgra1, uint* bgra2)
		{
			/*
			 * R = y + 1.5748 * (v - 128)
			 * G = y - 0.1873 * (u - 128) - 0.4681 * (v - 128);
			 * B = y + 1.8556 * (u - 128)
			 * 
			 * [Quantized by 256]
			 * y <<= 8
			 * u -= 128
			 * v -= 128
			 * R = (y + 403 * v) >> 8
			 * G = (y - 48 * u - 120 * v) >> 8
			 * B = (y + 475 * u) >> 8
			 * 
			 * Decompose 403 = 256 + 128 + 16 + 2 + 1
			 * Decompose 48 = 32 + 16
			 * Decompose 120 = 64 + 32 + 16 + 8
			 * Decompose 475 = 256 + 128 + 64 + 16 + 8 + 2 + 1
			 * 
			 * y <<= 8
			 * u -= 128
			 * v -= 128
			 * R = (y + (v << 8) + (v << 7) + (v << 4) + (v << 1) + v) >> 8
			 * G = (y - (u << 5) - (u << 4) - (v << 6) - (v << 5) - (v << 4) - (v << 3)) >> 8
			 * B = (y + (u << 8) + (u << 7) + (u << 6) + (u << 4) + (u << 3) + (u << 1) + u) >> 8
			 */
			var pixel1 = (byte*)bgra1;
			var pixel2 = (byte*)bgra2;
			y1 <<= 8;
			y2 <<= 8;
			u -= 128;
			v -= 128;
			var rCoeff = (v << 8) + (v << 7) + (v << 4) + (v << 1) + v;
			var gCoeff = (u << 5) + (u << 4) + (v << 6) + (v << 5) + (v << 4) + (v << 3);
			var bCoeff = (u << 8) + (u << 7) + (u << 6) + (u << 4) + (u << 3) + (u << 1) + u;
			pixel1[0] = ClipToByte((y1 + bCoeff) >> 8);
			pixel1[1] = ClipToByte((y1 - gCoeff) >> 8);
			pixel1[2] = ClipToByte((y1 + rCoeff) >> 8);
			pixel1[3] = 255;
			pixel2[0] = ClipToByte((y2 + bCoeff) >> 8);
			pixel2[1] = ClipToByte((y2 - gCoeff) >> 8);
			pixel2[2] = ClipToByte((y2 + rCoeff) >> 8);
			pixel2[3] = 255;
		}


		/// <summary>
		/// Convert YUV422 color to packed 64-bit BGRA color based-on BT.601.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv422ToBgra64BT601(int y1, int y2, int u, int v, ulong* bgra1, ulong* bgra2)
		{
			/*
			 * ITU-R formula:
			 * R = clip(Y + 1.402(Cr - 128))
			 * G = clip(Y - 0.344(Cb - 128) - 0.714(Cr - 128))
			 * B = clip(Y + 1.772(Cb - 128))
			 */
			var pixel1 = (ushort*)bgra1;
			var pixel2 = (ushort*)bgra2;
			var fY1 = y1 / 256.0;
			var fY2 = y2 / 256.0;
			var fU = (u / 256.0 - 128);
			var fV = (v / 256.0 - 128);
			pixel1[0] = ClipToUInt16((fY1 + 1.402 * fV) * 256);
			pixel1[1] = ClipToUInt16((fY1 - 0.344 * fU - 0.714 * fV) * 256);
			pixel1[2] = ClipToUInt16((fY1 + 1.772 * fU) * 256);
			pixel1[3] = 65535;
			pixel2[0] = ClipToUInt16((fY2 + 1.402 * fV) * 256);
			pixel2[1] = ClipToUInt16((fY2 - 0.344 * fU - 0.714 * fV) * 256);
			pixel2[2] = ClipToUInt16((fY2 + 1.772 * fU) * 256);
			pixel2[3] = 65535;
		}


		/// <summary>
		/// Convert YUV422 color to packed 64-bit BGRA color based-on BT.656.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv422ToBgra64BT656(int y1, int y2, int u, int v, ulong* bgra1, ulong* bgra2)
		{
			/*
			 * R = 1.164 * (y - 16) + 1.596 * (Cr - 128)
			 * G = 1.164 * (y - 16) - 0.391 * (Cb - 128) - 0.813 * (Cr - 128)
			 * B = 1.164 * (y - 16) + 2.018 * (Cb - 128)
			 */
			var pixel1 = (ushort*)bgra1;
			var pixel2 = (ushort*)bgra2;
			var fY1 = 1.164 * (y1 / 256.0 - 16);
			var fY2 = 1.164 * (y2 / 256.0 - 16);
			var fU = (u / 256.0 - 128);
			var fV = (v / 256.0 - 128);
			var rCoeff = 1.596 * fV;
			var gCoeff = 0.391 * fU + 0.813 * fV;
			var bCoeff = 2.018 * fU;
			pixel1[0] = ClipToUInt16((fY1 + bCoeff) * 256);
			pixel1[1] = ClipToUInt16((fY1 - gCoeff) * 256);
			pixel1[2] = ClipToUInt16((fY1 + rCoeff) * 256);
			pixel1[3] = 65535;
			pixel2[0] = ClipToUInt16((fY2 + bCoeff) * 256);
			pixel2[1] = ClipToUInt16((fY2 - gCoeff) * 256);
			pixel2[2] = ClipToUInt16((fY2 + rCoeff) * 256);
			pixel2[3] = 65535;
		}


		/// <summary>
		/// Convert YUV422 color to packed 64-bit BGRA color based-on BT.709.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv422ToBgra64BT709(int y1, int y2, int u, int v, ulong* bgra1, ulong* bgra2)
		{
			/*
			 * R = y + 1.5748 * (v - 128)
			 * G = y - 0.1873 * (u - 128) - 0.4681 * (v - 128);
			 * B = y + 1.8556 * (u - 128)
			 */
			var pixel1 = (ushort*)bgra1;
			var pixel2 = (ushort*)bgra2;
			var fY1 = (y1 / 256.0);
			var fY2 = (y2 / 256.0);
			var fU = ((u / 256.0) - 128);
			var fV = ((v / 256.0) - 128);
			pixel1[0] = ClipToUInt16((fY1 + 1.8556 * fU) * 256);
			pixel1[1] = ClipToUInt16((fY1 - 0.1873 * fU - 0.4681 * fV) * 256);
			pixel1[2] = ClipToUInt16((fY1 + 1.5748 * fV) * 256);
			pixel1[3] = 65535;
			pixel2[0] = ClipToUInt16((fY2 + 1.8556 * fU) * 256);
			pixel2[1] = ClipToUInt16((fY2 - 0.1873 * fU - 0.4681 * fV) * 256);
			pixel2[2] = ClipToUInt16((fY2 + 1.5748 * fV) * 256);
			pixel2[3] = 65535;
		}


		/// <summary>
		/// Convert YUV444 color to packed 32-bit BGRA color based-on BT.601.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv444ToBgra32BT601(int y, int u, int v, uint* bgra)
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
		/// Convert YUV444 color to packed 32-bit BGRA color based-on BT.656.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv444ToBgra32BT656(int y, int u, int v, uint* bgra)
		{
			/*
			 * R = 1.164 * (y - 16) + 1.596 * (Cr - 128)
			 * G = 1.164 * (y - 16) - 0.391 * (Cb - 128) - 0.813 * (Cr - 128)
			 * B = 1.164 * (y - 16) + 2.018 * (Cb - 128)
			 * 
			 * [Quantized by 256]
			 * y -= 16
			 * u -= 128
			 * v -= 128
			 * R = (298 * y + 409 * v) >> 8
			 * G = (298 * y - 100 * u - 208 * v) >> 8
			 * B = (298 * y + 516 * u) >> 8
			 * 
			 * Decompose 298 = 256 + 32 + 8 + 2
			 * Decompose 409 = 256 + 128 + 16 + 8 + 1
			 * Decompose 100 = 64 + 32 + 4
			 * Decompose 208 = 128 + 64 + 16
			 * Decompose 516 = 512 + 4
			 * 
			 * y -= 16
			 * u -= 128
			 * v -= 128
			 * R = ((y << 8) + (y << 5) + (y << 3) + y + (v << 8) + (v << 7) + (v << 4) + (v << 3) + v) >> 8
			 * G = ((y << 8) + (y << 5) + (y << 3) + y - (u << 6) - (u << 5) - (u << 2) - (v << 7) - (v << 6) - (v << 4)) >> 8
			 * B = ((y << 8) + (y << 5) + (y << 3) + y + (u << 9) + (u << 2)) >> 8
			 */
			var pixel = (byte*)bgra;
			y -= 16;
			y = (y << 8) + (y << 5) + (y << 3) + y;
			u -= 128;
			v -= 128;
			var rCoeff = (v << 8) + (v << 7) + (v << 4) + (v << 3) + v;
			var gCoeff = (u << 6) + (u << 5) + (u << 2) + (v << 7) + (v << 6) + (v << 4);
			var bCoeff = (u << 9) + (u << 2);
			pixel[0] = ClipToByte((y + bCoeff) >> 8);
			pixel[1] = ClipToByte((y - gCoeff) >> 8);
			pixel[2] = ClipToByte((y + rCoeff) >> 8);
			pixel[3] = 255;
		}


		/// <summary>
		/// Convert YUV444 color to packed 32-bit BGRA color based-on BT.709.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv444ToBgra32BT709(int y, int u, int v, uint* bgra)
		{
			/*
			 * R = y + 1.5748 * (v - 128)
			 * G = y - 0.1873 * (u - 128) - 0.4681 * (v - 128);
			 * B = y + 1.8556 * (u - 128)
			 * 
			 * [Quantized by 256]
			 * y <<= 8
			 * u -= 128
			 * v -= 128
			 * R = (y + 403 * v) >> 8
			 * G = (y - 48 * u - 120 * v) >> 8
			 * B = (y + 475 * u) >> 8
			 * 
			 * Decompose 403 = 256 + 128 + 16 + 2 + 1
			 * Decompose 48 = 32 + 16
			 * Decompose 120 = 64 + 32 + 16 + 8
			 * Decompose 475 = 256 + 128 + 64 + 16 + 8 + 2 + 1
			 * 
			 * y <<= 8
			 * u -= 128
			 * v -= 128
			 * R = (y + (v << 8) + (v << 7) + (v << 4) + (v << 1) + v) >> 8
			 * G = (y - (u << 5) - (u << 4) - (v << 6) - (v << 5) - (v << 4) - (v << 3)) >> 8
			 * B = (y + (u << 8) + (u << 7) + (u << 6) + (u << 4) + (u << 3) + (u << 1) + u) >> 8
			 */
			var pixel = (byte*)bgra;
			y <<= 8;
			u -= 128;
			v -= 128;
			pixel[0] = ClipToByte((y + (u << 8) + (u << 7) + (u << 6) + (u << 4) + (u << 3) + (u << 1) + u) >> 8);
			pixel[1] = ClipToByte((y - (u << 5) - (u << 4) - (v << 6) - (v << 5) - (v << 4) - (v << 3)) >> 8);
			pixel[2] = ClipToByte((y + (v << 8) + (v << 7) + (v << 4) + (v << 1) + v) >> 8);
			pixel[3] = 255;
		}


		/// <summary>
		/// Convert YUV444 color to packed 64-bit BGRA color based-on BT.601.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv444ToBgra64BT601(int y, int u, int v, ulong* bgra)
		{
			/*
			 * ITU-R formula:
			 * R = clip(Y + 1.402(Cr - 128))
			 * G = clip(Y - 0.344(Cb - 128) - 0.714(Cr - 128))
			 * B = clip(Y + 1.772(Cb - 128))
			 */
			var pixel = (ushort*)bgra;
			var fY = y / 256.0;
			var fU = (u / 256.0 - 128);
			var fV = (v / 256.0 - 128);
			pixel[0] = ClipToUInt16((fY + 1.402 * fV) * 256);
			pixel[1] = ClipToUInt16((fY - 0.344 * fU - 0.714 * fV) * 256);
			pixel[2] = ClipToUInt16((fY + 1.772 * fU) * 256);
			pixel[3] = 65535;
		}


		/// <summary>
		/// Convert YUV444 color to packed 64-bit BGRA color based-on BT.656.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv444ToBgra64BT656(int y, int u, int v, ulong* bgra)
		{
			/*
			 * R = 1.164 * (y - 16) + 1.596 * (Cr - 128)
			 * G = 1.164 * (y - 16) - 0.391 * (Cb - 128) - 0.813 * (Cr - 128)
			 * B = 1.164 * (y - 16) + 2.018 * (Cb - 128)
			 */
			var pixel = (ushort*)bgra;
			var fY1 = 1.164 * (y / 256.0 - 16);
			var fU = (u / 256.0 - 128);
			var fV = (v / 256.0 - 128);
			var rCoeff = 1.596 * fV;
			var gCoeff = 0.391 * fU + 0.813 * fV;
			var bCoeff = 2.018 * fU;
			pixel[0] = ClipToUInt16((fY1 + bCoeff) * 256);
			pixel[1] = ClipToUInt16((fY1 - gCoeff) * 256);
			pixel[2] = ClipToUInt16((fY1 + rCoeff) * 256);
			pixel[3] = 65535;
		}


		/// <summary>
		/// Convert YUV444 color to packed 64-bit BGRA color based-on BT.709.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void Yuv444ToBgra64BT709(int y, int u, int v, ulong* bgra)
		{
			/*
			 * R = y + 1.5748 * (v - 128)
			 * G = y - 0.1873 * (u - 128) - 0.4681 * (v - 128);
			 * B = y + 1.8556 * (u - 128)
			 */
			var pixel = (ushort*)bgra;
			var fY = (y / 256.0);
			var fU = ((u / 256.0) - 128);
			var fV = ((v / 256.0) - 128);
			pixel[0] = ClipToUInt16((fY + 1.8556 * fU) * 256);
			pixel[1] = ClipToUInt16((fY - 0.1873 * fU - 0.4681 * fV) * 256);
			pixel[2] = ClipToUInt16((fY + 1.5748 * fV) * 256);
			pixel[3] = 65535;
		}
	}
}
