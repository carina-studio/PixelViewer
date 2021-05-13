using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
	/// <summary>
	/// <see cref="IValueConverter"/> to convert from ratio to readable percentage string.
	/// </summary>
	class RatioToPercentageConverter : IValueConverter
	{
		/// <summary>
		/// Default instance.
		/// </summary>
		public static readonly RatioToPercentageConverter Default = new RatioToPercentageConverter();


		// Convert.
		public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is double doubleValue)
			{
				if (double.IsFinite(doubleValue))
					return $"{Math.Round(doubleValue * 100)}%";
			}
			else if (value is float floatValue)
			{
				if (float.IsFinite(floatValue))
					return $"{Math.Round(floatValue * 100)}%";
			}
			return null;
		}


		// Convert back.
		public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is string stringValue)
			{
				var length = stringValue.Length;
				if (length < 2 || stringValue[length - 1] != '%')
					return null;
				stringValue = stringValue.Substring(0, length - 1);
				if (int.TryParse(stringValue, out var intValue))
					return (intValue / 100.0);
			}
			return null;
		}
	}
}
