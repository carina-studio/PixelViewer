using System;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Extensions for <see cref="object"/>.
	/// </summary>
	static class ObjectExtensions
	{
		/// <summary>
		/// Perform action on given object and return it.
		/// </summary>
		/// <param name="obj">Object.</param>
		/// <param name="action">Action.</param>
		/// <returns>Object.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T Also<T>(this T obj, Action<T> action)
		{
			action(obj);
			return obj;
		}


		/// <summary>
		/// Make sure that object is non-null, or throw <see cref="NullReferenceException"/>.
		/// </summary>
		/// <param name="obj">Object.</param>
		/// <returns>Non-null object.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T EnsureNonNull<T>(this T? obj) where T : class => obj ?? throw new NullReferenceException();


		/// <summary>
		/// Perform action on given object.
		/// </summary>
		/// <param name="obj">Object.</param>
		/// <param name="action">Action.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Let<T>(this T obj, Action<T> action) => action(obj);


		/// <summary>
		/// Perform action on given object and return another one.
		/// </summary>
		/// <param name="obj">Object.</param>
		/// <param name="action">Action.</param>
		/// <returns>Another object.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static R Let<T, R>(this T obj, Func<T, R> action) => action(obj);
	}
}
