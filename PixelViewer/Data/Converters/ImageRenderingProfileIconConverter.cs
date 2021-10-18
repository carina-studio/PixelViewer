using Avalonia.Data.Converters;
using Avalonia.Media;
using Carina.PixelViewer.Media.Profiles;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
    /// <summary>
    /// <see cref="IValueConverter"/> to convert from <see cref="ImageRenderingProfile"/> to <see cref="Drawing"/>.
    /// </summary>
    class ImageRenderingProfileIconConverter : IValueConverter
    {
        /// <summary>
        /// Default instance.
        /// </summary>
        public static readonly ImageRenderingProfileIconConverter Default = new ImageRenderingProfileIconConverter();


        // Fields.
        readonly App app;


        // Constructor.
        ImageRenderingProfileIconConverter() => this.app = (App)App.Current;


        // Convert.
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!targetType.IsAssignableFrom(typeof(Drawing)))
                return null;
            if (value is ImageRenderingProfile profile)
            {
                if (app.Resources.TryGetResource($"Drawing/Icon.ImageRenderingProfile.{profile.Type}", out var res))
                    return res as Drawing;
            }
            return null;
        }


        // Convert back.
        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
}
