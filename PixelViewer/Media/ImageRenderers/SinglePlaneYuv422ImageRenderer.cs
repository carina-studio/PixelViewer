using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/> for single plane YUV422 format.
	/// </summary>
	abstract class SinglePlaneYuv422ImageRenderer : BaseImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="SinglePlaneYuv422ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		protected SinglePlaneYuv422ImageRenderer(ImageFormat format) : base(format)
		{ }


		// Create default plane options.
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also((it) =>
		{
			it.Add(new ImagePlaneOptions(4, width * 2));
		});


		// Evaluate pixel count.
		public override int EvaluatePixelCount(IImageDataSource source) => (int)(source.Size / 2);


		// Evaluate source data size.
		public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
		{
			width &= 0x7ffffffe;
			height &= 0x7ffffffe;
			if (width <= 0 || height <= 0)
				return 0;
			var rowStride = Math.Max(width * 2, planeOptions[0].RowStride);
			return (rowStride * height);
		}


		// Render.
		protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get state
			var width = (bitmapBuffer.Width & 0x7ffffffe);
			var height = (bitmapBuffer.Height & 0x7ffffffe);
			var pixelStride = planeOptions[0].PixelStride;
			var rowStride = planeOptions[0].RowStride;
			if (width <= 0 || height <= 0)
				throw new ArgumentException($"Invalid size: {width}x{height}.");
			if (pixelStride < 4 || pixelStride <= 0 || (pixelStride * width / 2) > rowStride)
				throw new ArgumentException($"Invalid pixel/row stride: {pixelStride}/{rowStride}.");

			// select color converter
			var converter = renderingOptions.YuvToBgraConverter ?? YuvToBgraConverter.Default;

			// render
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				var yuvRow = new byte[rowStride];
				fixed (byte* yuvRowPtr = yuvRow)
				{
					var bitmapRowPtr = (byte*)bitmapBaseAddress;
					for (var y = 0; y < height; ++y, bitmapRowPtr += bitmapBuffer.RowBytes)
					{
						// read YUV row
						imageStream.Read(yuvRow, 0, rowStride);

						// render row
						var yuvPixelPtr = yuvRowPtr;
						var bitmapPixelPtr = bitmapRowPtr;
						for (var x = 0; x < width; x += 2, yuvPixelPtr += pixelStride, bitmapPixelPtr += 8)
						{
							this.SelectYuv(yuvPixelPtr[0], yuvPixelPtr[1], yuvPixelPtr[2], yuvPixelPtr[3], out var y1, out var y2, out var u, out var v);
							converter.ConvertFromYuv422ToBgra32(y1, y2, u, v, (uint*)bitmapPixelPtr, (uint*)(bitmapPixelPtr + 4));
						}

						// stop rendering
						if (cancellationToken.IsCancellationRequested)
							break;

						// clear YUV row
						if (y < height - 1)
							Array.Clear(yuvRow, 0, rowStride);
					}
				}
			});

			// complete
			return new ImageRenderingResult();
		}


		/// <summary>
		/// Select YUV components from read bytes.
		/// </summary>
		/// <param name="byte1">1st read byte.</param>
		/// <param name="byte2">2nd read byte.</param>
		/// <param name="byte3">3rd read byte.</param>
		/// <param name="byte4">4th read byte.</param>
		/// <param name="y1">Selected Y for 1st pixel.</param>
		/// <param name="y2">Selected Y for 2nd pixel.</param>
		/// <param name="u">Selected U.</param>
		/// <param name="v">Selected V.</param>
		protected abstract void SelectYuv(byte byte1, byte byte2, byte byte3, byte byte4, out byte y1, out byte y2, out byte u, out byte v);
	}
}
