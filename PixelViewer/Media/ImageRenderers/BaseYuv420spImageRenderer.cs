using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with YUV420sp based format.
	/// </summary>
	abstract class BaseYuv420spImageRenderer : BaseImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="BaseYuv420spImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Supported format.</param>
		public BaseYuv420spImageRenderer(ImageFormat format) : base(format)
		{ }


		// Create default plane options.
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also((it) =>
		{
			it.Add(new ImagePlaneOptions(1, width));
			it.Add(new ImagePlaneOptions(2, width));
		});


		// Evaluate pixel count.
		public override int EvaluatePixelCount(IImageDataSource source) => (int)(source.Size * 2 / 3);


		// Evaluate source data size.
		public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
		{
			width &= 0x7ffffffe;
			height &= 0x7ffffffe;
			if (width <= 0 || height <= 0)
				return 0;
			var yRowStride = Math.Max(width, planeOptions[0].RowStride);
			var uvRowStride = Math.Max(width, planeOptions[1].RowStride);
			return (yRowStride * height) + (uvRowStride * height / 2);
		}


		// Render.
		protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get state
			var width = (bitmapBuffer.Width & 0x7ffffffe);
			var height = (bitmapBuffer.Height & 0x7ffffffe);
			var yPixelStride = planeOptions[0].PixelStride;
			var yRowStride = planeOptions[0].RowStride;
			var uvPixelStride = planeOptions[1].PixelStride;
			var uvRowStride = planeOptions[1].RowStride;
			if (width <= 0 || height <= 0 
				|| yPixelStride <= 0 || yRowStride <= 0 || (yPixelStride * width) > yRowStride
				|| uvPixelStride <= 0 || uvRowStride <= 0 || (uvPixelStride * width / 2) > uvRowStride)
			{
				throw new ArgumentException($"Invalid pixel/row stride.");
			}

			// select color converter
			var converter = renderingOptions.YuvToBgraConverter ?? YuvToBgraConverter.Default;

			// render
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				// read Y
				var yRow = new byte[yRowStride];
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				var bitmapRowStride = bitmapBuffer.RowBytes;
				fixed (byte* yRowPtr = yRow)
				{
					for (var rowIndex = 0; rowIndex < height; ++rowIndex, bitmapRowPtr += bitmapRowStride)
					{
						var yPixelPtr = yRowPtr;
						var bitmapPixelPtr = bitmapRowPtr;
						var isLastRow = imageStream.Read(yRow, 0, yRowStride) < yRowStride || rowIndex >= height - 1;
						for (var columnIndex = 0; columnIndex < width; ++columnIndex, yPixelPtr += yPixelStride, bitmapPixelPtr += 4)
							bitmapPixelPtr[0] = yPixelPtr[0];
						if (cancellationToken.IsCancellationRequested)
							return;
						if (isLastRow)
							break;
						Array.Clear(yRow, 0, yRowStride);
					}
				}

				// read UV
				var uvRow = new byte[uvRowStride];
				bitmapRowPtr = (byte*)bitmapBaseAddress;
				fixed (byte* uvRowPtr = uvRow)
				{
					var bitmapRowStride2 = bitmapRowStride << 1;
					for (var rowIndex = 0; rowIndex < height; rowIndex += 2, bitmapRowPtr += bitmapRowStride2)
					{
						// read UV row
						var isLastRow = imageStream.Read(uvRow, 0, uvRowStride) < uvRowStride || rowIndex >= height - 2;
						var vuPixelPtr = uvRowPtr;
						var bitmapPixelPtr = bitmapRowPtr;
						for (var columnIndex = 0; columnIndex < width; columnIndex += 2, vuPixelPtr += uvPixelStride, bitmapPixelPtr += 8)
						{
							var y1 = bitmapPixelPtr[0];
							var y2 = bitmapPixelPtr[4];
							this.SelectUV(vuPixelPtr[0], vuPixelPtr[1], out bitmapPixelPtr[1], out bitmapPixelPtr[2]);
						}

						// check state
						if (cancellationToken.IsCancellationRequested)
							return;
						if (isLastRow)
							break;
						Array.Clear(uvRow, 0, uvRowStride);
					}
				}

				// convert to BGRA
				ImageProcessing.ParallelFor(0, height >> 1, (y) =>
				{
					var bitmapPixelPtr = (byte*)bitmapBaseAddress + (y << 1) * bitmapRowStride;
					var bottomBitmapPixelPtr = bitmapPixelPtr + bitmapRowStride;
					for (var x = width; x > 0; x -= 2, bitmapPixelPtr += 8, bottomBitmapPixelPtr += 8)
					{
						var u = bitmapPixelPtr[1];
						var v = bitmapPixelPtr[2];
						converter.ConvertFromYuv422ToBgra32(bitmapPixelPtr[0], bitmapPixelPtr[4], u, v, (uint*)bitmapPixelPtr, (uint*)(bitmapPixelPtr + 4));
						converter.ConvertFromYuv422ToBgra32(bottomBitmapPixelPtr[0], bottomBitmapPixelPtr[4], u, v, (uint*)bottomBitmapPixelPtr, (uint*)(bottomBitmapPixelPtr + 4));
					}
					if (cancellationToken.IsCancellationRequested)
						throw new TaskCanceledException();
				});
			});

			// complete
			return new ImageRenderingResult();
		}


		/// <summary>
		/// Select U, V component.
		/// </summary>
		/// <param name="uv1">First component read from source.</param>
		/// <param name="uv2">Second component read from source.</param>
		/// <param name="u">Selected U.</param>
		/// <param name="v">Selected V.</param>
		protected abstract void SelectUV(byte uv1, byte uv2, out byte u, out byte v);
	}
}
