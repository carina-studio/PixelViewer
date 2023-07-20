using CarinaStudio;
using CarinaStudio.AppSuite;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// Base implementation of <see cref="IImageFilter{TParams}"/>.
    /// </summary>
    abstract class BaseImageFilter<TParams> : IImageFilter<TParams> where TParams : ImageFilterParams
    {
        /// <summary>
		/// Initialize new <see cref="BaseImageFilter{TParams}"/> instance.
		/// </summary>
		protected BaseImageFilter()
        {
            this.Logger = App.Current.LoggerFactory.CreateLogger(this.GetType().Name);
        }


        /// <inheritdoc/>
        public async Task ApplyFilterAsync(IBitmapBuffer source, IBitmapBuffer result, TParams parameters, CancellationToken cancellationToken)
        {
            // check parameters
            if (source == result)
                throw new ArgumentException("Source and result image are same.");
            if (source.Width != result.Width || source.Height != result.Height)
                throw new ArgumentException("Dimension of images are different.");
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();

            // apply filter
            parameters = (TParams)parameters.Clone();
            using var sharedSource = source.Share();
            using var sharedResult = result.Share();
            try
            {
                await Task.Run(() =>
                {
                    var stopWatch = IAppSuiteApplication.CurrentOrNull?.IsDebugMode == true ? new Stopwatch() : null;
                    stopWatch?.Start();
                    this.OnApplyFilter(sharedSource, sharedResult, parameters, cancellationToken);
                    stopWatch?.Let(it => this.Logger.LogTrace("Filtering time: {duration} ms", it.ElapsedMilliseconds));
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException || !cancellationToken.IsCancellationRequested)
                    throw;
            }
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
        }


        /// <summary>
		/// Logger.
		/// </summary>
		protected ILogger Logger { get; }


        /// <summary>
        /// Called to apply filter.
        /// </summary>
        /// <param name="source">Source image.</param>
        /// <param name="result">Result image.</param>
        /// <param name="parameters">Parameters.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        protected abstract void OnApplyFilter(IBitmapBuffer source, IBitmapBuffer result, TParams parameters, CancellationToken cancellationToken);


        /// <summary>
        /// Throw <see cref="ArgumentException"/> is format of images are different.
        /// </summary>
        /// <param name="x">First image.</param>
        /// <param name="y">Second image.</param>
        protected void VerifyFormats(IBitmapBuffer x, IBitmapBuffer y)
        {
            if (x.Format != y.Format)
                throw new ArgumentException("Format of images are different.");
        }
    }
}
