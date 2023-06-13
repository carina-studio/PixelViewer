using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ARGB_F16 based format.
	/// </summary>
    abstract class BaseArgbF16ImageRenderer : SinglePlaneImageRenderer
    {
        /// <summary>
		/// Initialize new <see cref="BaseArgbF16ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Supported format.</param>
		protected BaseArgbF16ImageRenderer(ImageFormat format) : base(format)
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
			var extractComponentFunc = renderingOptions.ByteOrdering == ByteOrdering.LittleEndian
				? new Func<byte, byte, Half>((b1, b2) => BitConverter.UInt16BitsToHalf((ushort)((b2 << 8) | b1)))
				: (b1, b2) => BitConverter.UInt16BitsToHalf((ushort)((b1 << 8) | b2));
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
							var component1 = extractComponentFunc(srcPixelPtr[0], srcPixelPtr[1]);
							var component2 = extractComponentFunc(srcPixelPtr[2], srcPixelPtr[3]);
							var component3 = extractComponentFunc(srcPixelPtr[4], srcPixelPtr[5]);
							var component4 = extractComponentFunc(srcPixelPtr[6], srcPixelPtr[7]);
							this.SelectArgb(component1, component2, component3, component4, out var a, out var r, out var g, out var b);
							*bitmapPixelPtr = packFunc(
                                ImageProcessing.ClipToUInt16((double)b * 65535), 
                                ImageProcessing.ClipToUInt16((double)g * 65535), 
                                ImageProcessing.ClipToUInt16((double)r * 65535), 
                                ImageProcessing.ClipToUInt16((double)a * 65535));
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
        public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, CancellationToken cancellationToken = default) =>
	        Task.FromResult(BitmapFormat.Bgra64);


        /// <summary>
		/// Select A, R, G, B components.
		/// </summary>
		/// <param name="component1">1st component read from source.</param>
		/// <param name="component2">2nd component read from source.</param>
		/// <param name="component3">3rd component read from source.</param>
		/// <param name="component4">4th component read from source.</param>
		/// <param name="a">Selected A.</param>
		/// <param name="r">Selected R.</param>
		/// <param name="g">Selected G.</param>
		/// <param name="b">Selected B.</param>
		protected abstract void SelectArgb(Half component1, Half component2, Half component3, Half component4, out Half a, out Half r, out Half g, out Half b);
    }


    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ABGR_F16 format.
	/// </summary>
    class AbgrF16ImageRenderer : BaseArgbF16ImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="AbgrF16ImageRenderer"/> instance.
        /// </summary>
        public AbgrF16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "ABGR_F16", true, new ImagePlaneDescriptor(8), new[]{ "ABGRF16", "ABGR_F16" }))
        { }


        /// <inheritdoc/>
        protected override void SelectArgb(Half component1, Half component2, Half component3, Half component4, out Half a, out Half r, out Half g, out Half b)
        {
            a = component1;
            b = component2;
            g = component3;
            r = component4;
        }
    }


    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ARGB_F16 format.
	/// </summary>
    class ArgbF16ImageRenderer : BaseArgbF16ImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="ArgbF16ImageRenderer"/> instance.
        /// </summary>
        public ArgbF16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "ARGB_F16", true, new ImagePlaneDescriptor(8), new[]{ "ARGBF16", "ARGB_F16" }))
        { }


        /// <inheritdoc/>
        protected override void SelectArgb(Half component1, Half component2, Half component3, Half component4, out Half a, out Half r, out Half g, out Half b)
        {
            a = component1;
            r = component2;
            g = component3;
            b = component4;
        }
    }


    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with BGRA_F16 format.
	/// </summary>
    class BgraF16ImageRenderer : BaseArgbF16ImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="BgraF16ImageRenderer"/> instance.
        /// </summary>
        public BgraF16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "BGRA_F16", true, new ImagePlaneDescriptor(8), new[]{ "BGRAF16", "BGRA_F16" }))
        { }


        /// <inheritdoc/>
        protected override void SelectArgb(Half component1, Half component2, Half component3, Half component4, out Half a, out Half r, out Half g, out Half b)
        {
            b = component1;
            g = component2;
            r = component3;
            a = component4;
        }
    }


    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with RGBA_F16 format.
	/// </summary>
    class RgbaF16ImageRenderer : BaseArgbF16ImageRenderer
    {
        /// <summary>
        /// Initialize new <see cref="RgbaF16ImageRenderer"/> instance.
        /// </summary>
        public RgbaF16ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "RGBA_F16", true, new ImagePlaneDescriptor(8), new[]{ "RGBAF16", "RGBA_F16" }))
        { }


        /// <inheritdoc/>
        protected override void SelectArgb(Half component1, Half component2, Half component3, Half component4, out Half a, out Half r, out Half g, out Half b)
        {
            r = component1;
            g = component2;
            b = component3;
            a = component4;
        }
    }
}