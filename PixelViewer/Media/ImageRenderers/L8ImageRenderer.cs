using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with L8 format.
	/// </summary>
	class L8ImageRenderer : SinglePlaneImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="L8ImageRenderer"/> instance.
		/// </summary>
		public L8ImageRenderer() : base(new ImageFormat(ImageFormatCategory.Luminance, "L8", new ImagePlaneDescriptor(1)))
		{ }


		// Render.
		protected override unsafe void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			if (planeOptions.Count != 1)
				return;
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			var pixelStride = planeOptions[0].PixelStride;
			var rowStride = planeOptions[0].RowStride;
			if (pixelStride <= 0 || rowStride <= 0)
				return;

			// render
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				var srcRow = new byte[rowStride];
				fixed (byte* srcRowPtr = srcRow)
				{
					var bitmapRowPtr = (byte*)bitmapBaseAddress;
					var bitmapRowStride = bitmapBuffer.RowBytes;
					for (var y = height; ; --y, bitmapRowPtr += bitmapRowStride)
					{
						var isLastRow = (imageStream.Read(srcRow) < rowStride || y == 1);
						var srcPixelPtr = srcRowPtr;
						var bitmapPixelPtr = bitmapRowPtr;
						for (var x = width; x > 0; --x, srcPixelPtr += pixelStride, bitmapPixelPtr += 4)
						{
							bitmapPixelPtr[0] = srcPixelPtr[0];
							bitmapPixelPtr[1] = srcPixelPtr[0];
							bitmapPixelPtr[2] = srcPixelPtr[0];
							bitmapPixelPtr[3] = 255;
						}
						if (isLastRow || cancellationToken.IsCancellationRequested)
							break;
						Array.Clear(srcRow, 0, rowStride);
					}
				}
			});
		}
	}
}
