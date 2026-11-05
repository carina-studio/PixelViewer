namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Implementation of <see cref="IImageRenderer"/> which renders image with 10-bit Bayer filter pattern whose samples are packed into bits.
/// </summary>
class PackedBayerPattern10ImageRenderer() : PackedBayerPatternImageRenderer(10, [ "Packed10", "RAW10P" ]);
