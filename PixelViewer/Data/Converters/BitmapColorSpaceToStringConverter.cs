using Avalonia.Data.Converters;
using Carina.PixelViewer.Media;
using CarinaStudio.AppSuite;
using CarinaStudio.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Data.Converters
{
    /// <summary>
    /// Convert from <see cref="BitmapColorSpace"/> to readable string.
    /// </summary>
    class BitmapColorSpaceToStringConverter : IValueConverter
    {
        /// <summary>
        /// Default instance.
        /// </summary>
        public static readonly BitmapColorSpaceToStringConverter Default = new BitmapColorSpaceToStringConverter();


        // Fields.
        static readonly IAppSuiteApplication? app = App.CurrentOrNull;


        /// <inheritdoc/>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (app == null)
                return null;
            if (targetType != typeof(object) && targetType != typeof(string))
                return null;
            if (value is BitmapColorSpace colorSpace)
                value = colorSpace.Name;
            if (value is not string name)
                return null;
            if (app.TryGetResource<string>($"String/BitmapColorSpace.{name}", out var res) == true)
                return res;
            return name;
        }


        /// <inheritdoc/>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}
