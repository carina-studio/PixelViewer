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
    /// Convert from TIFF orientation to rotation in degrees and horizontal/vertical flip flags.
    /// </summary>
    /// <param name="orientation">TIFF orientation (1-8).</param>
    /// <param name="rotation">Rotation in degrees, will be one of 0, 90, 180 and 270.</param>
    /// <param name="flipX">True if image is mirrored horizontally.</param>
    /// <param name="flipY">True if image is mirrored vertically.</param>
    public static void FromTiffOrientation(int orientation, out int rotation, out bool flipX, out bool flipY)
    {
        // EXIF/TIFF orientation table:
        // 1: 0°,        2: flip-X,         3: 180°,        4: flip-Y,
        // 5: 90° CW + flip-X, 6: 90° CW,   7: 270° + flip-X, 8: 270°.
        switch (orientation)
        {
            case 2:
                rotation = 0;
                flipX = true;
                flipY = false;
                break;
            case 3:
                rotation = 180;
                flipX = false;
                flipY = false;
                break;
            case 4:
                rotation = 0;
                flipX = false;
                flipY = true;
                break;
            case 5:
                rotation = 90;
                flipX = true;
                flipY = false;
                break;
            case 6:
                rotation = 90;
                flipX = false;
                flipY = false;
                break;
            case 7:
                rotation = 270;
                flipX = true;
                flipY = false;
                break;
            case 8:
                rotation = 270;
                flipX = false;
                flipY = false;
                break;
            default:
                rotation = 0;
                flipX = false;
                flipY = false;
                break;
        }
    }


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


    /// <summary>
    /// Get rotation and flip transformation from TIFF.
    /// </summary>
    /// <param name="stream">Stream contains TIFF.</param>
    /// <param name="rotation">Rotation in degrees, will be one of 0, 90, 180 and 270.</param>
    /// <param name="flipX">True if image is mirrored horizontally.</param>
    /// <param name="flipY">True if image is mirrored vertically.</param>
    /// <param name="fallbackToThumbnail">True to fall-back to orientation of thumbnail if original orientation is unavailable.</param>
    public static void GetTransformation(Stream stream, out int rotation, out bool flipX, out bool flipY, bool fallbackToThumbnail = true)
    {
        var orientation = -1;
        var thumbOrientation = -1;
        var entryReader = Global.RunOrDefault(() => new IfdEntryReader(stream));
        if (entryReader is null)
        {
            rotation = 0;
            flipX = false;
            flipY = false;
            return;
        }
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
            FromTiffOrientation(orientation, out rotation, out flipX, out flipY);
        else if (thumbOrientation >= 0 && fallbackToThumbnail)
            FromTiffOrientation(thumbOrientation, out rotation, out flipX, out flipY);
        else
        {
            rotation = 0;
            flipX = false;
            flipY = false;
        }
    }


    /// <summary>
    /// Convert from rotation in degrees to TIFF orientation.
    /// </summary>
    /// <param name="rotation">Rotation in degrees, which will be normalized into the range [0, 360).</param>
    /// <returns>TIFF orientation, the value will be one of 1, 3, 6 and 8.</returns>
    public static int ToTiffOrientation(int rotation) => (((rotation % 360) + 360) % 360) switch
    {
        90 => 6,
        180 => 3,
        270 => 8,
        _ => 1,
    };
}
