namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which renders image with MIPI CSI-2 10-bit YUV420 format.
/// </summary>
/// <remarks>Every 4 components are packed in 5 bytes: the 8 most significant bits of each component in the first 4 bytes, followed by 1 byte which carries the 2 least significant bits of each component, the first component in the lowest bits.</remarks>
class Yuv420Mipi10ImageRenderer() : BaseYuv420MipiImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "YUV420_10_MIPI", [
	new ImagePlaneDescriptor(0, 10, 10),
	new ImagePlaneDescriptor(0, 10, 10),
], [ "YUV420_10_MIPI" ]), 10)
{
	/// <inheritdoc/>
	protected override unsafe void ReadLumaChromaRow(byte* row, int width, ushort* y, ushort* u, ushort* v) =>
		Yuv422Mipi10ImageRenderer.ReadPackedUyvyRow(row, width, y, u, v);


	/// <inheritdoc/>
	protected override unsafe void ReadLumaRow(byte* row, int width, ushort* y)
	{
		for (var x = 0; x < width; x += 4, row += 5, y += 4)
		{
			var lsbs = row[4];
			y[0] = Yuv422Mipi10ImageRenderer.ScaleTo16Bit((row[0] << 2) | (lsbs & 0x3));
			y[1] = Yuv422Mipi10ImageRenderer.ScaleTo16Bit((row[1] << 2) | ((lsbs >> 2) & 0x3));
			y[2] = Yuv422Mipi10ImageRenderer.ScaleTo16Bit((row[2] << 2) | ((lsbs >> 4) & 0x3));
			y[3] = Yuv422Mipi10ImageRenderer.ScaleTo16Bit((row[3] << 2) | (lsbs >> 6));
		}
	}
}
