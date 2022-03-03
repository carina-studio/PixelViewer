using CarinaStudio;
using CarinaStudio.Configuration;
using System;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// Image filter to adjust vibrance.
    /// </summary>
    class VibranceImageFilter : BaseImageFilter<VibranceImageFilter.Params>
    {
        /// <summary>
        /// Parameters.
        /// </summary>
        public class Params : ImageFilterParams
        {
            // Fields.
            double vibrance;


            /// <inheritdoc/>
            public override object Clone() => new Params()
            {
                vibrance = this.vibrance,
            };


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


        /// <inheritdoc/>
        protected override unsafe void OnApplyFilter(IBitmapBuffer source, IBitmapBuffer result, Params parameters, CancellationToken cancellationToken)
        {
            this.VerifyFormats(source, result);
            var sensitivity = App.CurrentOrNull?.Configuration?.GetValueOrDefault(ConfigurationKeys.VibranceAdjustmentSensitivity)
                        ?? ConfigurationKeys.VibranceAdjustmentSensitivity.DefaultValue;
            var vibrance = parameters.Vibrance * sensitivity;
            if (Math.Abs(vibrance) >= 0.01)
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
                                vibrance /= 255;
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
                                        var avg = (r + g + b) / 3.0;
                                        var max = Math.Max(r, Math.Max(g, b));
                                        var ratio = Math.Abs(max - avg) * vibrance;
                                        var rDiff = (max - r);
                                        var gDiff = (max - g);
                                        var bDiff = (max - b);
                                        if (rDiff != 0)
                                            r = ImageProcessing.ClipToByte(r - rDiff * ratio);
                                        if (gDiff != 0)
                                            g = ImageProcessing.ClipToByte(g - gDiff * ratio);
                                        if (bDiff != 0)
                                            b = ImageProcessing.ClipToByte(b - bDiff * ratio);
                                        *destPixelPtr = packFunc(b, g, r, a);
                                    }
                                });
                            }
                            break;
                        case BitmapFormat.Bgra64:
                            {
                                var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
                                var packFunc = ImageProcessing.SelectBgra64Packing();
                                vibrance /= 65535;
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
                                        var avg = (r + g + b) / 3.0;
                                        var max = Math.Max(r, Math.Max(g, b));
                                        var ratio = Math.Abs(max - avg) * vibrance;
                                        var rDiff = (max - r);
                                        var gDiff = (max - g);
                                        var bDiff = (max - b);
                                        if (rDiff != 0)
                                            r = ImageProcessing.ClipToUInt16(r - rDiff * ratio);
                                        if (gDiff != 0)
                                            g = ImageProcessing.ClipToUInt16(g - gDiff * ratio);
                                        if (bDiff != 0)
                                            b = ImageProcessing.ClipToUInt16(b - bDiff * ratio);
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