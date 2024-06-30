using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with 8-bit RGB Planar depth format.
/// </summary>
class RgbPlanar888ImageRenderer : BaseImageRenderer
{
	/// <summary>
	/// Initialize new <see cref="RgbPlanar888ImageRenderer"/> instance.
	/// </summary>
	public RgbPlanar888ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "RGB_Planar_888", new[] {
		new ImagePlaneDescriptor(1),
		new ImagePlaneDescriptor(1),
		new ImagePlaneDescriptor(1),
	}, new[]{ "RGBPlanar888" }))
	{ }


	// Create default plane options.
	public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also(it =>
	{
		it.Add(new ImagePlaneOptions(1, width));
		it.Add(new ImagePlaneOptions(1, width));
		it.Add(new ImagePlaneOptions(1, width));
	});


	// Evaluate pixel count.
	public override int EvaluatePixelCount(IImageDataSource source) => (int)(source.Size / 3);


	// Evaluate source data size.
	public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
	{
		return planeOptions[0].RowStride * height * 3;
	}


	// Render.
	protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
        // get parameters
		if (planeOptions.Count != 3)
			throw new ArgumentException($"Invalid number of plane options: {planeOptions.Count}.");
		var width = bitmapBuffer.Width;
		var height = bitmapBuffer.Height;
		var pixelStride = planeOptions[0].PixelStride;
		var rowStride = planeOptions[0].RowStride;
		if (pixelStride <= 0 || rowStride <= 0)
			throw new ArgumentException($"Invalid pixel/row stride: {pixelStride}/{rowStride}.");

        // render
		bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
		{
			var srcRow = new byte[rowStride];
			fixed (byte* srcRowPtr = srcRow)
			{
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				var bitmapRowStride = bitmapBuffer.RowBytes;
                //Read R values
				for (var y = height; ; --y, bitmapRowPtr += bitmapRowStride)
				{
					var isLastRow = (imageStream.Read(srcRow) < rowStride || y == 1);
					var srcPixelPtr = srcRowPtr;
					var bitmapPixelPtr = bitmapRowPtr;
					for (var x = width; x > 0; --x, srcPixelPtr += pixelStride, bitmapPixelPtr += 4)
					{
						bitmapPixelPtr[0] = srcPixelPtr[0];
                        bitmapPixelPtr[3] = 255;
					}
					if (isLastRow || cancellationToken.IsCancellationRequested)
						break;
					Array.Clear(srcRow, 0, rowStride);
				}

                // Read G values
                bitmapRowPtr = (byte*)bitmapBaseAddress;
				for (var y = height; ; --y, bitmapRowPtr += bitmapRowStride)
				{
					var isLastRow = (imageStream.Read(srcRow) < rowStride || y == 1);
					var srcPixelPtr = srcRowPtr;
					var bitmapPixelPtr = bitmapRowPtr;
					for (var x = width; x > 0; --x, srcPixelPtr += pixelStride, bitmapPixelPtr += 4)
					{
						bitmapPixelPtr[1] = srcPixelPtr[0];
					}
					if (isLastRow || cancellationToken.IsCancellationRequested)
						break;
					Array.Clear(srcRow, 0, rowStride);
				}

                // Read B values
                bitmapRowPtr = (byte*)bitmapBaseAddress;
				for (var y = height; ; --y, bitmapRowPtr += bitmapRowStride)
				{
					var isLastRow = (imageStream.Read(srcRow) < rowStride || y == 1);
					var srcPixelPtr = srcRowPtr;
					var bitmapPixelPtr = bitmapRowPtr;
					for (var x = width; x > 0; --x, srcPixelPtr += pixelStride, bitmapPixelPtr += 4)
					{
						bitmapPixelPtr[2] = srcPixelPtr[0];
					}
					if (isLastRow || cancellationToken.IsCancellationRequested)
						break;
					Array.Clear(srcRow, 0, rowStride);
				}
			}
		});

        //complete
		return new ImageRenderingResult();
	}
}
