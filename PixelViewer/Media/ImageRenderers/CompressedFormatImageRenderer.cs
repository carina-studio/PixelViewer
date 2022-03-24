using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// Base implementation of <see cref="IImageRenderer"/> for compressed format.
    /// </summary>
    abstract class CompressedFormatImageRenderer : BaseImageRenderer
    {
        /// <summary>
		/// Initialize new <see cref="CompressedFormatImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format supported by this instance.</param>
		protected CompressedFormatImageRenderer(ImageFormat format) : base(format)
        { 
            if (format.Category != ImageFormatCategory.Compressed)
                throw new ArgumentException("Not a compressed format.");
        }


        /// <inheritdoc/>
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new ImagePlaneOptions[]{
			new ImagePlaneOptions(0, 0)
		};


        /// <inheritdoc/>
		public override int EvaluatePixelCount(IImageDataSource source) => 0;


		/// <inheritdoc/>
		public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions) => 0;
    }
}