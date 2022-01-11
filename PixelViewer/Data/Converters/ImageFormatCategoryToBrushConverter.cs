using Avalonia.Data.Converters;
using Avalonia.Media;
using Carina.PixelViewer.Media;
using CarinaStudio.AppSuite;
using CarinaStudio.Controls;
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
        readonly IAppSuiteApplication? app = App.CurrentOrNull;


        /// <inheritdoc/>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (targetType != typeof(object) && !typeof(IBrush).IsAssignableFrom(targetType))
                return null;
            if (value is not ImageFormatCategory category)
                return null;
            var brush = (IBrush?)null;
            app?.TryGetResource($"Brush/SessionControl.ImageFormatCategoryLabel.Background.{category}", out brush);
            return brush;
        }


        /// <inheritdoc/>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}
