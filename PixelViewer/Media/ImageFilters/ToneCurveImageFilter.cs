using CarinaStudio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageFilters;

/// <summary>
/// <see cref="IImageFilter{TParams}"/> which applies a tone curve to the luminance of each pixel and scales its linear RGB color accordingly, so that the hue and saturation of the pixel are kept.
/// </summary>
class ToneCurveImageFilter : BaseImageFilter<ToneCurveImageFilter.Params>
{
    /// <summary>
    /// Luminance weights of BT.709 primaries.
    /// </summary>
    public static readonly (double, double, double) Bt709LuminanceWeights = (0.2126, 0.7152, 0.0722);


    /// <summary>
    /// Parameters.
    /// </summary>
    public class Params : ImageFilterParams
    {
        /// <inheritdoc/>
        public override object Clone() => new Params()
        {
            ColorTransferFuncFromLinear = this.ColorTransferFuncFromLinear,
            ColorTransferFuncToLinear = this.ColorTransferFuncToLinear,
            LookupTable = this.LookupTable.ToArray(),
            LuminanceWeights = this.LuminanceWeights,
        };

        /// <summary>
        /// Function to transfer normalized linear color to color of image, or Null if color of image is linear.
        /// </summary>
        /// <remarks>The function is used only when <see cref="ColorTransferFuncToLinear"/> is also defined, and it should be the inverse of that function.</remarks>
        public Func<double, double>? ColorTransferFuncFromLinear { get; set; }

        /// <summary>
        /// Function to transfer normalized color of image to linear color, or Null if color of image is linear.
        /// </summary>
        /// <remarks>The function is used only when <see cref="ColorTransferFuncFromLinear"/> is also defined, and it should be the inverse of that function.</remarks>
        public Func<double, double>? ColorTransferFuncToLinear { get; set; }

        /// <summary>
        /// Tone curve as lookup table which maps color of image to color of image. The size should be 256 for 8-bit color and 65536 for 16-bit color.
        /// </summary>
        public IList<double> LookupTable { get; set; } = [];

        /// <summary>
        /// Weights of linear R, G and B to calculate luminance.
        /// </summary>
        public (double, double, double) LuminanceWeights { get; set; } = Bt709LuminanceWeights;
    }


    // Constants.
    const int LinearTableSize = 65536;


    // Apply tone curve to normalized linear color.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ApplyToneCurve(ref double r, ref double g, ref double b, double[] curveTable, double weightR, double weightG, double weightB)
    {
        // map luminance by tone curve
        var luminance = weightR * r + weightG * g + weightB * b;
        if (luminance <= 0)
        {
            var blackLuminance = curveTable[0];
            r = blackLuminance;
            g = blackLuminance;
            b = blackLuminance;
            return;
        }
        var targetLuminance = Interpolate(curveTable, luminance * (LinearTableSize - 1));

        // scale color
        var gain = targetLuminance / luminance;
        r *= gain;
        g *= gain;
        b *= gain;

        // keep hue by mixing with white when color exceeds the range
        var max = Math.Max(r, Math.Max(g, b));
        if (max <= 1)
            return;
        if (targetLuminance >= 1)
        {
            r = 1;
            g = 1;
            b = 1;
            return;
        }
        r /= max;
        g /= max;
        b /= max;
        var scaledLuminance = targetLuminance / max;
        var whiteRatio = (targetLuminance - scaledLuminance) / (1 - scaledLuminance);
        r += (1 - r) * whiteRatio;
        g += (1 - g) * whiteRatio;
        b += (1 - b) * whiteRatio;
    }


    // Build table which maps linear luminance to linear luminance by tone curve.
    static double[] BuildCurveTable(IList<double> lut, Func<double, double> toLinear, Func<double, double> fromLinear)
    {
        var maxColor = lut.Count - 1.0;
        var table = new double[LinearTableSize];
        for (var i = LinearTableSize - 1; i >= 0; --i)
        {
            var color = Interpolate(lut, fromLinear(i / (LinearTableSize - 1.0)) * maxColor) / maxColor;
            table[i] = toLinear(Math.Clamp(color, 0, 1));
        }
        return table;
    }


    // Build table which maps linear color to normalized color of image.
    static double[] BuildFromLinearTable(Func<double, double> fromLinear)
    {
        var table = new double[LinearTableSize];
        for (var i = LinearTableSize - 1; i >= 0; --i)
            table[i] = fromLinear(i / (LinearTableSize - 1.0));
        return table;
    }


    // Build table which maps color of image to normalized linear color.
    static double[] BuildToLinearTable(int size, Func<double, double> toLinear)
    {
        var maxColor = size - 1.0;
        var table = new double[size];
        for (var i = size - 1; i >= 0; --i)
            table[i] = toLinear(i / maxColor);
        return table;
    }


    // Interpolate value in table linearly.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Interpolate(IList<double> table, double position)
    {
        if (position <= 0)
            return table[0];
        var lastIndex = table.Count - 1;
        if (position >= lastIndex)
            return table[lastIndex];
        var index = (int)position;
        var value = table[index];
        return value + (table[index + 1] - value) * (position - index);
    }


    // Interpolate value in table linearly.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Interpolate(double[] table, double position)
    {
        if (position <= 0)
            return table[0];
        var lastIndex = table.Length - 1;
        if (position >= lastIndex)
            return table[lastIndex];
        var index = (int)position;
        var value = table[index];
        return value + (table[index + 1] - value) * (position - index);
    }


    /// <inheritdoc/>
    protected override unsafe void OnApplyFilter(IBitmapBuffer source, IBitmapBuffer result, Params parameters, CancellationToken cancellationToken)
    {
        // check parameters
        this.VerifyFormats(source, result);
        var lut = parameters.LookupTable;
        var colorCount = source.Format switch
        {
            BitmapFormat.Bgra32 => 256,
            BitmapFormat.Bgra64 => 65536,
            _ => throw new NotSupportedException(),
        };
        if (lut.Count != colorCount)
            throw new ArgumentException($"Size of lookup table should be {colorCount}.");
        var (weightR, weightG, weightB) = parameters.LuminanceWeights;
        var weightSum = weightR + weightG + weightB;
        if (!double.IsFinite(weightSum) || weightSum <= 0 || weightR < 0 || weightG < 0 || weightB < 0)
            throw new ArgumentException("Invalid luminance weights.");
        weightR /= weightSum;
        weightG /= weightSum;
        weightB /= weightSum;

        // build tables
        var toLinear = parameters.ColorTransferFuncToLinear;
        var fromLinear = parameters.ColorTransferFuncFromLinear;
        if (toLinear is null || fromLinear is null)
        {
            toLinear = static it => Math.Clamp(it, 0, 1);
            fromLinear = static it => Math.Clamp(it, 0, 1);
        }
        var toLinearTable = BuildToLinearTable(colorCount, toLinear);
        var fromLinearTable = BuildFromLinearTable(fromLinear);
        var curveTable = BuildCurveTable(lut, toLinear, fromLinear);
        cancellationToken.ThrowIfCancellationRequested();

        // apply
        (source.Memory, result.Memory).Pin((srcBaseAddress, destBaseAddress) =>
        {
            var width = source.Width;
            var srcRowStride = source.RowBytes;
            var destRowStride = result.RowBytes;
            switch (source.Format)
            {
                case BitmapFormat.Bgra32:
                    {
                        var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
                        var packFunc = ImageProcessing.SelectBgra32Packing();
                        ImageProcessing.ParallelFor(0, source.Height, y =>
                        {
                            var srcPixelPtr = (uint*)((byte*)srcBaseAddress + (srcRowStride * y));
                            var destPixelPtr = (uint*)((byte*)destBaseAddress + (destRowStride * y));
                            var a = (byte)0;
                            var r = (byte)0;
                            var g = (byte)0;
                            var b = (byte)0;
                            for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
                            {
                                unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
                                var linearR = toLinearTable[r];
                                var linearG = toLinearTable[g];
                                var linearB = toLinearTable[b];
                                ApplyToneCurve(ref linearR, ref linearG, ref linearB, curveTable, weightR, weightG, weightB);
                                *destPixelPtr = packFunc(
                                    ImageProcessing.ClipToByte(Interpolate(fromLinearTable, linearB * (LinearTableSize - 1)) * 255),
                                    ImageProcessing.ClipToByte(Interpolate(fromLinearTable, linearG * (LinearTableSize - 1)) * 255),
                                    ImageProcessing.ClipToByte(Interpolate(fromLinearTable, linearR * (LinearTableSize - 1)) * 255),
                                    a);
                            }
                        });
                    }
                    break;
                case BitmapFormat.Bgra64:
                    {
                        var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
                        var packFunc = ImageProcessing.SelectBgra64Packing();
                        ImageProcessing.ParallelFor(0, source.Height, y =>
                        {
                            var srcPixelPtr = (ulong*)((byte*)srcBaseAddress + (srcRowStride * y));
                            var destPixelPtr = (ulong*)((byte*)destBaseAddress + (destRowStride * y));
                            var a = (ushort)0;
                            var r = (ushort)0;
                            var g = (ushort)0;
                            var b = (ushort)0;
                            for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
                            {
                                unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
                                var linearR = toLinearTable[r];
                                var linearG = toLinearTable[g];
                                var linearB = toLinearTable[b];
                                ApplyToneCurve(ref linearR, ref linearG, ref linearB, curveTable, weightR, weightG, weightB);
                                *destPixelPtr = packFunc(
                                    ImageProcessing.ClipToUInt16(Interpolate(fromLinearTable, linearB * (LinearTableSize - 1)) * 65535 + 0.5),
                                    ImageProcessing.ClipToUInt16(Interpolate(fromLinearTable, linearG * (LinearTableSize - 1)) * 65535 + 0.5),
                                    ImageProcessing.ClipToUInt16(Interpolate(fromLinearTable, linearR * (LinearTableSize - 1)) * 65535 + 0.5),
                                    a);
                            }
                        });
                    }
                    break;
                default:
                    throw new NotSupportedException();
            }
        });
    }


    /// <summary>
    /// Select weights of linear R, G and B to calculate luminance in given color space.
    /// </summary>
    /// <param name="colorSpace">Color space.</param>
    /// <returns>Weights of R, G and B, or <see cref="Bt709LuminanceWeights"/> if they cannot be determined.</returns>
    public static (double, double, double) SelectLuminanceWeights(ColorSpace colorSpace)
    {
        var weightR = colorSpace.RgbToXyz(1.0, 0.0, 0.0).Item2;
        var weightG = colorSpace.RgbToXyz(0.0, 1.0, 0.0).Item2;
        var weightB = colorSpace.RgbToXyz(0.0, 0.0, 1.0).Item2;
        var weightSum = weightR + weightG + weightB;
        if (!double.IsFinite(weightSum) || weightSum <= 0 || weightR < 0 || weightG < 0 || weightB < 0)
            return Bt709LuminanceWeights;
        return (weightR / weightSum, weightG / weightSum, weightB / weightSum);
    }
}
