using System.Linq;
using CarinaStudio.Collections;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// RGB color space.
    /// </summary>
    class ColorSpace : IEquatable<ColorSpace>
    {
        /// <summary>
        /// Convert RGB color between color spaces.
        /// </summary>
        public class Converter
        {
            // Fields.
            readonly ColorSpace fromColorSpace;
            readonly bool isIdentical;
            readonly long[] matrix;
            readonly ColorSpace toColorSpace;

            /// <summary>
            /// Initialize new <see cref="Converter"/> instance.
            /// </summary>
            /// <param name="fromColorSpace">Source color space.</param>
            /// <param name="toColorSpace">Target color space.</param>
            public Converter(ColorSpace fromColorSpace, ColorSpace toColorSpace)
            {
                this.fromColorSpace = fromColorSpace;
                this.toColorSpace = toColorSpace;
                this.isIdentical = fromColorSpace.Equals(toColorSpace);
                if (!this.isIdentical)
                {
                    var m1 = this.fromColorSpace.skiaColorSpace.ToColorSpaceXyz();
                    var m2 = this.toColorSpace.skiaColorSpace.ToColorSpaceXyz().Invert();
                    this.matrix = Quantize(SKColorSpaceXyz.Concat(m2, m1));
                }
                else
                    this.matrix = new long[0];
            }

            /// <summary>
            /// Convert RGB color.
            /// </summary>
            /// <param name="r">Normalized R.</param>
            /// <param name="g">Normalized G.</param>
            /// <param name="b">Normalized B.</param>
            /// <returns>Converted normalized RGB color.</returns>
            public (double, double, double) Convert(double r, double g, double b)
            {
                if (this.isIdentical)
                    return (r, g, b);
                var qR = Quantize(r);
                var qG = Quantize(g);
                var qB = Quantize(b);
                if (this.fromColorSpace.hasTransferFunc)
                {
                    qR = this.fromColorSpace.NumericalTransferFromRgb(qR);
                    qG = this.fromColorSpace.NumericalTransferFromRgb(qG);
                    qB = this.fromColorSpace.NumericalTransferFromRgb(qB);
                }
                var m = this.matrix;
                qR = Clip((m[0] * qR + m[1] * qG + m[2] * qB) >> 16);
                qG = Clip((m[3] * qR + m[4] * qG + m[5] * qB) >> 16);
                qB = Clip((m[6] * qR + m[7] * qG + m[8] * qB) >> 16);
                if (this.toColorSpace.hasTransferFunc)
                {
                    qR = this.toColorSpace.NumericalTransferToRgb(qR);
                    qG = this.toColorSpace.NumericalTransferToRgb(qG);
                    qB = this.toColorSpace.NumericalTransferToRgb(qB);
                }
                return (qR / 65536.0, qG / 65536.0, qB / 65536.0);
            }
        }


        /// <summary>
        /// Adobe RGB (1998).
        /// </summary>
        public static readonly ColorSpace AdobeRGB_1998 = new ColorSpace("Adobe-RGB-1998", SKColorSpace.CreateRgb(SKColorSpaceTransferFn.TwoDotTwo, SKColorSpaceXyz.AdobeRgb));
        /// <summary>
        /// ITU-R BT.2020.
        /// </summary>
        public static readonly ColorSpace BT_2020 = new ColorSpace("BT.2020", SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Rec2020, SKColorSpaceXyz.Rec2020));
        /// <summary>
        /// ITU-R BT.601 525-line.
        /// </summary>
        public static readonly ColorSpace BT_601_525Line = new ColorSpace("BT.601-525-line", SKColorSpace.CreateRgb(new SKColorSpaceTransferFn()
            {
                G = 1 / 0.45f,
                A = 1 / 1.099f,
                B = 0.099f / 1.099f,
                C = 1 / 4.5f, 
                D = 0.081f, 
                E = 0.0f, 
                F = 0.0f
            },
            new SKColorSpaceXyz(
                0.3935f, 0.3653f, 0.1917f,
                0.2124f, 0.7011f, 0.0866f,
                0.0187f, 0.1119f, 0.9584f
            )));
        /// <summary>
        /// ITU-R BT.601 625-line.
        /// </summary>
        public static readonly ColorSpace BT_601_625Line = new ColorSpace("BT.601-625-line", SKColorSpace.CreateRgb(new SKColorSpaceTransferFn()
            {
                G = 1 / 0.45f,
                A = 1 / 1.099f,
                B = 0.099f / 1.099f,
                C = 1 / 4.5f, 
                D = 0.081f, 
                E = 0.0f, 
                F = 0.0f
            },
            new SKColorSpaceXyz(
                0.4306f, 0.3415f, 0.1784f,
                0.2220f, 0.7067f, 0.0713f,
                0.0202f, 0.1296f, 0.9393f
            )));
        /// <summary>
        /// DCI-P3 (D63).
        /// </summary>
#pragma warning disable CS0618
        public static readonly ColorSpace DCI_P3 = new ColorSpace("DCI-P3", SKColorSpace.CreateRgb(new SKColorSpaceTransferFn() { G = 2.6f, A = 1.0f }, SKColorSpaceXyz.Dcip3));
#pragma warning restore CS0618
        /// <summary>
        /// Default color space.
        /// </summary>
        public static readonly ColorSpace Default;
        /// <summary>
        /// Display-P3 (P3-D65).
        /// </summary>
        public static readonly ColorSpace Display_P3 = new ColorSpace("Display-P3", SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3));
        /// <summary>
        /// sRGB.
        /// </summary>
        public static readonly ColorSpace Srgb = new ColorSpace("sRGB", SKColorSpace.CreateSrgb());


        // Static fields.
        static readonly Dictionary<string, ColorSpace> builtInColorSpaces = new Dictionary<string, ColorSpace>()
        {
            { AdobeRGB_1998.Name, AdobeRGB_1998 },
            { BT_2020.Name, BT_2020 },
            { BT_601_525Line.Name, BT_601_525Line },
            { BT_601_625Line.Name, BT_601_625Line },
            { DCI_P3.Name, DCI_P3 },
            { Display_P3.Name, Display_P3 },
            { Srgb.Name, Srgb },
        };


        // Fields.
        readonly bool hasTransferFunc;
        readonly long[] matrixFromXyz;
        readonly long[] matrixToXyz;
        readonly SKColorSpaceTransferFn numericalTransferFuncFromRgb;
        readonly SKColorSpaceTransferFn numericalTransferFuncToRgb;
        volatile long[]? numericalTransferTableFromRgb;
        volatile long[]? numericalTransferTableToRgb;
        readonly SKColorSpace skiaColorSpace;
        readonly SKColorSpaceXyz skiaColorSpaceXyz;


        // Static initializer.
        static ColorSpace()
        {
            BuiltInColorSpaces = builtInColorSpaces.Values.ToArray().AsReadOnly();
            Default = Srgb;
        }


        // Constructor.
        ColorSpace(string name, SKColorSpace colorSpace)
        {
            this.skiaColorSpace = colorSpace;
            this.hasTransferFunc = colorSpace.GetNumericalTransferFunction(out this.numericalTransferFuncFromRgb);
            if (this.hasTransferFunc)
                this.numericalTransferFuncToRgb = this.numericalTransferFuncFromRgb.Invert();
            this.skiaColorSpaceXyz = colorSpace.ToColorSpaceXyz();
            this.matrixToXyz = Quantize(this.skiaColorSpaceXyz);
            this.matrixFromXyz = Quantize(this.skiaColorSpaceXyz.Invert());
            this.Name = name;
        }


        /// <summary>
        /// Get all built-in color spaces.
        /// </summary>
        public static IList<ColorSpace> BuiltInColorSpaces { get; }


        // Clip quantized color to valid range.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long Clip(long color)
        {
            if (color < 0)
                return 0;
            if (color > 65536)
                return 65536;
            return color;
        }


        /// <inheritdoc/>
        public bool Equals(ColorSpace? colorSpace) =>
            colorSpace is not null 
            && this.numericalTransferFuncFromRgb.Equals(colorSpace.numericalTransferFuncFromRgb)
            && this.skiaColorSpaceXyz.Equals(colorSpace.skiaColorSpaceXyz);


        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is ColorSpace colorSpace)
                return this.Equals(colorSpace);
            return false;
        }


        /// <summary>
        /// Convert from XYZ D50 color space.
        /// </summary>
        /// <param name="x">X.</param>
        /// <param name="y">Y.</param>
        /// <param name="z">Z.</param>
        /// <returns>Normalized RGB color.</returns>
        public (double, double, double) FromXyz(double x, double y, double z)
        {
            var m = this.matrixFromXyz;
            var qX = (long)(x * 65536 + 0.5);
            var qY = (long)(y * 65536 + 0.5);
            var qZ = (long)(z * 65536 + 0.5);
            var qR = Clip((m[0] * qX + m[1] * qY + m[2] * qZ) >> 16);
            var qG = Clip((m[3] * qX + m[4] * qY + m[5] * qZ) >> 16);
            var qB = Clip((m[6] * qX + m[7] * qY + m[8] * qZ) >> 16);
            if (this.hasTransferFunc)
            {
                qR = this.NumericalTransferToRgb(qR);
                qG = this.NumericalTransferToRgb(qG);
                qB = this.NumericalTransferToRgb(qB);
            }
            return (qR / 65536.0, qG / 65536.0, qB / 65536.0);
        }


        /// <inheritdoc/>
        public override int GetHashCode() => 
            (int)this.matrixToXyz[0];
        

        /// <summary>
        /// Get name of color space.
        /// </summary>
        public string Name { get; }


        // Numerical transfer from RGB.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        long NumericalTransferFromRgb(long color)
        {
            var table = this.numericalTransferTableFromRgb;
            if (table == null)
            {
                table = new long[65537];
                var transferFunc = this.numericalTransferFuncFromRgb;
                for (var i = 65536; i >= 0; --i)
                    table[i] = (long)(transferFunc.Transform(i / 65536f) * 65536 + 0.5);
                this.numericalTransferTableFromRgb = table;
            }
            return table[color];
        }


        // Numerical transfer to RGB.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        long NumericalTransferToRgb(long color)
        {
            var table = this.numericalTransferTableToRgb;
            if (table == null)
            {
                table = new long[65537];
                var transferFunc = this.numericalTransferFuncToRgb;
                for (var i = 65536; i >= 0; --i)
                    table[i] = (long)(transferFunc.Transform(i / 65536f) * 65536 + 0.5);
                this.numericalTransferTableToRgb = table;
            }
            return table[color];
        }


        // Quantize color.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long Quantize(double color)
        {
            if (color < 0)
                return 0;
            if (color > 1)
                return 65536;
            return (long)(color * 65536 + 0.5);
        }


        // Quantize matrix of XYZ color space.
        static long[] Quantize(SKColorSpaceXyz matrix) => new long[9]
        {
            (long)(matrix[0, 0] * 65536 + 0.5), (long)(matrix[1, 0] * 65536 + 0.5), (long)(matrix[2, 0] * 65536 + 0.5),
            (long)(matrix[0, 1] * 65536 + 0.5), (long)(matrix[1, 1] * 65536 + 0.5), (long)(matrix[2, 1] * 65536 + 0.5),
            (long)(matrix[0, 2] * 65536 + 0.5), (long)(matrix[1, 2] * 65536 + 0.5), (long)(matrix[2, 2] * 65536 + 0.5),
        };


        /// <summary>
        /// Convert to L*a*b* D50 color space.
        /// </summary>
        /// <param name="r">Normalized R.</param>
        /// <param name="g">Normalized G.</param>
        /// <param name="b">Normalized B.</param>
        /// <returns>L*a*b* color.</returns>
        public (double, double, double) ToLab(double r, double g, double b)
        {
            var (x, y, z) = this.ToXyz(r, g, b);
            double Convert(double t) // https://en.wikipedia.org/wiki/CIELAB_color_space
            {
                if (t > 0.008856451679036)
                    return Math.Pow(t, 0.3333);
                return (t / 0.128418549346017) + 0.137931034482759;
            }
            var labL = 116 * Convert(y) - 16; // [0, 100]
            var labA = 500 * (Convert(x / 0.964212) - Convert(y)); // [-128, 128]
            var labB = 200 * (Convert(y) - Convert(z / 0.825188)); // [-128, 128]
            return (labL / 100, labA / 128, labB / 128);
        }


        /// <summary>
        /// Convert to XYZ D50 color space.
        /// </summary>
        /// <param name="r">Normalized R.</param>
        /// <param name="g">Normalized G.</param>
        /// <param name="b">Normalized B.</param>
        /// <returns>XYZ color.</returns>
        public (double, double, double) ToXyz(double r, double g, double b)
        {
            var qR = Quantize(r);
            var qG = Quantize(g);
            var qB = Quantize(b);
            if (this.hasTransferFunc)
            {
                qR = this.NumericalTransferFromRgb(qR);
                qG = this.NumericalTransferFromRgb(qG);
                qB = this.NumericalTransferFromRgb(qB);
            }
            var m = this.matrixToXyz;
            return (
                (m[0] * qR + m[1] * qG + m[2] * qB) / 4294967296.0,
                (m[3] * qR + m[4] * qG + m[5] * qB) / 4294967296.0,
                (m[6] * qR + m[7] * qG + m[8] * qB) / 4294967296.0
            );
        }


        /// <summary>
        /// Try get built-in color space by name.
        /// </summary>
        /// <param name="name">Name of color space.</param>
        /// <param name="colorSpace">Found color space, or <see cref="Default"/> if specific color space cannot be found.</param>
        /// <returns>True if specific color space can be found.</returns>
        public static bool TryGetBuiltInColorSpace(string name, out ColorSpace colorSpace)
        {
            if (builtInColorSpaces.TryGetValue(name, out var value))
            {
                colorSpace = value;
                return true;
            }
            colorSpace = Default;
            return false;
        }
    }
}