using Carina.PixelViewer.Native;
using CarinaStudio;
using CarinaStudio.MacOS.CoreFoundation;
using CarinaStudio.MacOS.CoreGraphics;
using CarinaStudio.MacOS.ImageIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Implementation of <see cref="CompressedFormatImageRenderer"/> based-on macOS native API.
/// </summary>
abstract class MacOSNativeCompressedImageRenderer : CompressedFormatImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="MacOSNativeCompressedImageRenderer"/> instance.
    /// </summary>
    /// <param name="format">Format supported by this instance.</param>    
    protected MacOSNativeCompressedImageRenderer(ImageFormat format) : base(format)
    { }


    // Copy data from CGImage.
    static unsafe void CopyPixelsFromCGImageToBgra32BitmapBuffer(CGImage image, IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken)
    {
        using var imagePixels = image.DataProvider.ToData();
        var srcPixelStride = image.BitsPerPixel >> 3;
        var srcRowStride = image.BytesPerRow;
        var srcAlphaInfo = image.AlphaInfo;
        var srcPixelFormat = image.PixelFormat;
        var destRowStride = bitmapBuffer.RowBytes;
        imagePixels.AsSpan().Pin(srcBaseAddr =>
        {
            bitmapBuffer.Memory.Pin(destBaseAddr =>
            {
                var packFunc = ImageProcessing.SelectBgra32Packing();
                var srcRowPtr = (byte*)srcBaseAddr;
                var destRowPtr = (byte*)destBaseAddr;
                switch (srcPixelFormat)
                {
                    case CGImagePixelFormatInfo.Packed:
                        switch (srcAlphaInfo)
                        {
                            case CGImageAlphaInfo.AlphaFirst: // ARGB
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (uint*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                        *destPixelPtr = packFunc(srcPixelPtr[3], srcPixelPtr[2], srcPixelPtr[1], srcPixelPtr[0]);
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaLast: // RGBA
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (uint*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                        *destPixelPtr = packFunc(srcPixelPtr[2], srcPixelPtr[1], srcPixelPtr[0], srcPixelPtr[3]);
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaNoneSkipFirst: // XRGB
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (uint*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                        *destPixelPtr = packFunc(srcPixelPtr[3], srcPixelPtr[2], srcPixelPtr[1], 255);
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaNone: // RGB
                            case CGImageAlphaInfo.AlphaNoneSkipLast: // RGBX
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (uint*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                        *destPixelPtr = packFunc(srcPixelPtr[2], srcPixelPtr[1], srcPixelPtr[0], 255);
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            default:
                                throw new NotSupportedException($"Unsupported source alpha type: {srcAlphaInfo}.");
                        }
                        break;
                    case CGImagePixelFormatInfo.RGB101010:
                        switch (srcAlphaInfo)
                        {
                            case CGImageAlphaInfo.AlphaFirst: // ARGB
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (uint*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                    {
                                        var pixel = *(uint*)srcPixelPtr;
                                        var r = (byte)((pixel >> 22) & 0xff);
                                        var g = (byte)((pixel >> 12) & 0xff);
                                        var b = (byte)((pixel >> 2) & 0xff);
                                        var a = (byte)((pixel >> 30) switch
                                        {
                                            3 => 255,
                                            2 => 170,
                                            1 => 85,
                                            _ => 0,
                                        });
                                        *destPixelPtr = packFunc(b, g, r, a);
                                    }
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaLast: // RGBA
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (uint*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                    {
                                        var pixel = *(uint*)srcPixelPtr;
                                        var r = (byte)(pixel >> 24);
                                        var g = (byte)((pixel >> 14) & 0xff);
                                        var b = (byte)((pixel >> 4) & 0xff);
                                        var a = (byte)((pixel & 0x3) switch
                                        {
                                            3 => 255,
                                            2 => 170,
                                            1 => 85,
                                            _ => 0,
                                        });
                                        *destPixelPtr = packFunc(b, g, r, a);
                                    }
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaNoneSkipFirst: // XRGB
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (uint*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                    {
                                        var pixel = *(uint*)srcPixelPtr;
                                        var r = (byte)((pixel >> 22) & 0xff);
                                        var g = (byte)((pixel >> 12) & 0xff);
                                        var b = (byte)((pixel >> 2) & 0xff);
                                        *destPixelPtr = packFunc(b, g, r, 255);
                                    }
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaNone: // RGB
                            case CGImageAlphaInfo.AlphaNoneSkipLast: // RGBX
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (uint*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                    {
                                        var pixel = *(uint*)srcPixelPtr;
                                        var r = (byte)(pixel >> 24);
                                        var g = (byte)((pixel >> 14) & 0xff);
                                        var b = (byte)((pixel >> 4) & 0xff);
                                        *destPixelPtr = packFunc(b, g, r, 255);
                                    }
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            default:
                                throw new NotSupportedException($"Unsupported source alpha type: {srcAlphaInfo}.");
                        }
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported source pixel format: {srcPixelFormat}.");
                }
            });
        });
    }


    // Copy data from CGImage.
    static unsafe void CopyPixelsFromCGImageToBgra64BitmapBuffer(CGImage image, IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken)
    {
        using var imagePixels = image.DataProvider.ToData();
        var srcPixelStride = image.BitsPerPixel >> 3;
        var srcRowStride = image.BytesPerRow;
        var srcAlphaInfo = image.AlphaInfo;
        var srcPixelFormat = image.PixelFormat;
        var destRowStride = bitmapBuffer.RowBytes;
        imagePixels.AsSpan().Pin(srcBaseAddr =>
        {
            bitmapBuffer.Memory.Pin(destBaseAddr =>
            {
                var packFunc = ImageProcessing.SelectBgra64Packing();
                var srcRowPtr = (byte*)srcBaseAddr;
                var destRowPtr = (byte*)destBaseAddr;
                switch (srcPixelFormat)
                {
                    case CGImagePixelFormatInfo.Packed:
                        switch (srcAlphaInfo)
                        {
                            case CGImageAlphaInfo.AlphaFirst: // ARGB
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (ulong*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                        *destPixelPtr = packFunc((ushort)(srcPixelPtr[3] << 8 | srcPixelPtr[3]), (ushort)(srcPixelPtr[2] << 8 | srcPixelPtr[2]), (ushort)(srcPixelPtr[1] << 8 | srcPixelPtr[1]), (ushort)(srcPixelPtr[0] << 8 | srcPixelPtr[0]));
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaLast: // RGBA
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (ulong*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                        *destPixelPtr = packFunc((ushort)(srcPixelPtr[2] << 8 | srcPixelPtr[2]), (ushort)(srcPixelPtr[1] << 8 | srcPixelPtr[1]), (ushort)(srcPixelPtr[0] << 8 | srcPixelPtr[0]), (ushort)(srcPixelPtr[3] << 8 | srcPixelPtr[3]));
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaNoneSkipFirst: // XRGB
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (ulong*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                        *destPixelPtr = packFunc((ushort)(srcPixelPtr[3] << 8 | srcPixelPtr[3]), (ushort)(srcPixelPtr[2] << 8 | srcPixelPtr[2]), (ushort)(srcPixelPtr[1] << 8 | srcPixelPtr[1]), 65535);
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            case CGImageAlphaInfo.AlphaNone: // RGB
                            case CGImageAlphaInfo.AlphaNoneSkipLast: // RGBX
                                for (var y = bitmapBuffer.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                                {
                                    var srcPixelPtr = (byte*)srcRowPtr;
                                    var destPixelPtr = (ulong*)destRowPtr;
                                    for (var x = bitmapBuffer.Width; x > 0; --x, srcPixelPtr += srcPixelStride, ++destPixelPtr)
                                        *destPixelPtr = packFunc((ushort)(srcPixelPtr[2] << 8 | srcPixelPtr[2]), (ushort)(srcPixelPtr[1] << 8 | srcPixelPtr[1]), (ushort)(srcPixelPtr[0] << 8 | srcPixelPtr[0]), 65535);
                                    if (cancellationToken.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                }
                                break;
                            default:
                                throw new NotSupportedException($"Unsupported source alpha type: {srcAlphaInfo}.");
                        }
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported source pixel format: {srcPixelFormat}.");
                }
            });
        });
    }


    /// <summary>
    /// Called to check file header.
    /// </summary>
    /// <param name="source">Source of image.</param>
    /// <param name="imageStream">Stream to read image data.</param>
    /// <returns>True if header of file is correct.</returns>
    protected abstract bool OnCheckFileHeader(IImageDataSource source, Stream imageStream);


    /// <inheritdoc/>
    protected override unsafe ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
    {
        // check file header first to prevent decoding image
        var position = imageStream.Position;
        if (!this.OnCheckFileHeader(source, imageStream))
            throw new ArgumentException("Unsupported format.");
        imageStream.Position = position;

        // check data size
        var dataSize = Math.Max(0, imageStream.Length - position);
        if (dataSize > 256L << 20) // 256 MB
            throw new NotSupportedException($"Data is too large to load into memory: {dataSize}");

        // create image source
        using var imageSource = CGImageSource.FromStream(imageStream);
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        
        // check image dimensions
        using var imageProperties = CFObject.FromHandle(MacOS.CGImageSourceCopyPropertiesAtIndex(imageSource.Handle, (nuint)imageSource.PrimaryImageIndex, IntPtr.Zero), true);
        if (!MacOS.CFDictionaryGetValue(imageProperties.Handle, MacOS.kCGImagePropertyPixelWidth, out int width)
            || !MacOS.CFDictionaryGetValue(imageProperties.Handle, MacOS.kCGImagePropertyPixelHeight, out int height))
        {
            throw new Exception($"Unable to get dimensions of image.");
        }
        if (width != bitmapBuffer.Width || height != bitmapBuffer.Height)
            throw new ArgumentException($"Incorrect bitmap size: {bitmapBuffer.Width}x{bitmapBuffer.Height}, {width}x{height} expected.");
        
        // check image format
        if (!MacOS.CFDictionaryGetValue(imageProperties.Handle, MacOS.kCGImagePropertyColorModel, out string? colorModel)
            || colorModel != "RGB")
        {
            throw new Exception($"Only RGB color model is supported.");
        }

        // load image
        using var image = imageSource.CreateImage();
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        
        // copy pixels
        switch (bitmapBuffer.Format)
        {
            case BitmapFormat.Bgra32:
                CopyPixelsFromCGImageToBgra32BitmapBuffer(image, bitmapBuffer, cancellationToken);
                break;
            case BitmapFormat.Bgra64:
                CopyPixelsFromCGImageToBgra64BitmapBuffer(image, bitmapBuffer, cancellationToken);
                break;
        }

        // complete
        return new ImageRenderingResult();
    }


    /// <inheritdoc/>
    public override BitmapFormat RenderedFormat => BitmapFormat.Bgra64;
}


/// <summary>
/// <see cref="IImageRenderer"/> for HEIF format.
/// </summary>
class MacOSHeifImageRenderer : MacOSNativeCompressedImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="MacOSHeifImageRenderer"/> instance.
    /// </summary>
    public MacOSHeifImageRenderer() : base(new ImageFormat(ImageFormatCategory.Compressed, "HEIF", new ImagePlaneDescriptor(0), new string[] { "HEIF" }))
    { }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(IImageDataSource source, Stream imageStream) =>
        Media.FileFormatParsers.HeifFileFormatParser.CheckFileHeader(imageStream);
}