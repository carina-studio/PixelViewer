namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Implementation of <see cref="IImageRenderer"/> which renders image with 14-bit Bayer filter pattern whose samples are packed into bits.
/// </summary>
class PackedBayerPattern14ImageRenderer() : PackedBayerPatternImageRenderer(14, [ "Packed14", "RAW14P" ]);
