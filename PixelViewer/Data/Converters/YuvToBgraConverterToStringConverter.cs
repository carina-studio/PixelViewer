using Avalonia.Data.Converters;
using Carina.PixelViewer.Media;
using CarinaStudio;
using CarinaStudio.AppSuite;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
    /// <summary>
    /// Convert from <see cref="YuvToBgraConverter"/> to readable string.
    /// </summary>
    class YuvToBgraConverterToStringConverter : IValueConverter
    {
        /// <summary>
        /// Default instance.
        /// </summary>
        public static readonly YuvToBgraConverterToStringConverter Default = new YuvToBgraConverterToStringConverter();


        // Fields.
        static readonly IAppSuiteApplication? app = App.CurrentOrNull;


        /// <inheritdoc/>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (app == null)
                return null;
            if (targetType != typeof(object) && targetType != typeof(string))
                return null;
            if (value is YuvToBgraConverter converter)
                value = converter.Name;
            if (value is not string name)
                return null;
            if (app.TryFindResource<string>($"String/YuvToBgraConverter.{name}", out var res))
                return res;
            return name;
        }


        /// <inheritdoc/>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}
