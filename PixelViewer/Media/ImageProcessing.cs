using Carina.PixelViewer.Runtime.InteropServices;
using CarinaStudio;
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
		static readonly double[] colorNormalizingTable16 = new double[65536].Also(it =>
		{
			var d = it.Length - 1;
			for (var n = d; n >= 0; --n)
				it[n] = (double)n / d;
		});
		static readonly double[] colorNormalizingTable8 = new double[256].Also(it =>
		{
			var d = it.Length - 1;
			for (var n = d; n >= 0; --n)
				it[n] = (double)n / d;
		});
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
		/// Denormalize and pack B/G/R/A to 32-bit BGRA for Big-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="a">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint DenormalizeAndPackBgra32BE(double b, double g, double r, double a)
		{
			var b8 = ClipToByte(b * 255);
			var g8 = ClipToByte(g * 255);
			var r8 = ClipToByte(r * 255);
			var a8 = ClipToByte(a * 255);
			return (uint)((b8 << 24) | (g8 << 16) | (r8 << 8) | a8);
		}


		/// <summary>
		/// Denormalize and pack B/G/R/A to 32-bit BGRA for Little-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="a">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint DenormalizeAndPackBgra32LE(double b, double g, double r, double a)
		{
			var b8 = ClipToByte(b * 255);
			var g8 = ClipToByte(g * 255);
			var r8 = ClipToByte(r * 255);
			var a8 = ClipToByte(a * 255);
			return (uint)((a8 << 24) | (r8 << 16) | (g8 << 8) | b8);
		}


		/// <summary>
		/// Denormalize and pack B/G/R/A to 64-bit BGRA for Big-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="a">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong DenormalizeAndPackBgra64BE(double b, double g, double r, double a)
		{
			var b16 = ClipToUInt16(b * 65535);
			var g16 = ClipToUInt16(g * 65535);
			var r16 = ClipToUInt16(r * 65535);
			var a16 = ClipToUInt16(a * 65535);
			return ((ulong)b16 << 48) | ((ulong)g16 << 32) | ((ulong)r16 << 16) | a16;
		}


		/// <summary>
		/// Denormalize and pack B/G/R/A to 64-bit BGRA for Little-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="a">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong DenormalizeAndPackBgra64LE(double b, double g, double r, double a)
		{
			var b16 = ClipToUInt16(b * 65535);
			var g16 = ClipToUInt16(g * 65535);
			var r16 = ClipToUInt16(r * 65535);
			var a16 = ClipToUInt16(a * 65535);
			return ((ulong)a16 << 48) | ((ulong)r16 << 32) | ((ulong)g16 << 16) | b16;
		}


		/// <summary>
		/// Denormalize and pack B/G/R/X to 32-bit BGRA for Big-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="x">X.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint DenormalizeAndPackBgrx32BE(double b, double g, double r, byte x)
		{
			var b8 = ClipToByte(b * 255);
			var g8 = ClipToByte(g * 255);
			var r8 = ClipToByte(r * 255);
			return (uint)((b8 << 24) | (g8 << 16) | (r8 << 8) | x);
		}


		/// <summary>
		/// Denormalize and pack B/G/R/X to 32-bit BGRA for Little-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="x">A.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint DenormalizeAndPackBgrx32LE(double b, double g, double r, byte x)
		{
			var b8 = ClipToByte(b * 255);
			var g8 = ClipToByte(g * 255);
			var r8 = ClipToByte(r * 255);
			return (uint)((x << 24) | (r8 << 16) | (g8 << 8) | b8);
		}


		/// <summary>
		/// Denormalize and pack B/G/R/X to 64-bit BGRA for Big-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="x">X.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong DenormalizeAndPackBgrx64BE(double b, double g, double r, ushort x)
		{
			var b16 = ClipToUInt16(b * 65535);
			var g16 = ClipToUInt16(g * 65535);
			var r16 = ClipToUInt16(r * 65535);
			return ((ulong)b16 << 48) | ((ulong)g16 << 32) | ((ulong)r16 << 16) | x;
		}


		/// <summary>
		/// Denormalize and pack B/G/R/X to 64-bit BGRA for Little-Endian system.
		/// </summary>
		/// <param name="b">B.</param>
		/// <param name="g">G.</param>
		/// <param name="r">R.</param>
		/// <param name="x">X.</param>
		/// <returns>Packed BGRA.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong DenormalizeAndPackBgrx64LE(double b, double g, double r, ushort x)
		{
			var b16 = ClipToUInt16(b * 65535);
			var g16 = ClipToUInt16(g * 65535);
			var r16 = ClipToUInt16(r * 65535);
			return ((ulong)x << 48) | ((ulong)r16 << 32) | ((ulong)g16 << 16) | b16;
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


		// Convert from RGB to Luminance based-on ITU-R BT.709.
		static double RgbToLuminanceBT709(double r, double g, double b) =>
			(0.2126 * r) + (0.7152 * g) + (0.0722 * b);


		/// <summary>
		/// Select property function to denormalize and pack B/G/R/A into 32-bit integer.
		/// </summary>
		/// <returns>Pointer to denormalizing and packing function.</returns>
		public static delegate*<double, double, double, double, uint> SelectBgra32DenormalizingAndPacking()
		{
			if (IsLittleEndian)
				return &DenormalizeAndPackBgra32LE;
			return &DenormalizeAndPackBgra32BE;
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
		/// Select property function to unpack 32-bit integer into normalized B/G/R/A.
		/// </summary>
		/// <returns>Pointer to unpacking and normalizing function.</returns>
		public static delegate*<uint, double*, double*, double*, double*, void> SelectBgra32UnpackingAndNormalizing()
		{
			if (IsLittleEndian)
				return &UnpackAndNormalizeBgra32LE;
			return &UnpackAndNormalizeBgra32BE;
		}


		/// <summary>
		/// Select property function to denormalize and pack B/G/R/A into 64-bit integer.
		/// </summary>
		/// <returns>Pointer to denormalizing and packing function.</returns>
		public static delegate*<double, double, double, double, ulong> SelectBgra64DenormalizingAndPacking()
		{
			if (IsLittleEndian)
				return &DenormalizeAndPackBgra64LE;
			return &DenormalizeAndPackBgra64BE;
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
		/// Select property function to unpack 64-bit integer into normalized B/G/R/A.
		/// </summary>
		/// <returns>Pointer to unpacking and normalizing function.</returns>
		public static delegate*<ulong, double*, double*, double*, double*, void> SelectBgra64UnpackingAndNormalizing()
		{
			if (IsLittleEndian)
				return &UnpackAndNormalizeBgra64LE;
			return &UnpackAndNormalizeBgra64BE;
		}


		/// <summary>
		/// Select property function to denormalize and pack B/G/R/X into 32-bit integer.
		/// </summary>
		/// <returns>Pointer to denormalizing and packing function.</returns>
		public static delegate*<double, double, double, byte, uint> SelectBgrx32DenormalizingAndPacking()
		{
			if (IsLittleEndian)
				return &DenormalizeAndPackBgrx32LE;
			return &DenormalizeAndPackBgrx32BE;
		}


		/// <summary>
		/// Select property function to unpack 32-bit integer into normalized B/G/R/X.
		/// </summary>
		/// <returns>Pointer to unpacking and normalizing function.</returns>
		public static delegate*<uint, double*, double*, double*, byte*, void> SelectBgrx32UnpackingAndNormalizing()
		{
			if (IsLittleEndian)
				return &UnpackAndNormalizeBgrx32LE;
			return &UnpackAndNormalizeBgrx32BE;
		}


		/// <summary>
		/// Select property function to denormalize and pack B/G/R/X into 64-bit integer.
		/// </summary>
		/// <returns>Pointer to denormalizing and packing function.</returns>
		public static delegate*<double, double, double, ushort, ulong> SelectBgrx64DenormalizingAndPacking()
		{
			if (IsLittleEndian)
				return &DenormalizeAndPackBgrx64LE;
			return &DenormalizeAndPackBgrx64BE;
		}


		/// <summary>
		/// Select property function to unpack 64-bit integer into normalized B/G/R/X.
		/// </summary>
		/// <returns>Pointer to unpacking and normalizing function.</returns>
		public static delegate*<ulong, double*, double*, double*, ushort*, void> SelectBgrx64UnpackingAndNormalizing()
		{
			if (IsLittleEndian)
				return &UnpackAndNormalizeBgrx64LE;
			return &UnpackAndNormalizeBgrx64BE;
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
		/// Select proper function to convert from RGB to luminance.
		/// </summary>
		/// <returns>RGB to luminance conversion function.</returns>
		public static delegate*<double, double, double, double> SelectRgbToLuminanceConversion() => &RgbToLuminanceBT709;


		/// <summary>
		/// Unpack 32-bit integer into normalized B/G/R/A for Big-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked and normalized B.</param>
		/// <param name="g">Unpacked and normalized G.</param>
		/// <param name="r">Unpacked and normalized R.</param>
		/// <param name="a">Unpacked and normalized A.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackAndNormalizeBgra32BE(uint bgra, double* b, double* g, double* r, double* a)
        {
			*b = colorNormalizingTable8[(bgra >> 24)];
			*g = colorNormalizingTable8[((bgra >> 16) & 0xff)];
			*r = colorNormalizingTable8[((bgra >> 8) & 0xff)];
			*a = colorNormalizingTable8[(bgra & 0xff)];
		}


		/// <summary>
		/// Unpack 32-bit integer into normalized B/G/R/A for Little-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked and normalized B.</param>
		/// <param name="g">Unpacked and normalized G.</param>
		/// <param name="r">Unpacked and normalized R.</param>
		/// <param name="a">Unpacked and normalized A.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackAndNormalizeBgra32LE(uint bgra, double* b, double* g, double* r, double* a)
		{
			*a = colorNormalizingTable8[(bgra >> 24)];
			*r = colorNormalizingTable8[((bgra >> 16) & 0xff)];
			*g = colorNormalizingTable8[((bgra >> 8) & 0xff)];
			*b = colorNormalizingTable8[(bgra & 0xff)];
		}


		/// <summary>
		/// Unpack 64-bit integer into normalized B/G/R/A for Big-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked and normalized B.</param>
		/// <param name="g">Unpacked and normalized G.</param>
		/// <param name="r">Unpacked and normalized R.</param>
		/// <param name="a">Unpacked and normalized A.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackAndNormalizeBgra64BE(ulong bgra, double* b, double* g, double* r, double* a)
		{
			*b = colorNormalizingTable16[(bgra >> 48)];
			*g = colorNormalizingTable16[((bgra >> 32) & 0xffff)];
			*r = colorNormalizingTable16[((bgra >> 16) & 0xffff)];
			*a = colorNormalizingTable16[(bgra & 0xffff)];
		}


		/// <summary>
		/// Unpack 64-bit integer into normalized B/G/R/A for Little-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked and normalized B.</param>
		/// <param name="g">Unpacked and normalized G.</param>
		/// <param name="r">Unpacked and normalized R.</param>
		/// <param name="a">Unpacked and normalized A.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackAndNormalizeBgra64LE(ulong bgra, double* b, double* g, double* r, double* a)
		{
			*a = colorNormalizingTable16[(bgra >> 48)];
			*r = colorNormalizingTable16[((bgra >> 32) & 0xffff)];
			*g = colorNormalizingTable16[((bgra >> 16) & 0xffff)];
			*b = colorNormalizingTable16[(bgra & 0xffff)];
		}


		/// <summary>
		/// Unpack 32-bit integer into normalized B/G/R/X for Big-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked and normalized B.</param>
		/// <param name="g">Unpacked and normalized G.</param>
		/// <param name="r">Unpacked and normalized R.</param>
		/// <param name="x">Unpacked X.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackAndNormalizeBgrx32BE(uint bgra, double* b, double* g, double* r, byte* x)
		{
			*b = colorNormalizingTable8[(bgra >> 24)];
			*g = colorNormalizingTable8[((bgra >> 16) & 0xff)];
			*r = colorNormalizingTable8[((bgra >> 8) & 0xff)];
			*x = (byte)(bgra & 0xff);
		}


		/// <summary>
		/// Unpack 32-bit integer into normalized B/G/R/X for Little-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked and normalized B.</param>
		/// <param name="g">Unpacked and normalized G.</param>
		/// <param name="r">Unpacked and normalized R.</param>
		/// <param name="x">Unpacked X.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackAndNormalizeBgrx32LE(uint bgra, double* b, double* g, double* r, byte* x)
		{
			*x = (byte)(bgra >> 24);
			*r = colorNormalizingTable8[((bgra >> 16) & 0xff)];
			*g = colorNormalizingTable8[((bgra >> 8) & 0xff)];
			*b = colorNormalizingTable8[(bgra & 0xff)];
		}


		/// <summary>
		/// Unpack 64-bit integer into normalized B/G/R/X for Big-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked and normalized B.</param>
		/// <param name="g">Unpacked and normalized G.</param>
		/// <param name="r">Unpacked and normalized R.</param>
		/// <param name="x">Unpacked X.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackAndNormalizeBgrx64BE(ulong bgra, double* b, double* g, double* r, ushort* x)
		{
			*b = colorNormalizingTable16[(bgra >> 48)];
			*g = colorNormalizingTable16[((bgra >> 32) & 0xffff)];
			*r = colorNormalizingTable16[((bgra >> 16) & 0xffff)];
			*x = (ushort)(bgra & 0xffff);
		}


		/// <summary>
		/// Unpack 64-bit integer into normalized B/G/R/X for Little-Endian system.
		/// </summary>
		/// <param name="bgra">Packed BGRA.</param>
		/// <param name="b">Unpacked and normalized B.</param>
		/// <param name="g">Unpacked and normalized G.</param>
		/// <param name="r">Unpacked and normalized R.</param>
		/// <param name="x">Unpacked X.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnpackAndNormalizeBgrx64LE(ulong bgra, double* b, double* g, double* r, ushort* x)
		{
			*x = (ushort)(bgra >> 48);
			*r = colorNormalizingTable16[((bgra >> 32) & 0xffff)];
			*g = colorNormalizingTable16[((bgra >> 16) & 0xffff)];
			*b = colorNormalizingTable16[(bgra & 0xffff)];
		}


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
	}
}
