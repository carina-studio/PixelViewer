using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.Text;

namespace Carina.PixelViewer.Data.Converters
{
	/// <summary>
	/// <see cref="IValueConverter"/> which converts degrees to readable string.
	/// </summary>
	class DegreesToStringConverter : IValueConverter
	{
		/// <summary>
		/// Default instance.
		/// </summary>
		public static readonly DegreesToStringConverter Default = new DegreesToStringConverter();


		// Fields.
		readonly string format;


		/// <summary>
		/// Initialize new <see cref="DegreesToStringConverter"/> instance.
		/// </summary>
		/// <param name="decimalPlaces">Decimal places.</param>
		public DegreesToStringConverter(int decimalPlaces = 0)
		{
			if (decimalPlaces < 0)
				throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
			if (decimalPlaces == 0)
				this.format = "{0:0}°";
			else
			{
				var formatBuilder = new StringBuilder("{0:0.");
				for (var i = decimalPlaces; i > 0; --i)
					formatBuilder.Append('0');
				formatBuilder.Append("}°");
				this.format = formatBuilder.ToString();
			}
		}


		// Convert.
		public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is double doubleValue)
				return string.Format(this.format, doubleValue);
			if (value is float floatValue)
				return string.Format(this.format, floatValue);
			if (value is IConvertible convertable)
				return $"{convertable.ToInt32(null)}°";
			return null;
		}


		// Convert back.
		public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is string stringValue)
			{
				var length = stringValue.Length;
				if (length < 2 || stringValue[length - 1] != '°')
					return null;
				stringValue = stringValue.Substring(0, length - 1);
				if (int.TryParse(stringValue, out var intValue))
					return intValue;
				if (double.TryParse(stringValue, out var doubleValue))
					return doubleValue;
			}
			return null;
		}
	}
}
