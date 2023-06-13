using CarinaStudio;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
// ReSharper disable AccessToDisposedClosure

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Implementation of <see cref="CompressedFormatImageRenderer"/> based-on ImageMagick.
/// </summary>
abstract class MagickCompressedImageRenderer : CompressedFormatImageRenderer
{
    // Fields.
    readonly List<MagickFormat> magickFormats = new();


    /// <summary>
    /// Initialize new <see cref="MagickCompressedImageRenderer"/> instance.
    /// </summary>
    /// <param name="format">Format supported by this instance.</param>
    /// <param name="magickFormats">Formats defined by >.</param>      
    protected MagickCompressedImageRenderer(ImageFormat format, IEnumerable<MagickFormat> magickFormats) : base(format)
    {
        this.magickFormats.AddRange(magickFormats);
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

        // decode image info
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        var imageInfo = new MagickImageInfoFactory().Create(imageStream);
        if (imageInfo == null)
            throw new ArgumentException("Unable to decode image info.");
        if (!this.magickFormats.Contains(imageInfo.Format))
            throw new ArgumentException($"Incorrect format: {imageInfo.Format}.");
        if (imageInfo.Width != bitmapBuffer.Width || imageInfo.Height != bitmapBuffer.Height)
            throw new ArgumentException($"Incorrect bitmap size: {bitmapBuffer.Width}x{bitmapBuffer.Height}, {imageInfo.Width}x{imageInfo.Height} expected.");

        // decode
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        imageStream.Position = position;
        using var image = new MagickImageFactory().Create(imageStream);
        if (image == null)
            throw new ArgumentException("Failed to decode.");
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();

        // copy pixel data
        bitmapBuffer.Memory.Pin(destBaseAddr =>
        {
            using var srcUnsafePixels = image.GetPixelsUnsafe();
            var srcBaseAddr = srcUnsafePixels.GetAreaPointer(0, 0, image.Width, image.Height);
            var srcRowStride = imageInfo.Width * image.ChannelCount * sizeof(ushort);
            var destRowStride = bitmapBuffer.RowBytes;
            switch (bitmapBuffer.Format)
            {
                case BitmapFormat.Bgra32:
                    switch (image.ChannelCount)
                    {
                        case 1:
                            ImageProcessing.ParallelFor(0, image.Height, (y) =>
                            {
                                var packFunc = ImageProcessing.SelectBgra32Packing();
                                var srcPixelPtr = (ushort*)((byte*)srcBaseAddr + srcRowStride * y);
                                var destPixelPtr = (uint*)((byte*)destBaseAddr + destRowStride * y);
                                for (var x = imageInfo.Width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
                                {
                                    var l = (byte)(*srcPixelPtr >> 8);
                                    *destPixelPtr = packFunc(l, l, l, 255);
                                }
                                if (cancellationToken.IsCancellationRequested)
                                    throw new TaskCanceledException();
                            });
                            break;
                        case 3:
                            ImageProcessing.ParallelFor(0, image.Height, (y) =>
                            {
                                var packFunc = ImageProcessing.SelectBgra32Packing();
                                var srcPixelPtr = (ushort*)((byte*)srcBaseAddr + srcRowStride * y);
                                var destPixelPtr = (uint*)((byte*)destBaseAddr + destRowStride * y);
                                for (var x = imageInfo.Width; x > 0; --x, srcPixelPtr += 3, ++destPixelPtr)
                                    *destPixelPtr = packFunc((byte)(srcPixelPtr[2] >> 8), (byte)(srcPixelPtr[1] >> 8), (byte)(srcPixelPtr[0] >> 8), 255);
                                if (cancellationToken.IsCancellationRequested)
                                    throw new TaskCanceledException();
                            });
                            break;
                        case 4:
                            ImageProcessing.ParallelFor(0, image.Height, (y) =>
                            {
                                var packFunc = ImageProcessing.SelectBgra32Packing();
                                var srcPixelPtr = (ushort*)((byte*)srcBaseAddr + srcRowStride * y);
                                var destPixelPtr = (uint*)((byte*)destBaseAddr + destRowStride * y);
                                for (var x = imageInfo.Width; x > 0; --x, srcPixelPtr += 4, ++destPixelPtr)
                                    *destPixelPtr = packFunc((byte)(srcPixelPtr[2] >> 8), (byte)(srcPixelPtr[1] >> 8), (byte)(srcPixelPtr[0] >> 8), (byte)(srcPixelPtr[3] >> 8));
                                if (cancellationToken.IsCancellationRequested)
                                    throw new TaskCanceledException();
                            });
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported channel count: {image.ChannelCount}.");
                    }
                    break;
                case BitmapFormat.Bgra64:
                    switch (image.ChannelCount)
                    {
                        case 1:
                            ImageProcessing.ParallelFor(0, image.Height, (y) =>
                            {
                                var packFunc = ImageProcessing.SelectBgra64Packing();
                                var srcPixelPtr = (ushort*)((byte*)srcBaseAddr + srcRowStride * y);
                                var destPixelPtr = (ulong*)((byte*)destBaseAddr + destRowStride * y);
                                for (var x = imageInfo.Width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
                                {
                                    var l = *srcPixelPtr;
                                    *destPixelPtr = packFunc(l, l, l, 65535);
                                }
                                if (cancellationToken.IsCancellationRequested)
                                    throw new TaskCanceledException();
                            });
                            break;
                        case 3:
                            ImageProcessing.ParallelFor(0, image.Height, (y) =>
                            {
                                var packFunc = ImageProcessing.SelectBgra64Packing();
                                var srcPixelPtr = (ushort*)((byte*)srcBaseAddr + srcRowStride * y);
                                var destPixelPtr = (ulong*)((byte*)destBaseAddr + destRowStride * y);
                                for (var x = imageInfo.Width; x > 0; --x, srcPixelPtr += 3, ++destPixelPtr)
                                    *destPixelPtr = packFunc(srcPixelPtr[2], srcPixelPtr[1], srcPixelPtr[0], 65535);
                                if (cancellationToken.IsCancellationRequested)
                                    throw new TaskCanceledException();
                            });
                            break;
                        case 4:
                            ImageProcessing.ParallelFor(0, image.Height, (y) =>
                            {
                                var packFunc = ImageProcessing.SelectBgra64Packing();
                                var srcPixelPtr = (ushort*)((byte*)srcBaseAddr + srcRowStride * y);
                                var destPixelPtr = (ulong*)((byte*)destBaseAddr + destRowStride * y);
                                for (var x = imageInfo.Width; x > 0; --x, srcPixelPtr += 4, ++destPixelPtr)
                                    *destPixelPtr = packFunc(srcPixelPtr[2], srcPixelPtr[1], srcPixelPtr[0], srcPixelPtr[3]);
                                if (cancellationToken.IsCancellationRequested)
                                    throw new TaskCanceledException();
                            });
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported channel count: {image.ChannelCount}.");
                    }
                    break;
            }
        });

        // complete
        return new ImageRenderingResult();
    }


    /// <inheritdoc/>
    public override BitmapFormat RenderedFormat => BitmapFormat.Bgra64;
}


/// <summary>
/// <see cref="IImageRenderer"/> for HEIF format.
/// </summary>
class HeifImageRenderer : MagickCompressedImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="HeifImageRenderer"/> instance.
    /// </summary>
    public HeifImageRenderer() : base(new ImageFormat(ImageFormatCategory.Compressed, "HEIF", new ImagePlaneDescriptor(0), new[] { "HEIF" }), new[] { MagickFormat.Heic, MagickFormat.Heif })
    { }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(IImageDataSource source, Stream imageStream) =>
        FileFormatParsers.HeifFileFormatParser.CheckFileHeader(imageStream);
}