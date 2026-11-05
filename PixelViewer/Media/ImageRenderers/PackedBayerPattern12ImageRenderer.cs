namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Implementation of <see cref="IImageRenderer"/> which renders image with 12-bit Bayer filter pattern whose samples are packed into bits.
/// </summary>
class PackedBayerPattern12ImageRenderer() : PackedBayerPatternImageRenderer(12, [ "Packed12", "RAW12P" ]);
