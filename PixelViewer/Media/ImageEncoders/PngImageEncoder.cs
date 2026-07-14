using CarinaStudio;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageEncoders;

/// <summary>
/// <see cref="IImageEncoder"/> to encode image in <see cref="FileFormats.Png"/>.
/// </summary>
class PngImageEncoder : BaseImageEncoder
{
    // Constants.
    // Size of the PNG signature (8 bytes) plus the IHDR chunk (4 length + 4 type + 13 data + 4 CRC = 25 bytes); the 'iCCP' chunk is inserted right after it.
    const int SignatureAndIhdrSize = 8 + 25;
    const int ZLibCompressionLevel = 3;


    /// <summary>
    /// Initialize new <see cref="PngImageEncoder"/> instance.
    /// </summary>
    public PngImageEncoder() : base("Png", FileFormats.Png)
    { }


    // Encode.
    protected override void OnEncode(IBitmapBuffer bitmapBuffer, Stream stream, ImageEncodingOptions options, CancellationToken cancellationToken)
    {
        // encode without color space so that Skia does not embed its own color chunks
        using var bitmap = bitmapBuffer.CreateSkiaBitmap(options.Orientation, null, cancellationToken);
        using var pixmap = bitmap.PeekPixels().AsNonNull();

        // generate the ICC profile of the color space and embed it ourselves (best-effort)
        var iccProfile = (byte[]?)null;
        if (options.ColorSpace is not null)
        {
            try
            {
                iccProfile = options.ColorSpace.SaveAsIccProfile();
            }
            catch (Exception ex)
            {
                this.Logger?.LogWarning(ex, "Failed to generate ICC profile from color space '{name}' for PNG", options.ColorSpace.Name);
            }
        }

        // encode to a temporary file (with a single filter and a lower zlib level to keep encoding fast) to avoid holding the whole PNG in memory, then copy to the output stream while embedding the ICC profile
        var tempFileName = Path.Combine(Path.GetTempPath(), $"PixelViewer-PngEncode-{Guid.NewGuid()}.tmp");
        try
        {
            using (var pngWriteStream = new SKFileWStream(tempFileName))
            {
                if (!pixmap.Encode(pngWriteStream, new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, ZLibCompressionLevel)))
                    throw new Exception("Failed to encode image to PNG.");
            }
            using (var tempReadStream = new FileStream(tempFileName, FileMode.Open, FileAccess.Read))
            {
                if (iccProfile is not null)
                    WriteWithIccProfile(tempReadStream, iccProfile, stream);
                else
                    tempReadStream.CopyTo(stream);
            }
        }
        finally
        {
            // delete the temporary file
            try
            {
                File.Delete(tempFileName);
            }
            catch
            { /* best effort */ }
        }
    }


    // Copy the encoded PNG to the output stream, inserting the ICC profile as an 'iCCP' chunk right after the IHDR chunk.
    static void WriteWithIccProfile(Stream pngSource, byte[] iccProfile, Stream output)
    {
        // build the 'iCCP' chunk data: profile name (Latin-1) + null terminator + compression method (0 = deflate) + zlib-compressed profile
        using var chunkDataStream = new MemoryStream();
        chunkDataStream.Write("ICC Profile\0\0"u8);
        using (var deflaterStream = new ZLibStream(chunkDataStream, CompressionLevel.Optimal, leaveOpen: true))
            deflaterStream.Write(iccProfile, 0, iccProfile.Length);
        var chunkData = chunkDataStream.ToArray();

        // frame the chunk as length + type ('iCCP') + data + CRC-32 (computed over the type and data)
        var chunk = new byte[4 + 4 + chunkData.Length + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0), (uint)chunkData.Length);
        "iCCP"u8.CopyTo(chunk.AsSpan(4));
        chunkData.CopyTo(chunk.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + chunkData.Length), Crc32.HashToUInt32(chunk.AsSpan(4, 4 + chunkData.Length)));

        // copy the signature and IHDR chunk, then the 'iCCP' chunk, then the remaining PNG data
        var header = new byte[SignatureAndIhdrSize];
        pngSource.ReadExactly(header, 0, header.Length);
        output.Write(header, 0, header.Length);
        output.Write(chunk, 0, chunk.Length);
        pngSource.CopyTo(output);
    }
}
