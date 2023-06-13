using CarinaStudio;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
// ReSharper disable AccessToDisposedClosure

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Implementation of <see cref="CompressedFormatImageRenderer"/> based-on Skia.
/// </summary>
abstract class SkiaCompressedFormatImageRenderer : CompressedFormatImageRenderer
{
    // Fields.
    readonly SKEncodedImageFormat encodedFormat;


    /// <summary>
	/// Initialize new <see cref="SkiaCompressedFormatImageRenderer"/> instance.
	/// </summary>
	/// <param name="format">Format supported by this instance.</param>
    /// <param name="encodedFormat">Format defined by Skia.</param>      
	protected SkiaCompressedFormatImageRenderer(ImageFormat format, SKEncodedImageFormat encodedFormat) : base(format)
    { 
        this.encodedFormat = encodedFormat;
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

        // create codec
        // [Workaround] Read to memory first to prevent unrecoverable crash on some images
        // Please refer to https://github.com/mono/SkiaSharp/issues/1551
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        using var codec = SKData.Create(imageStream)?.Use(it => 
            SKCodec.Create(it)) ?? new SKManagedStream(imageStream, false).Use(stream =>
                SKCodec.Create(stream));
        if (codec == null)
            throw new ArgumentException("Unable to create codec.");
        if (codec.EncodedFormat != this.encodedFormat)
            throw new ArgumentException($"Incorrect format: {codec.EncodedFormat}, {this.encodedFormat} expected.");
        var imageInfo = codec.Info;
        if (imageInfo.Width != bitmapBuffer.Width || imageInfo.Height != bitmapBuffer.Height)
            throw new ArgumentException($"Incorrect bitmap size: {bitmapBuffer.Width}x{bitmapBuffer.Height}, {imageInfo.Width}x{imageInfo.Height} expected.");
        
        // decode
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        imageInfo.ColorType = bitmapBuffer.Format switch
        {
            BitmapFormat.Bgra32 => SKColorType.Bgra8888,
            BitmapFormat.Bgra64 => SKColorType.RgbaF16,
            _ => throw new NotSupportedException($"Unknown bitmap buffer format: {bitmapBuffer.Format}"),
        };
        using var bitmap = SKBitmap.Decode(codec, imageInfo);
        if (bitmap == null)
            throw new ArgumentException("Failed to decode.");
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();

        // copy pixel data
        bitmapBuffer.Memory.Pin(destBaseAddr =>
        {
            var srcBaseAddr = bitmap.GetAddress(0, 0);
            var srcRowStride = bitmap.RowBytes;
            var destRowStride = bitmapBuffer.RowBytes;
            switch (bitmapBuffer.Format)
            {
                case BitmapFormat.Bgra32:
                    if (destRowStride == srcRowStride)
                        Runtime.InteropServices.Marshal.Copy((void*)srcBaseAddr, (void*)destBaseAddr, srcRowStride * bitmap.Height);
                    else
                    {
                        var minRowStride = Math.Min(srcRowStride, destRowStride);
                        var srcRowPtr = (byte*)srcBaseAddr;
                        var destRowPtr = (byte*)destBaseAddr;
                        for (var y = bitmap.Height; y > 0; --y, srcRowPtr += srcRowStride, destRowPtr += destRowStride)
                            Runtime.InteropServices.Marshal.Copy(srcRowPtr, destRowPtr, minRowStride);
                    }
                    break;
                case BitmapFormat.Bgra64:
                    ImageProcessing.ParallelFor(0, bitmap.Height, (y) =>
                    {
                        var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
                        var packFunc = ImageProcessing.SelectBgra64Packing();
                        var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + srcRowStride * y);
                        var destPixelPtr = (ulong*)((byte*)destBaseAddr + destRowStride * y);
                        var r = (ushort)0;
                        var g = (ushort)0;
                        var b = (ushort)0;
                        var a = (ushort)0;
                        for (var x = bitmap.Width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
                        {
                            unpackFunc(*srcPixelPtr, &r, &g, &b, &a);
                            r = ImageProcessing.ClipToUInt16((double)BitConverter.UInt16BitsToHalf(r) * 65535);
                            g = ImageProcessing.ClipToUInt16((double)BitConverter.UInt16BitsToHalf(g) * 65535);
                            b = ImageProcessing.ClipToUInt16((double)BitConverter.UInt16BitsToHalf(b) * 65535);
                            a = ImageProcessing.ClipToUInt16((double)BitConverter.UInt16BitsToHalf(a) * 65535);
                            *destPixelPtr = packFunc(b, g, r, a);
                        }
                        if (cancellationToken.IsCancellationRequested)
                            throw new TaskCanceledException();
                    });
                    break;
            }
        });
        
        // complete
        return new ImageRenderingResult();
    }


    /// <inheritdoc/>
    public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, CancellationToken cancellationToken = default) =>
        Task.FromResult(BitmapFormat.Bgra64);
}


/// <summary>
/// <see cref="IImageRenderer"/> for JPEG format.
/// </summary>
class JpegImageRenderer : SkiaCompressedFormatImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="JpegImageRenderer"/> instance.
    /// </summary>
    public JpegImageRenderer() : base(new ImageFormat(ImageFormatCategory.Compressed, "JPEG", new ImagePlaneDescriptor(0), new[] { "JPEG" }), SKEncodedImageFormat.Jpeg)
    { }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(IImageDataSource source, Stream imageStream) =>
        FileFormatParsers.JpegFileFormatParser.CheckFileHeader(imageStream);
}


/// <summary>
/// <see cref="IImageRenderer"/> for PNG format.
/// </summary>
class PngImageRenderer : SkiaCompressedFormatImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="PngImageRenderer"/> instance.
    /// </summary>
    public PngImageRenderer() : base(new ImageFormat(ImageFormatCategory.Compressed, "PNG", new ImagePlaneDescriptor(0), new[] { "PNG" }), SKEncodedImageFormat.Png)
    { }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(IImageDataSource source, Stream imageStream) =>
        FileFormatParsers.PngFileFormatParser.CheckFileHeader(imageStream);
}