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
        /// Convert to <see cref="ColorSpace"/>.
        /// </summary>
        /// <param name="colorSpace"><see cref="ScreenColorSpace"/>.</param>
        /// <returns><see cref="ColorSpace"/>.</returns>
        public static ColorSpace ToColorSpace(this ScreenColorSpace colorSpace) => colorSpace switch
        {
            ScreenColorSpace.DCI_P3 => ColorSpace.DCI_P3,
            ScreenColorSpace.Display_P3 => ColorSpace.Display_P3,
            _ => ColorSpace.Srgb,
        };
    }
}
