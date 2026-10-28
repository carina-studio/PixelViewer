namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which renders image with MIPI CSI-2 8-bit YUV420 format.
/// </summary>
class Yuv420Mipi8ImageRenderer() : BaseYuv420MipiImageRenderer(new ImageFormat(ImageFormatCategory.YUV, "YUV420_8_MIPI", [
	new ImagePlaneDescriptor(0, 8, 8),
	new ImagePlaneDescriptor(0, 8, 8),
], [ "YUV420_8_MIPI" ]), 8)
{
	/// <inheritdoc/>
	protected override unsafe void ReadLumaChromaRow(byte* row, int width, ushort* y, ushort* u, ushort* v)
	{
		for (var x = 0; x < width; x += 2, row += 4, y += 2, ++u, ++v)
		{
			*u = row[0];
			y[0] = row[1];
			*v = row[2];
			y[1] = row[3];
		}
	}


	/// <inheritdoc/>
	protected override unsafe void ReadLumaRow(byte* row, int width, ushort* y)
	{
		for (var x = 0; x < width; ++x)
			y[x] = row[x];
	}
}
