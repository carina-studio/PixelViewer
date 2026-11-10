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
    internal const double BrightnessShoulderKnee = 0.8;
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
        if (intensity >= 0)
        {
            intensity = Math.Pow(intensity, ArctanTransformationSensitivity);
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
            intensity = Math.Pow(-intensity, ArctanTransformationSensitivity);
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
        var gain = Math.Pow(2, ev);
        var maxColor = lut.Count - 1.0;
        var shoulderRange = 1 - BrightnessShoulderKnee;

        // apply
        for (var i = lut.Count - 1; i >= 0; --i)
        {
            var color = toLinear(lut[i] / maxColor) * gain;
            if (ev > 0 && color > BrightnessShoulderKnee)
                color = 1 - shoulderRange * Math.Exp(-(color - BrightnessShoulderKnee) / shoulderRange);
            lut[i] = fromLinear(color) * maxColor;
        }
        return Task.CompletedTask;
    }


    /// <summary>
    /// Transform contrast.
    /// </summary>
    /// <param name="lut">LUT.</param>
    /// <param name="intensity">Intensity of contrast. Range is [-1.0, 1.0].</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of transformation.</returns>
    public static Task ContrastTransformAsync(IList<double> lut, double intensity, CancellationToken cancellationToken = default)
    {
        // check parameter
        if (Math.Abs(intensity) < 0.01)
            return Task.CompletedTask;
        if (!double.IsFinite(intensity))
            throw new ArgumentException();
        if (intensity < -1)
            intensity = -1;
        else if (intensity > 1)
            intensity = 1;

        // apply
        ArctanTransform(lut, 0, lut.Count / 2, intensity * -ContrastStrength);
        ArctanTransform(lut, lut.Count / 2, lut.Count, intensity * ContrastStrength);
        return Task.CompletedTask;
    }


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
        var evScale = -intensity;
        var pivotDistance = 1 - HighlightPivot;
        var maxColor = lut.Count - 1.0;

        // apply
        for (var i = lut.Count - 1; i >= 0; --i)
        {
            var distance = 1 - toLinear(lut[i] / maxColor);
            if (distance <= 0 || distance >= pivotDistance)
                continue;
            var stops = Math.Log2(pivotDistance / distance);
            var ramp = stops < HighlightRampStops
                ? stops * stops / (2 * HighlightRampStops)
                : stops - HighlightRampStops / 2;
            var ev = HighlightMaxEv * (1 - Math.Exp(-HighlightStrength * ramp / HighlightMaxEv));
            lut[i] = fromLinear(1 - distance * Math.Pow(2, evScale * ev)) * maxColor;
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
        var ev = intensity * ShadowMaxEv;
        var log2Pivot = Math.Log2(ShadowPivot);
        var maxColor = lut.Count - 1.0;

        // apply
        for (var i = lut.Count - 1; i >= 0; --i)
        {
            var color = toLinear(lut[i] / maxColor);
            if (color <= 0 || color >= ShadowPivot)
                continue;
            var t = Math.Clamp((Math.Log2(color) - log2Pivot) / ShadowRangeStops + 1, 0, 1);
            var weight = 1 - t * t * (3 - 2 * t);
            lut[i] = fromLinear(color * Math.Pow(2, ev * weight)) * maxColor;
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

