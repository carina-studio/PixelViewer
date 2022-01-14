using System;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// Color space of screen.
    /// </summary>
    enum ScreenColorSpace
    {
        /// <summary>
        /// sRGB.
        /// </summary>
        Srgb,
        /// <summary>
        /// Display-P3.
        /// </summary>
        Display_P3,
        /// <summary>
        /// DCI-P3.
        /// </summary>
        DCI_P3,
    }


    /// <summary>
    /// Extensions for <see cref="ScreenColorSpace"/>.
    /// </summary>
    static class ScreenColorSpaceExtensions
    {
        /// <summary>
        /// Convert to <see cref="BitmapColorSpace"/>.
        /// </summary>
        /// <param name="colorSpace"><see cref="ScreenColorSpace"/>.</param>
        /// <returns><see cref="BitmapColorSpace"/>.</returns>
        public static BitmapColorSpace ToBitmapColorSpace(this ScreenColorSpace colorSpace) => colorSpace switch
        {
            ScreenColorSpace.DCI_P3 => BitmapColorSpace.DCI_P3,
            ScreenColorSpace.Display_P3 => BitmapColorSpace.Display_P3,
            _ => BitmapColorSpace.Srgb,
        };
    }
}
