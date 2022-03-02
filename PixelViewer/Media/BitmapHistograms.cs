using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.Collections;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// Histograms generated from <see cref="IBimapBuffer"/>.
    /// </summary>
    class BitmapHistograms
    {
        // Static fields.
        static readonly ILogger? Logger = AppSuiteApplication.CurrentOrNull?.LoggerFactory?.CreateLogger(nameof(BitmapHistograms));


        // Constructor.
        BitmapHistograms(int effectivePixelCount, IList<int> red, IList<int> green, IList<int> blue, IList<int> luminance, int medianOfLuminance)
        {
            this.ColorCount = red.Count;
            if (this.ColorCount != green.Count || this.ColorCount != blue.Count || this.ColorCount != luminance.Count)
                throw new ArgumentException();
            this.Blue = blue.AsReadOnly();
            this.EffectivePixelCount = effectivePixelCount;
            this.Green = green.AsReadOnly();
            this.Luminance = luminance.AsReadOnly();
            this.Maximum = Math.Max(Math.Max(red.Max(), green.Max()), Math.Max(blue.Max(), luminance.Max()));
            this.MedianOfLuminance = medianOfLuminance;
            this.Red = red.AsReadOnly();
        }


        /// <summary>
        /// Histogram of blue channel.
        /// </summary>
        public IList<int> Blue { get; }


        /// <summary>
        /// Get number of available colors for each channel.
        /// </summary>
        public int ColorCount { get; }


        /// <summary>
        /// Create <see cref="BitmapHistograms"/> asynchronously.
        /// </summary>
        /// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of histogram creation.</returns>
        public static Task<BitmapHistograms> CreateAsync(IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken) => bitmapBuffer.Format switch
        {
            BitmapFormat.Bgra32 => CreateFromBgra32Async(bitmapBuffer, cancellationToken),
            BitmapFormat.Bgra64 => CreateFromBgra64Async(bitmapBuffer, cancellationToken),
            _ => throw new NotSupportedException(),
        };


        // Create histograms from BGRA_32 bitmap.
        static async Task<BitmapHistograms> CreateFromBgra32Async(IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken)
        {
            bitmapBuffer = bitmapBuffer.Share();
            try
            {
                return await Task.Run(() =>
                {
                    var stopWatch = AppSuiteApplication.CurrentOrNull?.IsDebugMode == true
                        ? new Stopwatch().Also(it => it.Start())
                        : null;
                    var red = new int[256];
                    var green = new int[256];
                    var blue = new int[256];
                    var luminance = new int[256];
                    unsafe
                    {
                        bitmapBuffer.Memory.Pin(ptr =>
                        {
                            var rgbToLuminance = ImageProcessing.SelectRgb24ToLuminanceConversion();
                            var unpackFunc = ImageProcessing.SelectBgra32Unpacking();
                            var width = bitmapBuffer.Width;
                            var syncLock = new object();
                            ImageProcessing.ParallelFor(0, bitmapBuffer.Height, (y) =>
                            {
                                fixed (int* localHistograms = new int[256 * 4])
                                {
                                    var r = (byte)0;
                                    var g = (byte)0;
                                    var b = (byte)0;
                                    var a = (byte)0;
                                    var localRHistogram = localHistograms;
                                    var localGHistogram = localRHistogram + 256;
                                    var localBHistogram = localGHistogram + 256;
                                    var localLHistogram = localBHistogram + 256;
                                    var pixelPtr = (uint*)((byte*)ptr + y * bitmapBuffer.RowBytes);
                                    for (var x = width; x > 0; --x, ++pixelPtr)
                                    {
                                        unpackFunc(*pixelPtr, &b, &g, &r, &a);
                                        var l = rgbToLuminance(r, g, b);
                                        ++localRHistogram[r];
                                        ++localGHistogram[g];
                                        ++localBHistogram[b];
                                        ++localLHistogram[l];
                                    }
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                    lock (syncLock)
                                    {
                                        for (var i = 255; i >= 0; --i)
                                        {
                                            red[i] += localRHistogram[i];
                                            green[i] += localGHistogram[i];
                                            blue[i] += localBHistogram[i];
                                            luminance[i] += localLHistogram[i];
                                        }
                                    }
                                }
                            });
                        });
                    }
                    var halfPixelCount = (bitmapBuffer.Width * bitmapBuffer.Height) >> 1;
                    var accuPixelCount = 0;
                    var medianOfLuminance = 0;
                    for (int i = 0, colorCount = luminance.Length; i < colorCount; ++i)
                    {
                        accuPixelCount += luminance[i];
                        if (accuPixelCount >= halfPixelCount)
                        {
                            medianOfLuminance = i;
                            break;
                        }
                    }
                    if (stopWatch != null)
                        Logger.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to create histograms for {bitmapBuffer.Width}x{bitmapBuffer.Height} {bitmapBuffer.Format} bitmap");
                    return new BitmapHistograms(bitmapBuffer.Width * bitmapBuffer.Height, red, green, blue, luminance, medianOfLuminance);
                });
            }
            finally
            {
                bitmapBuffer.Dispose();
            }
        }


        // Create histograms from BGRA_64 bitmap.
        static async Task<BitmapHistograms> CreateFromBgra64Async(IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken)
        {
            bitmapBuffer = bitmapBuffer.Share();
            try
            {
                return await Task.Run(() =>
                {
                    var stopWatch = AppSuiteApplication.CurrentOrNull?.IsDebugMode == true
                        ? new Stopwatch().Also(it => it.Start())
                        : null;
                    var red = new int[256];
                    var green = new int[256];
                    var blue = new int[256];
                    var luminance = new int[256];
                    unsafe
                    {
                        bitmapBuffer.Memory.Pin(ptr =>
                        {
                            var rgbToLuminance = ImageProcessing.SelectRgb48ToLuminanceConversion();
                            var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
                            var width = bitmapBuffer.Width;
                            var syncLock = new object();
                            ImageProcessing.ParallelFor(0, bitmapBuffer.Height, (y) =>
                            {
                                fixed (int* localHistograms = new int[256 * 4])
                                {
                                    var r = (ushort)0;
                                    var g = (ushort)0;
                                    var b = (ushort)0;
                                    var a = (ushort)0;
                                    var localRHistogram = localHistograms;
                                    var localGHistogram = localRHistogram + 256;
                                    var localBHistogram = localGHistogram + 256;
                                    var localLHistogram = localBHistogram + 256;
                                    var pixelPtr = (ulong*)((byte*)ptr + y * bitmapBuffer.RowBytes);
                                    for (var x = width; x > 0; --x, ++pixelPtr)
                                    {
                                        unpackFunc(*pixelPtr, &b, &g, &r, &a);
                                        var l = rgbToLuminance(r, g, b);
                                        ++localRHistogram[r >> 8];
                                        ++localGHistogram[g >> 8];
                                        ++localBHistogram[b >> 8];
                                        ++localLHistogram[l >> 8];
                                    }
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                    lock (syncLock)
                                    {
                                        for (var i = 255; i >= 0; --i)
                                        {
                                            red[i] += localRHistogram[i];
                                            green[i] += localGHistogram[i];
                                            blue[i] += localBHistogram[i];
                                            luminance[i] += localLHistogram[i];
                                        }
                                    }
                                }
                            });
                        });
                    }
                    var halfPixelCount = (bitmapBuffer.Width * bitmapBuffer.Height) >> 1;
                    var accuPixelCount = 0;
                    var medianOfLuminance = 0;
                    for (int i = 0, colorCount = luminance.Length; i < colorCount; ++i)
                    {
                        accuPixelCount += luminance[i];
                        if (accuPixelCount >= halfPixelCount)
                        {
                            medianOfLuminance = i;
                            break;
                        }
                    }
                    if (stopWatch != null)
                        Logger.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to create histograms for {bitmapBuffer.Width}x{bitmapBuffer.Height} {bitmapBuffer.Format} bitmap");
                    return new BitmapHistograms(bitmapBuffer.Width * bitmapBuffer.Height, red, green, blue, luminance, medianOfLuminance);
                });
            }
            finally
            {
                bitmapBuffer.Dispose();
            }
        }


        /// <summary>
        /// Get number of effective pixels to generate this histograms.
        /// </summary>
        public int EffectivePixelCount { get; }


        /// <summary>
        /// Histogram of green channel.
        /// </summary>
        public IList<int> Green { get; }


        /// <summary>
        /// Histogram of luminance.
        /// </summary>
        public IList<int> Luminance { get; }


        /// <summary>
        /// Get maximum value in all histograms.
        /// </summary>
        public int Maximum { get; }


        /// <summary>
        /// Get median value of luminance.
        /// </summary>
        public int MedianOfLuminance { get; }


        /// <summary>
        /// Histogram of red channel.
        /// </summary>
        public IList<int> Red { get; }
    }
}
