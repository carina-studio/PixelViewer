using Avalonia.Data.Converters;
using Carina.PixelViewer.Media;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Carina.PixelViewer.Data.Converters
{
	/// <summary>
	/// <see cref="IValueConverter"/> to convert <see cref="AspectRatio"/> to readable string.
	/// </summary>
	class AspectRatioToStringConverter : IValueConverter
	{
		/// <summary>
		/// Default instance.
		/// </summary>
		public static readonly AspectRatioToStringConverter Default = new AspectRatioToStringConverter();


		// Static fields.
		static readonly Regex EnumNameRegex = new Regex("Ratio_(?<X>[\\d]+)x(?<Y>[\\d]+)");
		static readonly Regex ReadableNameRegex = new Regex("^(?<X>[\\d]+)[\\s]*:[\\s]*(?<Y>[\\d]+)$");


		// Convert.
		public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is AspectRatio aspectRatio)
			{
				if (aspectRatio == AspectRatio.Unknown)
					return App.Current.GetString("AspectRatio.Unknown");
				var match = EnumNameRegex.Match(aspectRatio.ToString());
				if (match.Success)
					return $"{match.Groups["X"].Value}:{match.Groups["Y"].Value}";
			}
			return null;
		}


		// Convert back.
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is string stringValue)
			{
				var match = ReadableNameRegex.Match(stringValue);
				if (match.Success)
				{
					var name = $"Ratio_{match.Groups["X"].Value}x{match.Groups["Y"].Value}";
					if (Enum.TryParse<AspectRatio>(name, out var aspectRatio))
						return aspectRatio;
				}
			}
			return AspectRatio.Unknown;
		}
	}
}
