using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Carina.PixelViewer.Data.Converters
{
	/// <summary>
	/// <see cref="IValueConverter"/> to convert from <see cref="bool"/> to specific value type.
	/// </summary>
	class BooleanToValueConverter<TValue> : IValueConverter
	{
		/// <summary>
		/// Initialize new <see cref="BooleanToDoubleConverter"/> instance.
		/// </summary>
		/// <param name="trueValue"><see cref="TValue"/> value represents True.</param>
		/// <param name="falseValue"><see cref="TValue"/> value represents False.</param>
		public BooleanToValueConverter(TValue trueValue, TValue falseValue)
		{
			this.TrueValue = trueValue;
			this.FalseValue = falseValue;
		}


		// Convert.
		public virtual object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (!(value is bool boolValue) || !boolValue)
				return this.FalseValue;
			return this.TrueValue;
		}


		// Convert back.
		public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (!(value is TValue targetValue))
				return false;
			if (targetValue is IEquatable<TValue> equatable)
				return equatable.Equals(this.TrueValue);
			if (targetValue == null)
				return this.TrueValue == null;
			return targetValue.Equals(this.TrueValue);
		}


		/// <summary>
		/// Get <see cref="TValue"/> value represents False.
		/// </summary>
		public TValue FalseValue { get; }


		/// <summary>
		/// Get <see cref="TValue"/> value represents True.
		/// </summary>
		public TValue TrueValue { get; }
	}
}
