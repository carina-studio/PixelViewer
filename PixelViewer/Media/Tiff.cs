using System;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Constants and utility functions for TIFF.
/// </summary>
static class Tiff
{
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
}
