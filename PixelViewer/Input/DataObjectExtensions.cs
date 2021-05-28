using Avalonia.Input;
using CarinaStudio;

namespace Carina.PixelViewer.Input
{
	/// <summary>
	/// Extensions for <see cref="IDataObject"/>.
	/// </summary>
	static class DataObjectExtensions
	{
		/// <summary>
		/// Get the only file name contained in <see cref="IDataObject"/>.
		/// </summary>
		/// <param name="data"><see cref="IDataObject"/>.</param>
		/// <returns>File name contained in <see cref="IDataObject"/>, or null if no file name or more than one file names are contained.</returns>
		public static string? GetSingleFileName(this IDataObject data) => data.GetFileNames()?.Let((fileNames) =>
		{
			string? fileName = null;
			foreach (var candidate in fileNames)
			{
				if (fileName == null)
					fileName = candidate;
				else
					return null;
			}
			return fileName;
		});


		/// <summary>
		/// Check whether only one file name is contained in <see cref="IDataObject"/> or not.
		/// </summary>
		/// <param name="data"><see cref="IDataObject"/>.</param>
		/// <returns>True if only one file name is contained in <see cref="IDataObject"/>.</returns>
		public static bool HasSingleFileName(this IDataObject data) => data.GetFileNames()?.Let((fileNames) =>
		{
			var count = 0;
			foreach (var _ in fileNames)
			{
				++count;
				if (count > 1)
					return false;
			}
			return (count == 1);
		}) ?? false;
	}
}
