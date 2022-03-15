using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
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
        /// Read-only identity LUT for 16-bit color.
        /// </summary>
        public static IList<double> Identity16 = new double[65536].Also(it =>
            ResetToIdentity(it)).AsReadOnly();
        

        /// <summary>
        /// Read-only identity LUT for 8-bit color.
        /// </summary>
        public static IList<double> Identity8 = new double[256].Also(it =>
            ResetToIdentity(it)).AsReadOnly();
        

        // Constants.
        const int LutCacheSize = 8;


        // Fields.
        static readonly Stack<double[]> LutPool16 = new Stack<double[]>(LutCacheSize);
        static readonly Stack<double[]> LutPool8 = new Stack<double[]>(LutCacheSize);


        /// <summary>
        /// Apply arctangent transformation.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="intensity">Intensity.</param>
        public static void ArctanTransform(IList<double> lut, double intensity) =>
            ArctanTransform(lut, 0, lut.Count, intensity);

        
        /// <summary>
        /// Apply arctangent transformation on specific range of LUT.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="start">Inclusive start of range of LUT.</param>
        /// <param name="end">Exclusive end of range of LUT.</param>
        /// <param name="intensity">Intensity.</param>
        public static void ArctanTransform(IList<double> lut, int start, int end, double intensity)
        {
            // check parameter
            if (end <= start)
                return;
            if (Math.Abs(intensity) < 0.001)
                return;
            
            // apply
            var sensitivity = Math.Max(0.1, App.CurrentOrNull?.Configuration?.GetValueOrDefault(ConfigurationKeys.ArctanTransformationSensitivity) ?? ConfigurationKeys.ArctanTransformationSensitivity.DefaultValue);
            var maxColor = lut.Count - 1.0;
            var startNormColor = start / maxColor;
            var endNormColor = (end - 1) / maxColor;
            var colorRatio = (end - start - 1) / maxColor;
            var colorThreshold = (1 / maxColor);
            if (intensity >= 0)
            {
                intensity = Math.Pow(intensity, sensitivity);
                var coeff = 1 / Math.Atan(intensity);
                for (var i = lut.Count - 1; i >= 0; --i)
                {
                    var normColor = lut[i] / maxColor;
                    if (normColor < startNormColor || normColor > endNormColor)
                        continue;
                    normColor = (normColor - startNormColor) / colorRatio; // expand to [0.0, 1.0]
                    if (Math.Abs(normColor) < colorThreshold)
                        normColor = normColor >= 0 ? colorThreshold : -colorThreshold;
                    lut[i] = (startNormColor + (Math.Atan(intensity * normColor) * coeff * colorRatio)) * maxColor;
                }
            }
            else
            {
                intensity = Math.Pow(-intensity, sensitivity);
                var coeff = -1 / Math.Atan(-intensity);
                for (var i = lut.Count - 1; i >= 0; --i)
                {
                    var normColor = lut[i] / maxColor;
                    if (normColor < startNormColor || normColor > endNormColor)
                        continue;
                    normColor = (normColor - startNormColor) / colorRatio; // expand to [0.0, 1.0]
                    if (Math.Abs(normColor - 1) < colorThreshold)
                        normColor = normColor > 1 ? 1 + colorThreshold : 1 - colorThreshold;
                    lut[i] = (startNormColor + ((Math.Atan(intensity * (normColor - 1)) * coeff + 1) * colorRatio)) * maxColor;
                }
            }
        }


        /// <summary>
        /// Transform brightness with given transformation function.
        /// </summary>
        /// <param name="histograms">Histograms of original image.</param>
        /// <param name="lut">LUT.</param>
        /// <param name="targetEv">Target brightness in EV.</param>
        /// <param name="function">Transformation function.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of transformation.</returns>
        public static async Task BrightnessTransformAsync(BitmapHistograms histograms, IList<double> lut, double targetEv, BrightnessTransformationFunction function, CancellationToken cancellationToken = default)
        {
            if (Math.Abs(targetEv) < 0.01)
                return;
            switch (function)
            {
                case BrightnessTransformationFunction.Arctan:
                    {
                        var intensity = await SelectArctanIntensityForBrightnessAsync(histograms, targetEv, cancellationToken);
                        if (double.IsFinite(intensity))
                            ArctanTransform(lut, intensity);
                    }
                    break;
                case BrightnessTransformationFunction.Gamma:
                    {
                        var gamma = await SelectGammaForBrightnessAsync(histograms, targetEv, cancellationToken);
                        if (double.IsFinite(gamma))
                            GammaTransform(lut, gamma);
                    }
                    break;
                case BrightnessTransformationFunction.Linear:
                    Multiply(lut, Math.Pow(2, targetEv));
                    break;
                default:
                    throw new ArgumentException();
            }
        }


        /// <summary>
        /// Transform contrast with given transformation function.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="intensity">Intensity of contrast. Range is [-1.0, 1.0].</param>
        /// <param name="function">Transformation function.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of transformation.</returns>
        public static Task ContrastTransformAsync(IList<double> lut, double intensity, ContrastTransformationFunction function, CancellationToken cancellationToken = default)
        {
            if (Math.Abs(intensity) < 0.01)
                return Task.CompletedTask;
            if (!double.IsFinite(intensity))
                throw new ArgumentException();
            if (intensity < -1)
                intensity = -1;
            else if (intensity > 1)
                intensity = 1;
            switch (function)
            {
                case ContrastTransformationFunction.Arctan:
                    {
                        var intensityL = intensity * -20;
						var intensityR = intensity * 20;
						ArctanTransform(lut, 0, lut.Count / 2, intensityL);
						ArctanTransform(lut, lut.Count / 2, lut.Count, intensityR);
                    }
                    break;
                case ContrastTransformationFunction.Lagrange:
                    {
                        var interpFunc = GenerateLagrangeInterpolation((0, 0), (0.25, 0.25 - (0.2 * intensity)), (0.5, 0.5), (1, 1));
                        var maxColor = lut.Count - 1;
                        for (var i = lut.Count - 1; i >= 0; --i)
                        {
                            var color = lut[i] / maxColor;
                            if (color < 0 || color > 1)
                                continue;
                            lut[i] = interpFunc(color) * maxColor;
                        }
                    }
                    break;
                case ContrastTransformationFunction.Linear:
                    {
                        var middleColor = (lut.Count - 1) / 2.0;
						var factor = intensity.Let(it => it >= 0 ? it + 1 : -1 / (it - 1));
						Multiply(lut, factor);
						Translate(lut, (1 - factor) * middleColor);
                    }
                    break;
                default:
                    throw new ArgumentException();
            }
            return Task.CompletedTask;
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
            if (end <= start)
                return;
            if (Math.Abs(gamma - 1) < 0.001)
                return;

            // apply
            var maxColor = lut.Count - 1.0;
            var startNormColor = start / maxColor;
            var endNormColor = (end - 1) / maxColor;
            var colorRatio = (end - start - 1) / maxColor;
            var colorThreshold = (1 / maxColor);
            if (gamma < 1)
            {
                for (var n = lut.Count - 1; n >= 0; --n)
                {
                    var normColor = lut[n] / maxColor;
                    if (normColor < startNormColor || normColor > endNormColor)
                        continue;
                    normColor = (normColor - startNormColor) / colorRatio; // expand to [0.0, 1.0]
                    if (Math.Abs(normColor) < colorThreshold)
                    {
                        if (normColor < 0)
                            continue;
                        normColor = colorThreshold;
                    }
                    lut[n] = (startNormColor + Math.Pow(normColor, gamma) * colorRatio) * maxColor;
                }
            }
            else
            {
                for (var n = lut.Count - 1; n >= 0; --n)
                {
                    var normColor = lut[n] / maxColor;
                    if (normColor < startNormColor || normColor > endNormColor)
                        continue;
                    normColor = (normColor - startNormColor) / colorRatio; // expand to [0.0, 1.0]
                    if (Math.Abs(normColor - 1) < colorThreshold)
                        normColor = (normColor >= 1) ? 1 + colorThreshold : 1 - colorThreshold;
                    lut[n] = (startNormColor + Math.Pow(normColor, gamma) * colorRatio) * maxColor;
                }
            }
        }


        // Generate Lagrange interpolation function by 4 points.
        static Func<double, double> GenerateLagrangeInterpolation((double, double) p0, (double, double) p1, (double, double) p2, (double, double) p3)
        {
            var d0 = p0.Item2 / ((p0.Item1 - p1.Item1) * (p0.Item1 - p2.Item1) * (p0.Item1 - p3.Item1));
            var d1 = p1.Item2 / ((p1.Item1 - p0.Item1) * (p1.Item1 - p2.Item1) * (p1.Item1 - p3.Item1));
            var d2 = p2.Item2 / ((p2.Item1 - p0.Item1) * (p2.Item1 - p1.Item1) * (p2.Item1 - p3.Item1));
            var d3 = p3.Item2 / ((p3.Item1 - p0.Item1) * (p3.Item1 - p1.Item1) * (p3.Item1 - p2.Item1));
            return (x) =>
            {
                var n0 = (x - p1.Item1) * (x - p2.Item1) * (x - p3.Item1);
                var n1 = (x - p0.Item1) * (x - p2.Item1) * (x - p3.Item1);
                var n2 = (x - p0.Item1) * (x - p1.Item1) * (x - p3.Item1);
                var n3 = (x - p0.Item1) * (x - p1.Item1) * (x - p2.Item1);
                return (n0 * d0) + (n1 * d1) + (n2 * d2) + (n3 * d3);
            };
        }


        /// <summary>
        /// Apply highlight transformation.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="intensity">Intensity, range is [-1.0, 1.0].</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of applying transformation.</returns>
        public static Task HighlightTransformAsync(IList<double> lut, double intensity, CancellationToken cancellationToken = default)
        {
            if (!double.IsFinite(intensity))
                throw new ArgumentException();
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            if (lut.Count <= 2)
                return Task.CompletedTask;
            if (intensity < -1)
                intensity = -1;
            else if (intensity > 1)
                intensity = 1;
            else if (Math.Abs(intensity) < 0.001)
                return Task.CompletedTask;
            var interpFunc = GenerateLagrangeInterpolation((0.4, 0.4), (0.5, 0.5), (0.75, 0.75 + (0.2 * intensity)), (1, 1));
            var maxColor = lut.Count - 1;
            for (var i = lut.Count - 1; i >= 0; --i)
            {
                var color = lut[i] / maxColor;
                if (color <= 0.5)
                    continue;
                lut[i] = interpFunc(color) * maxColor;
            }
            return Task.CompletedTask;
        }


        /// <summary>
        /// Apply inverse gamma transformation on specific range of LUT.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="start">Inclusive start of range of LUT.</param>
        /// <param name="end">Exclusive end of range of LUT.</param>
        /// <param name="gamma">Gamma.</param>
        public static void InverseGammaTransform(IList<double> lut, int start, int end, double gamma)
        {
            // check parameter
            if (end <= start)
                return;
            if (Math.Abs(gamma - 1) < 0.001)
                return;

            // apply
            var maxColor = lut.Count - 1.0;
            var startNormColor = start / maxColor;
            var endNormColor = (end - 1) / maxColor;
            var colorRatio = (end - start - 1) / maxColor;
            var colorThreshold = (1 / maxColor);
            if (gamma >= 1)
            {
                for (var n = lut.Count - 1; n >= 0; --n)
                {
                    var normColor = lut[n] / maxColor;
                    if (normColor < startNormColor || normColor > endNormColor)
                        continue;
                    normColor = (normColor - startNormColor) / colorRatio; // expand to [0.0, 1.0]
                    if (Math.Abs(normColor) < colorThreshold)
                        normColor = (normColor >= 0) ? colorThreshold : -colorThreshold;
                    lut[n] = (startNormColor + (1 - Math.Pow(1 - normColor, gamma)) * colorRatio) * maxColor;
                }
            }
            else
            {
                for (var n = lut.Count - 1; n >= 0; --n)
                {
                    var normColor = lut[n] / maxColor;
                    if (normColor < startNormColor || normColor > endNormColor)
                        continue;
                    normColor = (normColor - startNormColor) / colorRatio; // expand to [0.0, 1.0]
                    if (Math.Abs(normColor - 1) < colorThreshold)
                    {
                        if (normColor > 1)
                            continue;
                        normColor = 1 - colorThreshold;
                    }
                    lut[n] = (startNormColor + (1 - Math.Pow(1 - normColor, gamma)) * colorRatio) * maxColor;
                }
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
        /// Obtain an LUT.
        /// </summary>
        /// <param name="template">Template LUT.</param>
        /// <returns>LUT.</returns>
        public static IList<double> Obtain(IList<double> template)
        {
            var lut = template.Count switch
            {
                256 => LutPool8.Lock(pool =>
                {
                    if (!pool.TryPop(out var lut))
                        lut = new double[256];
                    return lut;
                }),
                65536 => LutPool16.Lock(pool =>
                {
                    if (!pool.TryPop(out var lut))
                        lut = new double[65536];
                    return lut;
                }),
                _ => new double[template.Count],
            };
            template.CopyTo(lut, 0);
            return lut;
        }


        /// <summary>
        /// Obtain an identity LUT for given format.
        /// </summary>
        /// <param name="format">Format.</param>
        /// <returns>Identity LUT.</returns>
        public static IList<double> ObtainIdentity(BitmapFormat format) => format switch
        {
            BitmapFormat.Bgra32 => LutPool8.Lock(pool =>
            {
                if (!pool.TryPop(out var lut))
                    lut = new double[256];
                ResetToIdentity(lut);
                return lut;
            }),
            BitmapFormat.Bgra64 => LutPool16.Lock(pool =>
            {
                if (!pool.TryPop(out var lut))
                    lut = new double[65536];
                ResetToIdentity(lut);
                return lut;
            }),
            _ => throw new NotSupportedException($"Unsupported format: {format}."),
        };


         /// <summary>
        /// Obtain an read-only identity LUT for given format.
        /// </summary>
        /// <param name="format">Format.</param>
        /// <returns>Identity LUT.</returns>
        public static IList<double> ObtainReadOnlyIdentity(BitmapFormat format) => format switch
        {
            BitmapFormat.Bgra32 => Identity8,
            BitmapFormat.Bgra64 => Identity16,
            _ => throw new NotSupportedException($"Unsupported format: {format}."),
        };


        /// <summary>
        /// Recycle given LUT.
        /// </summary>
        /// <param name="lut">LUT.</param>
        public static void Recycle(IList<double> lut)
        {
            if (lut is not double[] arrayLut)
                return;
            switch (lut.Count)
            {
                case 256:
                    lock (LutPool8)
                    {
                        if (LutPool8.Count < LutCacheSize)
                            LutPool8.Push(arrayLut);
                    }
                    break;
                case 65536:
                    lock (LutPool16)
                    {
                        if (LutPool16.Count < LutCacheSize)
                            LutPool16.Push(arrayLut);
                    }
                    break;
            }
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
        /// Select intensity of arctangen transformation for brightness adjustment by given EV.
        /// </summary>
        /// <param name="histograms">Histograms of original image.</param>
        /// <param name="ev">EV.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Selected intensity.</returns>
        public static Task<double> SelectArctanIntensityForBrightnessAsync(BitmapHistograms histograms, double ev, CancellationToken cancellationToken = default) => Task.Run(() =>
        {
            // check parameter
            if (Math.Abs(ev) < 0.001)
                return 0;
            
            // calculate original luminance
            var colorCount = histograms.ColorCount;
            var pixelCount = histograms.EffectivePixelCount;
            var maxColor = (colorCount - 1.0);
            if (maxColor <= 0.01)
                return double.NaN;
            var histogram = histograms.Luminance;
            var originalLuminance = 0.0;
            for (var i = colorCount - 1; i >= 0; --i)
                originalLuminance += (i / maxColor) * histogram[i] / pixelCount;
            
            // find target intensity
            var sensitivity = Math.Max(0.1, App.CurrentOrNull?.Configuration?.GetValueOrDefault(ConfigurationKeys.ArctanTransformationSensitivity) ?? ConfigurationKeys.ArctanTransformationSensitivity.DefaultValue);
            var targetLuminance = originalLuminance * Math.Pow(2, ev);
            var min = 0.0;
            var max = 0.0;
            if (ev >= 0)
                max = 1000;
            else
                min = -1000;
            while (Math.Abs(max - min) >= 0.01)
            {
                var intensity = (min + max) / 2;
                var luminance = 0.0;
                if (intensity >= 0)
                {
                    var expoIntensity = Math.Pow(intensity, sensitivity);
                    var coeff = 1 / Math.Atan(expoIntensity);
                    for (var i = colorCount - 1; i >= 0; --i)
                    {
                        var color = i / maxColor;
                        luminance += (Math.Atan(expoIntensity * color) * coeff) * histogram[i] / pixelCount;
                    }
                }
                else
                {
                    var expoIntensity = Math.Pow(-intensity, sensitivity);
                    var coeff = -1 / Math.Atan(-expoIntensity);
                    for (var i = colorCount - 1; i >= 0; --i)
                    {
                        var color = i / maxColor;
                        luminance += (Math.Atan(expoIntensity * (color - 1)) * coeff + 1) * histogram[i] / pixelCount;
                    }
                }
                if (Math.Abs(luminance - targetLuminance) <= 0.01)
                    return intensity;
                if (luminance < targetLuminance)
                    min = intensity;
                else
                    max = intensity;
            }
            return min;
        });


        /// <summary>
        /// Select gamma for brightness adjustment by given EV.
        /// </summary>
        /// <param name="histograms">Histograms of original image.</param>
        /// <param name="ev">EV.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Selected Gamma.</returns>
        public static Task<double> SelectGammaForBrightnessAsync(BitmapHistograms histograms, double ev, CancellationToken cancellationToken = default) => Task.Run(() =>
        {
            // check parameter
            if (Math.Abs(ev) < 0.001)
                return 1.0;
            
            // calculate original luminance
            var colorCount = histograms.ColorCount;
            var pixelCount = histograms.EffectivePixelCount;
            var maxColor = (colorCount - 1.0);
            if (maxColor <= 0.01)
                return double.NaN;
            var histogram = histograms.Luminance;
            var originalLuminance = 0.0;
            for (var i = colorCount - 1; i >= 0; --i)
                originalLuminance += (i / maxColor) * histogram[i] / pixelCount;
            
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
                    luminance += Math.Pow((i / maxColor), gamma) * histogram[i] / pixelCount;
                if (Math.Abs(luminance - targetLuminance) <= 0.01)
                    return gamma;
                if (luminance < targetLuminance)
                    max = gamma;
                else
                    min = gamma;
            }
            return min;
        });


        /// <summary>
        /// Apply shadow transformation.
        /// </summary>
        /// <param name="lut">LUT.</param>
        /// <param name="intensity">Intensity, range is [-1.0, 1.0].</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of applying transformation.</returns>
        public static Task ShadowTransformAsync(IList<double> lut, double intensity, CancellationToken cancellationToken = default)
        {
            if (!double.IsFinite(intensity))
                throw new ArgumentException();
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            if (lut.Count <= 2)
                return Task.CompletedTask;
            if (intensity < -1)
                intensity = -1;
            else if (intensity > 1)
                intensity = 1;
            else if (Math.Abs(intensity) < 0.001)
                return Task.CompletedTask;
            var interpFunc = GenerateLagrangeInterpolation((0, 0), (0.25, 0.25 + (0.2 * intensity)), (0.5, 0.5), (0.6, 0.6));
            var maxColor = lut.Count - 1;
            for (var i = lut.Count - 1; i >= 0; --i)
            {
                var color = lut[i] / maxColor;
                if (color >= 0.5)
                    continue;
                lut[i] = interpFunc(color) * maxColor;
            }
            return Task.CompletedTask;
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


    /// <summary>
    /// Function to transform brightness.
    /// </summary>
    enum BrightnessTransformationFunction
    {
        /// <summary>
        /// Linear transformation.
        /// </summary>
        Linear,
        /// <summary>
        /// Gamma transformation.
        /// </summary>
        Gamma,
        /// <summary>
        /// Arctangen transformation.
        /// </summary>
        Arctan,
    }


    /// <summary>
    /// Function to transform contrast.
    /// </summary>
    enum ContrastTransformationFunction
    {
        /// <summary>
        /// Linear transformation.
        /// </summary>
        Linear,
        /// <summary>
        /// Arctangen transformation.
        /// </summary>
        Arctan,
        /// <summary>
        /// Lagrange interpolation.
        /// </summary>
        Lagrange,
    }
}
