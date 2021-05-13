using System.Diagnostics.CodeAnalysis;

namespace Carina.PixelViewer
{
	/// <summary>
	/// <see cref="ObservableValue{T}"/> which supports updating value from outside of instance.
	/// </summary>
	class MutableObservableValue<T> : ObservableValue<T>
	{
		/// <summary>
		/// Initialize new <see cref="MutableObservableValue{T}"/> instance.
		/// </summary>
		/// <param name="initValue">Initial value.</param>
		public MutableObservableValue([AllowNull] T initValue = default) : base(initValue)
		{ }


		/// <summary>
		/// Update value.
		/// </summary>
		/// <param name="value">New value.</param>
		public void Update(T value) => this.Value = value;
	}
}
