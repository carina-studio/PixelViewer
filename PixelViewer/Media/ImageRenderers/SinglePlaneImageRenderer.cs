using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> for single plane format.
	/// </summary>
	abstract class SinglePlaneImageRenderer : BaseImageRenderer
	{
		// Fields.
		readonly int bytesPerPixel;


		/// <summary>
		/// Initialize new <see cref="BaseImageRendererFactory"/> instance.
		/// </summary>
		/// <param name="format">Format supported by <see cref="IImageRenderer"/> created by this factory.</param>
		protected SinglePlaneImageRenderer(ImageFormat format) : base(format)
		{
			if (format.PlaneCount != 1)
				throw new ArgumentException("Plane count of format should be 1.");
			this.bytesPerPixel = format.PlaneDescriptors[0].PixelStride;
		}


		/// <inheritdoc/>
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new ImagePlaneOptions[]{
			new ImagePlaneOptions(this.bytesPerPixel, width * this.bytesPerPixel)
		};


		/// <inheritdoc/>
		public override int EvaluatePixelCount(IImageDataSource source) => (int)(source.Size / this.bytesPerPixel);


		/// <inheritdoc/>
		public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
		{
			if (width <= 0 || height <= 0)
				return 0;
			var rowStride = Math.Max(width * this.bytesPerPixel, planeOptions[0].RowStride);
			return rowStride * height;
		}
	}
}
