using System;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Extensions for <see cref="IDisposable"/>.
	/// </summary>
	static class DisposableExtensions
	{
		/// <summary>
		/// Call <see cref="IDisposable.Dispose"/> and return null.
		/// </summary>
		/// <param name="disposable"><see cref="IDisposable.Dispose"/>.</param>
		/// <returns>Null.</returns>
		public static T? DisposeAndReturnNull<T>(this T? disposable) where T : class, IDisposable
		{
			disposable?.Dispose();
			return null;
		}


		/// <summary>
		/// Exhange the source <see cref="IDisposable"/> with another one.
		/// </summary>
		/// <param name="source">Source <see cref="IDisposable"/>.</param>
		/// <param name="func">Exchanging function.</param>
		/// <returns>Exchanged <see cref="IDisposable"/>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static R Exchange<T, R>(this T source, Func<R> func) where T : IDisposable where R : IDisposable
		{
			R result = default;
			try
			{
				result = func();
			}
			finally
			{
				if (!object.ReferenceEquals(source, result))
					source.Dispose();
			}
			return result;
		}


		/// <summary>
		/// Exhange the source <see cref="IDisposable"/> with another one.
		/// </summary>
		/// <param name="source">Source <see cref="IDisposable"/>.</param>
		/// <param name="func">Exchanging function.</param>
		/// <returns>Exchanged <see cref="IDisposable"/>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static R Exchange<T, R>(this T source, Func<T, R> func) where T : IDisposable where R : IDisposable
		{
			R result = default;
			try
			{
				result = func(source);
			}
			finally
			{
				if (!object.ReferenceEquals(source, result))
					source.Dispose();
			}
			return result;
		}


		/// <summary>
		/// Use the given <see cref="IDisposable"/> to generate value then dispose it before returning from method.
		/// </summary>
		/// <param name="disposable"><see cref="IDisposable"/>.</param>
		/// <param name="func">Using function.</param>
		/// <returns>Generated value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static R Use<T, R>(this T disposable, Func<T, R> func) where T : IDisposable
		{
			try
			{
				return func(disposable);
			}
			finally
			{
				disposable.Dispose();
			}
		}
	}
}
