using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ARGB_8888 based format.
	/// </summary>
	abstract class BaseArgb8888ImageRenderer : SinglePlaneImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="BaseArgb8888ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Supported format.</param>
		protected BaseArgb8888ImageRenderer(ImageFormat format) : base(format)
		{ }


		/// <inheritdoc/>
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => [ new(8, 4, width * 4) ];


		/// <inheritdoc/>
		public override bool IsRgbGainSupported => true;


		// Render.
		protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			var pixelStride = planeOptions[0].PixelStride;
			var rowStride = planeOptions[0].RowStride;
			if (pixelStride <= 0 || rowStride <= 0)
				throw new ArgumentException($"Invalid pixel/row stride: {pixelStride}/{rowStride}.");

			// prepare accumulating colors to calculate the means of RGB colors
			var meansAccumulator = new RgbMeansAccumulator(width, height, 255, renderingOptions);

			// allocate the tables which apply the base exposure and the RGB gains, no table is needed if the colors are not changed
			var colorTransformationTables = AllocateNativeBytesWhen(renderingOptions.HasBaseExposure || renderingOptions.HasRgbGain, 256 * 3);
			try
			{
				// build the tables
				if (colorTransformationTables is not null)
				{
					BuildColorTransformationTableUnsafe(colorTransformationTables, renderingOptions, gain: ImageRenderingOptions.GetValidRgbGain(renderingOptions.BlueGain));
					BuildColorTransformationTableUnsafe(colorTransformationTables + 256, renderingOptions, gain: ImageRenderingOptions.GetValidRgbGain(renderingOptions.GreenGain));
					BuildColorTransformationTableUnsafe(colorTransformationTables + 512, renderingOptions, gain: ImageRenderingOptions.GetValidRgbGain(renderingOptions.RedGain));
				}

				// render
				var srcRow = new byte[rowStride];
				fixed (byte* srcRowAddress = srcRow)
				{
					var srcRowPtr = srcRowAddress;
					bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
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
								this.SelectArgb(srcPixelPtr[0], srcPixelPtr[1], srcPixelPtr[2], srcPixelPtr[3], out var a, out var r, out var g, out var b);
								meansAccumulator.Accumulate(width - x, height - y, b, g, r);
								if (colorTransformationTables is not null)
								{
									b = colorTransformationTables[b];
									g = colorTransformationTables[256 + g];
									r = colorTransformationTables[512 + r];
								}
								bitmapPixelPtr[0] = b;
								bitmapPixelPtr[1] = g;
								bitmapPixelPtr[2] = r;
								bitmapPixelPtr[3] = a;
							}
							if (isLastRow || cancellationToken.IsCancellationRequested)
								break;
							Array.Clear(srcRow, 0, rowStride);
						}
					});
				}
			}
			finally
			{
				NativeMemory.Free(colorTransformationTables);
			}

			// complete
			var result = new ImageRenderingResult();
			meansAccumulator.WriteToRenderingResult(ref result);
			return result;
		}


		/// <summary>
		/// Select A, R, G, B components.
		/// </summary>
		/// <param name="component1">1st component read from source.</param>
		/// <param name="component2">2nd component read from source.</param>
		/// <param name="component3">3rd component read from source.</param>
		/// <param name="component4">4th component read from source.</param>
		/// <param name="a">Selected A.</param>
		/// <param name="r">Selected R.</param>
		/// <param name="g">Selected G.</param>
		/// <param name="b">Selected B.</param>
		protected abstract void SelectArgb(byte component1, byte component2, byte component3, byte component4, out byte a, out byte r, out byte g, out byte b);
	}
}
