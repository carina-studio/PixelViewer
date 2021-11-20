using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// <see cref="IImageRenderer"/> which supports rendering image with 10-bit YUV420sp based format.
    /// </summary>
    class Y010ImageRenderer : BaseYuv420sp16ImageRenderer
    {
        public Y010ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "Y010", "Y010 (10-bit YUV420sp)", true, new ImagePlaneDescriptor[] {
            new ImagePlaneDescriptor(2),
            new ImagePlaneDescriptor(4),
        }), 10)
        { }


        // Select UV component.
        protected override void SelectUV(ushort uv1, ushort uv2, out ushort u, out ushort v)
        {
            u = uv1;
            v = uv2;
        }
    }
}
