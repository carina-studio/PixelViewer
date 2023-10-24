using CarinaStudio;
using System;
using System.IO;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Constants and utility functions for TIFF.
/// </summary>
static class Tiff
{
    /// <summary>
    /// Check whether header of file represents TIFF or not.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <param name="isLittleEndian">True if data are represented in little-endian.</param>
    /// <returns>True if header represents TIFF.</returns>
    public static unsafe bool CheckFileHeader(Stream stream, out bool isLittleEndian)
    {
        isLittleEndian = false;
        var buffer = stackalloc byte[4];
        if (stream.Read(new Span<byte>(buffer, 4)) < 4)
            return false;
        if (buffer[0] == 'I' && buffer[1] == 'I')
        {
            isLittleEndian = true;
            return buffer[2] == 0x2a && buffer[3] == 0;
        }
        if (buffer[0] == 'M' && buffer[1] == 'M')
            return buffer[2] == 0 && buffer[3] == 0x2a;
        return false;
    }
    
    
    /// <summary>
    /// Convert from TIFF orientation to degrees.
    /// </summary>
    /// <param name="orientation">TIFF orientation.</param>
    /// <returns>Orientation in degrees, the value will be one of 0, 90, 180 and 270.</returns>
    public static int FromTiffOrientation(int orientation) => orientation switch
    {
        3 or 4 => 180,
        5 or 8 => 270,
        6 or 7 => 90,
        _ => 0,
    };


    /// <summary>
    /// Get orientation from TIFF.
    /// </summary>
    /// <param name="stream">Stream contains TIFF.</param>
    /// <param name="fallbackToThumbnail">True to fall-back to orientation of thumbnail if original orientation is unavailable.</param>
    /// <returns>Orientation.</returns>
    public static int GetOrientation(Stream stream, bool fallbackToThumbnail = true)
    {
        var orientation = -1;
        var thumbOrientation = -1;
        var entryReader = Global.RunOrDefault(() => new IfdEntryReader(stream));
        if (entryReader is null)
            return 0;
        var isFullSizeImage = false;
        while (entryReader.Read() && orientation < 0)
        {
            switch (entryReader.CurrentIfdName)
            {
                case IfdNames.Default:
                case "Raw":
                {
                    switch (entryReader.CurrentEntryId)
                    {
                        case 0x00fe: // NewSubfileType
                            if (entryReader.TryGetEntryData(out uint[]? uintData) && uintData != null)
                                isFullSizeImage = (uintData[0] == 0);
                            break;
                        case 0x0112: // Orientation
                            if (entryReader.TryGetEntryData(out ushort[]? ushortData) && ushortData != null)
                            {
                                if (isFullSizeImage)
                                    orientation = ushortData[0];
                                else if (thumbOrientation < 0)
                                    thumbOrientation = ushortData[0];
                            }
                            break;
                    }
                    break;
                }
            }
        }
        if (orientation >= 0)
            return FromTiffOrientation(orientation);
        if (thumbOrientation >= 0 && fallbackToThumbnail)
            return FromTiffOrientation(thumbOrientation);
        return 0;
    }
}
