using CarinaStudio;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ARGB_2101010 based format.
	/// </summary>
    abstract class BaseArgb2101010ImageRenderer : SinglePlaneImageRenderer
    {
        /// <summary>
		/// Initialize new <see cref="BaseArgb2101010ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Supported format.</param>
		protected BaseArgb2101010ImageRenderer(ImageFormat format) : base(format)
		{ }


        /// <inheritdoc/>
        protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			var pixelStride = planeOptions[0].PixelStride;
			var rowStride = planeOptions[0].RowStride;
			if (pixelStride <= 0 || rowStride <= 0)
				throw new ArgumentException($"Invalid pixel/row stride: {pixelStride}/{rowStride}.");

			// prepare packing function
			var readPixelFunc = renderingOptions.ByteOrdering == ByteOrdering.LittleEndian
                ? new Func<byte, byte, byte, byte, uint>((c0, c1, c2, c3) => (uint)((c3 << 24) | (c2 << 16) | (c1 << 8) | c0))
                : new Func<byte, byte, byte, byte, uint>((c0, c1, c2, c3) => (uint)((c0 << 24) | (c1 << 16) | (c2 << 8) | c3));
			var packFunc = ImageProcessing.SelectBgra64Packing();

			// render
			var srcRow = new byte[rowStride];
			fixed (byte* srcRowAddress = srcRow)
			{
				var srcRowPtr = srcRowAddress;
				bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
				{
					var bitmapRowPtr = (byte*)bitmapBaseAddress;
					var bitmapRowStride = bitmapBuffer.RowBytes;
					for (var y = height; ; --y, bitmapRowPtr += bitmapRowStride)
					{
						var isLastRow = (imageStream.Read(srcRow) < rowStride || y == 1);
						var srcPixelPtr = srcRowPtr;
						var bitmapPixelPtr = (ulong*)bitmapRowPtr;
						for (var x = width; x > 0; --x, srcPixelPtr += pixelStride, ++bitmapPixelPtr)
						{
							var pixel = readPixelFunc(srcPixelPtr[0], srcPixelPtr[1], srcPixelPtr[2], srcPixelPtr[3]);
							this.UnpackArgb(pixel, out var a, out var r, out var g, out var b);
                            a = a switch
                            {
                                3 => 65535,
                                2 => 49151,
                                1 => 32767,
                                _ => 0,
                            };
							*bitmapPixelPtr = packFunc(
                                (ushort)((b << 6) & 0xffff), 
                                (ushort)((g << 6) & 0xffff), 
                                (ushort)((r << 6) & 0xffff), 
                                a);
						}
						if (isLastRow || cancellationToken.IsCancellationRequested)
							break;
						Array.Clear(srcRow, 0, rowStride);
					}
				});
			}

			// complete
			return new ImageRenderingResult();
		}


        /// <inheritdoc/>
        public override BitmapFormat RenderedFormat => BitmapFormat.Bgra64;


        /// <summary>
		/// Unpack A, R, G, B components.
		/// </summary>
		/// <param name="pixel">Packed ARGB.</param>
		/// <param name="a">Unpacked A.</param>
		/// <param name="r">Unpacked R.</param>
		/// <param name="g">Unpacked G.</param>
		/// <param name="b">Unpacked B.</param>
		protected abstract void UnpackArgb(uint pixel, out ushort a, out ushort r, out ushort g, out ushort b);
    }


    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ABGR_2101010 based format.
	/// </summary>
    class Abgr2101010ImageRenderer : BaseArgb2101010ImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="Abgr2101010ImageRenderer"/> instance.
        /// </summary>
        public Abgr2101010ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "ABGR_2101010", true, new ImagePlaneDescriptor(4), new string[]{ "ABGR2101010" }))
        { }


        /// <inheritdoc/>
        protected override void UnpackArgb(uint pixel, out ushort a, out ushort r, out ushort g, out ushort b)
        {
            a = (ushort)((pixel >> 30) & 0x3);
            b = (ushort)((pixel >> 20) & 0x3ff);
            g = (ushort)((pixel >> 10) & 0x3ff);
            r = (ushort)(pixel & 0x3ff);
        }
    }


    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ARGB_2101010 based format.
	/// </summary>
    class Argb2101010ImageRenderer : BaseArgb2101010ImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="Argb2101010ImageRenderer"/> instance.
        /// </summary>
        public Argb2101010ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "ARGB_2101010", true, new ImagePlaneDescriptor(4), new string[]{ "ARGB2101010" }))
        { }


        /// <inheritdoc/>
        protected override void UnpackArgb(uint pixel, out ushort a, out ushort r, out ushort g, out ushort b)
        {
            a = (ushort)((pixel >> 30) & 0x3);
            r = (ushort)((pixel >> 20) & 0x3ff);
            g = (ushort)((pixel >> 10) & 0x3ff);
            b = (ushort)(pixel & 0x3ff);
        }
    }


    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with BGRA_1010102 based format.
	/// </summary>
    class Bgra1010102ImageRenderer : BaseArgb2101010ImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="Bgra1010102ImageRenderer"/> instance.
        /// </summary>
        public Bgra1010102ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "BGRA_1010102", true, new ImagePlaneDescriptor(4), new string[]{ "BGRA1010102" }))
        { }


        /// <inheritdoc/>
        protected override void UnpackArgb(uint pixel, out ushort a, out ushort r, out ushort g, out ushort b)
        {
            b = (ushort)(pixel >> 22);
            g = (ushort)((pixel >> 12) & 0x3ff);
            r = (ushort)((pixel >> 2) & 0x3ff);
            a = (ushort)(pixel & 0x3);
        }
    }


    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with RGBA_1010102 based format.
	/// </summary>
    class Rgba1010102ImageRenderer : BaseArgb2101010ImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="Rgba1010102ImageRenderer"/> instance.
        /// </summary>
        public Rgba1010102ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "RGBA_1010102", true, new ImagePlaneDescriptor(4), new string[]{ "RGBA1010102" }))
        { }


        /// <inheritdoc/>
        protected override void UnpackArgb(uint pixel, out ushort a, out ushort r, out ushort g, out ushort b)
        {
            r = (ushort)(pixel >> 22);
            g = (ushort)((pixel >> 12) & 0x3ff);
            b = (ushort)((pixel >> 2) & 0x3ff);
            a = (ushort)(pixel & 0x3);
        }
    }
}