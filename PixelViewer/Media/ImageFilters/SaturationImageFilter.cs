using CarinaStudio;
using CarinaStudio.Configuration;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// Image filter to adjust saturation and vibrance.
    /// </summary>
    class SaturationImageFilter : BaseImageFilter<SaturationImageFilter.Params>
    {
        /// <summary>
        /// Parameters.
        /// </summary>
        public class Params : ImageFilterParams
        {
            // Fields.
            double saturation;
            double vibrance;


            /// <inheritdoc/>
            public override object Clone() => new Params()
            {
                saturation = this.saturation,
                vibrance = this.vibrance,
            };


            /// <summary>
            /// Get or set vibrance. The range is [-1.0, 1.0].
            /// </summary>
            /// <value></value>
            public double Saturation
            {
                get => this.saturation;
                set
                {
                    if (!double.IsFinite(value) || value < -1 || value > 1)
                        throw new ArgumentOutOfRangeException();
                    this.saturation = value;
                }
            }


            /// <summary>
            /// Get or set vibrance. The range is [-1.0, 1.0].
            /// </summary>
            /// <value></value>
            public double Vibrance
            {
                get => this.vibrance;
                set
                {
                    if (!double.IsFinite(value) || value < -1 || value > 1)
                        throw new ArgumentOutOfRangeException();
                    this.vibrance = value;
                }
            }
        }


        // Find min and max color.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void FindMinMax(byte x, byte y, byte z, out byte min, out byte max)
        {
            if (x > y)
            {
                if (x > z)
                {
                    min = (y > z) ? z : y;
                    max = x;
                }
                else
                {
                    min = y;
                    max = z;
                }
            }
            else if (x > z)
            {
                min = z;
                max = y;
            }
            else
            {
                min = x;
                max = (y > z) ? y : z;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void FindMinMax(ushort x, ushort y, ushort z, out ushort min, out ushort max)
        {
            if (x > y)
            {
                if (x > z)
                {
                    min = (y > z) ? z : y;
                    max = x;
                }
                else
                {
                    min = y;
                    max = z;
                }
            }
            else if (x > z)
            {
                min = z;
                max = y;
            }
            else
            {
                min = x;
                max = (y > z) ? y : z;
            }
        }


        /// <inheritdoc/>
        protected override unsafe void OnApplyFilter(IBitmapBuffer source, IBitmapBuffer result, Params parameters, CancellationToken cancellationToken)
        {
            this.VerifyFormats(source, result);
            var saturation = Math.Max(-1, parameters.Saturation * (App.CurrentOrNull?.Configuration).GetValueOrDefault(ConfigurationKeys.SaturationAdjustmentSensitivity));
            var redMajorPixelRatio = (App.CurrentOrNull?.Configuration).GetValueOrDefault(ConfigurationKeys.VibranceAdjustmentRedMajorPixelRatio);
            var vibrance = parameters.Vibrance * (App.CurrentOrNull?.Configuration).GetValueOrDefault(ConfigurationKeys.VibranceAdjustmentSensitivity);
            var hasVibrance = Math.Abs(vibrance) >= 0.001;
            if (Math.Abs(saturation) >= 0.001 || hasVibrance)
            {
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
                                var normTable = ImageProcessing.ColorNormalizingTableUnsafe8;
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
                                        FindMinMax(r, g, b, out var min, out var max);
                                        var minMaxDiff = (max - min);
                                        if (minMaxDiff == 0)
                                        {
                                            *destPixelPtr = *srcPixelPtr;
                                            continue;
                                        }
                                        var avg = (r + g + g + b) >> 2;
                                        var shiftRatio = saturation;
                                        if (hasVibrance)
                                        {
                                            var satRatio = normTable[minMaxDiff];
                                            var vibrationShiftRatio = (1 - satRatio) * vibrance;
                                            if (max == r)
                                                vibrationShiftRatio *= (1 + Math.Abs(g - b) / (double)minMaxDiff) * redMajorPixelRatio;
                                            shiftRatio += vibrationShiftRatio;
                                            if (shiftRatio < -1)
                                                shiftRatio = -1;
                                        }
                                        r = ImageProcessing.ClipToByte(r + (r - avg) * shiftRatio);
                                        g = ImageProcessing.ClipToByte(g + (g - avg) * shiftRatio);
                                        b = ImageProcessing.ClipToByte(b + (b - avg) * shiftRatio);
                                        *destPixelPtr = packFunc(b, g, r, a);
                                    }
                                });
                            }
                            break;
                        case BitmapFormat.Bgra64:
                            {
                                var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
                                var packFunc = ImageProcessing.SelectBgra64Packing();
                                var normTable = ImageProcessing.ColorNormalizingTableUnsafe16;
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
                                        FindMinMax(r, g, b, out var min, out var max);
                                        var minMaxDiff = (max - min);
                                        if (minMaxDiff == 0)
                                        {
                                            *destPixelPtr = *srcPixelPtr;
                                            continue;
                                        }
                                        var avg = (r + g + g + b) >> 2;
                                        var shiftRatio = saturation;
                                        if (hasVibrance)
                                        {
                                            var satRatio = normTable[minMaxDiff];
                                            var vibrationShiftRatio = (1 - satRatio) * vibrance;
                                            if (max == r)
                                                vibrationShiftRatio *= (1 + Math.Abs(g - b) / (double)minMaxDiff) * redMajorPixelRatio;
                                            shiftRatio += vibrationShiftRatio;
                                            if (shiftRatio < -1)
                                                shiftRatio = -1;
                                        }
                                        r = ImageProcessing.ClipToUInt16(r + (r - avg) * shiftRatio);
                                        g = ImageProcessing.ClipToUInt16(g + (g - avg) * shiftRatio);
                                        b = ImageProcessing.ClipToUInt16(b + (b - avg) * shiftRatio);
                                        *destPixelPtr = packFunc(b, g, r, a);
                                    }
                                });
                            }
                            break;
                        default:
                            throw new NotSupportedException();
                    }
                });
            }
            else
                source.CopyTo(result);
        }
    }
}