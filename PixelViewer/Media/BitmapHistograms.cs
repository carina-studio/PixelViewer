using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.Collections;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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


        // Fields.
        readonly int highlightOfBlue;
        readonly int highlightOfGreen;
        readonly int highlightOfLuminance;
        readonly int highlightOfRed;
        readonly int maxOfBlue;
        readonly int maxOfGreen;
        readonly int maxOfLuminance;
        readonly int maxOfRed;
        readonly double meanOfBlue;
        readonly double meanOfGreen;
        readonly double meanOfLuminance;
        readonly double meanOfRed;
        readonly int medianOfBlue;
        readonly int medianOfGreen;
        readonly int medianOfLuminance;
        readonly int medianOfRed;
        readonly int minOfBlue;
        readonly int minOfGreen;
        readonly int minOfLuminance;
        readonly int minOfRed;
        readonly int shadowOfBlue;
        readonly int shadowOfGreen;
        readonly int shadowOfLuminance;
        readonly int shadowOfRed;


        // Constructor.
        BitmapHistograms(int effectivePixelCount, IList<int> red, IList<int> green, IList<int> blue, IList<int> luminance)
        {
            this.ColorCount = red.Count;
            if (this.ColorCount != green.Count || this.ColorCount != blue.Count || this.ColorCount != luminance.Count)
                throw new ArgumentException();
            this.Blue = blue.AsReadOnly();
            this.EffectivePixelCount = effectivePixelCount;
            this.Green = green.AsReadOnly();
            this.Luminance = luminance.AsReadOnly();
            this.Maximum = Math.Max(Math.Max(red.Max(), green.Max()), Math.Max(blue.Max(), luminance.Max()));
            this.Red = red.AsReadOnly();
            Analyze(luminance, effectivePixelCount, out this.meanOfLuminance, out this.minOfLuminance, out this.shadowOfLuminance, out this.medianOfLuminance, out this.highlightOfLuminance, out this.maxOfLuminance);
            Analyze(red, effectivePixelCount, out this.meanOfRed, out this.minOfRed, out this.shadowOfRed, out this.medianOfRed, out this.highlightOfRed, out this.maxOfRed);
            Analyze(green, effectivePixelCount, out this.meanOfGreen, out this.minOfGreen, out this.shadowOfGreen, out this.medianOfGreen, out this.highlightOfGreen, out this.maxOfGreen);
            Analyze(blue, effectivePixelCount, out this.meanOfBlue, out this.minOfBlue, out this.shadowOfBlue, out this.medianOfBlue, out this.highlightOfBlue, out this.maxOfBlue);
        }


        // Analyze histogram.
        static void Analyze(IList<int> histogram, int pixelCount, out double mean, out int min, out int shadow, out int median, out int highlight, out int max)
        {
            var shadowPixelCount = pixelCount >> 2;
            var medianPixelCount = pixelCount >> 1;
            var highlightPixelCount = (pixelCount * 3) >> 2;
            var accuPixelCount = 0;
            mean = 0.0;
            min = 0;
            shadow = 0;
            median = 0;
            highlight = 0;
            max = 0;
            for (int i = 0, colorCount = histogram.Count; i < colorCount; ++i)
            {
                var count = histogram[i];
                if (count == 0)
                    continue;
                var prevAccuPixelCount = accuPixelCount;
                accuPixelCount += count;
                mean += i * (double)count / pixelCount;
                if (prevAccuPixelCount == 0)
                    min = i;
                if (prevAccuPixelCount < shadowPixelCount)
                {
                    if (accuPixelCount >= shadowPixelCount)
                        shadow = i;
                }
                if (prevAccuPixelCount < medianPixelCount)
                {
                    if (accuPixelCount >= medianPixelCount)
                        median = i;
                }
                if (prevAccuPixelCount < highlightPixelCount)
                {
                    if (accuPixelCount >= highlightPixelCount)
                        highlight = i;
                }
                max = i;
            }
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
                    if (stopWatch != null)
                        Logger.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to create histograms for {bitmapBuffer.Width}x{bitmapBuffer.Height} {bitmapBuffer.Format} bitmap");
                    return new BitmapHistograms(bitmapBuffer.Width * bitmapBuffer.Height, red, green, blue, luminance);
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
                    if (stopWatch != null)
                        Logger.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to create histograms for {bitmapBuffer.Width}x{bitmapBuffer.Height} {bitmapBuffer.Format} bitmap");
                    return new BitmapHistograms(bitmapBuffer.Width * bitmapBuffer.Height, red, green, blue, luminance);
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
        /// Get highlight value of blue channel.
        /// </summary>
        public int HighlightOfBlue { get => this.highlightOfBlue; }


        /// <summary>
        /// Get highlight value of green channel.
        /// </summary>
        public int HighlightOfGreen { get => this.highlightOfGreen; }


        /// <summary>
        /// Get highlight value of luminance.
        /// </summary>
        public int HighlightOfLuminance { get => this.highlightOfLuminance; }


        /// <summary>
        /// Get highlight value of red channel.
        /// </summary>
        public int HighlightOfRed { get => this.highlightOfRed; }


        /// <summary>
        /// Histogram of luminance.
        /// </summary>
        public IList<int> Luminance { get; }


        /// <summary>
        /// Get maximum value in all histograms.
        /// </summary>
        public int Maximum { get; }


        /// <summary>
        /// Get maximum value of blue channel.
        /// </summary>
        public int MaxOfBlue { get => this.maxOfBlue; }


        /// <summary>
        /// Get maximum value of green channel.
        /// </summary>
        public int MaxOfGreen { get => this.maxOfGreen; }


        /// <summary>
        /// Get maximum value of luminance.
        /// </summary>
        public int MaxOfLuminance { get => this.maxOfLuminance; }


        /// <summary>
        /// Get maximum value of red channel.
        /// </summary>
        public int MaxOfRed { get => this.maxOfRed; }


        /// <summary>
        /// Get mean value of blue channel.
        /// </summary>
        public double MeanOfBlue { get => this.meanOfBlue; }


        /// <summary>
        /// Get mean value of green channel.
        /// </summary>
        public double MeanOfGreen { get => this.meanOfGreen; }


        /// <summary>
        /// Get mean value of luminance.
        /// </summary>
        public double MeanOfLuminance { get => this.meanOfLuminance; }


        /// <summary>
        /// Get mean value of red channel.
        /// </summary>
        public double MeanOfRed { get => this.meanOfRed; }


        /// <summary>
        /// Get median value of blue channel.
        /// </summary>
        public int MedianOfBlue { get => this.medianOfBlue; }


        /// <summary>
        /// Get median value of green channel.
        /// </summary>
        public int MedianOfGreen { get => this.medianOfGreen; }


        /// <summary>
        /// Get median value of luminance.
        /// </summary>
        public int MedianOfLuminance { get => this.medianOfLuminance; }


        /// <summary>
        /// Get median value of red channel.
        /// </summary>
        public int MedianOfRed { get => this.medianOfRed; }


        /// <summary>
        /// Get minimum value of blue channel.
        /// </summary>
        public int MinOfBlue { get => this.minOfBlue; }


        /// <summary>
        /// Get minimum value of green channel.
        /// </summary>
        public int MinOfGreen { get => this.minOfGreen; }


        /// <summary>
        /// Get minimum value of luminance.
        /// </summary>
        public int MinOfLuminance { get => this.minOfLuminance; }


        /// <summary>
        /// Get minimum value of red channel.
        /// </summary>
        public int MinOfRed { get => this.minOfRed; }


        /// <summary>
        /// Histogram of red channel.
        /// </summary>
        public IList<int> Red { get; }


        /// <summary>
        /// Get shadow value of blue channel.
        /// </summary>
        public int ShadowOfBlue { get => this.shadowOfBlue; }


        /// <summary>
        /// Get shadow value of green channel.
        /// </summary>
        public int ShadowOfGreen { get => this.shadowOfGreen; }


        /// <summary>
        /// Get shadow value of luminance.
        /// </summary>
        public int ShadowOfLuminance { get => this.shadowOfLuminance; }


        /// <summary>
        /// Get shadow value of red channel.
        /// </summary>
        public int ShadowOfRed { get => this.shadowOfRed; }
    }
}
