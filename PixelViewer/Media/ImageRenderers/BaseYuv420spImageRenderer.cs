using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

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
		protected override unsafe void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
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

			// select color conversion
			var yuv422ToBgra = ImageProcessing.SelectYuv422ToBgra32Conversion(renderingOptions.YuvConversionMode);

			// render
			bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
			{
				// render Y
				var yRow = new byte[yRowStride];
				var bitmapRowPtr = (byte*)bitmapBaseAddress;
				var bitmapRowStride = bitmapBuffer.RowBytes;
				fixed (byte* yRowPtr = yRow)
				{
					for (var rowIndex = 0; rowIndex < height; ++rowIndex, bitmapRowPtr += bitmapRowStride)
					{
						var yPixelPtr = yRowPtr;
						var bitmapPixelPtr = bitmapRowPtr;
						imageStream.Read(yRow, 0, yRowStride);
						for (var columnIndex = 0; columnIndex < width; ++columnIndex, yPixelPtr += yPixelStride, bitmapPixelPtr += 4)
							bitmapPixelPtr[0] = yPixelPtr[0];
						if (cancellationToken.IsCancellationRequested)
							break;
						if (rowIndex < height - 1)
							Array.Clear(yRow, 0, yRowStride);
					}
				}

				// render UV
				var uvRow = new byte[uvRowStride];
				bitmapRowPtr = (byte*)bitmapBaseAddress;
				fixed (byte* uvRowPtr = uvRow)
				{
					for (var rowIndex = 0; rowIndex < height; ++rowIndex, bitmapRowPtr += bitmapRowStride)
					{
						// read UV row
						imageStream.Read(uvRow, 0, uvRowStride);

						// render the M row
						var vuPixelPtr = uvRowPtr;
						var bitmapPixelPtr = bitmapRowPtr;
						for (var columnIndex = 0; columnIndex < width; columnIndex += 2, vuPixelPtr += uvPixelStride, bitmapPixelPtr += 8)
						{
							int y1 = bitmapPixelPtr[0];
							int y2 = bitmapPixelPtr[4];
							this.SelectUV(vuPixelPtr[0], vuPixelPtr[1], out var u, out var v);
							yuv422ToBgra(y1, y2, u, v, (uint*)bitmapPixelPtr, (uint*)(bitmapPixelPtr + 4));
						}
						++rowIndex;
						bitmapRowPtr += bitmapRowStride;

						// render the (M+1) row
						vuPixelPtr = uvRowPtr;
						bitmapPixelPtr = bitmapRowPtr;
						for (var columnIndex = 0; columnIndex < width; columnIndex += 2, vuPixelPtr += uvPixelStride, bitmapPixelPtr += 8)
						{
							int y1 = bitmapPixelPtr[0];
							int y2 = bitmapPixelPtr[4];
							this.SelectUV(vuPixelPtr[0], vuPixelPtr[1], out var u, out var v);
							yuv422ToBgra(y1, y2, u, v, (uint*)bitmapPixelPtr, (uint*)(bitmapPixelPtr + 4));
						}

						// check state
						if (cancellationToken.IsCancellationRequested)
							break;
						if (rowIndex < height - 1)
							Array.Clear(uvRow, 0, uvRowStride);
					}
				}
			});
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
