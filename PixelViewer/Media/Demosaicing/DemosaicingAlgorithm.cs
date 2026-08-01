using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using CarinaStudio.Threading;
using System;
using System.Threading;

namespace Carina.PixelViewer.Media.Demosaicing;

/// <summary>
/// Algorithm to perform demosaicing on image which is rendered with Bayer Filter pattern.
/// </summary>
/// <param name="id">Unique identifier of algorithm.</param>
abstract class DemosaicingAlgorithm(string id)
{
	/// <summary>
	/// Perform demosaicing on the given image.
	/// </summary>
	/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> which contains the image rendered with Bayer Filter pattern, the missing color components of its pixels are interpolated in place.</param>
	/// <param name="bayerPattern">Pattern of Bayer Filter which the image is rendered with.</param>
	/// <param name="colorComponentSelector">Function which accepts horizontal and vertical position of pixel, and returns the color component provided by the pixel.</param>
	/// <param name="renderingOptions">Rendering options which the image is rendered with.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	[CalledOnBackgroundThread]
	public abstract void Demosaic(IBitmapBuffer bitmapBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken);


	/// <summary>
	/// Get name of algorithm for displaying to user.
	/// </summary>
	/// <remarks>The name defined in string resource is returned for a built-in algorithm, or its <see cref="Id"/> if no string resource is defined for it.</remarks>
	public virtual string DisplayName =>
		Application.CurrentOrNull?.GetStringNonNull($"DemosaicingAlgorithm.{this.Id}", this.Id) ?? this.Id;


	/// <summary>
	/// Get unique identifier of algorithm.
	/// </summary>
	/// <remarks>The identifier is the value persisted to refer to the algorithm, it is stable across renaming of a user-defined algorithm.</remarks>
	public string Id { get; } = id;


	/// <inheritdoc/>
	public override string ToString() => this.Id;
}
