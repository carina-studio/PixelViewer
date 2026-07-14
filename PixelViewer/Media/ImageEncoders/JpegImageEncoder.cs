using CarinaStudio;
using ExifLibrary;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Carina.PixelViewer.Media.ImageEncoders;

/// <summary>
/// <see cref="IImageEncoder"/> to encode image in <see cref="FileFormats.Jpeg"/>.
/// </summary>
class JpegImageEncoder : BaseImageEncoder
{
    // Constants.
    // Maximum size of ICC profile data that fits in a single JPEG APP2 segment (0xffff minus the 2-byte length and the 14-byte 'ICC_PROFILE' segment header).
    const int MaxIccProfileSegmentDataSize = 0xffff - 2 - 14;


    /// <summary>
    /// Initialize new <see cref="JpegImageEncoder"/> instance.
    /// </summary>
    public JpegImageEncoder() : base("Jpeg", FileFormats.Jpeg)
    { }


    // Encode.
    protected override void OnEncode(IBitmapBuffer bitmapBuffer, Stream stream, ImageEncodingOptions options, CancellationToken cancellationToken)
    {
        // encode to JPEG without color space so that Skia does not embed its own ICC profile
        using var bitmap = bitmapBuffer.CreateSkiaBitmap(options.Orientation, null, cancellationToken);
        using var memoryStream = new MemoryStream();
        bitmap.Encode(memoryStream, SKEncodedImageFormat.Jpeg, Math.Max(1, Math.Min(100, options.QualityLevel)));

        // set the Software tag, reusing the same stream buffer to hold the re-encoded data
        memoryStream.Position = 0;
        var jpegFile = ImageFile.FromStream(memoryStream);
        memoryStream.SetLength(0);
        jpegFile.Properties.Set(ExifTag.Software, new ExifAscii(ExifTag.Software, Application.Current.Name, Encoding.ASCII));
        jpegFile.Save(memoryStream);
        var jpegData = memoryStream.GetBuffer();
        var jpegDataLength = (int)memoryStream.Length;

        // generate the ICC profile of the color space and embed it ourselves (best-effort)
        var iccProfile = (byte[]?)null;
        if (options.ColorSpace is not null)
        {
            try
            {
                iccProfile = options.ColorSpace.SaveAsIccProfile();
                if (iccProfile.Length > MaxIccProfileSegmentDataSize)
                {
                    Logger?.LogWarning("ICC profile of color space '{name}' is too large ({size} bytes) to embed into a single JPEG APP2 segment", options.ColorSpace.Name, iccProfile.Length);
                    iccProfile = null;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to generate ICC profile from color space '{name}' for JPEG", options.ColorSpace.Name);
            }
        }

        // write the JPEG with the ICC profile embedded, or as-is when there is no profile
        if (iccProfile is not null)
            WriteWithIccProfile(jpegData, jpegDataLength, iccProfile, stream);
        else
            stream.Write(jpegData, 0, jpegDataLength);
    }


    // Write the JPEG data to the stream, inserting the ICC profile as a single 'ICC_PROFILE' APP2 segment after the leading SOI and JFIF (APP0) markers.
    static void WriteWithIccProfile(byte[] jpegData, int jpegDataLength, byte[] iccProfile, Stream stream)
    {
        // find the offset right after the SOI marker, skipping a leading APP0 (JFIF) segment if present
        var insertOffset = 2;
        if (jpegDataLength >= insertOffset + 4 && jpegData[insertOffset] == 0xff && jpegData[insertOffset + 1] == 0xe0)
        {
            var app0SegmentSize = (jpegData[insertOffset + 2] << 8) | jpegData[insertOffset + 3];
            insertOffset += 2 + app0SegmentSize;
        }

        // build the APP2 segment header ('ICC_PROFILE\0' identifier + chunk index 1 + chunk count 1)
        var segmentSize = 2 + 12 + 1 + 1 + iccProfile.Length;
        var segmentHeader = new byte[4 + 12 + 1 + 1];
        segmentHeader[0] = 0xff;
        segmentHeader[1] = 0xe2;
        segmentHeader[2] = (byte)((segmentSize >> 8) & 0xff);
        segmentHeader[3] = (byte)(segmentSize & 0xff);
        "ICC_PROFILE\0"u8.ToArray().CopyTo(segmentHeader, 4);
        segmentHeader[16] = 0x01;
        segmentHeader[17] = 0x01;

        // write the leading markers, the ICC profile segment, then the remaining JPEG data
        stream.Write(jpegData, 0, insertOffset);
        stream.Write(segmentHeader, 0, segmentHeader.Length);
        stream.Write(iccProfile, 0, iccProfile.Length);
        stream.Write(jpegData, insertOffset, jpegDataLength - insertOffset);
    }
}
