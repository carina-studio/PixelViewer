using Avalonia.Data.Converters;
using Avalonia.Media;
using Carina.PixelViewer.Media;
using CarinaStudio;
using CarinaStudio.AppSuite;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
    /// <summary>
    /// <see cref="IValueConverter"/> to convert from <see cref="ImageFormatCategory"/> to <see cref="IBrush"/>.
    /// </summary>
    class ImageFormatCategoryToBrushConverter : IValueConverter
    {
        /// <summary>
        /// Default instance.
        /// </summary>
        public static readonly ImageFormatCategoryToBrushConverter Default = new ImageFormatCategoryToBrushConverter();


        // Fields.
        readonly IAppSuiteApplication? app = IAppSuiteApplication.CurrentOrNull;


        /// <inheritdoc/>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (targetType != typeof(object) && !typeof(IBrush).IsAssignableFrom(targetType))
                return null;
            if (value is not ImageFormatCategory category)
                return null;
            return app?.FindResourceOrDefault<IBrush?>($"Brush/SessionControl.ImageFormatCategoryLabel.Background.{category}");
        }


        /// <inheritdoc/>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}
