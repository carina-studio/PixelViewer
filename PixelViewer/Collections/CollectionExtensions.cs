using System;
using System.Collections.Generic;
using System.Text;

namespace Carina.PixelViewer.Collections
{
	/// <summary>
	/// Extensions for <see cref="ICollection{T}"/> and related types.
	/// </summary>
	static class CollectionExtensions
	{
		/// <summary>
		/// Find given element by binary-search.
		/// </summary>
		/// <param name="list">List to find element.</param>
		/// <param name="element">ELement to be found.</param>
		/// <param name="comparer"><see cref="IComparer{T}"/> to compare elements.</param>
		/// <returns>Index of found element, or bitwise complement of index of proper position to put element.</returns>
		public static int BinarySearch<T>(this IList<T> list, T element, IComparer<T> comparer) => BinarySearch<T>(list, 0, list.Count, element, comparer.Compare);


		/// <summary>
		/// Find given element by binary-search.
		/// </summary>
		/// <param name="list">List to find element.</param>
		/// <param name="element">ELement to be found.</param>
		/// <param name="comparison">Comparison function.</param>
		/// <returns>Index of found element, or bitwise complement of index of proper position to put element.</returns>
		public static int BinarySearch<T>(this IList<T> list, T element, Comparison<T> comparison) => BinarySearch<T>(list, 0, list.Count, element, comparison);


		/// <summary>
		/// Find given element by binary-search.
		/// </summary>
		/// <param name="list">List to find element.</param>
		/// <param name="element">ELement to be found.</param>
		/// <returns>Index of found element, or bitwise complement of index of proper position to put element.</returns>
		public static int BinarySearch<T>(this IList<T> list, T element) where T : IComparable<T> => BinarySearch<T>(list, 0, list.Count, element);


		// Binary search.
		static int BinarySearch<T>(IList<T> list, int start, int end, T element, Comparison<T> comparison)
		{
			if (start >= end)
				return ~start;
			var middle = (start + end) / 2;
			var result = comparison(element, list[middle]);
			if (result == 0)
				return middle;
			if (result < 0)
				return BinarySearch<T>(list, start, middle, element, comparison);
			return BinarySearch<T>(list, middle + 1, end, element, comparison);
		}
		static int BinarySearch<T>(IList<T> list, int start, int end, T element) where T : IComparable<T>
		{
			if (start >= end)
				return ~start;
			var middle = (start + end) / 2;
			var result = element.CompareTo(list[middle]);
			if (result == 0)
				return middle;
			if (result < 0)
				return BinarySearch<T>(list, start, middle, element);
			return BinarySearch<T>(list, middle + 1, end, element);
		}


		/// <summary>
		/// Generate readable string represents content in <see cref="IEnumerable{T}"/>.
		/// </summary>
		/// <typeparam name="T">Type of element.</typeparam>
		/// <param name="enumerable"><see cref="IEnumerable{T}"/>.</param>
		/// <returns>Readable string represents content.</returns>
		public static string ContentToString<T>(this IEnumerable<T> enumerable)
		{
			var stringBuilder = new StringBuilder("[");
			foreach (var element in enumerable)
			{
				if (stringBuilder.Length > 1)
					stringBuilder.Append(", ");
				if (element is string)
				{
					stringBuilder.Append('"');
					stringBuilder.Append(element.ToString());
					stringBuilder.Append('"');
				}
				else if (element != null)
					stringBuilder.Append(element.ToString());
				else
					stringBuilder.Append("Null");
			}
			stringBuilder.Append(']');
			return stringBuilder.ToString();
		}


		/// <summary>
		/// Find the first element which matches the given condition in <see cref="IEnumerable{T}"/>.
		/// </summary>
		/// <typeparam name="T">Type of element.</typeparam>
		/// <param name="enumerable"><see cref="IEnumerable{T}"/>.</param>
		/// <param name="predicate"><see cref="Predicate{T}"/> to check whether element matches the condition or not.</param>
		/// <returns>The first element which matches the given condition.</returns>
		public static T? Find<T>(this IEnumerable<T> enumerable, Predicate<T> predicate)
		{
			if (enumerable is IList<T> list)
			{
				for (int i = 0, count = list.Count; i < count; ++i)
				{
					var element = list[i];
					if (predicate(element))
						return element;
				}
			}
			else
			{
				foreach (var element in enumerable)
				{
					if (predicate(element))
						return element;
				}
			}
			return default;
		}


		/// <summary>
		/// Check whether collection is empty or not.
		/// </summary>
		/// <param name="collection"><see cref="ICollection{T}"/>.</param>
		/// <returns>True if collection is empty.</returns>
		public static bool IsEmpty<T>(this ICollection<T> collection) => collection.Count == 0;


		/// <summary>
		/// Check whether collection is not empty or not.
		/// </summary>
		/// <param name="collection"><see cref="ICollection{T}"/>.</param>
		/// <returns>True if collection is not empty.</returns>
		public static bool IsNotEmpty<T>(this ICollection<T>? collection) => collection != null && collection.Count > 0;


		/// <summary>
		/// Check whether collection is either null or empty.
		/// </summary>
		/// <param name="collection"><see cref="ICollection{T}"/>.</param>
		/// <returns>True if collection is either null or empty.</returns>
		public static bool IsNullOrEmpty<T>(this ICollection<T>? collection) => collection == null || collection.Count == 0;
	}
}
