using Avalonia.Data.Converters;
using Carina.PixelViewer.Media;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
	/// <summary>
	/// <see cref="IValueConverter"/> to convert from <see cref="YuvConversionMode"/> to readable string.
	/// </summary>
	class YuvConversionModeToStringConverter : IValueConverter
	{
		/// <summary>
		/// Default instance.
		/// </summary>
		public static readonly YuvConversionModeToStringConverter Default = new YuvConversionModeToStringConverter();


		// Convert.
		public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if(value is YuvConversionMode conversionMode)
			{
				return conversionMode switch
				{
					YuvConversionMode.ITU_R => App.Current.GetString("YuvConversionMode.ITU_R"),
					YuvConversionMode.NTSC => App.Current.GetString("YuvConversionMode.NTSC"),
					_ => null,
				};
			}
			return null;
		}


		// Convert back.
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is string stringValue)
			{
				if (stringValue.Contains("ITU-R"))
					return YuvConversionMode.ITU_R;
			}
			return YuvConversionMode.NTSC;
		}
	}
}
