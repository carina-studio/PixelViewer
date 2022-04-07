using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        public static readonly YuvToBgraConverter BT_2020 = new YuvToBgraConverter("BT.2020", ColorSpace.BT_2020,
            -16, -128, -128,
            1.1632,
            0, 1.6794,
            -0.187, -0.6497,
            2.1421, 0);
        
        /// <summary>
        /// ITU-R BT.601.
        /// </summary>
        public static readonly YuvToBgraConverter BT_601 = new YuvToBgraConverter("BT.601", ColorSpace.BT_601_625Line,
            0, -128, -128,
            1,
            0, 1.402,
            -0.344, -0.714,
            1.772, 0);
        
        /// <summary>
        /// ITU-R BT.656.
        /// </summary>
        public static readonly YuvToBgraConverter BT_656 = new YuvToBgraConverter("BT.656", ColorSpace.BT_601_625Line,
            -16, -128, -128,
            1.164,
            0, 1.596,
            -0.391, -0.813,
            2.018, 0);
        
        /// <summary>
        /// ITU-R BT.709.
        /// </summary>
        public static readonly YuvToBgraConverter BT_709 = new YuvToBgraConverter("BT.709", ColorSpace.Srgb,
            0, -128, -128,
            1,
            0, 1.5748,
            -0.1873, -0.4681,
            1.8556, 0);


        /// <summary>
        /// Default converter.
        /// </summary>
        public static readonly YuvToBgraConverter Default = BT_709;


        // Static fields.
        static readonly delegate*<ushort, ushort, ushort, ushort, ulong> PackingFunction16 = ImageProcessing.SelectBgra64Packing();
        static readonly delegate*<byte, byte, byte, byte, uint> PackingFunction8 = ImageProcessing.SelectBgra32Packing();


        // Fields.
        readonly long* bCoeff16Table1 = (long*)NativeMemory.Alloc(sizeof(long) * 65536);
        readonly long* bCoeff16Table2 = (long*)NativeMemory.Alloc(sizeof(long) * 65536);
        readonly long* bCoeff8Table1 = (long*)NativeMemory.Alloc(sizeof(long) * 256);
        readonly long* bCoeff8Table2 = (long*)NativeMemory.Alloc(sizeof(long) * 256);
        readonly long* gCoeff16Table1 = (long*)NativeMemory.Alloc(sizeof(long) * 65536);
        readonly long* gCoeff16Table2 = (long*)NativeMemory.Alloc(sizeof(long) * 65536);
        readonly long* gCoeff8Table1 = (long*)NativeMemory.Alloc(sizeof(long) * 256);
        readonly long* gCoeff8Table2 = (long*)NativeMemory.Alloc(sizeof(long) * 256);
        readonly long* rCoeff16Table1 = (long*)NativeMemory.Alloc(sizeof(long) * 65536);
        readonly long* rCoeff16Table2 = (long*)NativeMemory.Alloc(sizeof(long) * 65536);
        readonly long* rCoeff8Table1 = (long*)NativeMemory.Alloc(sizeof(long) * 256);
        readonly long* rCoeff8Table2 = (long*)NativeMemory.Alloc(sizeof(long) * 256);
        readonly long* yCoeff16Table = (long*)NativeMemory.Alloc(sizeof(long) * 65536);
        readonly long* yCoeff8Table = (long*)NativeMemory.Alloc(sizeof(long) * 256);


        // Constructor.
        YuvToBgraConverter(string name, ColorSpace colorSpace,
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
            var yShift8 = yShift;
            var uShift8 = uShift;
            var vShift8 = vShift;
            var yFactor8 = (long)(yFactor * 256 + 0.5);
            var uFactorForR8 = (long)(uFactorForR * 256 + 0.5);
            var vFactorForR8 = (long)(vFactorForR * 256 + 0.5);
            var uFactorForG8 = (long)(uFactorForG * 256 + 0.5);
            var vFactorForG8 = (long)(vFactorForG * 256 + 0.5);
            var uFactorForB8 = (long)(uFactorForB * 256 + 0.5);
            var vFactorForB8 = (long)(vFactorForB * 256 + 0.5);

            // calculate quantized factors for 16-bit integer
            var yShift16 = yShift << 8;
            var uShift16 = uShift << 8;
            var vShift16 = vShift << 8;
            var yFactor16 = (long)(yFactor * 65536 + 0.5);
            var uFactorForR16 = (long)(uFactorForR * 65536 + 0.5);
            var vFactorForR16 = (long)(vFactorForR * 65536 + 0.5);
            var uFactorForG16 = (long)(uFactorForG * 65536 + 0.5);
            var vFactorForG16 = (long)(vFactorForG * 65536 + 0.5);
            var uFactorForB16 = (long)(uFactorForB * 65536 + 0.5);
            var vFactorForB16 = (long)(vFactorForB * 65536 + 0.5);

            // pre-calculate coefficient for 8-bit integer
            unsafe
            {
                var rCoeffTable1 = this.rCoeff8Table1;
                var rCoeffTable2 = this.rCoeff8Table2;
                var gCoeffTable1 = this.gCoeff8Table1;
                var gCoeffTable2 = this.gCoeff8Table2;
                var bCoeffTable1 = this.bCoeff8Table1;
                var bCoeffTable2 = this.bCoeff8Table2;
                for (var n = 255L; n >= 0; --n)
                {
                    var u32 = (n + uShift8);
                    var v32 = (n + uShift8);
                    yCoeff8Table[n] = (n + yShift8) * yFactor8;
                    rCoeffTable1[n] = uFactorForR8 * u32;
                    rCoeffTable2[n] = vFactorForR8 * v32;
                    gCoeffTable1[n] = uFactorForG8 * u32;
                    gCoeffTable2[n] = vFactorForG8 * v32;
                    bCoeffTable1[n] = uFactorForB8 * u32;
                    bCoeffTable2[n] = vFactorForB8 * v32;
                }
            }

            // pre-calculate coefficient for 16-bit integer
            unsafe
            {
                var rCoeffTable1 = this.rCoeff16Table1;
                var rCoeffTable2 = this.rCoeff16Table2;
                var gCoeffTable1 = this.gCoeff16Table1;
                var gCoeffTable2 = this.gCoeff16Table2;
                var bCoeffTable1 = this.bCoeff16Table1;
                var bCoeffTable2 = this.bCoeff16Table2;
                for (var n = 65535L; n >= 0; --n)
                {
                    var u64 = (n + uShift16);
                    var v64 = (n + uShift16);
                    yCoeff16Table[n] = (n + yShift16) * yFactor16;
                    rCoeffTable1[n] = uFactorForR16 * u64;
                    rCoeffTable2[n] = vFactorForR16 * v64;
                    gCoeffTable1[n] = uFactorForG16 * u64;
                    gCoeffTable2[n] = vFactorForG16 * v64;
                    bCoeffTable1[n] = uFactorForB16 * u64;
                    bCoeffTable2[n] = vFactorForB16 * v64;
                }
            }
        }


        /// <summary>
        /// Get all available converters.
        /// </summary>
        public static IList<YuvToBgraConverter> All { get; } = Converters.AsReadOnly();


        /// <summary>
        /// Color space of RGB converted by this converter.
        /// </summary>
        public ColorSpace ColorSpace { get; }


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
            var y132 = this.yCoeff8Table[y1];
            var y232 = this.yCoeff8Table[y2];
            var rCoeff = this.rCoeff8Table1[u] + this.rCoeff8Table2[v];
            var gCoeff = this.gCoeff8Table1[u] + this.gCoeff8Table2[v];
            var bCoeff = this.bCoeff8Table1[u] + this.bCoeff8Table2[v];
            *bgra1 = PackingFunction8(
                ImageProcessing.ClipToByte((y132 + bCoeff) >> 8),
                ImageProcessing.ClipToByte((y132 + gCoeff) >> 8),
                ImageProcessing.ClipToByte((y132 + rCoeff) >> 8),
                255
            );
            *bgra2 = PackingFunction8(
                ImageProcessing.ClipToByte((y232 + bCoeff) >> 8),
                ImageProcessing.ClipToByte((y232 + gCoeff) >> 8),
                ImageProcessing.ClipToByte((y232 + rCoeff) >> 8),
                255
            );
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
            var y164 = this.yCoeff16Table[y1];
            var y264 = this.yCoeff16Table[y2];
            var rCoeff = this.rCoeff16Table1[u] + this.rCoeff16Table2[v];
            var gCoeff = this.gCoeff16Table1[u] + this.gCoeff16Table2[v];
            var bCoeff = this.bCoeff16Table1[u] + this.bCoeff16Table2[v];
            *bgra1 = PackingFunction16(
                ImageProcessing.ClipToUInt16((y164 + bCoeff) >> 16),
                ImageProcessing.ClipToUInt16((y164 + gCoeff) >> 16),
                ImageProcessing.ClipToUInt16((y164 + rCoeff) >> 16),
                65535
            );
            *bgra2 = PackingFunction16(
                ImageProcessing.ClipToUInt16((y264 + bCoeff) >> 16),
                ImageProcessing.ClipToUInt16((y264 + gCoeff) >> 16),
                ImageProcessing.ClipToUInt16((y264 + rCoeff) >> 16),
                65535
            );
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
            var y32 = this.yCoeff8Table[y];
            var rCoeff = this.rCoeff8Table1[u] + this.rCoeff8Table2[v];
            var gCoeff = this.gCoeff8Table1[u] + this.gCoeff8Table2[v];
            var bCoeff = this.bCoeff8Table1[u] + this.bCoeff8Table2[v];
            *bgra = PackingFunction8(
                ImageProcessing.ClipToByte((y32 + bCoeff) >> 8),
                ImageProcessing.ClipToByte((y32 + gCoeff) >> 8),
                ImageProcessing.ClipToByte((y32 + rCoeff) >> 8),
                255
            );
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
            var y64 = this.yCoeff16Table[y];
            var rCoeff = this.rCoeff16Table1[u] + this.rCoeff16Table2[v];
            var gCoeff = this.gCoeff16Table1[u] + this.gCoeff16Table2[v];
            var bCoeff = this.bCoeff16Table1[u] + this.bCoeff16Table2[v];
            *bgra = PackingFunction16(
                ImageProcessing.ClipToUInt16((y64 + bCoeff) >> 16),
                ImageProcessing.ClipToUInt16((y64 + gCoeff) >> 16),
                ImageProcessing.ClipToUInt16((y64 + rCoeff) >> 16),
                65535
            );
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
