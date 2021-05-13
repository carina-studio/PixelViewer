using System;
using System.Collections.Generic;
using System.Text;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Extensions for array.
	/// </summary>
	static class ArrayExtensions
	{
		/// <summary>
		/// Check whether given array is an empty array or not.
		/// </summary>
		/// <typeparam name="T">Type of array.</typeparam>
		/// <param name="array">Array to check.</param>
		/// <returns>True if given array is an empty array.</returns>
		public static bool IsEmpty<T>(this T[] array) => array.Length == 0;


		/// <summary>
		/// Check whether given array is not an empty array or not.
		/// </summary>
		/// <typeparam name="T">Type of array.</typeparam>
		/// <param name="array">Array to check.</param>
		/// <returns>True if given array is not an empty array.</returns>
		public static bool IsNotEmpty<T>(this T[]? array) => array != null && array.Length > 0;


		/// <summary>
		/// Check whether given array is either null or an empty array.
		/// </summary>
		/// <typeparam name="T">Type of array.</typeparam>
		/// <param name="array">Array to check.</param>
		/// <returns>True if given array is either null or an empty array.</returns>
		public static bool IsNullOrEmpty<T>(this T[]? array) => array == null || array.Length == 0;
	}
}
