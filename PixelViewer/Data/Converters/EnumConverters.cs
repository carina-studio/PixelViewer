using Carina.PixelViewer.Media;
using CarinaStudio.AppSuite.Converters;
using System;

namespace Carina.PixelViewer.Data.Converters
{
    /// <summary>
    /// Common converters for enumerations.
    /// </summary>
    static class EnumConverters
    {
        /// <summary>
        /// Convert from <see cref="ImageFormatCategory"/>.
        /// </summary>
        public static readonly EnumConverter ImageFormatCategory = new EnumConverter(App.CurrentOrNull, typeof(ImageFormatCategory));
    }
}
