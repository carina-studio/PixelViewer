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
        ArctanTransform(lut, 0, lut.Count / 2, intensity * -20);
        ArctanTransform(lut, lut.Count / 2, lut.Count, intensity * 20);
        return Task.CompletedTask;
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
            if (color <= 0.5 || color > 1)
                continue;
            lut[i] = interpFunc(color) * maxColor;
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
            if (color >= 0.5 || color < 0)
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

