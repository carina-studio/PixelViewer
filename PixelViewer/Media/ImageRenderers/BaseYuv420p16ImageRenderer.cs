using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with 16-bit YUV420p based format.
/// </summary>
abstract class BaseYuv420p16ImageRenderer : BaseImageRenderer
{
	// Fields.
	readonly ByteOrdering? byteOrdering;
	readonly int effectiveBits;
	readonly bool eliminateTailPadding;
	readonly bool isLsbAligned;


	/// <summary>
	/// Initialize new <see cref="BaseYuv420p16ImageRenderer"/> instance.
	/// </summary>
	/// <param name="format">Supported format.</param>
	/// <param name="effectiveBits">Effective bits for each Y/U/V component.</param>
	/// <param name="lsbAligned">True if effective bits are aligned to LSB, False if aligned to MSB.</param>
	/// <param name="eliminateTailPadding">True to ignore padding bytes in each tail of plane.</param>
	/// <param name="byteOrdering">Fixed byte ordering, or Null if byte ordering can be specified by user.</param>
	protected BaseYuv420p16ImageRenderer(ImageFormat format, int effectiveBits, bool lsbAligned, bool eliminateTailPadding = false, ByteOrdering? byteOrdering = null) : base(format)
	{
		if (effectiveBits < 10 || effectiveBits > 16)
			throw new ArgumentOutOfRangeException(nameof(effectiveBits));
		if (byteOrdering.HasValue == format.HasMultipleByteOrderings)
			throw new ArgumentException("Invalid combination of fixed byte ordering and image format.");
		this.byteOrdering = byteOrdering;
		this.effectiveBits = effectiveBits;
		this.eliminateTailPadding = eliminateTailPadding;
		this.isLsbAligned = lsbAligned;
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
			|| uv2PixelStride <= 0 || uv2RowStride <= 0 || (uv2PixelStride * width / 2) > uv2RowStride
			|| uv1PixelStride <= 0 || uv1RowStride <= 0 || (uv1PixelStride * width / 2) > uv1RowStride)
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
			var bitmapRowStride2 = bitmapRowStride << 1;
			fixed (byte* yRowPtr = yRow)
			{
				for (var rowIndex = 0; rowIndex < height; ++rowIndex, bitmapRowPtr += bitmapRowStride)
				{
					var yPixelPtr = yRowPtr;
					var bitmapPixelPtr = (ushort*)bitmapRowPtr;
					var isLastRow = (rowIndex == height - 1);
					if (isLastRow && this.eliminateTailPadding)
						_ = imageStream.Read(yRow, 0, yPixelStride * (width - 1) + 2);
					else
						_ = imageStream.Read(yRow, 0, yRowStride);
					for (var columnIndex = 0; columnIndex < width; ++columnIndex, yPixelPtr += yPixelStride, bitmapPixelPtr += 4)
						bitmapPixelPtr[0] = yuvExtractor(yPixelPtr[0], yPixelPtr[1]);
					if (cancellationToken.IsCancellationRequested)
						return;
					if (!isLastRow)
						Array.Clear(yRow, 0, yRowStride);
				}
			}

			// read UV1
			var uv1Row = new byte[uv1RowStride];
			bitmapRowPtr = (byte*)bitmapBaseAddress;
			fixed (byte* uv1RowPtr = uv1Row)
			{
				for (var rowIndex = 0; rowIndex < height; rowIndex += 2, bitmapRowPtr += bitmapRowStride2)
				{
					// read UV row
					var isLastRow = (rowIndex == height - 2);
					if (isLastRow && this.eliminateTailPadding)
						_ = imageStream.Read(uv1Row, 0, uv1PixelStride * (width / 2 - 1) + 2);
					else
						_ = imageStream.Read(uv1Row, 0, uv1RowStride);
					var uvPixelPtr = uv1RowPtr;
					var bitmapPixelPtr = (ushort*)bitmapRowPtr;
					for (var columnIndex = 0; columnIndex < width; columnIndex += 2, uvPixelPtr += uv1PixelStride, bitmapPixelPtr += 8)
						bitmapPixelPtr[1] = yuvExtractor(uvPixelPtr[0], uvPixelPtr[1]);

					// check state
					if (cancellationToken.IsCancellationRequested)
						return;
					if (!isLastRow)
						Array.Clear(uv1Row, 0, uv1RowStride);
				}
			}

			// read UV2
			var uv2Row = new byte[uv2RowStride];
			bitmapRowPtr = (byte*)bitmapBaseAddress;
			fixed (byte* uv2RowPtr = uv2Row)
			{
				for (var rowIndex = 0; rowIndex < height; rowIndex += 2, bitmapRowPtr += bitmapRowStride2)
				{
					// read UV row
					var isLastRow = (rowIndex == height - 2);
					if (isLastRow && this.eliminateTailPadding)
						_ = imageStream.Read(uv2Row, 0, uv2PixelStride * (width / 2 - 1) + 2);
					else
						_ = imageStream.Read(uv2Row, 0, uv2RowStride);
					var uvPixelPtr = uv2RowPtr;
					var bitmapPixelPtr = (ushort*)bitmapRowPtr;
					for (var columnIndex = 0; columnIndex < width; columnIndex += 2, uvPixelPtr += uv2PixelStride, bitmapPixelPtr += 8)
						bitmapPixelPtr[2] = yuvExtractor(uvPixelPtr[0], uvPixelPtr[1]);

					// check state
					if (cancellationToken.IsCancellationRequested)
						return;
					if (!isLastRow)
						Array.Clear(uv2Row, 0, uv2RowStride);
				}
			}

			// convert to BGRA
			ImageProcessing.ParallelFor(0, height >> 1, (y) =>
			{
				var bitmapPixelPtr = (byte*)bitmapBaseAddress + (y << 1) * bitmapRowStride;
				var bottomBitmapPixelPtr = bitmapPixelPtr + bitmapRowStride;
				for (var x = width; x > 0; x -= 2, bitmapPixelPtr += 16, bottomBitmapPixelPtr += 16)
				{
					this.SelectUV(*(ushort*)(bitmapPixelPtr + 2), *(ushort*)(bitmapPixelPtr + 4), out var u, out var v);
					converter.ConvertFromYuv422ToBgra64(*(ushort*)bitmapPixelPtr, *(ushort*)(bitmapPixelPtr + 8), u, v, (ulong*)bitmapPixelPtr, (ulong*)(bitmapPixelPtr + 8));
					converter.ConvertFromYuv422ToBgra64(*(ushort*)bottomBitmapPixelPtr, *(ushort*)(bottomBitmapPixelPtr + 8), u, v, (ulong*)bottomBitmapPixelPtr, (ulong*)(bottomBitmapPixelPtr + 8));
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


/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with 10-bit YUV420sp based format.
/// </summary>
class Y010ImageRenderer() : BaseYuv420p16ImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "Y010", false, [
	new ImagePlaneDescriptor(2),
	new ImagePlaneDescriptor(2),
	new ImagePlaneDescriptor(2)
], [ "Y010" ]), 10, false, byteOrdering: ByteOrdering.LittleEndian)
{
	// Select UV component.
	protected override void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v)
	{
		u = uv1;
		v = uv2;
	}
}


/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with 16-bit YUV420sp based format.
/// </summary>
class Y016ImageRenderer() : BaseYuv420p16ImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "Y016", false, [
	new ImagePlaneDescriptor(2),
	new ImagePlaneDescriptor(2),
	new ImagePlaneDescriptor(2)
], [ "Y016" ]), 16, false, byteOrdering: ByteOrdering.LittleEndian)
{
	// Select UV component.
	protected override void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v)
	{
		u = uv1;
		v = uv2;
	}
}