using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Base implementation of <see cref="IImageRenderer"/> which renders image with bayer filter pattern.
/// </summary>
abstract class BayerPatternImageRenderer : SinglePlaneImageRenderer
{
    /// <summary>
    /// Index of blue color.
    /// </summary>
    protected const int BlueColorComponent = (int)BayerPatternColorComponent.Blue;
	/// <summary>
	/// Index of green color.
	/// </summary>
    protected const int GreenColorComponent = (int)BayerPatternColorComponent.Green;
	/// <summary>
	/// Index of red color.
	/// </summary>
    protected const int RedColorComponent = (int)BayerPatternColorComponent.Red;


    /// <summary>
    /// Initialize new <see cref="BayerPatternImageRenderer"/> instance.
    /// </summary>
    /// <param name="format">Format.</param>
    protected BayerPatternImageRenderer(ImageFormat format) : base(format)
    { }
    
    
    /// <summary>
    /// Build color transformation table for single color of BGRA32.
    /// </summary>
    /// <param name="table">Pointer to table, the length should be 256.</param>
    /// <param name="gain">Gain for color.</param>
    protected static unsafe void BuildColorTransformationTableUnsafe(byte* table, double gain)
    {
        table += 255;
        if (Math.Abs(gain - 1) <= 0.0001)
        {
	        for (var i = 255; i >= 0; --i, --table)
		        *table = (byte)i;
        }
        else
        {
	        for (var i = 255; i >= 0; --i, --table)
		        *table = ImageProcessing.ClipToByte(i * gain);
        }
    }


	/// <summary>
	/// Build color transformation table for single color of BGRA64.
	/// </summary>
	/// <param name="table">Pointer to table, the length should be 65536.</param>
	/// <param name="gain">Gain for color.</param>
	protected static unsafe void BuildColorTransformationTableUnsafe(ushort* table, double gain)
	{
		table += 65535;
		if (Math.Abs(gain - 1) <= 0.0001)
		{
			for (var i = 65535; i >= 0; --i, --table)
				*table = (ushort)i;
		}
		else
		{
			for (var i = 65535; i >= 0; --i, --table)
				*table = ImageProcessing.ClipToUInt16(i * gain);
		}
	}


	/// <inheritdoc/>
	protected override ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// get parameters
		var width = bitmapBuffer.Width;
		var height = bitmapBuffer.Height;
		if (width <= 0 || height <= 0)
			throw new ArgumentException($"Invalid size: {width}x{height}.");

		// select color pattern
		var colorComponentSelector = renderingOptions.BayerPattern.CreateColorComponentSelector();

		// render, demosaicing is performed by the caller because it may need another buffer to receive the result
		var result = this.OnRender(source, imageStream, bitmapBuffer, colorComponentSelector, renderingOptions, planeOptions, cancellationToken);
		if (cancellationToken.IsCancellationRequested)
			throw new TaskCanceledException();

		// complete
		return result;
	}


	/// <summary>
	/// Called to render image.
	/// </summary>
	/// <param name="source">Source of image data.</param>
	/// <param name="imageStream">Stream to read image data.</param>
	/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> to put rendered bayer pattern image.</param>
	/// <param name="colorComponentSelector">Function to select color component for given pixel position.</param>
	/// <param name="renderingOptions">Rendering options.</param>
	/// <param name="planeOptions">Plane options.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Result of rendering.</returns>
	protected abstract ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


    /// <inheritdoc/>
    public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
        Task.FromResult(BitmapFormat.Bgra64);
}