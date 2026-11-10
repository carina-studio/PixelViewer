using CarinaStudio;
using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageFilters;

/// <summary>
/// Methods for generating color Lookup Table (LUT).
/// </summary>
static class ColorLut
{
    /// <summary>
    /// Read-only identity LUT for 16-bit color.
    /// </summary>
    public static readonly IList<double> Identity16 = ListExtensions.AsReadOnly(new double[65536].Also(ResetToIdentity));
    

    /// <summary>
    /// Read-only identity LUT for 8-bit color.
    /// </summary>
    public static readonly IList<double> Identity8 = ListExtensions.AsReadOnly(new double[256].Also(ResetToIdentity));
    

    // Constants.
    const double ArctanTransformationSensitivity = 0.5;
    internal const double AutoBrightnessLowerLuminance = 0.17;
    internal const double AutoBrightnessMaxEv = 1.5;
    internal const double AutoBrightnessMinEv = 0.1;
    internal const double AutoBrightnessUpperLuminance = 0.4;
    internal const double AutoContrastMax = 0.5;
    internal const double AutoContrastSpreadFullStart = 0.15;
    internal const double AutoContrastSpreadStart = 0.05;
    internal const double AutoContrastTargetSpread = 0.7;
    internal const double AutoHighlightClippedLuminance = 0.999;
    internal const double AutoHighlightMax = 0.6;
    internal const double AutoHighlightShareFull = 0.15;
    internal const double AutoHighlightShareStart = 0.02;
    internal const double AutoHighlightThreshold = 0.8;
    internal const double AutoShadowBlackLuminance = 0.0001;
    internal const double AutoShadowMax = 0.4;
    internal const double AutoShadowShareFull = 0.3;
    internal const double AutoShadowShareStart = 0.05;
    internal const double AutoShadowThreshold = 0.01;
    internal const double AutoTintBand = 0.15;
    internal const double AutoWarmthCoolingBand = 0.4;
    internal const double AutoWarmthWarmingBand = 0.15;
    internal const double AutoWhiteBalanceMaxColor = 0.98;
    internal const double AutoWhiteBalanceMinColor = 0.01;
    internal const double AutoWhiteBalanceNeutralityExponent = 4;
    internal const double BrightnessShoulderKnee = 0.8;
    const double ContrastColorThreshold = 1 / 65535.0;
    internal const double ContrastStrength = 8;
    internal const double HighlightMaxEv = 2;
    internal const double HighlightPivot = 0.05;
    internal const double HighlightRampStops = 0.5;
    internal const double HighlightStrength = 1;
    const int LutCacheSize = 8;
    internal const double ShadowMaxEv = 2.5;
    internal const double ShadowPivot = 0.5;
    internal const double ShadowRangeStops = 5;
    internal const double TintMaxEv = 0.75;
    internal const double WarmthMaxEv = 0.75;


    // Fields.
    static readonly Stack<double[]> LutPool16 = new Stack<double[]>(LutCacheSize);
    static readonly Stack<double[]> LutPool8 = new Stack<double[]>(LutCacheSize);


    /// <summary>
    /// Apply the safe bands of auto color adjustment to the warmth and tint which neutralize the color cast of an image.
    /// </summary>
    /// <param name="warmth">Warmth which neutralizes the color cast.</param>
    /// <param name="tint">Tint which neutralizes the color cast.</param>
    /// <returns>Warmth and tint moved towards 0 by the widths of the bands.</returns>
    /// <remarks>A slight color cast is likely the color the image is expected to have, so each value is moved towards 0 by the width of its band and becomes 0 inside it. The band of warmth is wider for cooling a warm image, <see cref="AutoWarmthCoolingBand"/>, than for warming a cool one, <see cref="AutoWarmthWarmingBand"/>, because a warm image is more often intended.</remarks>
    public static (double, double) ApplyAutoWhiteBalanceBands(double warmth, double tint)
    {
        // check parameter
        if (!double.IsFinite(warmth) || !double.IsFinite(tint))
            throw new ArgumentException();

        // move towards 0 by the widths of bands
        static double Shrink(double value, double band) => Math.Sign(value) * Math.Max(Math.Abs(value) - band, 0);
        return (Shrink(warmth, warmth < 0 ? AutoWarmthCoolingBand : AutoWarmthWarmingBand), Shrink(tint, AutoTintBand));
    }


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
        var maxColor = lut.Count - 1.0;
        var startNormColor = start / maxColor;
        var endNormColor = (end - 1) / maxColor;
        var colorRatio = (end - start - 1) / maxColor;
        var colorThreshold = (1 / maxColor);
        for (var i = lut.Count - 1; i >= 0; --i)
        {
            var normColor = lut[i] / maxColor;
            if (normColor < startNormColor || normColor > endNormColor)
                continue;
            lut[i] = ArctanTransform(normColor, startNormColor, colorRatio, colorThreshold, intensity) * maxColor;
        }
    }


    // Apply arctangent transformation to normalized color in the range which starts from the given color and spans the given ratio of colors.
    static double ArctanTransform(double normColor, double startNormColor, double colorRatio, double colorThreshold, double intensity)
    {
        if (intensity >= 0)
        {
            intensity = Math.Pow(intensity, ArctanTransformationSensitivity);
            var coeff = 1 / Math.Atan(intensity);
            normColor = (normColor - startNormColor) / colorRatio; // expand to [0.0, 1.0]
            if (Math.Abs(normColor) < colorThreshold)
                normColor = normColor >= 0 ? colorThreshold : -colorThreshold;
            return startNormColor + (Math.Atan(intensity * normColor) * coeff * colorRatio);
        }
        else
        {
            intensity = Math.Pow(-intensity, ArctanTransformationSensitivity);
            var coeff = -1 / Math.Atan(-intensity);
            normColor = (normColor - startNormColor) / colorRatio; // expand to [0.0, 1.0]
            if (Math.Abs(normColor - 1) < colorThreshold)
                normColor = normColor > 1 ? 1 + colorThreshold : 1 - colorThreshold;
            return startNormColor + ((Math.Atan(intensity * (normColor - 1)) * coeff + 1) * colorRatio);
        }
    }


    /// <summary>
    /// Transform brightness.
    /// </summary>
    /// <param name="lut">LUT.</param>
    /// <param name="ev">Brightness to apply in EV.</param>
    /// <param name="toLinear">Function to transfer normalized color in the LUT to linear color, or Null if color in the LUT is linear.</param>
    /// <param name="fromLinear">Function to transfer normalized linear color back to color in the LUT, or Null if color in the LUT is linear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of transformation.</returns>
    /// <remarks>The linear color is scaled by 2^EV. A positive EV compresses the linear colors above <see cref="BrightnessShoulderKnee"/> towards 1 instead of clipping them. The transfer functions are used only when both of them are defined, and <paramref name="fromLinear"/> should be the inverse of <paramref name="toLinear"/>.</remarks>
    public static Task BrightnessTransformAsync(IList<double> lut, double ev, Func<double, double>? toLinear, Func<double, double>? fromLinear, CancellationToken cancellationToken = default)
    {
        // check parameter
        if (!double.IsFinite(ev))
            throw new ArgumentException();
        cancellationToken.ThrowIfCancellationRequested();
        if (Math.Abs(ev) < 0.01)
            return Task.CompletedTask;

        // prepare
        if (toLinear is null || fromLinear is null)
        {
            toLinear = static it => it;
            fromLinear = static it => it;
        }
        var maxColor = lut.Count - 1.0;

        // apply
        for (var i = lut.Count - 1; i >= 0; --i)
            lut[i] = fromLinear(TransformBrightness(toLinear(lut[i] / maxColor), ev)) * maxColor;
        return Task.CompletedTask;
    }


    /// <summary>
    /// Transform contrast.
    /// </summary>
    /// <param name="lut">LUT.</param>
    /// <param name="intensity">Intensity of contrast. Range is [-1.0, 1.0].</param>
    /// <param name="toLinear">Function to transfer normalized color in the LUT to linear color, or Null if color in the LUT is linear.</param>
    /// <param name="fromLinear">Function to transfer normalized linear color back to color in the LUT, or Null if color in the LUT is linear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of transformation.</returns>
    /// <remarks>The S-curve is applied to linear color encoded by the transfer function of sRGB, so that it pivots around middle gray whatever the encoding of color in the LUT is. The transfer functions are used only when both of them are defined, and <paramref name="fromLinear"/> should be the inverse of <paramref name="toLinear"/>.</remarks>
    public static Task ContrastTransformAsync(IList<double> lut, double intensity, Func<double, double>? toLinear, Func<double, double>? fromLinear, CancellationToken cancellationToken = default)
    {
        // check parameter
        if (!double.IsFinite(intensity))
            throw new ArgumentException();
        cancellationToken.ThrowIfCancellationRequested();
        if (Math.Abs(intensity) < 0.01)
            return Task.CompletedTask;
        intensity = Math.Clamp(intensity, -1, 1);

        // prepare
        if (toLinear is null || fromLinear is null)
        {
            toLinear = static it => it;
            fromLinear = static it => it;
        }
        var maxColor = lut.Count - 1.0;

        // apply
        for (var i = lut.Count - 1; i >= 0; --i)
            lut[i] = fromLinear(TransformContrast(toLinear(lut[i] / maxColor), intensity)) * maxColor;
        return Task.CompletedTask;
    }


    // Decode normalized color encoded by the transfer function of sRGB to linear color.
    static double DecodeSrgb(double color) =>
        color <= 0.04045 ? color / 12.92 : Math.Pow((color + 0.055) / 1.055, 2.4);


    // Encode normalized linear color by the transfer function of sRGB.
    static double EncodeSrgb(double color) =>
        color <= 0.0031308 ? color * 12.92 : 1.055 * Math.Pow(color, 1 / 2.4) - 0.055;


    /// <summary>
    /// Transform by a gain applied to linear color.
    /// </summary>
    /// <param name="lut">LUT.</param>
    /// <param name="gain">Gain to apply to linear color.</param>
    /// <param name="toLinear">Function to transfer normalized color in the LUT to linear color, or Null if color in the LUT is linear.</param>
    /// <param name="fromLinear">Function to transfer normalized linear color back to color in the LUT, or Null if color in the LUT is linear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of transformation.</returns>
    /// <remarks>The linear color is multiplied by <paramref name="gain"/> and clipped to 1. The transfer functions are used only when both of them are defined, and <paramref name="fromLinear"/> should be the inverse of <paramref name="toLinear"/>.</remarks>
    public static Task GainTransformAsync(IList<double> lut, double gain, Func<double, double>? toLinear, Func<double, double>? fromLinear, CancellationToken cancellationToken = default)
    {
        // check parameter
        if (!double.IsFinite(gain) || gain < 0)
            throw new ArgumentException();
        cancellationToken.ThrowIfCancellationRequested();
        if (Math.Abs(gain - 1) < 0.0001)
            return Task.CompletedTask;

        // prepare
        if (toLinear is null || fromLinear is null)
        {
            toLinear = static it => it;
            fromLinear = static it => it;
        }
        var maxColor = lut.Count - 1.0;

        // apply
        for (var i = lut.Count - 1; i >= 0; --i)
        {
            var color = toLinear(lut[i] / maxColor) * gain;
            lut[i] = fromLinear(Math.Min(color, 1)) * maxColor;
        }
        return Task.CompletedTask;
    }


    /// <summary>
    /// Apply highlight transformation.
    /// </summary>
    /// <param name="lut">LUT.</param>
    /// <param name="intensity">Intensity, range is [-1.0, 1.0].</param>
    /// <param name="toLinear">Function to transfer normalized color in the LUT to linear color, or Null if color in the LUT is linear.</param>
    /// <param name="fromLinear">Function to transfer normalized linear color back to color in the LUT, or Null if color in the LUT is linear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of applying transformation.</returns>
    /// <remarks>The distance from linear color to white is scaled by 2^(-intensity × <see cref="HighlightStrength"/> × g(u)), where u is the number of stops by which the distance is below the distance of <see cref="HighlightPivot"/> to white, and g(u) rises quadratically over the first <see cref="HighlightRampStops"/> stops and linearly beyond, so that the slope of the curve is continuous. The exponent e = <see cref="HighlightStrength"/> × g(u) is then capped smoothly as <see cref="HighlightMaxEv"/> × (1 - exp(-e / <see cref="HighlightMaxEv"/>)), which bounds the slope of the curve next to white by 2^<see cref="HighlightMaxEv"/> and keeps the local contrast in log2 space at 1 - <see cref="HighlightStrength"/> × exp(-<see cref="HighlightStrength"/> × <see cref="HighlightRampStops"/> / (2 × <see cref="HighlightMaxEv"/>)) at least. Colors at and below <see cref="HighlightPivot"/> and white are kept. The transfer functions are used only when both of them are defined, and <paramref name="fromLinear"/> should be the inverse of <paramref name="toLinear"/>.</remarks>
    public static Task HighlightTransformAsync(IList<double> lut, double intensity, Func<double, double>? toLinear, Func<double, double>? fromLinear, CancellationToken cancellationToken = default)
    {
        // check parameter
        if (!double.IsFinite(intensity))
            throw new ArgumentException();
        cancellationToken.ThrowIfCancellationRequested();
        if (lut.Count <= 2)
            return Task.CompletedTask;
        if (intensity < -1)
            intensity = -1;
        else if (intensity > 1)
            intensity = 1;
        else if (Math.Abs(intensity) < 0.001)
            return Task.CompletedTask;

        // prepare
        if (toLinear is null || fromLinear is null)
        {
            toLinear = static it => it;
            fromLinear = static it => it;
        }
        var maxColor = lut.Count - 1.0;

        // apply, keeping colors which are not transformed as they are
        for (var i = lut.Count - 1; i >= 0; --i)
        {
            var color = toLinear(lut[i] / maxColor);
            var distance = 1 - color;
            if (distance <= 0 || distance >= 1 - HighlightPivot)
                continue;
            lut[i] = fromLinear(TransformHighlight(color, intensity)) * maxColor;
        }
        return Task.CompletedTask;
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


    // Get the position in [0.0, 1.0] of value between start and end linearly.
    static double Ramp(double value, double start, double end) =>
        Math.Clamp((value - start) / (end - start), 0, 1);


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
    /// Select the means of linear R, G and B of an image, weighted towards the colors which are close to neutral.
    /// </summary>
    /// <param name="colors">Normalized linear (R, G, B) colors sampled from the image.</param>
    /// <returns>Weighted means of linear R, G and B, or 0 for all of them if no color can be taken.</returns>
    /// <remarks>Each color is weighted by (1 - saturation)^<see cref="AutoWhiteBalanceNeutralityExponent"/>, where the saturation is (max - min) / max, so that a large area of a vivid color such as a blue sky contributes little while a color cast, which shifts the neutral colors as well, still shows. Colors whose largest component is at or below <see cref="AutoWhiteBalanceMinColor"/> or at or above <see cref="AutoWhiteBalanceMaxColor"/> are skipped, because noise dominates the former and clipping changes the ratios of the latter.</remarks>
    public static (double, double, double) SelectNeutralMeans(IList<(double, double, double)> colors)
    {
        // sum colors weighted by how neutral they are
        var sumR = 0.0;
        var sumG = 0.0;
        var sumB = 0.0;
        var sumWeight = 0.0;
        for (var i = colors.Count - 1; i >= 0; --i)
        {
            var (r, g, b) = colors[i];
            var max = Math.Max(r, Math.Max(g, b));
            if (max <= AutoWhiteBalanceMinColor || max >= AutoWhiteBalanceMaxColor)
                continue;
            var saturation = (max - Math.Min(r, Math.Min(g, b))) / max;
            var weight = Math.Pow(1 - saturation, AutoWhiteBalanceNeutralityExponent);
            sumR += r * weight;
            sumG += g * weight;
            sumB += b * weight;
            sumWeight += weight;
        }

        // calculate means
        if (sumWeight <= 0)
            return (0, 0, 0);
        return (sumR / sumWeight, sumG / sumWeight, sumB / sumWeight);
    }


    /// <summary>
    /// Select brightness, contrast, highlight and shadow adjustments for an image by the luminance of its pixels.
    /// </summary>
    /// <param name="luminances">Normalized linear luminances sampled from the image.</param>
    /// <returns>Brightness in EV, contrast, highlight and shadow.</returns>
    /// <remarks>Each adjustment is selected by a formula of what it is measured on, in the order they are applied by the LUT, and is measured after applying the ones selected before it. Brightness brings the median luminance of midtones, which excludes shadows and highlights, into the range from <see cref="AutoBrightnessLowerLuminance"/> to <see cref="AutoBrightnessUpperLuminance"/> and is 0 when it is already there, so that shadows and highlights are left to shadow and highlight rather than brightening or darkening the whole image, and a positive brightness is limited so that it pushes no more than <see cref="AutoHighlightShareStart"/> of the luminance which has not been clipped into highlights; contrast rises as the spread between the 5th and 95th percentiles of luminance encoded by the transfer function of sRGB falls below <see cref="AutoContrastTargetSpread"/>, and is never negative; highlight falls as the share of luminance at and above <see cref="AutoHighlightThreshold"/> rises, and shadow rises as the share at and below <see cref="AutoShadowThreshold"/> rises, each excluding the luminance which has been clipped to white or black in the image, which no adjustment can recover.</remarks>
    public static (double, double, double, double) SelectToneAdjustments(IList<double> luminances)
    {
        // check parameter
        if (luminances.IsEmpty())
            return (0, 0, 0, 0);
        var count = luminances.Count;

        // select brightness which brings median luminance of midtones into the range, leaving shadows and highlights to shadow and highlight
        var midtones = new List<double>(count);
        for (var i = count - 1; i >= 0; --i)
        {
            var luminance = luminances[i];
            if (luminance > AutoShadowThreshold && luminance < AutoHighlightThreshold)
                midtones.Add(luminance);
        }
        var brightness = 0.0;
        if (midtones.IsNotEmpty())
        {
            midtones.Sort();
            var median = midtones[midtones.Count / 2];
            if (median < AutoBrightnessLowerLuminance)
                brightness = Math.Log2(AutoBrightnessLowerLuminance / median);
            else if (median > AutoBrightnessUpperLuminance)
                brightness = Math.Log2(AutoBrightnessUpperLuminance / median);
            brightness = Math.Clamp(brightness, -AutoBrightnessMaxEv, AutoBrightnessMaxEv);
            brightness = Math.Abs(brightness) < AutoBrightnessMinEv ? 0 : Math.Round(brightness * 20) / 20;
        }

        // limit positive brightness so that it does not push more luminance which has not been clipped into highlights than highlight leaves alone
        if (brightness > 0)
        {
            var unclippedLuminances = new List<double>(count);
            for (var i = count - 1; i >= 0; --i)
            {
                if (luminances[i] < AutoHighlightClippedLuminance)
                    unclippedLuminances.Add(luminances[i]);
            }
            if (unclippedLuminances.IsNotEmpty())
            {
                unclippedLuminances.Sort();
                var headroomLuminance = unclippedLuminances[(int)((unclippedLuminances.Count - 1) * (1 - AutoHighlightShareStart))];
                if (headroomLuminance > 0)
                    brightness = Math.Min(brightness, Math.Max(0, Math.Floor(Math.Log2(AutoHighlightThreshold / headroomLuminance) * 20) / 20));
            }
        }

        // select contrast by spread of encoded luminance after applying brightness
        var colors = new double[count];
        var encodedColors = new double[count];
        for (var i = count - 1; i >= 0; --i)
        {
            colors[i] = TransformBrightness(Math.Clamp(luminances[i], 0, 1), brightness);
            encodedColors[i] = EncodeSrgb(Math.Clamp(colors[i], 0, 1));
        }
        Array.Sort(encodedColors);
        var spread = encodedColors[(int)((count - 1) * 0.95)] - encodedColors[(int)((count - 1) * 0.05)];
        var contrast = Math.Round(AutoContrastMax * Math.Clamp(1 - spread / AutoContrastTargetSpread, 0, 1) * Ramp(spread, AutoContrastSpreadStart, AutoContrastSpreadFullStart), 2);

        // select highlight by share of highlights after applying contrast, excluding white which has been clipped in the image
        var highlightCount = 0;
        for (var i = count - 1; i >= 0; --i)
        {
            var luminance = TransformContrast(colors[i], contrast);
            colors[i] = luminance;
            if (luminance >= AutoHighlightThreshold && luminances[i] < AutoHighlightClippedLuminance)
                ++highlightCount;
        }
        var highlight = Math.Round(-AutoHighlightMax * Ramp((double)highlightCount / count, AutoHighlightShareStart, AutoHighlightShareFull), 2);

        // select shadow by share of shadows after applying highlight, excluding black in the image
        var shadowCount = 0;
        for (var i = count - 1; i >= 0; --i)
        {
            if (luminances[i] > AutoShadowBlackLuminance && TransformHighlight(colors[i], highlight) <= AutoShadowThreshold)
                ++shadowCount;
        }
        var shadow = Math.Round(AutoShadowMax * Ramp((double)shadowCount / count, AutoShadowShareStart, AutoShadowShareFull), 2);
        return (brightness, contrast, highlight, shadow);
    }


    /// <summary>
    /// Select warmth and tint which give the same ratios of R, G and B as given linear gains.
    /// </summary>
    /// <param name="gainR">Linear gain of R.</param>
    /// <param name="gainG">Linear gain of G.</param>
    /// <param name="gainB">Linear gain of B.</param>
    /// <returns>Warmth and tint, which may be out of the range [-1.0, 1.0].</returns>
    /// <remarks>This is the inverse of <see cref="SelectWhiteBalanceGains"/> up to the scale of the gains.</remarks>
    public static (double, double) SelectWarmthAndTint(double gainR, double gainG, double gainB)
    {
        // check parameter
        if (!double.IsFinite(gainR) || !double.IsFinite(gainG) || !double.IsFinite(gainB) || gainR <= 0 || gainG <= 0 || gainB <= 0)
            throw new ArgumentException();

        // select
        var log2R = Math.Log2(gainR);
        var log2G = Math.Log2(gainG);
        var log2B = Math.Log2(gainB);
        var warmth = (log2R - log2B) / (2 * WarmthMaxEv);
        var tint = ((log2R + log2B) / 2 - log2G) / TintMaxEv;
        return (warmth, tint);
    }


    /// <summary>
    /// Select linear gains of R, G and B for given warmth and tint.
    /// </summary>
    /// <param name="warmth">Warmth, range is [-1.0, 1.0]. Positive value makes the color warmer.</param>
    /// <param name="tint">Tint, range is [-1.0, 1.0]. Positive value moves the color towards magenta, negative value towards green.</param>
    /// <param name="luminanceWeights">Weights of R, G and B to get luminance of linear color.</param>
    /// <returns>Linear gains of R, G and B.</returns>
    /// <remarks>R is scaled by 2^(warmth × <see cref="WarmthMaxEv"/>), B by 2^(-warmth × <see cref="WarmthMaxEv"/>) and G by 2^(-tint × <see cref="TintMaxEv"/>), then all of them are divided by the luminance they give to white, so that a gray keeps its luminance.</remarks>
    public static (double, double, double) SelectWhiteBalanceGains(double warmth, double tint, (double, double, double) luminanceWeights)
    {
        // check parameter
        if (!double.IsFinite(warmth) || !double.IsFinite(tint))
            throw new ArgumentException();
        warmth = Math.Clamp(warmth, -1, 1);
        tint = Math.Clamp(tint, -1, 1);

        // select gains
        var gainR = Math.Pow(2, warmth * WarmthMaxEv);
        var gainG = Math.Pow(2, -tint * TintMaxEv);
        var gainB = Math.Pow(2, -warmth * WarmthMaxEv);

        // keep luminance of gray
        var (weightR, weightG, weightB) = luminanceWeights;
        var luminance = (weightR * gainR + weightG * gainG + weightB * gainB) / (weightR + weightG + weightB);
        if (!double.IsFinite(luminance) || luminance <= 0)
            return (gainR, gainG, gainB);
        return (gainR / luminance, gainG / luminance, gainB / luminance);
    }


    /// <summary>
    /// Apply shadow transformation.
    /// </summary>
    /// <param name="lut">LUT.</param>
    /// <param name="intensity">Intensity, range is [-1.0, 1.0].</param>
    /// <param name="toLinear">Function to transfer normalized color in the LUT to linear color, or Null if color in the LUT is linear.</param>
    /// <param name="fromLinear">Function to transfer normalized linear color back to color in the LUT, or Null if color in the LUT is linear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of applying transformation.</returns>
    /// <remarks>The linear color is scaled by 2^(intensity × <see cref="ShadowMaxEv"/> × weight), where the weight is 1 at and below <see cref="ShadowRangeStops"/> stops under <see cref="ShadowPivot"/>, 0 at and above <see cref="ShadowPivot"/>, and fades in between by a smoothstep in log2 space. The transfer functions are used only when both of them are defined, and <paramref name="fromLinear"/> should be the inverse of <paramref name="toLinear"/>.</remarks>
    public static Task ShadowTransformAsync(IList<double> lut, double intensity, Func<double, double>? toLinear, Func<double, double>? fromLinear, CancellationToken cancellationToken = default)
    {
        // check parameter
        if (!double.IsFinite(intensity))
            throw new ArgumentException();
        cancellationToken.ThrowIfCancellationRequested();
        if (lut.Count <= 2)
            return Task.CompletedTask;
        if (intensity < -1)
            intensity = -1;
        else if (intensity > 1)
            intensity = 1;
        else if (Math.Abs(intensity) < 0.001)
            return Task.CompletedTask;

        // prepare
        if (toLinear is null || fromLinear is null)
        {
            toLinear = static it => it;
            fromLinear = static it => it;
        }
        var maxColor = lut.Count - 1.0;

        // apply, keeping colors which are not transformed as they are
        for (var i = lut.Count - 1; i >= 0; --i)
        {
            var color = toLinear(lut[i] / maxColor);
            if (color <= 0 || color >= ShadowPivot)
                continue;
            lut[i] = fromLinear(TransformShadow(color, intensity)) * maxColor;
        }
        return Task.CompletedTask;
    }


    // Transform normalized linear color by brightness in EV.
    internal static double TransformBrightness(double color, double ev)
    {
        // check parameter
        if (Math.Abs(ev) < 0.01)
            return color;

        // scale color, and compress colors above the knee towards 1 for positive EV
        color *= Math.Pow(2, ev);
        if (ev > 0 && color > BrightnessShoulderKnee)
        {
            var shoulderRange = 1 - BrightnessShoulderKnee;
            color = 1 - shoulderRange * Math.Exp(-(color - BrightnessShoulderKnee) / shoulderRange);
        }
        return color;
    }


    // Transform normalized linear color by contrast, which applies an S-curve pivoting at 0.5 to the color encoded by the transfer function of sRGB.
    internal static double TransformContrast(double color, double intensity)
    {
        // check parameter
        if (Math.Abs(intensity) < 0.01)
            return color;
        intensity = Math.Clamp(intensity, -1, 1);

        // transform by the arctangent of the half of range where the encoded color is
        var encodedColor = EncodeSrgb(Math.Clamp(color, 0, 1));
        encodedColor = encodedColor <= 0.5
            ? ArctanTransform(encodedColor, 0, 0.5, ContrastColorThreshold, intensity * -ContrastStrength)
            : ArctanTransform(encodedColor, 0.5, 0.5, ContrastColorThreshold, intensity * ContrastStrength);
        return DecodeSrgb(Math.Clamp(encodedColor, 0, 1));
    }


    // Transform normalized linear color by highlight.
    internal static double TransformHighlight(double color, double intensity)
    {
        // check parameter
        intensity = Math.Clamp(intensity, -1, 1);
        if (Math.Abs(intensity) < 0.001)
            return color;

        // check whether the color is a highlight or not
        var distance = 1 - color;
        var pivotDistance = 1 - HighlightPivot;
        if (distance <= 0 || distance >= pivotDistance)
            return color;

        // scale distance to white
        var stops = Math.Log2(pivotDistance / distance);
        var ramp = stops < HighlightRampStops
            ? stops * stops / (2 * HighlightRampStops)
            : stops - HighlightRampStops / 2;
        var ev = HighlightMaxEv * (1 - Math.Exp(-HighlightStrength * ramp / HighlightMaxEv));
        return 1 - distance * Math.Pow(2, -intensity * ev);
    }


    // Transform normalized linear color by shadow.
    internal static double TransformShadow(double color, double intensity)
    {
        // check parameter
        intensity = Math.Clamp(intensity, -1, 1);
        if (Math.Abs(intensity) < 0.001)
            return color;

        // check whether the color is a shadow or not
        if (color <= 0 || color >= ShadowPivot)
            return color;

        // scale color by weight which fades from the deepest shadows to the pivot
        var t = Math.Clamp((Math.Log2(color) - Math.Log2(ShadowPivot)) / ShadowRangeStops + 1, 0, 1);
        var weight = 1 - t * t * (3 - 2 * t);
        return color * Math.Pow(2, intensity * ShadowMaxEv * weight);
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

