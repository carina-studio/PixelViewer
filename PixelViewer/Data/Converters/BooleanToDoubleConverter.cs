using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
	/// <summary>
	/// <see cref="IValueConverter"/> to convert from <see cref="bool"/> to <see cref="double"/>.
	/// </summary>
	class BooleanToDoubleConverter : BooleanToValueConverter<double>
	{
		/// <summary>
		/// Default instance which maps <see cref="bool"/> to 0.0 (False) and 1.0 (True).
		/// </summary>
		public static readonly BooleanToDoubleConverter Default = new BooleanToDoubleConverter(1.0, 0.0);


		/// <summary>
		/// Initialize new <see cref="BooleanToDoubleConverter"/> instance.
		/// </summary>
		/// <param name="trueValue"><see cref="double"/> value represents True.</param>
		/// <param name="falseValue"><see cref="double"/> value represents False.</param>
		public BooleanToDoubleConverter(double trueValue, double falseValue) : base(trueValue, falseValue)
		{
			if (double.IsNaN(trueValue) || double.IsNaN(falseValue))
				throw new ArgumentException("Cannot map to NaN.");
			if (double.IsInfinity(trueValue) || double.IsInfinity(falseValue))
				throw new ArgumentException("Cannot map to infinity.");
		}


		// Convert.
		public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (!(value is bool boolValue) || !boolValue)
				return this.FalseValue;
			return this.TrueValue;
		}


		// Convert back.
		public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (!(value is double doubleValue))
				return false;
			return Math.Abs(doubleValue - this.TrueValue) <= 0.001;
		}
	}
}
