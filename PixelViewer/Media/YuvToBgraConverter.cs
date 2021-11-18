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
        public static readonly YuvToBgraConverter BT_2020 = new YuvToBgraConverter("BT.2020",
            -16, -128, -128,
            1.1632,
            0, 1.6794,
            -0.187, -0.6497,
            2.1421, 0);
        /// <summary>
        /// ITU-R BT.601.
        /// </summary>
        public static readonly YuvToBgraConverter BT_601 = new YuvToBgraConverter("BT.601",
            0, -128, -128,
            1,
            0, 1.402,
            -0.344, -0.714,
            1.772, 0);
        /// <summary>
        /// ITU-R BT.656.
        /// </summary>
        public static readonly YuvToBgraConverter BT_656 = new YuvToBgraConverter("BT.656",
            -16, -128, -128,
            1.164,
            0, 1.596,
            -0.391, -0.813,
            2.018, 0);
        /// <summary>
        /// ITU-R BT.709.
        /// </summary>
        public static readonly YuvToBgraConverter BT_709 = new YuvToBgraConverter("BT.709",
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
        readonly int uFactorForB8;
        readonly int uFactorForG8;
        readonly int uFactorForR8;
        readonly int uShift8;
        readonly int vFactorForB8;
        readonly int vFactorForG8;
        readonly int vFactorForR8;
        readonly int vShift8;
        readonly int yFactor8;
        readonly int yShift8;


        // Constructor.
        YuvToBgraConverter(string name, 
            int yShift, int uShift, int vShift,
            double yFactor,
            double uFactorForR, double vFactorForR,
            double uFactorForG, double vFactorForG,
            double uFactorForB, double vFactorForB)
        {
            // setup name
            this.Name = name;
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
        }


        /// <summary>
        /// Get all available converters.
        /// </summary>
        public static IList<YuvToBgraConverter> All { get; } = Converters.AsReadOnly();


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
        public void ConvertFromYuv422ToBgra32(int y1, int y2, int u, int v, uint* bgra1, uint* bgra2)
        {
            var pixel1 = (byte*)bgra1;
            var pixel2 = (byte*)bgra2;
            y1 = (y1 + this.yShift8) * this.yFactor8;
            y2 = (y2 + this.yShift8) * this.yFactor8;
            u += this.uShift8;
            v += this.vShift8;
            var rCoeff = this.uFactorForR8 * u + this.vFactorForR8 * v;
            var gCoeff = this.uFactorForG8 * u + this.vFactorForG8 * v;
            var bCoeff = this.uFactorForB8 * u + this.vFactorForB8 * v;
            pixel1[0] = ImageProcessing.ClipToByte((y1 + bCoeff) >> 8);
            pixel1[1] = ImageProcessing.ClipToByte((y1 + gCoeff) >> 8);
            pixel1[2] = ImageProcessing.ClipToByte((y1 + rCoeff) >> 8);
            pixel1[3] = 255;
            pixel2[0] = ImageProcessing.ClipToByte((y2 + bCoeff) >> 8);
            pixel2[1] = ImageProcessing.ClipToByte((y2 + gCoeff) >> 8);
            pixel2[2] = ImageProcessing.ClipToByte((y2 + rCoeff) >> 8);
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
        public void ConvertFromYuv422ToBgra64(int y1, int y2, int u, int v, ulong* bgra1, ulong* bgra2)
        {
            var pixel1 = (ushort*)bgra1;
            var pixel2 = (ushort*)bgra2;
            var fY1 = (y1 / 256.0 + this.yShift8) * this.yFactor8;
            var fY2 = (y2 / 256.0 + this.yShift8) * this.yFactor8;
            var fU = (u / 256.0 + this.uShift8);
            var fV = (v / 256.0 + this.vShift8);
            var rCoeff = this.uFactorForR8 * fU + this.vFactorForR8 * fV;
            var gCoeff = this.uFactorForG8 * fU + this.vFactorForG8 * fV;
            var bCoeff = this.uFactorForB8 * fU + this.vFactorForB8 * fV;
            pixel1[0] = ImageProcessing.ClipToUInt16(fY1 + bCoeff);
            pixel1[1] = ImageProcessing.ClipToUInt16(fY1 + gCoeff);
            pixel1[2] = ImageProcessing.ClipToUInt16(fY1 + rCoeff);
            pixel1[3] = 65535;
            pixel2[0] = ImageProcessing.ClipToUInt16(fY2 + bCoeff);
            pixel2[1] = ImageProcessing.ClipToUInt16(fY2 + gCoeff);
            pixel2[2] = ImageProcessing.ClipToUInt16(fY2 + rCoeff);
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
        public void ConvertFromYuv444ToBgra32(int y, int u, int v, uint* bgra)
        {
            var pixel = (byte*)bgra;
            y = (y + this.yShift8) * this.yFactor8;
            u += this.uShift8;
            v += this.vShift8;
            var rCoeff = this.uFactorForR8 * u + this.vFactorForR8 * v;
            var gCoeff = this.uFactorForG8 * u + this.vFactorForG8 * v;
            var bCoeff = this.uFactorForB8 * u + this.vFactorForB8 * v;
            pixel[0] = ImageProcessing.ClipToByte((y + bCoeff) >> 8);
            pixel[1] = ImageProcessing.ClipToByte((y + gCoeff) >> 8);
            pixel[2] = ImageProcessing.ClipToByte((y + rCoeff) >> 8);
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
        public void ConvertFromYuv444ToBgra64(int y, int u, int v, ulong* bgra)
        {
            var pixel = (ushort*)bgra;
            var fY = (y / 256.0 + this.yShift8) * this.yFactor8;
            var fU = (u / 256.0 + this.uShift8);
            var fV = (v / 256.0 + this.vShift8);
            var rCoeff = this.uFactorForR8 * fU + this.vFactorForR8 * fV;
            var gCoeff = this.uFactorForG8 * fU + this.vFactorForG8 * fV;
            var bCoeff = this.uFactorForB8 * fU + this.vFactorForB8 * fV;
            pixel[0] = ImageProcessing.ClipToUInt16(fY + bCoeff);
            pixel[1] = ImageProcessing.ClipToUInt16(fY + gCoeff);
            pixel[2] = ImageProcessing.ClipToUInt16(fY + rCoeff);
            pixel[3] = 255;
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
        public static bool TryGetByName(string name, out YuvToBgraConverter converter)
        {
            foreach (var candidate in Converters)
            {
                if (candidate.Name == name)
                {
                    converter = candidate;
                    return true;
                }
            }
            converter = Default;
            return false;
        }
    }
}
