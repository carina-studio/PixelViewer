using CarinaStudio;
using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// Methods for generating color Loookup Table (LUT).
    /// </summary>
    static class ColorLut
    {
        /// <summary>
        /// Build LUT with identity transformation.
        /// </summary>
        /// <param name="targetFormat">Target bitmap format.</param>
        /// <returns>LUT.</returns>
        public static IList<double> BuildIdentity(BitmapFormat targetFormat)
        {
            return (targetFormat switch
            {
                BitmapFormat.Bgra32 => new double[256],
                BitmapFormat.Bgra64 => new double[65536],
                _ => throw new ArgumentException($"Unsupported format: {targetFormat}"),
            }).Also(it => ResetToIdentity(it));
        }


        /// <summary>
        /// Apply gamma transformation.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="gamma">Gamma.</param>
        public static void GammaTransform(IList<double> lut, double gamma) =>
            GammaTransform(lut, 0, lut.Count, gamma);


        // Apply gamma transformation on specific range of LUT.
        static void GammaTransform(IList<double> lut, int start, int end, double gamma)
        {
            // check range
            var count = (double)(end - start - 1);
            if (count < -0.1)
                return;

            // apply
            for (var n = start; n < end; ++n)
            {
                var input = (n - start) / count;
                if (Math.Abs(lut[n]) > 0.0001)
                    lut[n] *= Math.Pow(input, gamma);
            }
        }


        /// <summary>
        /// Apply multiplication.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="factor">Factor.</param>
        public static void Multiply(IList<double> lut, double factor)
        {
            for (var n = lut.Count - 1; n >= 0; --n)
                lut[n] *= factor;
        }


        /// <summary>
        /// Reset LUT to identity transformation.
        /// </summary>
        /// <param name="lut">LUT.</param>
        public static void ResetToIdentity(IList<double> lut)
        {
            for (var n = lut.Count - 1; n >= 0; --n)
                lut[n] = n;
        }


        /// <summary>
        /// Apply translation.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="offset">Offset.</param>
        public static void Translate(IList<double> lut, double offset)
        {
            for (var n = lut.Count - 1; n >= 0; --n)
                lut[n] += offset;
        }
    }
}
