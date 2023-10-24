using Carina.PixelViewer.Media.FileFormatParsers;
using CarinaStudio;
using CarinaStudio.IO;
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
    /// <param name="magickFormats">Formats defined by <see cref="MagickFormat"/>.</param>      
    protected MagickCompressedImageRenderer(ImageFormat format, IEnumerable<MagickFormat> magickFormats) : base(format)
    {
        this.magickFormats.AddRange(magickFormats);
    }
    
    
    // Create IMagickImageInfo from stream.
    IMagickImageInfo CreateImageInfo(IImageDataSource source, Stream stream, CancellationToken cancellationToken)
    {
        // check file header first to prevent decoding image
        var position = stream.Position;
        if (!this.OnCheckFileHeader(source, stream))
            throw new ArgumentException("Unsupported format.");
        stream.Position = position;

        // decode image info
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        var imageInfo = default(IMagickImageInfo);
        try
        {
            imageInfo = new MagickImageInfoFactory().Create(stream);
        }
        catch
        {
            if (source is FileImageDataSource fileImageDataSource)
                imageInfo = new MagickImageInfoFactory().Create(fileImageDataSource.FileName);
        }
        if (imageInfo is null)
            throw new ArgumentException("Unable to decode image info.");
        if (!this.magickFormats.Contains(imageInfo.Format))
            throw new ArgumentException($"Incorrect format: {imageInfo.Format}.");
        return imageInfo;
    }


    /// <summary>
    /// Called to check dimensions of bitmap buffer for image rendering.
    /// </summary>
    /// <param name="source">Source of image.</param>
    /// <param name="imageStream">Stream to read image data.</param>
    /// <param name="bufferWidth">Pixel width of buffer.</param>
    /// <param name="bufferHeight">Pixel height of buffer.</param>
    /// <param name="expectedWidth">Expected pixel width of buffer.</param>
    /// <param name="expectedHeight">Expected pixel height of buffer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if dimensions of bitmap buffer is correct.</returns>
    protected virtual bool OnCheckBufferDimensions(IImageDataSource source, Stream imageStream, int bufferWidth, int bufferHeight, out int expectedWidth, out int expectedHeight, CancellationToken cancellationToken)
    {
        var imageInfo = this.CreateImageInfo(source, imageStream, cancellationToken);
        expectedWidth = imageInfo.Width;
        expectedHeight = imageInfo.Height;
        return bufferWidth == expectedWidth && bufferHeight == expectedHeight;
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
        
        // check buffer dimensions
        if (!this.OnCheckBufferDimensions(source, imageStream, bitmapBuffer.Width, bitmapBuffer.Height, out var width, out var height, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            throw new ArgumentException($"Incorrect bitmap size: {bitmapBuffer.Width}x{bitmapBuffer.Height}, {width}x{height} expected.");
        }
        imageStream.Position = position;

        // decode
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();
        using var image = new MagickImageFactory().Create(imageStream, new MagickReadSettings
        {
            Format = this.OnSelectMagickFormatToRender(this.magickFormats),
        });
        if (image is null)
            throw new ArgumentException("Failed to decode.");
        if (bitmapBuffer.Width != image.Width || bitmapBuffer.Height != image.Height)
            throw new ArgumentException($"Incorrect bitmap size: {bitmapBuffer.Width}x{bitmapBuffer.Height}, {image.Width}x{image.Height} expected.");
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();

        // copy pixel data
        bitmapBuffer.Memory.Pin(destBaseAddr =>
        {
            using var srcUnsafePixels = image.GetPixelsUnsafe();
            var srcBaseAddr = srcUnsafePixels.GetAreaPointer(0, 0, image.Width, image.Height);
            var srcRowStride = width * image.ChannelCount * sizeof(ushort);
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
                                for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
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
                                for (var x = width; x > 0; --x, srcPixelPtr += 3, ++destPixelPtr)
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
                                for (var x = width; x > 0; --x, srcPixelPtr += 4, ++destPixelPtr)
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
                                for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
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
                                for (var x = width; x > 0; --x, srcPixelPtr += 3, ++destPixelPtr)
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
                                for (var x = width; x > 0; --x, srcPixelPtr += 4, ++destPixelPtr)
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


    /// <summary>
    /// Called to select proper <see cref="MagickFormat"/> to decode/render image.
    /// </summary>
    /// <param name="candidates">Candidate formats.</param>
    /// <returns>Selected <see cref="MagickFormat"/>.</returns>
    protected virtual MagickFormat OnSelectMagickFormatToRender(IReadOnlyList<MagickFormat> candidates) =>
        candidates[0];


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
                stream.Position = renderingOptions.DataOffset;
                return this.CreateImageInfo(source, stream, cancellationToken).Compression switch
                {
                    CompressionMethod.JPEG => BitmapFormat.Bgra32,
                    _ => BitmapFormat.Bgra64,
                };
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
class HeifImageRenderer : MagickCompressedImageRenderer
{
    /// <summary>
    /// Initialize new <see cref="HeifImageRenderer"/> instance.
    /// </summary>
    public HeifImageRenderer() : base(new ImageFormat(ImageFormatCategory.Compressed, "HEIF", new ImagePlaneDescriptor(0), new[] { "HEIF" }), new[] { MagickFormat.Heic, MagickFormat.Heif })
    { }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(IImageDataSource source, Stream imageStream) =>
        HeifFileFormatParser.CheckFileHeader(imageStream);
}