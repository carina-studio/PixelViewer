using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with 16-bit YUV420sp based format.
/// </summary>
abstract class BaseYuv420sp16ImageRenderer : BaseImageRenderer
{
	// Fields.
	readonly ByteOrdering? byteOrdering;
	readonly int effectiveBits;
	readonly bool isLsbAligned;


	/// <summary>
	/// Initialize new <see cref="BaseYuv420sp16ImageRenderer"/> instance.
	/// </summary>
	/// <param name="format">Supported format.</param>
	/// <param name="effectiveBits">Effective bits for each Y/U/V component.</param>
	/// <param name="lsbAligned">True if effective bits are aligned to LSB, False if aligned to MSB.</param>
	/// <param name="byteOrdering">Fixed byte ordering, or Null if byte ordering can be specified by user.</param>
	protected BaseYuv420sp16ImageRenderer(ImageFormat format, int effectiveBits, bool lsbAligned, ByteOrdering? byteOrdering = null) : base(format)
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
		it.Add(new ImagePlaneOptions(4, width * 2));
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
		var uvRowStride = Math.Max(width * 2, planeOptions[1].RowStride);
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
			throw new ArgumentException("Invalid pixel/row stride.");
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
					var bitmapPixelPtr = (ushort*)bitmapRowPtr;
					for (var columnIndex = 0; columnIndex < width; columnIndex += 2, vuPixelPtr += uvPixelStride, bitmapPixelPtr += 8)
						this.SelectUV(yuvExtractor(vuPixelPtr[0], vuPixelPtr[1]), yuvExtractor(vuPixelPtr[2], vuPixelPtr[3]), out bitmapPixelPtr[1], out bitmapPixelPtr[2]);

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
				var bitmapPixelPtr = (ushort*)((byte*)bitmapBaseAddress + (y << 1) * bitmapRowStride);
				var bottomBitmapPixelPtr = (ushort*)((byte*)bitmapPixelPtr + bitmapRowStride);
				for (var x = width; x > 0; x -= 2, bitmapPixelPtr += 8, bottomBitmapPixelPtr += 8)
				{
					var u = bitmapPixelPtr[1];
					var v = bitmapPixelPtr[2];
					converter.ConvertFromYuv422ToBgra64(bitmapPixelPtr[0], bitmapPixelPtr[4], u, v, (ulong*)bitmapPixelPtr, (ulong*)(bitmapPixelPtr + 4));
					converter.ConvertFromYuv422ToBgra64(bottomBitmapPixelPtr[0], bottomBitmapPixelPtr[4], u, v, (ulong*)bottomBitmapPixelPtr, (ulong*)(bottomBitmapPixelPtr + 4));
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
class P010ImageRenderer() : BaseYuv420sp16ImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "P010", false, [
	new(2),
	new(4)
], [ "P010" ]), 10, false, ByteOrdering.LittleEndian)
{
    // Select UV component.
    protected override void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v)
    {
        u = uv1;
        v = uv2;
    }
}


/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with 12-bit YUV420sp based format.
/// </summary>
class P012ImageRenderer() : BaseYuv420sp16ImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "P012", false, [
	new(2),
	new(4)
], [ "P012" ]), 12, false, ByteOrdering.LittleEndian)
{
    // Select UV component.
    protected override void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v)
    {
        u = uv1;
        v = uv2;
    }
}


/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with 16-bit YUV420p based format.
/// </summary>
class P016ImageRenderer() : BaseYuv420sp16ImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "P016", false, [
	new(2),
	new(4)
], [ "P016" ]), 16, false, ByteOrdering.LittleEndian)
{
    // Select UV component.
    protected override void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v)
    {
        u = uv1;
        v = uv2;
    }
}