using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with 16-bit YUV444p based format.
/// </summary>
abstract class BaseYuv444p16ImageRenderer : BaseImageRenderer
{
	// Fields.
	readonly ByteOrdering? byteOrdering;
	readonly int effectiveBits;
	readonly bool isLsbAligned;


	/// <summary>
	/// Initialize new <see cref="BaseYuv444p16ImageRenderer"/> instance.
	/// </summary>
	/// <param name="format">Supported format.</param>
	/// <param name="effectiveBits">Effective bits for each Y/U/V component.</param>
	/// <param name="lsbAligned">True if effective bits are aligned to LSB, False if aligned to MSB.</param>
	/// <param name="byteOrdering">Fixed byte ordering, or Null if byte ordering can be specified by user.</param>
	protected BaseYuv444p16ImageRenderer(ImageFormat format, int effectiveBits, bool lsbAligned, ByteOrdering? byteOrdering = null) : base(format)
	{
		if (effectiveBits < 10 || effectiveBits > 16)
			throw new ArgumentOutOfRangeException(nameof(effectiveBits));
		if (byteOrdering.HasValue == format.HasMultipleByteOrderings)
			throw new ArgumentException("Invalid combination of fixed byte ordering and image format.");
		this.byteOrdering = byteOrdering;
		this.effectiveBits = effectiveBits;
		this.isLsbAligned = lsbAligned;
	}


	// Create default plane options.
	public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new List<ImagePlaneOptions>().Also((it) =>
	{
		it.Add(new ImagePlaneOptions(2, width * 2));
		it.Add(new ImagePlaneOptions(2, width * 2));
		it.Add(new ImagePlaneOptions(2, width * 2));
	});


	// Evaluate pixel count.
	public override int EvaluatePixelCount(IImageDataSource source) => (int)(source.Size / 6);


	// Evaluate source data size.
	public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions)
	{
		width &= 0x7ffffffe;
		height &= 0x7ffffffe;
		if (width <= 0 || height <= 0)
			return 0;
		var yRowStride = Math.Max(width * 2, planeOptions[0].RowStride);
		var uv1RowStride = Math.Max(width * 2, planeOptions[1].RowStride);
		var uv2RowStride = Math.Max(width * 2, planeOptions[2].RowStride);
		return (yRowStride * height) + (uv1RowStride * height) + (uv2RowStride * height);
	}


	// Render.
	protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
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
		if (width <= 0 || height <= 0
			|| yPixelStride <= 0 || yRowStride <= 0 || (yPixelStride * width) > yRowStride
			|| uv2PixelStride <= 0 || uv2RowStride <= 0 || (uv2PixelStride * width) > uv2RowStride
			|| uv1PixelStride <= 0 || uv1RowStride <= 0 || (uv1PixelStride * width) > uv1RowStride)
		{
			throw new ArgumentException($"Invalid pixel/row stride.");
		}

		// select color converter
		var converter = renderingOptions.YuvToBgraConverter ?? YuvToBgraConverter.Default;
		var yuvExtractor = this.Create16BitColorExtraction(this.byteOrdering ?? renderingOptions.ByteOrdering, this.effectiveBits, lsbAligned: this.isLsbAligned);

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
					var bitmapPixelPtr = (ushort*)bitmapRowPtr;
					var isLastRow = imageStream.Read(yRow, 0, yRowStride) < yRowStride || rowIndex >= height - 1;
					for (var columnIndex = 0; columnIndex < width; ++columnIndex, yPixelPtr += yPixelStride, bitmapPixelPtr += 4)
						bitmapPixelPtr[0] = yuvExtractor(yPixelPtr[0], yPixelPtr[1]);
					if (cancellationToken.IsCancellationRequested)
						return;
					if (isLastRow)
						break;
					Array.Clear(yRow, 0, yRowStride);
				}
			}

			// read UV1
			var uv1Row = new byte[uv1RowStride];
			bitmapRowPtr = (byte*)bitmapBaseAddress;
			fixed (byte* uv1RowPtr = uv1Row)
			{
				for (var rowIndex = 0; rowIndex < height; ++rowIndex, bitmapRowPtr += bitmapRowStride)
				{
					// read UV row
					var uvPixelPtr = uv1RowPtr;
					var bitmapPixelPtr = (ushort*)bitmapRowPtr;
					var isLastRow = imageStream.Read(uv1Row, 0, uv1RowStride) < uv1RowStride || rowIndex >= height - 1;
					for (var columnIndex = 0; columnIndex < width; ++columnIndex, uvPixelPtr += uv1PixelStride, bitmapPixelPtr += 4)
						bitmapPixelPtr[1] = yuvExtractor(uvPixelPtr[0], uvPixelPtr[1]);

					// check state
					if (cancellationToken.IsCancellationRequested)
						return;
					if (isLastRow)
						break;
					Array.Clear(uv1Row, 0, uv1RowStride);
				}
			}

			// read UV2
			var uv2Row = new byte[uv2RowStride];
			bitmapRowPtr = (byte*)bitmapBaseAddress;
			fixed (byte* uv2RowPtr = uv2Row)
			{
				for (var rowIndex = 0; rowIndex < height; ++rowIndex, bitmapRowPtr += bitmapRowStride)
				{
					// read UV row
					var uvPixelPtr = uv2RowPtr;
					var bitmapPixelPtr = (ushort*)bitmapRowPtr;
					var isLastRow = imageStream.Read(uv2Row, 0, uv2RowStride) < uv2RowStride || rowIndex >= height - 1;
					for (var columnIndex = 0; columnIndex < width; ++columnIndex, uvPixelPtr += uv2PixelStride, bitmapPixelPtr += 4)
						bitmapPixelPtr[2] = yuvExtractor(uvPixelPtr[0], uvPixelPtr[1]);

					// check state
					if (cancellationToken.IsCancellationRequested)
						return;
					if (isLastRow)
						break;
					Array.Clear(uv2Row, 0, uv2RowStride);
				}
			}

			// convert to BGRA
			ImageProcessing.ParallelFor(0, height, (y) =>
			{
				var bitmapPixelPtr = (ushort*)((byte*)bitmapBaseAddress + y * bitmapRowStride);
				for (var x = width; x > 0; --x, bitmapPixelPtr += 4)
				{
					this.SelectUV(bitmapPixelPtr[1], bitmapPixelPtr[2], out var u, out var v);
					converter.ConvertFromYuv444ToBgra64(bitmapPixelPtr[0], u, v, (ulong*)bitmapPixelPtr);
				}
				if (cancellationToken.IsCancellationRequested)
					throw new TaskCanceledException();
			});
		});

		// complete
		return new ImageRenderingResult();
	}


	// Rendered format.
	public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
		Task.FromResult(BitmapFormat.Bgra64);


	/// <summary>
	/// Select U, V component.
	/// </summary>
	/// <param name="uv1">First component read from source.</param>
	/// <param name="uv2">Second component read from source.</param>
	/// <param name="u">Selected U.</param>
	/// <param name="v">Selected V.</param>
	protected abstract void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v);
}
