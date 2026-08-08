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
	/// Check whether a dedicated buffer is needed to receive the result of demosaicing the given image or not.
	/// </summary>
	/// <param name="pattern">Pattern of Bayer Filter which the image is rendered with.</param>
	/// <param name="width">Width of image in pixels.</param>
	/// <param name="height">Height of image in pixels.</param>
	/// <returns>Requirement of dedicated buffer to receive the result of demosaicing.</returns>
	/// <remarks>
	/// The algorithm reports <see cref="OutputBufferRequirement.NotRequired"/> as long as interpolating a color component never needs a color component interpolated before, which costs no extra buffer at all. <see cref="OutputBufferRequirement.Required"/> makes the caller allocate a dedicated buffer, and demosaicing with the same buffer as both the source and the destination is rejected instead of corrupting the pixels silently.
	/// <see cref="OutputBufferRequirement.Preferred"/> is for the algorithm which works in both ways but interpolates better with a dedicated buffer. The caller is free to hand over either arrangement for it, so the algorithm must check <see cref="IBitmapBuffer.IsBufferSharedWith"/> to know which one it got instead of assuming the preferred one.
	/// The answer is allowed to differ between patterns and dimensions because an algorithm may need to pre-process the mosaic of one pattern while it can interpolate another directly, and because the cost of a dedicated buffer grows with the size of image. It is meaningful only for a pattern which <see cref="IsBayerPatternSupported"/> accepts, the caller never demosaics an image with an unsupported pattern.
	/// </remarks>
	public abstract OutputBufferRequirement CheckOutputBufferRequirement(BayerPattern pattern, int width, int height);


	/// <summary>
	/// Perform demosaicing on the given image.
	/// </summary>
	/// <param name="srcBuffer"><see cref="IBitmapBuffer"/> which contains the image rendered with Bayer Filter pattern, each of its pixels provides one color component only.</param>
	/// <param name="destBuffer"><see cref="IBitmapBuffer"/> to receive the image with all color components of its pixels.</param>
	/// <param name="bayerPattern">Pattern of Bayer Filter which the image is rendered with.</param>
	/// <param name="colorComponentSelector">Function which accepts horizontal and vertical position of pixel, and returns the color component provided by the pixel.</param>
	/// <param name="renderingOptions">Rendering options which the image is rendered with.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <remarks>Both buffers are guaranteed to have the same format and dimensions by the caller. <paramref name="destBuffer"/> shares its buffer with <paramref name="srcBuffer"/>, which can be checked by <see cref="IBitmapBuffer.IsBufferSharedWith"/>, only if <see cref="CheckOutputBufferRequirement"/> doesn't report <see cref="OutputBufferRequirement.Required"/> for <paramref name="bayerPattern"/>. Every pixel of <paramref name="destBuffer"/> should be filled by the algorithm, including the color component provided by the pixel itself, because the buffer may be a newly allocated one.</remarks>
	[CalledOnBackgroundThread]
	public abstract void Demosaic(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken);


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


	/// <summary>
	/// Check whether the given pattern of Bayer Filter is supported by the algorithm or not.
	/// </summary>
	/// <param name="pattern">Pattern of Bayer Filter.</param>
	/// <returns>True if the pattern is supported by the algorithm.</returns>
	/// <remarks>The algorithm is excluded from <see cref="Carina.PixelViewer.ViewModels.Session.DemosaicingAlgorithms"/> for an unsupported pattern, so it cannot be selected by user at all instead of falling back to another behavior silently.</remarks>
	public virtual bool IsBayerPatternSupported(BayerPattern pattern) => true;


	/// <inheritdoc/>
	public override string ToString() => this.Id;
}
