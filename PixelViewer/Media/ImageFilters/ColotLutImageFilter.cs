using CarinaStudio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// <see cref="IImageFilter{TParams}"/> which performs color transformation by Lookup Table (LUT).
    /// </summary>
    class ColotLutImageFilter : BaseImageFilter<ColotLutImageFilter.Params>
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
                    var sourceRowPtr = (byte*)sourceBaseAddr;
                    var resultRowPtr = (byte*)resultBaseAddr;
                    var width = source.Width;
                    var height = source.Height;
                    switch (source.Format)
                    {
                        case BitmapFormat.Bgra32:
                            {
                                var luts = (byte*)Marshal.AllocHGlobal(256 * 4);
                                var r = (byte)0;
                                var g = (byte)0;
                                var b = (byte)0;
                                var a = (byte)0;
                                var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
                                var packFunc = ImageProcessing.SelectBgra32Packing();
                                try
                                {
                                    // build LUT
                                    var rLut = this.BuildFinalLookupTable(parameters.RedLookupTable, luts);
                                    var gLut = this.BuildFinalLookupTable(parameters.RedLookupTable, rLut + 256);
                                    var bLut = this.BuildFinalLookupTable(parameters.RedLookupTable, gLut + 256);
                                    var aLut = this.BuildFinalLookupTable(parameters.RedLookupTable, bLut + 256);

                                    // apply
                                    for (var y = height; y > 0; --y, sourceRowPtr += sourceRowStride, resultRowPtr += resultRowStride)
                                    {
                                        var sourcePixelPtr = (uint*)sourceRowPtr;
                                        var resultPixelPtr = (uint*)resultRowPtr;
                                        for (var x = width; x > 0; --x, ++sourcePixelPtr, ++resultPixelPtr)
                                        {
                                            unpackFunc(*sourcePixelPtr, &b, &g, &r, &a);
                                            *resultPixelPtr = packFunc(bLut[b], gLut[g], rLut[r], aLut[a]);
                                        }
                                        if (cancellationToken.IsCancellationRequested)
                                            return;
                                    }
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
                                var r = (ushort)0;
                                var g = (ushort)0;
                                var b = (ushort)0;
                                var a = (ushort)0;
                                var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
                                var packFunc = ImageProcessing.SelectBgra64Packing();
                                try
                                {
                                    // build LUT
                                    var rLut = this.BuildFinalLookupTable(parameters.RedLookupTable, luts);
                                    var gLut = this.BuildFinalLookupTable(parameters.RedLookupTable, rLut + 65536);
                                    var bLut = this.BuildFinalLookupTable(parameters.RedLookupTable, gLut + 65536);
                                    var aLut = this.BuildFinalLookupTable(parameters.RedLookupTable, bLut + 65536);

                                    // apply
                                    for (var y = height; y > 0; --y, sourceRowPtr += sourceRowStride, resultRowPtr += resultRowStride)
                                    {
                                        var sourcePixelPtr = (ulong*)sourceRowPtr;
                                        var resultPixelPtr = (ulong*)resultRowPtr;
                                        for (var x = width; x > 0; --x, ++sourcePixelPtr, ++resultPixelPtr)
                                        {
                                            unpackFunc(*sourcePixelPtr, &b, &g, &r, &a);
                                            *resultPixelPtr = packFunc(bLut[b], gLut[g], rLut[r], aLut[a]);
                                        }
                                        if (cancellationToken.IsCancellationRequested)
                                            return;
                                    }
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
