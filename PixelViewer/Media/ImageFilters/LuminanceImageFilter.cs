using CarinaStudio;
using System;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// Filter to convert image into luminance (grayscale) image.
    /// </summary>
    class LuminanceImageFilter : BaseImageFilter<ImageFilterParams>
    {
        /// <inheritdoc/>.
        protected override unsafe void OnApplyFilter(IBitmapBuffer source, IBitmapBuffer result, ImageFilterParams parameters, CancellationToken cancellationToken)
        {
            this.VerifyFormats(source, result);
            var sourceRowStride = source.RowBytes;
            var resultRowStride = result.RowBytes;
            var width = source.Width;
            var height = source.Height;
            source.Memory.Pin(srcBaseAddress =>
            {
                result.Memory.Pin(resultBaseAddress =>
                {
                    var sourceRowPtr = (byte*)srcBaseAddress;
                    var resultRowPtr = (byte*)resultBaseAddress;
                    switch (source.Format)
                    {
                        case BitmapFormat.Bgra32:
                            {
                                var b = (byte)0;
                                var g = (byte)0;
                                var r = (byte)0;
                                var a = (byte)0;
                                var unpackBgraFunc = ImageProcessing.SelectBgra32Unpacking();
                                var packBgra32Func = ImageProcessing.SelectBgra32Packing();
                                var rgbToLuminanceFunc = ImageProcessing.SelectRgb24ToLuminanceConversion();
                                for (var y = height; y > 0; --y, sourceRowPtr += sourceRowStride, resultRowPtr += resultRowStride)
                                {
                                    var sourcePixelPtr = (uint*)sourceRowPtr;
                                    var resultPixelPtr = (uint*)resultRowPtr;
                                    for (var x = width; x > 0; --x, ++sourcePixelPtr, ++resultPixelPtr)
                                    {
                                        unpackBgraFunc(*sourcePixelPtr, &b, &g, &r, &a);
                                        var l = rgbToLuminanceFunc(r, g, b);
                                        *resultPixelPtr = packBgra32Func(l, l, l, a);
                                    }
                                    if (cancellationToken.IsCancellationRequested)
                                        return;
                                }
                            }
                            break;
                        case BitmapFormat.Bgra64:
                            {
                                var b = (ushort)0;
                                var g = (ushort)0;
                                var r = (ushort)0;
                                var a = (ushort)0;
                                var unpackBgraFunc = ImageProcessing.SelectBgra64Unpacking();
                                var packBgra32Func = ImageProcessing.SelectBgra64Packing();
                                var rgbToLuminanceFunc = ImageProcessing.SelectRgb48ToLuminanceConversion();
                                for (var y = height; y > 0; --y, sourceRowPtr += sourceRowStride, resultRowPtr += resultRowStride)
                                {
                                    var sourcePixelPtr = (ulong*)sourceRowPtr;
                                    var resultPixelPtr = (ulong*)resultRowPtr;
                                    for (var x = width; x > 0; --x, ++sourcePixelPtr, ++resultPixelPtr)
                                    {
                                        unpackBgraFunc(*sourcePixelPtr, &b, &g, &r, &a);
                                        var l = rgbToLuminanceFunc(r, g, b);
                                        *resultPixelPtr = packBgra32Func(l, l, l, a);
                                    }
                                    if (cancellationToken.IsCancellationRequested)
                                        return;
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException();
                    }
                });
            });
        }
    }
}
