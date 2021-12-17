using CarinaStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ARGB_16161616 based format.
	/// </summary>
    abstract class BaseArgb16161616ImageRenderer : SinglePlaneImageRenderer
    {
		/// <summary>
		/// Initialize new <see cref="BaseArgb16161616ImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Supported format.</param>
		protected BaseArgb16161616ImageRenderer(ImageFormat format) : base(format)
		{ }


        /// <inheritdoc/>
        protected override unsafe void OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			var pixelStride = planeOptions[0].PixelStride;
			var rowStride = planeOptions[0].RowStride;
			if (pixelStride <= 0 || rowStride <= 0)
				throw new ArgumentException($"Invalid pixel/row stride: {pixelStride}/{rowStride}.");

			// prepare packing function
			var unpackFunc = renderingOptions.ByteOrdering == ByteOrdering.LittleEndian
				? (delegate*<ulong, ushort*, ushort*, ushort*, ushort*, void>)&ImageProcessing.UnpackBgra64LE
				: &ImageProcessing.UnpackBgra64BE;
			var packFunc = ImageProcessing.SelectBgra64Packing();

			// render
			var srcRow = new byte[rowStride];
			fixed (byte* srcRowAddress = srcRow)
			{
				var srcRowPtr = srcRowAddress;
				bitmapBuffer.Memory.Pin((bitmapBaseAddress) =>
				{
					var component1 = (ushort)0;
					var component2 = (ushort)0;
					var component3 = (ushort)0;
					var component4 = (ushort)0;
					var bitmapRowPtr = (byte*)bitmapBaseAddress;
					var bitmapRowStride = bitmapBuffer.RowBytes;
					for (var y = height; ; --y, bitmapRowPtr += bitmapRowStride)
					{
						var isLastRow = (imageStream.Read(srcRow) < rowStride || y == 1);
						var srcPixelPtr = srcRowPtr;
						var bitmapPixelPtr = (ulong*)bitmapRowPtr;
						for (var x = width; x > 0; --x, srcPixelPtr += pixelStride, ++bitmapPixelPtr)
						{
							unpackFunc(*(ulong*)srcPixelPtr, &component1, &component2, &component3, &component4);
							this.SelectArgb(component1, component2, component3, component4, out var a, out var r, out var g, out var b);
							*bitmapPixelPtr = packFunc(b, g, r, a);
						}
						if (isLastRow || cancellationToken.IsCancellationRequested)
							break;
						Array.Clear(srcRow, 0, rowStride);
					}
				});
			}
		}


		/// <inheritdoc/>
		public override BitmapFormat RenderedFormat => BitmapFormat.Bgra64;


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
        protected abstract void SelectArgb(ushort component1, ushort component2, ushort component3, ushort component4, out ushort a, out ushort r, out ushort g, out ushort b);
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ABGR_16161616 format.
	/// </summary>
	class Abgr16161616ImageRenderer : BaseArgb16161616ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Abgr16161616ImageRenderer"/> instance.
		/// </summary>
		public Abgr16161616ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "ABGR_16161616", true, new ImagePlaneDescriptor(8)))
		{ }


		/// <inheritdoc/>
		protected override void SelectArgb(ushort component1, ushort component2, ushort component3, ushort component4, out ushort a, out ushort r, out ushort g, out ushort b)
		{
			a = component1;
			b = component2;
			g = component3;
			r = component4;
		}
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with ARGB_16161616 format.
	/// </summary>
	class Argb16161616ImageRenderer : BaseArgb16161616ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Argb16161616ImageRenderer"/> instance.
		/// </summary>
		public Argb16161616ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "ARGB_16161616", true, new ImagePlaneDescriptor(8)))
		{ }


		/// <inheritdoc/>
		protected override void SelectArgb(ushort component1, ushort component2, ushort component3, ushort component4, out ushort a, out ushort r, out ushort g, out ushort b)
		{
			a = component1;
			r = component2;
			g = component3;
			b = component4;
		}
	}


	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with BGRA_16161616 format.
	/// </summary>
	class Bgra16161616ImageRenderer : BaseArgb16161616ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Bgra16161616ImageRenderer"/> instance.
		/// </summary>
		public Bgra16161616ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "BGRA_16161616", true, new ImagePlaneDescriptor(8)))
		{ }


		/// <inheritdoc/>
        protected override void SelectArgb(ushort component1, ushort component2, ushort component3, ushort component4, out ushort a, out ushort r, out ushort g, out ushort b)
        {
			b = component1;
			g = component2;
			r = component3;
			a = component4;
        }
    }


	/// <summary>
	/// <see cref="IImageRenderer"/> which supports rendering image with RGBA_16161616 format.
	/// </summary>
	class Rgba16161616ImageRenderer : BaseArgb16161616ImageRenderer
	{
		/// <summary>
		/// Initialize new <see cref="Rgba16161616ImageRenderer"/> instance.
		/// </summary>
		public Rgba16161616ImageRenderer() : base(new ImageFormat(ImageFormatCategory.ARGB, "RGBA_16161616", true, new ImagePlaneDescriptor(8)))
		{ }


		/// <inheritdoc/>
		protected override void SelectArgb(ushort component1, ushort component2, ushort component3, ushort component4, out ushort a, out ushort r, out ushort g, out ushort b)
		{
			r = component1;
			g = component2;
			b = component3;
			a = component4;
		}
	}
}
