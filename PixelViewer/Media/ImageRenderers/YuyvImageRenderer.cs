namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// <see cref="IImageRenderer"/> which supports rendering image with YUYV format.
/// </summary>
class YuyvImageRenderer : SinglePlaneYuv422ImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="YuyvImageRenderer"/> instance.
    /// </summary>
    public YuyvImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "YUYV", new ImagePlaneDescriptor(4), new string[]{ "YUYV" }))
    { }


    // Select YUV component.
    protected override void SelectYuv(byte byte1, byte byte2, byte byte3, byte byte4, out byte y1, out byte y2, out byte u, out byte v)
    {
        y1 = byte1;
        y2 = byte3;
        u = byte2;
        v = byte4;
    }
}