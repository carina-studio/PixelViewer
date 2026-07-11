using CarinaStudio;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageEncoders;

/// <summary>
/// <see cref="IImageEncoder"/> to encode image in <see cref="FileFormats.Tiff"/>, with or without compression.
/// </summary>
class TiffImageEncoder : BaseImageEncoder
{
    // Constants.
    const ushort CompressionDeflate = 8;
    const ushort CompressionUncompressed = 1;
    const ushort TiffTypeAscii = 2;
    const ushort TiffTypeLong = 4;
    const ushort TiffTypeShort = 3;
    const ushort TiffTypeUndefined = 7;


    // Static fields.
    static readonly ILogger? Logger = Application.CurrentOrNull?.LoggerFactory.CreateLogger(nameof(TiffImageEncoder));


    /// <summary>
    /// Initialize new <see cref="TiffImageEncoder"/> instance.
    /// </summary>
    public TiffImageEncoder() : base("Tiff", FileFormats.Tiff)
    { }


    // Convert the source BGRA bitmap buffer to an RGBA single strip and Deflate-compress it.
    static byte[] CompressPixels(IBitmapBuffer bitmapBuffer, int rowStride, int bytesPerSample, CancellationToken cancellationToken)
    {
        var width = bitmapBuffer.Width;
        var height = bitmapBuffer.Height;
        using var compressedStream = new MemoryStream();
        // a fast Deflate level keeps the encode responsive; it still shrinks the strip enough to matter for the clipboard, and callers that need the exact pixels use uncompressed pixels instead
        using (var deflaterStream = new ZLibStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
        {
            var srcSpan = bitmapBuffer.Memory.Span;
            var srcRowStride = bitmapBuffer.RowBytes;
            var rowBuffer = new byte[rowStride];
            for (var y = 0; y < height; ++y)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConvertBgraRowToRgba(srcSpan.Slice(y * srcRowStride, srcRowStride), rowBuffer, width, bytesPerSample);
                deflaterStream.Write(rowBuffer, 0, rowStride);
            }
        }
        return compressedStream.ToArray();
    }


    // Convert one row of BGRA pixels to RGBA (swapping the B and R channels) into the given buffer.
    static void ConvertBgraRowToRgba(ReadOnlySpan<byte> srcRow, byte[] rowBuffer, int width, int bytesPerSample)
    {
        if (bytesPerSample == 1)
        {
            for (var x = 0; x < width; ++x)
            {
                var s = x * 4;
                rowBuffer[s] = srcRow[s + 2];
                rowBuffer[s + 1] = srcRow[s + 1];
                rowBuffer[s + 2] = srcRow[s];
                rowBuffer[s + 3] = srcRow[s + 3];
            }
        }
        else
        {
            for (var x = 0; x < width; ++x)
            {
                var s = x * 8;
                rowBuffer[s] = srcRow[s + 4];
                rowBuffer[s + 1] = srcRow[s + 5];
                rowBuffer[s + 2] = srcRow[s + 2];
                rowBuffer[s + 3] = srcRow[s + 3];
                rowBuffer[s + 4] = srcRow[s];
                rowBuffer[s + 5] = srcRow[s + 1];
                rowBuffer[s + 6] = srcRow[s + 6];
                rowBuffer[s + 7] = srcRow[s + 7];
            }
        }
    }


    // Encode.
    protected override void OnEncode(IBitmapBuffer bitmapBuffer, Stream stream, ImageEncodingOptions options, CancellationToken cancellationToken)
    {
        // resolve the ICC profile of the color space (best-effort)
        var iccProfile = (byte[]?)null;
        if (options.ColorSpace is not null)
        {
            try
            {
                using var iccStream = new MemoryStream();
                if (options.ColorSpace.TrySaveAsIccProfile(iccStream))
                    iccProfile = iccStream.ToArray();
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to extract ICC profile from color space '{name}' for TIFF", options.ColorSpace.Name);
            }
        }

        // prepare the single pixel strip, Deflate-compressing it unless the caller prefers uncompressed pixels
        var width = bitmapBuffer.Width;
        var height = bitmapBuffer.Height;
        var bytesPerSample = bitmapBuffer.Format == BitmapFormat.Bgra64 ? 2 : 1;
        var rowStride = width * 4 * bytesPerSample;
        var compress = !options.PreferUncompressedPixels;
        var compressedPixels = compress
            ? CompressPixels(bitmapBuffer, rowStride, bytesPerSample, cancellationToken)
            : null;
        var pixelDataSize = compressedPixels is not null
            ? compressedPixels.Length
            : (long)rowStride * height;

        // compute the layout of the little-endian TIFF file (header, single strip of pixels, out-of-line values, IFD)
        var pixelDataOffset = 8L;
        var bitsPerSampleOffset = pixelDataOffset + pixelDataSize;
        var sampleFormatOffset = bitsPerSampleOffset + 8;
        var softwareOffset = sampleFormatOffset + 8;
        var softwareBytes = Encoding.ASCII.GetBytes((Application.CurrentOrNull?.Name ?? "PixelViewer") + "\0");
        var iccOffset = softwareOffset + softwareBytes.Length + (softwareBytes.Length & 1);
        var iccLength = iccProfile?.Length ?? 0;
        var ifdOffset = iccProfile is not null
            ? iccOffset + iccLength + (iccLength & 1)
            : iccOffset;
        var tmp = new byte[4];

        // write the TIFF header
        stream.WriteByte((byte)'I');
        stream.WriteByte((byte)'I');
        WriteUInt16(42);
        WriteUInt32((uint)ifdOffset);

        // write the pixel data as a single strip (the pre-compressed buffer, or the raw RGBA rows when uncompressed)
        if (compressedPixels is not null)
            stream.Write(compressedPixels, 0, compressedPixels.Length);
        else
        {
            var srcSpan = bitmapBuffer.Memory.Span;
            var srcRowStride = bitmapBuffer.RowBytes;
            var rowBuffer = new byte[rowStride];
            for (var y = 0; y < height; ++y)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConvertBgraRowToRgba(srcSpan.Slice(y * srcRowStride, srcRowStride), rowBuffer, width, bytesPerSample);
                stream.Write(rowBuffer, 0, rowStride);
            }
        }

        // write the out-of-line field values (bits per sample, sample format, software name, ICC profile)
        for (var i = 0; i < 4; ++i)
            WriteUInt16((ushort)(bytesPerSample * 8));
        for (var i = 0; i < 4; ++i)
            WriteUInt16(1);
        stream.Write(softwareBytes, 0, softwareBytes.Length);
        if ((softwareBytes.Length & 1) != 0)
            stream.WriteByte(0);
        if (iccProfile is not null)
        {
            stream.Write(iccProfile, 0, iccProfile.Length);
            if ((iccProfile.Length & 1) != 0)
                stream.WriteByte(0);
        }

        // write the image file directory with entries sorted by tag in ascending order
        WriteUInt16((ushort)(iccProfile is not null ? 15 : 14));
        WriteEntry(0x0100, TiffTypeLong, 1, (uint)width);
        WriteEntry(0x0101, TiffTypeLong, 1, (uint)height);
        WriteEntry(0x0102, TiffTypeShort, 4, (uint)bitsPerSampleOffset);
        WriteEntry(0x0103, TiffTypeShort, 1, (uint)(compress ? CompressionDeflate : CompressionUncompressed));
        WriteEntry(0x0106, TiffTypeShort, 1, 2);
        WriteEntry(0x0111, TiffTypeLong, 1, (uint)pixelDataOffset);
        WriteEntry(0x0112, TiffTypeShort, 1, (uint)Tiff.ToTiffOrientation(options.Orientation));
        WriteEntry(0x0115, TiffTypeShort, 1, 4);
        WriteEntry(0x0116, TiffTypeLong, 1, (uint)height);
        WriteEntry(0x0117, TiffTypeLong, 1, (uint)pixelDataSize);
        WriteEntry(0x011c, TiffTypeShort, 1, 1);
        WriteEntry(0x0131, TiffTypeAscii, (uint)softwareBytes.Length, (uint)softwareOffset);
        WriteEntry(0x0152, TiffTypeShort, 1, 2);
        WriteEntry(0x0153, TiffTypeShort, 4, (uint)sampleFormatOffset);
        if (iccProfile is not null)
            WriteEntry(0x8773, TiffTypeUndefined, (uint)iccProfile.Length, (uint)iccOffset);
        WriteUInt32(0);

        // write a 16-bit unsigned integer in little-endian
        void WriteUInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
            stream.Write(tmp, 0, 2);
        }

        // write a 32-bit unsigned integer in little-endian
        void WriteUInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
            stream.Write(tmp, 0, 4);
        }

        // write a 12-byte IFD entry
        void WriteEntry(ushort tag, ushort type, uint count, uint value)
        {
            WriteUInt16(tag);
            WriteUInt16(type);
            WriteUInt32(count);
            WriteUInt32(value);
        }
    }
}
