using CarinaStudio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// <see cref="IImageFilter{TParams}"/> which performs color transformation by Lookup Table (LUT).
    /// </summary>
    class ColorLutImageFilter : BaseImageFilter<ColorLutImageFilter.Params>
    {
        /// <summary>
        /// Parameters.
        /// </summary>
        public class Params : ImageFilterParams
        {
            /// <summary>
            /// Lookup table for alpha.
            /// </summary>
            public IList<double> AlphaLookupTable { get; set; } = new double[0];

            /// <summary>
            /// Lookup table for blue color.
            /// </summary>
            public IList<double> BlueLookupTable { get; set; } = new double[0];

            /// <inheritdoc/>
            public override object Clone() => new Params()
            {
                AlphaLookupTable = this.AlphaLookupTable.ToArray(),
                BlueLookupTable = this.BlueLookupTable.ToArray(),
                GreenLookupTable = this.GreenLookupTable.ToArray(),
                RedLookupTable = this.RedLookupTable.ToArray()
            };

            /// <summary>
            /// Lookup table for green color.
            /// </summary>
            public IList<double> GreenLookupTable { get; set; } = new double[0];

            /// <summary>
            /// Lookup table for red color.
            /// </summary>
            public IList<double> RedLookupTable { get; set; } = new double[0];
        }


        // Build final lookup table for 8-bit color transformation.
        unsafe byte* BuildFinalLookupTable(IList<double> source, byte* lut)
        {
            if (source.Count != 256)
                throw new ArgumentException("Size of lookup table should be 256.");
            lut += 256;
            for (var n = 255; n >= 0; --n)
                *(--lut) = ImageProcessing.ClipToByte((int)(source[n] + 0.5));
            return lut;
        }


        // Build final lookup table for 16-bit color transformation.
        unsafe ushort* BuildFinalLookupTable(IList<double> source, ushort* lut)
        {
            if (source.Count != 65536)
                throw new ArgumentException("Size of lookup table should be 65536.");
            lut += 65536;
            for (var n = 65535; n >= 0; --n)
                *(--lut) = ImageProcessing.ClipToUInt16(source[n]);
            return lut;
        }


        /// <inheritdoc/>
        protected override unsafe void OnApplyFilter(IBitmapBuffer source, IBitmapBuffer result, Params parameters, CancellationToken cancellationToken)
        {
            // check state
            this.VerifyFormats(source, result);

            // apply transformations
            source.Memory.Pin(sourceBaseAddr =>
            {
                result.Memory.Pin(resultBaseAddr =>
                {
                    var sourceRowStride = source.RowBytes;
                    var resultRowStride = result.RowBytes;
                    var width = source.Width;
                    switch (source.Format)
                    {
                        case BitmapFormat.Bgra32:
                            {
                                var luts = (byte*)Marshal.AllocHGlobal(256 * 4);
                                var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
                                var packFunc = ImageProcessing.SelectBgra32Packing();
                                try
                                {
                                    // build LUT
                                    var rLut = this.BuildFinalLookupTable(parameters.RedLookupTable, luts);
                                    var gLut = this.BuildFinalLookupTable(parameters.GreenLookupTable, rLut + 256);
                                    var bLut = this.BuildFinalLookupTable(parameters.BlueLookupTable, gLut + 256);
                                    var aLut = this.BuildFinalLookupTable(parameters.AlphaLookupTable, bLut + 256);

                                    // apply
                                    Parallel.For(0, source.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
                                    {
                                        var r = (byte)0;
                                        var g = (byte)0;
                                        var b = (byte)0;
                                        var a = (byte)0;
                                        var sourcePixelPtr = (uint*)((byte*)sourceBaseAddr + sourceRowStride * y);
                                        var resultPixelPtr = (uint*)((byte*)resultBaseAddr + resultRowStride * y);
                                        for (var x = width; x > 0; --x, ++sourcePixelPtr, ++resultPixelPtr)
                                        {
                                            unpackFunc(*sourcePixelPtr, &b, &g, &r, &a);
                                            *resultPixelPtr = packFunc(bLut[b], gLut[g], rLut[r], aLut[a]);
                                        }
                                        if (cancellationToken.IsCancellationRequested)
                                            return;
                                    });
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal((IntPtr)luts);
                                }
                            }
                            break;
                        case BitmapFormat.Bgra64:
                            {
                                var luts = (ushort*)Marshal.AllocHGlobal(65536 * 4);
                                var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
                                var packFunc = ImageProcessing.SelectBgra64Packing();
                                try
                                {
                                    // build LUT
                                    var rLut = this.BuildFinalLookupTable(parameters.RedLookupTable, luts);
                                    var gLut = this.BuildFinalLookupTable(parameters.GreenLookupTable, rLut + 65536);
                                    var bLut = this.BuildFinalLookupTable(parameters.BlueLookupTable, gLut + 65536);
                                    var aLut = this.BuildFinalLookupTable(parameters.AlphaLookupTable, bLut + 65536);

                                    // apply
                                    Parallel.For(0, source.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
                                    {
                                        var r = (ushort)0;
                                        var g = (ushort)0;
                                        var b = (ushort)0;
                                        var a = (ushort)0;
                                        var sourcePixelPtr = (ulong*)((byte*)sourceBaseAddr + sourceRowStride * y);
                                        var resultPixelPtr = (ulong*)((byte*)resultBaseAddr + resultRowStride * y);
                                        for (var x = width; x > 0; --x, ++sourcePixelPtr, ++resultPixelPtr)
                                        {
                                            unpackFunc(*sourcePixelPtr, &b, &g, &r, &a);
                                            *resultPixelPtr = packFunc(bLut[b], gLut[g], rLut[r], aLut[a]);
                                        }
                                        if (cancellationToken.IsCancellationRequested)
                                            return;
                                    });
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal((IntPtr)luts);
                                }
                            }
                            break;
                    }
                });
            });
        }
    }
}
