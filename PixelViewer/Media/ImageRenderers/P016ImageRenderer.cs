using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// <see cref="IImageRenderer"/> which supports rendering image with 16-bit YUV420p based format.
    /// </summary>
    class P016ImageRenderer : BaseYuv420p16ImageRenderer
    {
        public P016ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "P016", "P016 (16-bit YUV420p)", true, new ImagePlaneDescriptor[] {
            new ImagePlaneDescriptor(2),
            new ImagePlaneDescriptor(2),
            new ImagePlaneDescriptor(2),
        }), 16)
        { }


        // Select UV component.
        protected override void SelectUV(byte uv1, byte uv2, out byte u, out byte v)
        {
            u = uv1;
            v = uv2;
        }
    }
}
