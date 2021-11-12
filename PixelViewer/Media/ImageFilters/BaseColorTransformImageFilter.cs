using CarinaStudio;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// Base implementation of <see cref="IImageFilter{TParams}"/> for color transformation.
    /// </summary>
    abstract class BaseColorTransformImageFilter<TParams> : BaseImageFilter<TParams> where TParams : ImageFilterParams
    {
        // Build color transformation table for 8-bit color.
        unsafe void BuildColorTramsform(byte* transform, double factor)
        {
            transform += 255;
            for (var n = 255; n >= 0; --n)
                *(transform--) = ImageProcessing.ClipToByte((int)(n * factor + 0.5));
        }


        // Build color transformation table for 16-bit color.
        unsafe void BuildColorTramsform(ushort* transform, double factor)
        {
            transform += 65535;
            for (var n = 65535; n >= 0; --n)
                *(transform--) = ImageProcessing.ClipToUInt16((int)(n * factor + 0.5));
        }


        /// <inheritdoc/>
        protected sealed override unsafe void OnApplyFilter(IBitmapBuffer source, IBitmapBuffer result, TParams parameters, CancellationToken cancellationToken)
        {
            // check state
            this.VerifyFormats(source, result);

            // get transformations
            this.ParseColorTransforms(parameters, out var rFactor, out var gFactor, out var bFactor, out var aFactor);

            // copy directly
            if (Math.Abs(rFactor - 1) <= 0.001 && Math.Abs(gFactor - 1) <= 0.001 
                && Math.Abs(bFactor - 1) <= 0.001 && Math.Abs(aFactor - 1) <= 0.001)
            {
                source.CopyTo(result);
                return;
            }

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
                                var transforms = (byte*)Marshal.AllocHGlobal(256 * 4);
                                var rTransform = transforms;
                                var gTransform = rTransform + 256;
                                var bTransform = gTransform + 256;
                                var aTransform = bTransform + 256;
                                var r = (byte)0;
                                var g = (byte)0;
                                var b = (byte)0;
                                var a = (byte)0;
                                var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
                                var packFunc = ImageProcessing.SelectBgra32Packing();
                                try
                                {
                                    // build transformation tables
                                    this.BuildColorTramsform(rTransform, rFactor);
                                    this.BuildColorTramsform(gTransform, gFactor);
                                    this.BuildColorTramsform(bTransform, bFactor);
                                    this.BuildColorTramsform(aTransform, aFactor);

                                    // apply
                                    for (var y = height; y > 0; --y, sourceRowPtr += sourceRowStride, resultRowPtr += resultRowStride)
                                    {
                                        var sourcePixelPtr = (uint*)sourceRowPtr;
                                        var resultPixelPtr = (uint*)resultRowPtr;
                                        for (var x = width; x > 0; --x, ++sourcePixelPtr, ++resultPixelPtr)
                                        {
                                            unpackFunc(*sourcePixelPtr, &b, &g, &r, &a);
                                            *resultPixelPtr = packFunc(bTransform[b], gTransform[g], rTransform[r], aTransform[a]);
                                        }
                                        if (cancellationToken.IsCancellationRequested)
                                            return;
                                    }
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal((IntPtr)transforms);
                                }
                            }
                            break;
                        case BitmapFormat.Bgra64:
                            {
                                var transforms = (ushort*)Marshal.AllocHGlobal(65536 * 4);
                                var rTransform = transforms;
                                var gTransform = rTransform + 65536;
                                var bTransform = gTransform + 65536;
                                var aTransform = bTransform + 65536;
                                var r = (ushort)0;
                                var g = (ushort)0;
                                var b = (ushort)0;
                                var a = (ushort)0;
                                var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
                                var packFunc = ImageProcessing.SelectBgra64Packing();
                                try
                                {
                                    // build transformation tables
                                    this.BuildColorTramsform(rTransform, rFactor);
                                    this.BuildColorTramsform(gTransform, gFactor);
                                    this.BuildColorTramsform(bTransform, bFactor);
                                    this.BuildColorTramsform(aTransform, aFactor);

                                    // apply
                                    for (var y = height; y > 0; --y, sourceRowPtr += sourceRowStride, resultRowPtr += resultRowStride)
                                    {
                                        var sourcePixelPtr = (ulong*)sourceRowPtr;
                                        var resultPixelPtr = (ulong*)resultRowPtr;
                                        for (var x = width; x > 0; --x, ++sourcePixelPtr, ++resultPixelPtr)
                                        {
                                            unpackFunc(*sourcePixelPtr, &b, &g, &r, &a);
                                            *resultPixelPtr = packFunc(bTransform[b], gTransform[g], rTransform[r], aTransform[a]);
                                        }
                                        if (cancellationToken.IsCancellationRequested)
                                            return;
                                    }
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal((IntPtr)transforms);
                                }
                            }
                            break;
                    }
                });
            });
        }


        /// <summary>
        /// Parse color transforms from filtering parameters.
        /// </summary>
        /// <param name="parameters">Parameters.</param>
        /// <param name="rFactor">Factor of red color.</param>
        /// <param name="gFactor">Factor of green color.</param>
        /// <param name="bFactor">Factor of blue color.</param>
        /// <param name="aFactor">Factor of alpha.</param>
        protected abstract void ParseColorTransforms(TParams parameters, out double rFactor, out double gFactor, out double bFactor, out double aFactor);
    }
}
