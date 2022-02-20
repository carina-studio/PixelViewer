using System.Runtime.CompilerServices;
using CarinaStudio;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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


        /// <summary>
        /// Apply gamma transformation on specific range of LUT.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="start">Inclusive start of range of LUT.</param>
        /// <param name="end">Exclusive end of range of LUT.</param>
        /// <param name="gamma">Gamma.</param>
        public static void GammaTransform(IList<double> lut, int start, int end, double gamma)
        {
            // check parameter
            var count = (end - start - 1.0);
            if (count < -0.1)
                return;
            if (Math.Abs(gamma - 1) < 0.001)
                return;

            // apply
            var baseColor = lut[start];
            var colorThreshold = (1 / count);
            if (gamma < 1)
            {
                for (var n = start; n < end; ++n)
                {
                    var input = (lut[n] - baseColor) / count;
                    if (Math.Abs(input) < colorThreshold)
                        input = (input >= 0) ? colorThreshold : -colorThreshold;
                    lut[n] = baseColor + (Math.Pow(input, gamma) * count);
                }
            }
            else
            {
                for (var n = start; n < end; ++n)
                {
                    var input = (lut[n] - baseColor) / count;
                    if (Math.Abs(input - 1) < colorThreshold)
                        input = (input >= 1) ? 1 + colorThreshold : 1 - colorThreshold;
                    lut[n] = baseColor + (Math.Pow(input, gamma) * count);
                }
            }
            /*
            for (var n = start + 1; n < end; ++n)
            {
                var input = (lut[n] - baseColor) / count;
                if (Math.Abs(input) > 0.00001)
                    lut[n] = baseColor + (Math.Pow(input, gamma) * count);
            }
            */
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
        /// Select gamma for brightness adjustment by given EV.
        /// </summary>
        /// <param name="histograms">Histograms of original image.</param>
        /// <param name="ev">EV.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Selected Gamma.</returns>
        public static Task<double> SelectGammaByEvAsync(BitmapHistograms histograms, double ev, CancellationToken cancellationToken = default) => Task.Run(() =>
        {
            // check parameter
            if (Math.Abs(ev) < 0.001)
                return 1.0;
            
            // calculate original luminance
            var colorCount = histograms.ColorCount;
            var maxColor = (colorCount - 1.0);
            if (maxColor <= 0.01)
                return double.NaN;
            var histogram = histograms.Luminance;
            var originalLuminance = 0.0;
            for (var i = colorCount - 1; i >= 0; --i)
                originalLuminance += (i / maxColor) * histogram[i];
            
            // find target gamma
            var targetLuminance = originalLuminance * Math.Pow(2, ev);
            var min = 0.0;
            var max = 0.0;
            if (ev >= 0)
            {
                min = 0.01;
                max = 1.0;
            }
            else
            {
                min = 1.0;
                max = 100.0;
            }
            while (Math.Abs(max - min) >= 0.01)
            {
                var gamma = (min + max) / 2;
                var luminance = 0.0;
                for (var i = colorCount - 1; i >= 0; --i)
                    luminance += Math.Pow((i / maxColor), gamma) * histogram[i];
                if (Math.Abs((luminance / originalLuminance) - targetLuminance) <= 0.01)
                    return gamma;
                if (luminance < targetLuminance)
                    max = gamma;
                else
                    min = gamma;
            }
            return min;
        });


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
