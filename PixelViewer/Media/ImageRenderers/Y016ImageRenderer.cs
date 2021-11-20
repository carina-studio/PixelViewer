using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// <see cref="IImageRenderer"/> which supports rendering image with 16-bit YUV420sp based format.
    /// </summary>
    class Y016ImageRenderer : BaseYuv420sp16ImageRenderer
    {
        public Y016ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "Y016", "Y016 (16-bit YUV420sp)", true, new ImagePlaneDescriptor[] {
            new ImagePlaneDescriptor(2),
            new ImagePlaneDescriptor(4),
        }), 16)
        { }


        // Select UV component.
        protected override void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v)
        {
            u = uv1;
            v = uv2;
        }
    }
}
