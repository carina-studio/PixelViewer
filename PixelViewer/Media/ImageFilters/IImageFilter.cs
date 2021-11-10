using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// Image filter.
    /// </summary>
    interface IImageFilter<TParams> where TParams : ImageFilterParams
    {
        /// <summary>
        /// Apply filter on image asynchronously.
        /// </summary>
        /// <param name="source">Source image.</param>
        /// <param name="result">Result image.</param>
        /// <param name="parameters">Parameters.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of applying filter.</returns>
        Task ApplyFilterAsync(IBitmapBuffer source, IBitmapBuffer result, TParams parameters, CancellationToken cancellationToken);
    }


    /// <summary>
    /// Parameters to filter image.
    /// </summary>
    abstract class ImageFilterParams : ICloneable
    {
        /// <summary>
        /// Empty parameters.
        /// </summary>
        public static readonly ImageFilterParams Empty = new EmptyParams();


        // Empty implementation.
        class EmptyParams : ImageFilterParams
        {
            public override object Clone() => new EmptyParams();
        }


        /// <inheritdoc/>
        public abstract object Clone();
    }
}
