using System;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// <see cref="IImageRenderer"/> which supports rendering image with 10-bit YUV422p based format.
    /// </summary>
    class P210ImageRenderer : BaseYuv422p16ImageRenderer
    {
        public P210ImageRenderer() : base(new ImageFormat(ImageFormatCategory.YUV, "P210", true, new ImagePlaneDescriptor[] {
            new ImagePlaneDescriptor(2),
            new ImagePlaneDescriptor(2),
            new ImagePlaneDescriptor(2),
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
