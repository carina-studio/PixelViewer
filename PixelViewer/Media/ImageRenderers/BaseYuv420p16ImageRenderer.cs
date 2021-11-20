using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with 16-bit YUV420p based format.
	/// </summary>
	abstract class BaseYuv420p16ImageRenderer : BaseImageRenderer
	{
		// Fields.
		readonly int effectiveBits;
		readonly bool eliminateTailPadding;


		/// <summary>
		/// Initialize new <see cref="BaseYuv420p16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Supported format.</param>
		/// <param name="effectiveBits">Effective bits for each Y/U/V coponent.</param>
		/// <param name="eliminateTailPadding">True to ignore padding bytes in each tail of plane.</param>
		public BaseYuv420p16ImageRenderer(ImageFormat format, int effectiveBits, bool eliminateTailPadding = false) : base(format)
		{
			if (effectiveBits < 10 || effectiveBits > 16)
				throw new ArgumentOutOfRangeException(nameof(effectiveBits));
			this.effectiveBits = effectiveBits;
			this.eliminateTailPadding = eliminateTailPadding;
		}


		// Create default plane options.
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also((it) =>
		{
			it.Add(new ImagePlaneOptions(2, width * 2));
			it.Add(new ImagePlaneOptions(2, width));
			it.Add(new ImagePlaneOptions(2, width));
		});


		// Evaluate pixel count.
		public override int EvaluatePixelCount(IImageDataSource source) => (int)(source.Size * 1 / 3);


		// Evaluate source data size.
		public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
		{
			width &= 0x7ffffffe;
			height &= 0x7ffffffe;
			if (width <= 0 || height <= 0)
				return 0;
			var yRowStride = Math.Max(width * 2, planeOptions[0].RowStride);
			var uv1RowStride = Math.Max(width, planeOptions[1].RowStride);
			var uv2RowStride = Math.Max(width, planeOptions[2].RowStride);
			return (yRowStride * height) + (uv1RowStride * height / 2) + (uv2RowStride * height / 2);
		}


		// Render.
		protected override unsafe void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get state
			var width = (bitmapBuffer.Width & 0x7ffffffe);
			var height = (bitmapBuffer.Height & 0x7ffffffe);
			var yPixelStride = planeOptions[0].PixelStride;
			var yRowStride = planeOptions[0].RowStride;
			var uv1PixelStride = planeOptions[1].PixelStride;
			var uv1RowStride = planeOptions[1].RowStride;
			var uv2PixelStride = planeOptions[2].PixelStride;
			var uv2RowStride = planeOptions[2].RowStride;
			bool isLittleEndian = renderingOptions.ByteOrdering == ByteOrdering.LittleEndian;
			if (width <= 0 || height <= 0
				|| yPixelStride <= 0 || yRowStride <= 0 || (yPixelStride * width) > yRowStride
				|| uv2PixelStride <= 0 || uv2RowStride <= 0 || (uv2PixelStride * width / 2) > uv2RowStride
				|| uv1PixelStride <= 0 || uv1RowStride <= 0 || (uv1PixelStride * width / 2) > uv1RowStride)
			{
				throw new ArgumentException($"Invalid pixel/row stride.");
			}

			// select color converter
			var converter = renderingOptions.YuvToBgraConverter ?? YuvToBgraConverter.Default;
			var yuvExtractor = this.Create16BitColorExtraction(renderingOptions.ByteOrdering, this.effectiveBits);

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
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						var isLastRow = (rowIndex == height - 1);
						if (isLastRow && this.eliminateTailPadding)
							imageStream.Read(yRow, 0, yPixelStride * (width - 1) + 2);
						else
							imageStream.Read(yRow, 0, yRowStride);
						for (var columnIndex = 0; columnIndex < width; ++columnIndex, yPixelPtr += yPixelStride, bitmapPixelPtr += 4)
							bitmapPixelPtr[0] = yuvExtractor(yPixelPtr[0], yPixelPtr[1]);
						if (cancellationToken.IsCancellationRequested)
							break;
						if (!isLastRow)
							Array.Clear(yRow, 0, yRowStride);
					}
				}

				// render UV1
				var uv1Row = new byte[uv1RowStride];
				bitmapRowPtr = (byte*)bitmapBaseAddress;
				fixed (byte* uv1RowPtr = uv1Row)
				{
					for (var rowIndex = 0; rowIndex < height; ++rowIndex, bitmapRowPtr += bitmapRowStride)
					{
						// read UV row
						var isLastRow = (rowIndex == height - 2);
						if (isLastRow && this.eliminateTailPadding)
							imageStream.Read(uv1Row, 0, uv1PixelStride * (width / 2 - 1) + 2);
						else
							imageStream.Read(uv1Row, 0, uv1RowStride);

						// render the M row
						var uvPixelPtr = uv1RowPtr;
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						for (var columnIndex = 0; columnIndex < width; columnIndex += 2, uvPixelPtr += uv1PixelStride, bitmapPixelPtr += 8)
							bitmapPixelPtr[1] = yuvExtractor(uvPixelPtr[0], uvPixelPtr[1]);
						++rowIndex;
						bitmapRowPtr += bitmapRowStride;

						// render the (M+1) row
						uvPixelPtr = uv1RowPtr;
						bitmapPixelPtr = (ushort*)bitmapRowPtr;
						for (var columnIndex = 0; columnIndex < width; columnIndex += 2, uvPixelPtr += uv1PixelStride, bitmapPixelPtr += 8)
							bitmapPixelPtr[1] = yuvExtractor(uvPixelPtr[0], uvPixelPtr[1]);

						// check state
						if (cancellationToken.IsCancellationRequested)
							break;
						if (rowIndex < height - 1)
							Array.Clear(uv1Row, 0, uv1RowStride);
					}
				}

				// render UV2
				var uv2Row = new byte[uv2RowStride];
				bitmapRowPtr = (byte*)bitmapBaseAddress;
				fixed (byte* uv2RowPtr = uv2Row)
				{
					for (var rowIndex = 0; rowIndex < height; ++rowIndex, bitmapRowPtr += bitmapRowStride)
					{
						// read UV row
						var isLastRow = (rowIndex == height - 2);
						if (isLastRow && this.eliminateTailPadding)
							imageStream.Read(uv2Row, 0, uv2PixelStride * (width / 2 - 1) + 2);
						else
							imageStream.Read(uv2Row, 0, uv2RowStride);

						// render the M row
						var uvPixelPtr = uv2RowPtr;
						var bitmapPixelPtr = (ushort*)bitmapRowPtr;
						for (var columnIndex = 0; columnIndex < width; columnIndex += 2, uvPixelPtr += uv2PixelStride, bitmapPixelPtr += 8)
						{
							var y1 = bitmapPixelPtr[0];
							var y2 = bitmapPixelPtr[4];
							this.SelectUV(bitmapPixelPtr[1], yuvExtractor(uvPixelPtr[0], uvPixelPtr[1]), out var u, out var v);
							converter.ConvertFromYuv422ToBgra64(y1, y2, u, v, (ulong*)bitmapPixelPtr, (ulong*)(bitmapPixelPtr + 4));
						}
						++rowIndex;
						bitmapRowPtr += bitmapRowStride;

						// render the (M+1) row
						uvPixelPtr = uv2RowPtr;
						bitmapPixelPtr = (ushort*)bitmapRowPtr;
						for (var columnIndex = 0; columnIndex < width; columnIndex += 2, uvPixelPtr += uv2PixelStride, bitmapPixelPtr += 8)
						{
							var y1 = bitmapPixelPtr[0];
							var y2 = bitmapPixelPtr[4];
							this.SelectUV(bitmapPixelPtr[1], yuvExtractor(uvPixelPtr[0], uvPixelPtr[1]), out var u, out var v);
							converter.ConvertFromYuv422ToBgra64(y1, y2, u, v, (ulong*)bitmapPixelPtr, (ulong*)(bitmapPixelPtr + 4));
						}

						// check state
						if (cancellationToken.IsCancellationRequested)
							break;
						if (rowIndex < height - 1)
							Array.Clear(uv2Row, 0, uv2RowStride);
					}
				}
			});
		}


		// Rendered format.
		public override BitmapFormat RenderedFormat => BitmapFormat.Bgra64;


		/// <summary>
		/// Select U, V component.
		/// </summary>
		/// <param name="uv1">First component read from source.</param>
		/// <param name="uv2">Second component read from source.</param>
		/// <param name="u">Selected U.</param>
		/// <param name="v">Selected V.</param>
		protected abstract void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v);
	}
}
