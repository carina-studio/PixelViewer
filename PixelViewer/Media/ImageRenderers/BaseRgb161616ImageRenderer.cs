using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with RGB_161616 based format.
/// </summary>
abstract class BaseRgb161616ImageRenderer : SinglePlaneImageRenderer
{
    /// <summary>
	/// Initialize new <see cref="BaseRgb161616ImageRenderer"/> instance.
	/// </summary>
	/// <param name="format">Supported format.</param>
	protected BaseRgb161616ImageRenderer(ImageFormat format) : base(format)
	{ }


	/// <inheritdoc/>
	public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => new ImagePlaneOptions[]{
		new(16, 6, width * 6)
	};


	// Create the color table which maps the value of a color to its color, a color without color table is rendered
	// by its own value so the value covers the full range which the samples can carry.
	static ColorTable CreateSourceColorTable(ColorTable? colorTable)
	{
		if (colorTable is not null)
			return colorTable.Share();
		return new ColorTable(65536, 16).Also(it =>
		{
			var colors = it.Memory.Span;
			for (var i = 65535; i >= 0; --i)
				colors[i] = (uint)i;
		});
	}


	/// <inheritdoc/>
	public override bool IsColorTableSupported => true;


    /// <inheritdoc/>
    protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// get parameters
		var width = bitmapBuffer.Width;
		var height = bitmapBuffer.Height;
		var pixelStride = planeOptions[0].PixelStride;
		var rowStride = planeOptions[0].RowStride;
		if (pixelStride <= 0 || rowStride <= 0)
			throw new ArgumentException($"Invalid pixel/row stride: {pixelStride}/{rowStride}.");

		// prepare packing function, the color tables replace the effective bits of the image so each component is read as a whole 16-bit value to index them
		var hasColorTables = renderingOptions.HasColorTables;
		var extractComponentFunc = this.Create16BitColorExtraction(renderingOptions.ByteOrdering, hasColorTables ? 16 : planeOptions[0].EffectiveBits);
		var packFunc = ImageProcessing.SelectBgra64Packing();

		// build the mapping of each color through its color table, the levels are colors in the color tables instead of values of components
		ushort[][]? mappings = null;
		if (hasColorTables)
		{
			var blackLevel = planeOptions[0].BlackLevel.GetValueOrDefault();
			var whiteLevel = planeOptions[0].WhiteLevel ?? SelectDefaultWhiteLevel(renderingOptions);
			if (blackLevel >= whiteLevel)
				throw new ArgumentException($"Invalid black/white level: {blackLevel}, {whiteLevel}.");
			using var redSourceColorTable = CreateSourceColorTable(renderingOptions.RedColorTable);
			using var greenSourceColorTable = CreateSourceColorTable(renderingOptions.GreenColorTable);
			using var blueSourceColorTable = CreateSourceColorTable(renderingOptions.BlueColorTable);
			mappings =
			[
				CreateColorTableTo16BitColorMapping(redSourceColorTable, blackLevel, whiteLevel),
				CreateColorTableTo16BitColorMapping(greenSourceColorTable, blackLevel, whiteLevel),
				CreateColorTableTo16BitColorMapping(blueSourceColorTable, blackLevel, whiteLevel),
			];
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
					var bitmapPixelPtr = (ulong*)bitmapRowPtr;
					for (var x = width; x > 0; --x, srcPixelPtr += pixelStride, ++bitmapPixelPtr)
					{
						var component1 = extractComponentFunc(srcPixelPtr[0], srcPixelPtr[1]);
						var component2 = extractComponentFunc(srcPixelPtr[2], srcPixelPtr[3]);
						var component3 = extractComponentFunc(srcPixelPtr[4], srcPixelPtr[5]);
						this.SelectRgb(component1, component2, component3, out var r, out var g, out var b);
						if (mappings is not null)
						{
							r = mappings[0][r];
							g = mappings[1][g];
							b = mappings[2][b];
						}
						*bitmapPixelPtr = packFunc(b, g, r, 65535);
					}
					if (isLastRow || cancellationToken.IsCancellationRequested)
						break;
					Array.Clear(srcRow, 0, rowStride);
				}
			});
		}

		// complete
		return new ImageRenderingResult();
	}


	// Select the white level to be used when it is not defined by the image plane.
	static uint SelectDefaultWhiteLevel(ImageRenderingOptions renderingOptions)
	{
		var colorBitDepth = Math.Max(renderingOptions.BlueColorTable?.ColorBitDepth ?? 0, renderingOptions.GreenColorTable?.ColorBitDepth ?? 0);
		colorBitDepth = Math.Max(colorBitDepth, renderingOptions.RedColorTable?.ColorBitDepth ?? 0);
		return (uint)((1L << colorBitDepth) - 1);
	}


    /// <inheritdoc/>
    public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
        Task.FromResult(BitmapFormat.Bgra64);


    /// <summary>
    /// Select R, G, B components.
    /// </summary>
    /// <param name="component1">1st component read from source.</param>
    /// <param name="component2">2nd component read from source.</param>
    /// <param name="component3">3rd component read from source.</param>
    /// <param name="r">Selected R.</param>
    /// <param name="g">Selected G.</param>
    /// <param name="b">Selected B.</param>
    protected abstract void SelectRgb(ushort component1, ushort component2, ushort component3, out ushort r, out ushort g, out ushort b);
}


/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with BGR_161616 format.
/// </summary>
class Bgr161616ImageRenderer : BaseRgb161616ImageRenderer
{
	/// <summary>
	/// Initialize new <see cref="Bgr161616ImageRenderer"/> instance.
	/// </summary>
	public Bgr161616ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "BGR_161616", true, new ImagePlaneDescriptor(6, 9, 16), new[]{ "BGR161616", "BGR_161616", "BGR48" }))
	{ }


	/// <inheritdoc/>
	protected override void SelectRgb(ushort component1, ushort component2, ushort component3, out ushort r, out ushort g, out ushort b)
	{
		b = component1;
		g = component2;
		r = component3;
	}
}


/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with RGB_161616 format.
/// </summary>
class Rgb161616ImageRenderer : BaseRgb161616ImageRenderer
{
	/// <summary>
	/// Initialize new <see cref="Rgb161616ImageRenderer"/> instance.
	/// </summary>
	public Rgb161616ImageRenderer() : base(new ImageFormat(ImageFormatCategory.RGB, "RGB_161616", true, new ImagePlaneDescriptor(6, 9, 16), new[]{ "RGB161616", "RGB_161616", "RGB48" }))
	{ }


	/// <inheritdoc/>
	protected override void SelectRgb(ushort component1, ushort component2, ushort component3, out ushort r, out ushort g, out ushort b)
	{
		r = component1;
		g = component2;
		b = component3;
	}
}