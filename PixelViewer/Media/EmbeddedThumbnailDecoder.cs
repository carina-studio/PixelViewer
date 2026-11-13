using Avalonia.Media.Imaging;
using CarinaStudio;
using SkiaSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Decoder of thumbnails embedded in image files, which are shown as placeholder before rendering the image.
/// </summary>
static class EmbeddedThumbnailDecoder
{
    /// <summary>
    /// Maximum width and height of decoded thumbnail in pixels.
    /// </summary>
    public const int MaxSize = 256;


    // Constants.
    const int JpegDataChunkSize = 1 << 20;
    const int MaxJpegDataSize = 64 << 20;


    // Create thumbnail of at most MaxSize from the decoded image.
    static async Task<WriteableBitmap> CreateThumbnailAsync(IBitmapBuffer bitmapBuffer, CancellationToken cancellationToken)
    {
        using (bitmapBuffer)
            return await bitmapBuffer.CreateThumbnailAvaloniaBitmapAsync(MaxSize, cancellationToken);
    }


    // Decode the JPEG image at the smallest scale which keeps the longer side at least MaxSize, JPEG can be decoded at a scale of N/8 directly.
    static IBitmapBuffer DecodeJpeg(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        // create codec
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData) ?? throw new ArgumentException("Unable to create codec.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Jpeg)
            throw new ArgumentException($"Incorrect format: {codec.EncodedFormat}.");
        cancellationToken.ThrowIfCancellationRequested();

        // select scale
        var longerSide = Math.Max(codec.Info.Width, codec.Info.Height);
        var scale = 1f;
        for (var numerator = 1; numerator < 8; ++numerator)
        {
            if (longerSide * numerator >= MaxSize * 8)
            {
                scale = numerator / 8f;
                break;
            }
        }
        var size = codec.GetScaledDimensions(scale);

        // decode without color management, as the rendered image of JPEG is
        var bitmapBuffer = new BitmapBuffer(BitmapFormat.Bgra32, ColorSpace.Default, size.Width, size.Height);
        try
        {
            var imageInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var result = SKCodecResult.InternalError;
            bitmapBuffer.Memory.Pin(address =>
            {
                result = codec.GetPixels(imageInfo, address, bitmapBuffer.RowBytes, new SKCodecOptions());
            });
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                throw new ArgumentException($"Failed to decode: {result}.");
            cancellationToken.ThrowIfCancellationRequested();
            return bitmapBuffer;
        }
        catch
        {
            bitmapBuffer.Dispose();
            throw;
        }
    }


    /// <summary>
    /// Decode thumbnail from the JPEG image in the given data.
    /// </summary>
    /// <param name="data">Data which contains the JPEG image.</param>
    /// <param name="offset">Offset of the JPEG image in the data.</param>
    /// <param name="length">Length of the JPEG image in bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of decoding thumbnail of at most <see cref="MaxSize"/> in BGRA32.</returns>
    public static async Task<WriteableBitmap> DecodeJpegAsync(byte[] data, int offset, int length, CancellationToken cancellationToken)
    {
        // check parameters
        if (offset < 0 || length <= 0 || offset + (long)length > data.Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        // decode
        var bitmapBuffer = await Task.Run(() => DecodeJpeg(data.AsSpan(offset, length), cancellationToken), cancellationToken);
        return await CreateThumbnailAsync(bitmapBuffer, cancellationToken);
    }


    /// <summary>
    /// Decode thumbnail from the JPEG image which starts at the given position of stream.
    /// </summary>
    /// <param name="stream">Stream to read the JPEG image.</param>
    /// <param name="offset">Position of the JPEG image in the stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of decoding thumbnail of at most <see cref="MaxSize"/> in BGRA32.</returns>
    /// <remarks>The JPEG image is read by following its segments to the end of image, so its length is not needed.</remarks>
    public static async Task<WriteableBitmap> DecodeJpegAsync(Stream stream, long offset, CancellationToken cancellationToken)
    {
        var bitmapBuffer = await Task.Run(() =>
        {
            stream.Position = offset;
            var data = ReadJpegData(stream, cancellationToken) ?? throw new ArgumentException("Invalid JPEG image.");
            return DecodeJpeg(data, cancellationToken);
        }, cancellationToken);
        return await CreateThumbnailAsync(bitmapBuffer, cancellationToken);
    }


    /// <summary>
    /// Decode thumbnail from the uncompressed RGB image of 8 bits per sample which starts at the given position of stream.
    /// </summary>
    /// <param name="stream">Stream to read the image.</param>
    /// <param name="offset">Position of the image in the stream.</param>
    /// <param name="width">Width of the image.</param>
    /// <param name="height">Height of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of decoding thumbnail of at most <see cref="MaxSize"/> in BGRA32.</returns>
    /// <remarks>Rows of the image are packed without padding.</remarks>
    public static async Task<WriteableBitmap> DecodeRgb888Async(Stream stream, long offset, int width, int height, CancellationToken cancellationToken)
    {
        // check parameters
        if (width <= 0 || height <= 0 || (long)width * height * 3 > MaxJpegDataSize)
            throw new ArgumentOutOfRangeException(nameof(width));

        // decode
        var bitmapBuffer = await Task.Run(() =>
        {
            // read data
            var srcRowStride = width * 3;
            var data = new byte[srcRowStride * height];
            stream.Position = offset;
            stream.ReadExactly(data);
            cancellationToken.ThrowIfCancellationRequested();

            // convert to BGRA32
            var bitmapBuffer = new BitmapBuffer(BitmapFormat.Bgra32, ColorSpace.Default, width, height);
            try
            {
                bitmapBuffer.Memory.Pin(address =>
                {
                    unsafe
                    {
                        var packFunc = ImageProcessing.SelectBgra32Packing();
                        var destRowStride = bitmapBuffer.RowBytes;
                        fixed (byte* srcBaseAddress = data)
                        {
                            for (var y = 0; y < height; ++y)
                            {
                                var srcPixelPtr = srcBaseAddress + (y * srcRowStride);
                                var destPixelPtr = (uint*)((byte*)address + (y * destRowStride));
                                for (var x = width; x > 0; --x, srcPixelPtr += 3, ++destPixelPtr)
                                    *destPixelPtr = packFunc(srcPixelPtr[2], srcPixelPtr[1], srcPixelPtr[0], 255);
                            }
                        }
                    }
                });
                return bitmapBuffer;
            }
            catch
            {
                bitmapBuffer.Dispose();
                throw;
            }
        }, cancellationToken);
        return await CreateThumbnailAsync(bitmapBuffer, cancellationToken);
    }


    // Read data of the JPEG image which starts at current position of stream by following its segments to the end of image, or null if it is not a valid JPEG image. The segments are skipped by their lengths rather than searched because an APP segment may embed a thumbnail with an end of image of its own.
    static byte[]? ReadJpegData(Stream stream, CancellationToken cancellationToken)
    {
        // make sure that the given number of bytes after the position are available
        var data = new MemoryStream();
        var chunk = new byte[JpegDataChunkSize];
        var position = 0;
        bool EnsureData(int count)
        {
            while (data.Length - position < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readCount = stream.Read(chunk, 0, chunk.Length);
                if (readCount <= 0 || data.Length + readCount > MaxJpegDataSize)
                    return false;
                data.Write(chunk, 0, readCount);
            }
            return true;
        }

        // check start of image
        if (!EnsureData(2))
            return null;
        var buffer = data.GetBuffer();
        if (buffer[0] != 0xff || buffer[1] != 0xd8)
            return null;
        position = 2;

        // follow segments to the end of image
        while (true)
        {
            // read marker
            if (!EnsureData(2))
                return null;
            buffer = data.GetBuffer();
            if (buffer[position] != 0xff)
                return null;
            var marker = buffer[position + 1];
            if (marker == 0xff)
            {
                ++position;
                continue;
            }
            position += 2;
            if (marker == 0xd9)
                return buffer.AsSpan(0, position).ToArray();
            if (marker == 0x01 || (marker >= 0xd0 && marker <= 0xd7))
                continue;

            // skip segment
            if (!EnsureData(2))
                return null;
            buffer = data.GetBuffer();
            var segmentSize = (buffer[position] << 8) | buffer[position + 1];
            if (segmentSize < 2 || !EnsureData(segmentSize))
                return null;
            position += segmentSize;
            if (marker != 0xda)
                continue;

            // skip entropy-coded data after start of scan, 0xff in it is followed by 0x00 or a restart marker
            while (true)
            {
                if (!EnsureData(2))
                    return null;
                buffer = data.GetBuffer();
                var index = buffer.AsSpan(position, (int)data.Length - position - 1).IndexOf((byte)0xff);
                if (index < 0)
                {
                    position = (int)data.Length - 1;
                    continue;
                }
                position += index;
                var nextByte = buffer[position + 1];
                if (nextByte == 0x00 || (nextByte >= 0xd0 && nextByte <= 0xd7))
                {
                    position += 2;
                    continue;
                }
                break;
            }
        }
    }
}
