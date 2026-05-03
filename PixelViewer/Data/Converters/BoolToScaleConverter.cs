using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters;

/// <summary>
/// <see cref="IValueConverter"/> which converts <see cref="bool"/> to scale value (-1 when true, 1 when false).
/// </summary>
class BoolToScaleConverter : IValueConverter
{
	/// <summary>
	/// Default instance.
	/// </summary>
	public static readonly BoolToScaleConverter Default = new();


	// Convert.
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is true ? -1.0 : 1.0;


	// Convert back.
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value switch
		{
			double d => d < 0,
			float f => f < 0,
			_ => false,
		};
}
