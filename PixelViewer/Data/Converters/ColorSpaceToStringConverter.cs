using Avalonia.Data.Converters;
using Carina.PixelViewer.Media;
using CarinaStudio;
using CarinaStudio.AppSuite;
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
        static readonly IAppSuiteApplication? app = IAppSuiteApplication.CurrentOrNull;


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
                if (colorSpace.IsEmbedded)
                    return app.GetString("ColorSpace.EmbeddedInFile");
                if (colorSpace.IsSystemDefined)
                    return app.GetString("ColorSpace.SystemDefined");
                value = colorSpace.Name;
            }
            if (value is not string name)
                return null;
            if (app.TryFindResource<string>($"String/ColorSpace.{name}", out var res))
                return res;
            return name;
        }


        /// <inheritdoc/>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}
