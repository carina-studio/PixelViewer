using Avalonia.Data.Converters;
using Carina.PixelViewer.Media;
using CarinaStudio;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
    /// <summary>
    /// <see cref="IValueConverter"/> to convert from <see cref="ImageFormat"/> to readable string.
    /// </summary>
    class ImageFormatConverter : IValueConverter
    {
        /// <summary>
        /// Default instance.
        /// </summary>
        public static readonly ImageFormatConverter Default = new ImageFormatConverter();


        // Fields.
        readonly IApplication app = App.Current;


        /// <inheritdoc/>
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(object) && targetType != typeof(string))
                return null;
            if (value is ImageFormat format)
                return this.app.GetStringNonNull($"ImageFormat.{format.Name}", format.Name);
            return null;
        }


        /// <inheritdoc/>
        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
}
