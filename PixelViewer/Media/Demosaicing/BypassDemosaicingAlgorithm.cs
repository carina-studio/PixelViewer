using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using CarinaStudio.Threading;
using System;
using System.Threading;

namespace Carina.PixelViewer.Media.Demosaicing;

/// <summary>
/// <see cref="DemosaicingAlgorithm"/> which keeps the mosaic of image without demosaicing.
/// </summary>
/// <remarks>The algorithm is the selectable representation of "no demosaicing", it is converted to null before reaching an image renderer or being saved to a rendering profile.</remarks>
class BypassDemosaicingAlgorithm() : DemosaicingAlgorithm("Bypass")
{
	/// <inheritdoc/>
	[CalledOnBackgroundThread]
	public override void Demosaic(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		// the mosaic is kept as-is, copying is needed only when the destination is another buffer
		srcBuffer.CopyTo(destBuffer);
	}


	/// <inheritdoc/>
	public override bool IsInPlaceDemosaicingSupported => true;
}
