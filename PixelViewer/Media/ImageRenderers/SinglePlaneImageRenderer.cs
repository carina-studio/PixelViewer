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


		// Create default image plane options.
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new ImagePlaneOptions[]{
			new ImagePlaneOptions(this.bytesPerPixel, width * this.bytesPerPixel)
		};


		// Evaluate pixel count.
		public override int EvaluatePixelCount(IImageDataSource source) => (int)(source.Size / this.bytesPerPixel);
	}
}
