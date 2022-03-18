using Avalonia.Data.Converters;
using Carina.PixelViewer.Media;
using CarinaStudio.AppSuite;
using CarinaStudio.Controls;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
    /// <summary>
    /// Convert from <see cref="ColorSpace"/> to readable string.
    /// </summary>
    class ColorSpaceToStringConverter : IValueConverter
    {
        /// <summary>
        /// Default instance.
        /// </summary>
        public static readonly ColorSpaceToStringConverter Default = new ColorSpaceToStringConverter();


        // Fields.
        static readonly IAppSuiteApplication? app = App.CurrentOrNull;


        /// <inheritdoc/>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (app == null)
                return null;
            if (targetType != typeof(object) && targetType != typeof(string))
                return null;
            if (value is ColorSpace colorSpace)
            {
                if (colorSpace.CustomName != null)
                    return colorSpace.CustomName;
                value = colorSpace.Name;
            }
            if (value is not string name)
                return null;
            if (app.TryGetResource<string>($"String/ColorSpace.{name}", out var res) == true)
                return res;
            return name;
        }


        /// <inheritdoc/>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}
