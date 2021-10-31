using CarinaStudio;
using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Initialize new <see cref="BitmapHistograms"/> instance.
        /// </summary>
        /// <param name="red">Histogram of red channel.</param>
        /// <param name="green">Histogram of green channel.</param>
        /// <param name="blue">Histogram of blue channel.</param>
        /// <param name="luminance">Histogram of luminance.</param>
        public BitmapHistograms(IList<int> red, IList<int> green, IList<int> blue, IList<int> luminance)
        {
            if (red.Count != green.Count || green.Count != blue.Count || blue.Count != luminance.Count)
                throw new ArgumentException();
            this.Blue = blue.AsReadOnly();
            this.Green = green.AsReadOnly();
            this.Luminance = luminance.AsReadOnly();
            this.Maximum = Math.Max(Math.Max(red.Max(), green.Max()), Math.Max(blue.Max(), luminance.Max()));
            this.Red = red.AsReadOnly();
        }


        /// <summary>
        /// Histogram of blue channel.
        /// </summary>
        public IList<int> Blue { get; }


        /// <summary>
        /// Create <see cref="BitmapHistograms"/> asynchronously.
        /// </summary>
        /// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of histogram creation.</returns>
        public static Task<BitmapHistograms> CreateAsync(IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken) => bitmapBuffer.Format switch
        {
            BitmapFormat.Bgra32 => CreateFromBgra32Async(bitmapBuffer, cancellationToken),
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
                    var red = new int[256];
                    var green = new int[256];
                    var blue = new int[256];
                    var luminance = new int[256];
                    unsafe
                    {
                        bitmapBuffer.Memory.Pin(ptr =>
                        {
                            var bitmapPtr = (byte*)ptr;
                            fixed (int* redHistoram = red, greenHistogram = green, blueHistogram = blue, luminanceHistogram = luminance)
                            {
                                var width = bitmapBuffer.Width;
                                var bitmapRowPtr = bitmapPtr;
                                for (var y = bitmapBuffer.Height; y > 0; --y, bitmapRowPtr += bitmapBuffer.RowBytes)
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                    var pixelPtr = bitmapRowPtr;
                                    for (var x = width; x > 0; --x, pixelPtr += 4)
                                    {
                                        var r = pixelPtr[2];
                                        var g = pixelPtr[1];
                                        var b = pixelPtr[0];
                                        var l = (r + g + b) / 3;
                                        ++redHistoram[r];
                                        ++greenHistogram[g];
                                        ++blueHistogram[b];
                                        ++luminanceHistogram[l];
                                    }
                                }
                            }
                        });
                    }
                    return new BitmapHistograms(red, green, blue, luminance);
                });
            }
            finally
            {
                bitmapBuffer.Dispose();
            }
        }


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
        int Maximum { get; }


        /// <summary>
        /// Histogram of red channel.
        /// </summary>
        public IList<int> Red { get; }
    }
}
