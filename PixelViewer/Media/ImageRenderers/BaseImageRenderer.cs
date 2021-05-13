using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/>.
	/// </summary>
	abstract class BaseImageRenderer : IImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="BaseImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format supported by this instance.</param>
		protected BaseImageRenderer(ImageFormat format)
		{
			this.Format = format;
			this.Logger = LogManager.GetLogger(this.GetType().Name);
		}


		/// <summary>
		/// Logger.
		/// </summary>
		protected ILogger Logger { get; }


		/// <summary>
		/// Called to perform image rendering. The method will be called in background thread.
		/// </summary>
		/// <param name="source">Source of raw image data.</param>
		/// <param name="imageStream"><see cref="Stream"/> to read raw image data.</param>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> to receive rendered image.</param>
		/// <param name="renderingOptions">Rendering options.</param>
		/// <param name="planeOptions">List of <see cref="ImagePlaneOptions"/> for rendering.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		protected abstract void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


		// Render.
		public Task Render(IImageDataSource source, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			if (bitmapBuffer.Format != this.RenderedFormat)
				throw new ArgumentException($"Invalid format of bitmap buffer: {bitmapBuffer.Format}.");
			var sharedSource = source.Share();
			var sharedBitmapBuffer = bitmapBuffer.Share();
			return Task.Run(() =>
			{
				try
				{
					using var stream = sharedSource.Open();
					this.OnRender(sharedSource, stream, sharedBitmapBuffer, renderingOptions, planeOptions, cancellationToken);
				}
				finally
				{
					sharedBitmapBuffer.Dispose();
					sharedSource.Dispose();
				}
			});
		}


		// Implementations.
		public abstract IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height);
		public abstract int EvaluatePixelCount(IImageDataSource source);
		public ImageFormat Format { get; }
		public virtual BitmapFormat RenderedFormat => BitmapFormat.Bgra32;
	}
}
