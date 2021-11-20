using CarinaStudio;
using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// Class to convert from YUV to BGRA.
    /// </summary>
    unsafe class YuvToBgraConverter
    {
        // Static fields.
        static readonly SortedObservableList<YuvToBgraConverter> Converters = new SortedObservableList<YuvToBgraConverter>((x, y) =>
        {
            return x?.Name?.CompareTo(y?.Name) ?? -1;
        });


        /// <summary>
        /// ITU-R BT.2020.
        /// </summary>
        public static readonly YuvToBgraConverter BT_2020 = new YuvToBgraConverter("BT.2020", BitmapColorSpace.BT_2020,
            -16, -128, -128,
            1.1632,
            0, 1.6794,
            -0.187, -0.6497,
            2.1421, 0);
        /// <summary>
        /// ITU-R BT.601.
        /// </summary>
        public static readonly YuvToBgraConverter BT_601 = new YuvToBgraConverter("BT.601", BitmapColorSpace.BT_601,
            0, -128, -128,
            1,
            0, 1.402,
            -0.344, -0.714,
            1.772, 0);
        /// <summary>
        /// ITU-R BT.656.
        /// </summary>
        public static readonly YuvToBgraConverter BT_656 = new YuvToBgraConverter("BT.656", BitmapColorSpace.BT_601,
            -16, -128, -128,
            1.164,
            0, 1.596,
            -0.391, -0.813,
            2.018, 0);
        /// <summary>
        /// ITU-R BT.709.
        /// </summary>
        public static readonly YuvToBgraConverter BT_709 = new YuvToBgraConverter("BT.709", BitmapColorSpace.Srgb,
            0, -128, -128,
            1,
            0, 1.5748,
            -0.1873, -0.4681,
            1.8556, 0);


        /// <summary>
        /// Default converter.
        /// </summary>
        public static readonly YuvToBgraConverter Default = BT_709;


        // Fields.
        readonly int uFactorForB16;
        readonly int uFactorForB8;
        readonly int uFactorForG16;
        readonly int uFactorForG8;
        readonly int uFactorForR16;
        readonly int uFactorForR8;
        readonly int uShift16;
        readonly int uShift8;
        readonly int vFactorForB16;
        readonly int vFactorForB8;
        readonly int vFactorForG16;
        readonly int vFactorForG8;
        readonly int vFactorForR16;
        readonly int vFactorForR8;
        readonly int vShift16;
        readonly int vShift8;
        readonly int yFactor16;
        readonly int yFactor8;
        readonly int yShift16;
        readonly int yShift8;


        // Constructor.
        YuvToBgraConverter(string name, BitmapColorSpace colorSpace,
            int yShift, int uShift, int vShift,
            double yFactor,
            double uFactorForR, double vFactorForR,
            double uFactorForG, double vFactorForG,
            double uFactorForB, double vFactorForB)
        {
            // setup properties
            this.ColorSpace = colorSpace;
            this.Name = name;

            // register
            Converters.Add(this);

            // calculate quantized factors for 8-bit integer
            yShift8 = yShift;
            uShift8 = uShift;
            vShift8 = vShift;
            yFactor8 = (int)(yFactor * 256 + 0.5);
            uFactorForR8 = (int)(uFactorForR * 256 + 0.5);
            vFactorForR8 = (int)(vFactorForR * 256 + 0.5);
            uFactorForG8 = (int)(uFactorForG * 256 + 0.5);
            vFactorForG8 = (int)(vFactorForG * 256 + 0.5);
            uFactorForB8 = (int)(uFactorForB * 256 + 0.5);
            vFactorForB8 = (int)(vFactorForB * 256 + 0.5);

            // calculate quantized factors for 16-bit integer
            yShift16 = yShift << 8;
            uShift16 = uShift << 8;
            vShift16 = vShift << 8;
            yFactor16 = (int)(yFactor * 65536 + 0.5);
            uFactorForR16 = (int)(uFactorForR * 65536 + 0.5);
            vFactorForR16 = (int)(vFactorForR * 65536 + 0.5);
            uFactorForG16 = (int)(uFactorForG * 65536 + 0.5);
            vFactorForG16 = (int)(vFactorForG * 65536 + 0.5);
            uFactorForB16 = (int)(uFactorForB * 65536 + 0.5);
            vFactorForB16 = (int)(vFactorForB * 65536 + 0.5);
        }


        /// <summary>
        /// Get all available converters.
        /// </summary>
        public static IList<YuvToBgraConverter> All { get; } = Converters.AsReadOnly();


        /// <summary>
        /// Color space of RGB converted by this converter.
        /// </summary>
        public BitmapColorSpace ColorSpace { get; }


        /// <summary>
		/// Convert YUV422 color to packed 32-bit BGRA color.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ConvertFromYuv422ToBgra32(byte y1, byte y2, byte u, byte v, uint* bgra1, uint* bgra2)
        {
            var pixel1 = (byte*)bgra1;
            var pixel2 = (byte*)bgra2;
            var y132 = (y1 + this.yShift8) * this.yFactor8;
            var y232 = (y2 + this.yShift8) * this.yFactor8;
            var u32 = (u + this.uShift8);
            var v32 = (v + this.vShift8);
            var rCoeff = this.uFactorForR8 * u32 + this.vFactorForR8 * v32;
            var gCoeff = this.uFactorForG8 * u32 + this.vFactorForG8 * v32;
            var bCoeff = this.uFactorForB8 * u32 + this.vFactorForB8 * v32;
            pixel1[0] = ImageProcessing.ClipToByte((y132 + bCoeff) >> 8);
            pixel1[1] = ImageProcessing.ClipToByte((y132 + gCoeff) >> 8);
            pixel1[2] = ImageProcessing.ClipToByte((y132 + rCoeff) >> 8);
            pixel1[3] = 255;
            pixel2[0] = ImageProcessing.ClipToByte((y232 + bCoeff) >> 8);
            pixel2[1] = ImageProcessing.ClipToByte((y232 + gCoeff) >> 8);
            pixel2[2] = ImageProcessing.ClipToByte((y232 + rCoeff) >> 8);
            pixel2[3] = 255;
        }


        /// <summary>
		/// Convert YUV422 color to packed 64-bit BGRA color.
		/// </summary>
		/// <param name="y1">1st Y.</param>
		/// <param name="y2">2nd Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra1">Address of 1st packed BGRA pixel.</param>
		/// <param name="bgra2">Address of 2nd packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ConvertFromYuv422ToBgra64(ushort y1, ushort y2, ushort u, ushort v, ulong* bgra1, ulong* bgra2)
        {
            var pixel1 = (ushort*)bgra1;
            var pixel2 = (ushort*)bgra2;
            var y164 = ((long)y1 + this.yShift16) * this.yFactor16;
            var y264 = ((long)y2 + this.yShift16) * this.yFactor16;
            var u64 = ((long)u + this.uShift16);
            var v64 = ((long)v + this.uShift16);
            var rCoeff = this.uFactorForR16 * u64 + this.vFactorForR16 * v64;
            var gCoeff = this.uFactorForG16 * u64 + this.vFactorForG16 * v64;
            var bCoeff = this.uFactorForB16 * u64 + this.vFactorForB16 * v64;
            pixel1[0] = ImageProcessing.ClipToUInt16((y164 + bCoeff) >> 16);
            pixel1[1] = ImageProcessing.ClipToUInt16((y164 + gCoeff) >> 16);
            pixel1[2] = ImageProcessing.ClipToUInt16((y164 + rCoeff) >> 16);
            pixel1[3] = 65535;
            pixel2[0] = ImageProcessing.ClipToUInt16((y264 + bCoeff) >> 16);
            pixel2[1] = ImageProcessing.ClipToUInt16((y264 + gCoeff) >> 16);
            pixel2[2] = ImageProcessing.ClipToUInt16((y264 + rCoeff) >> 16);
            pixel2[3] = 65535;
        }


        /// <summary>
		/// Convert YUV444 color to packed 32-bit BGRA color.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ConvertFromYuv444ToBgra32(byte y, byte u, byte v, uint* bgra)
        {
            var pixel = (byte*)bgra;
            var y32 = (y + this.yShift8) * this.yFactor8;
            var u32 = (u + this.uShift8);
            var v32 = (v + this.vShift8);
            var rCoeff = this.uFactorForR8 * u32 + this.vFactorForR8 * v32;
            var gCoeff = this.uFactorForG8 * u32 + this.vFactorForG8 * v32;
            var bCoeff = this.uFactorForB8 * u32 + this.vFactorForB8 * v32;
            pixel[0] = ImageProcessing.ClipToByte((y32 + bCoeff) >> 8);
            pixel[1] = ImageProcessing.ClipToByte((y32 + gCoeff) >> 8);
            pixel[2] = ImageProcessing.ClipToByte((y32 + rCoeff) >> 8);
            pixel[3] = 255;
        }


        /// <summary>
		/// Convert YUV444 color to packed 64-bit BGRA color.
		/// </summary>
		/// <param name="y">Y.</param>
		/// <param name="u">U.</param>
		/// <param name="v">V.</param>
		/// <param name="bgra">Address of packed BGRA pixel.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ConvertFromYuv444ToBgra64(ushort y, ushort u, ushort v, ulong* bgra)
        {
            var pixel1 = (ushort*)bgra;
            var y64 = ((long)y + this.yShift16) * this.yFactor16;
            var u64 = ((long)u + this.uShift16);
            var v64 = ((long)v + this.uShift16);
            var rCoeff = this.uFactorForR16 * u64 + this.vFactorForR16 * v64;
            var gCoeff = this.uFactorForG16 * u64 + this.vFactorForG16 * v64;
            var bCoeff = this.uFactorForB16 * u64 + this.vFactorForB16 * v64;
            pixel1[0] = ImageProcessing.ClipToUInt16((y64 + bCoeff) >> 16);
            pixel1[1] = ImageProcessing.ClipToUInt16((y64 + gCoeff) >> 16);
            pixel1[2] = ImageProcessing.ClipToUInt16((y64 + rCoeff) >> 16);
            pixel1[3] = 65535;
        }


        /// <summary>
        /// Get name of conversion.
        /// </summary>
        public string Name { get; }


        /// <inheritdoc/>
        public override string ToString() => this.Name;


        /// <summary>
        /// Try get converter by name.
        /// </summary>
        /// <param name="name">Name of converter.</param>
        /// <param name="converter">Converter with specific name, or <see cref="Default"/> is no converter matches.</param>
        /// <returns>True if converter found for specific name.</returns>
        public static bool TryGetByName(string? name, out YuvToBgraConverter converter)
        {
            if (name != null)
            {
                foreach (var candidate in Converters)
                {
                    if (candidate.Name == name)
                    {
                        converter = candidate;
                        return true;
                    }
                }
            }
            converter = Default;
            return false;
        }
    }
}
