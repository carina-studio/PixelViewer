using CarinaStudio;
using CarinaStudio.IO;
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
    // Constants.
    const long MaxImageDataSize = 256L << 20; // 256 MB
    
    
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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
                                    var srcPixelPtr = srcRowPtr;
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


    // Create CGImageSource from stream.
    CGImageSource CreateImageSource(IImageDataSource source, Stream stream, CancellationToken cancellationToken)
    {
        // check file header first to prevent decoding image
        var position = stream.Position;
        if (!this.OnCheckFileHeader(source, stream))
            throw new ArgumentException("Unsupported format.");
        stream.Position = position;

        // check data size
        var dataSize = Math.Max(0, stream.Length - position);
        if (dataSize > MaxImageDataSize)
            throw new NotSupportedException($"Data is too large to load into memory: {dataSize}");

        // create image source
        var imageSource = CGImageSource.FromStream(stream);
        if (cancellationToken.IsCancellationRequested)
        {
            Global.RunWithoutError(imageSource.Release);
            throw new TaskCanceledException();
        }
        return imageSource;
    }


    /// <summary>
    /// Called to check file header.
    /// </summary>
    /// <param name="source">Source of image.</param>
    /// <param name="imageStream">Stream to read image data.</param>
    /// <returns>True if header of file is correct.</returns>
    protected abstract bool OnCheckFileHeader(IImageDataSource source, Stream imageStream);


    /// <inheritdoc/>
    protected override ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
    {
        // create image source
        using var imageSource = this.CreateImageSource(source, imageStream, cancellationToken);
        
        // check image dimensions
        using var imageProperties = imageSource.CopyPropertiesAtIndex(imageSource.PrimaryImageIndex);
        if (imageProperties is null 
            || !imageProperties.TryGetValue(CGImageProperties.PixelWidth, out var widthNumber)
            || widthNumber?.TypeDescription != nameof(CFNumber)
            || !imageProperties.TryGetValue(CGImageProperties.PixelHeight, out var heightNumber)
            || heightNumber?.TypeDescription != nameof(CFNumber))
        {
            throw new Exception($"Unable to get dimensions of image.");
        }
        var width = CFObject.FromHandle<CFNumber>(widthNumber.Handle).ToInt32();
        var height = CFObject.FromHandle<CFNumber>(heightNumber.Handle).ToInt32();
        if (width != bitmapBuffer.Width || height != bitmapBuffer.Height)
            throw new ArgumentException($"Incorrect bitmap size: {bitmapBuffer.Width}x{bitmapBuffer.Height}, {width}x{height} expected.");
        
        // check image format
        if (!imageProperties.TryGetValue(CGImageProperties.ColorModel, out var colorModelString)
            || colorModelString?.TypeDescription != nameof(CFString)
            || CFObject.FromHandle<CFString>(colorModelString.Handle).ToString() != CGImageProperties.ColorModelRGB.ToString())
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
    public override async Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default)
    {
        var stream = await source.OpenStreamAsync(StreamAccess.Read, cancellationToken);
        try
        {
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            return await Task.Run(() =>
            {
                // create image source
                stream.Position = renderingOptions.DataOffset;
                using var imageSource = this.CreateImageSource(source, stream, cancellationToken);
                
                // check color model
                using var imageProperties = imageSource.CopyPropertiesAtIndex(imageSource.PrimaryImageIndex);
                if (imageProperties is null)
                    return BitmapFormat.Bgra32;
                if (!imageProperties.TryGetValue(CGImageProperties.ColorModel, out var colorModelString)
                    || colorModelString?.TypeDescription != nameof(CFString)
                    || CFObject.FromHandle<CFString>(colorModelString.Handle).ToString() != CGImageProperties.ColorModelRGB.ToString())
                {
                    throw new Exception($"Only RGB color model is supported.");
                }

                // check color depth
                using var depthKey = new CFString("Depth");
                if (imageProperties.TryGetValue(depthKey, out var depthNumber)
                    && depthNumber?.TypeDescription == nameof(CFNumber))
                {
                    return CFObject.FromHandle<CFNumber>(depthNumber.Handle).ToInt32() switch
                    {  
                        >= 16 => BitmapFormat.Bgra64,
                        _ => BitmapFormat.Bgra32,
                    };
                }
                return BitmapFormat.Bgra32;
            }, cancellationToken);
        }
        finally
        {
            Global.RunWithoutErrorAsync(stream.Close);
        }
    }
}


/// <summary>
/// <see cref="IImageRenderer"/> for HEIF format.
/// </summary>
class MacOSHeifImageRenderer : MacOSNativeCompressedImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="MacOSHeifImageRenderer"/> instance.
    /// </summary>
    public MacOSHeifImageRenderer() : base(new ImageFormat(ImageFormatCategory.Compressed, "HEIF", new ImagePlaneDescriptor(0), new[] { "HEIF" }))
    { }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(IImageDataSource source, Stream imageStream) =>
        FileFormatParsers.HeifFileFormatParser.CheckFileHeader(imageStream);
}