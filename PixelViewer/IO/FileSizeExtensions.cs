
namespace Carina.PixelViewer.IO
{
	/// <summary>
	/// Extensions of operations for file size.
	/// </summary>
	static class FileSizeExtensions
	{
		// Constants.
		const long ByteCountGB = 1L << 30;
		const long ByteCountKB = 1L << 10;
		const long ByteCountMB = 1L << 20;
		const long ByteCountTB = 1L << 40;


		/// <summary>
		/// Convert to readable file size string.
		/// </summary>
		/// <param name="size">File size in bytes.</param>
		/// <returns>File size string.</returns>
		public static string ToFileSizeString(this int size) => ((long)size).ToFileSizeString();


		/// <summary>
		/// Convert to readable file size string.
		/// </summary>
		/// <param name="size">File size in bytes.</param>
		/// <returns>File size string.</returns>
		public static string ToFileSizeString(this long size)
		{
			if (size >= ByteCountTB || size <= -ByteCountTB)
				return $"{(double)size / ByteCountTB:.00} TB";
			if (size >= ByteCountGB || size <= -ByteCountGB)
				return $"{(double)size / ByteCountGB:.00} GB";
			if (size >= ByteCountMB || size <= -ByteCountMB)
				return $"{(double)size / ByteCountMB:.00} MB";
			if (size >= ByteCountKB || size <= -ByteCountKB)
				return $"{(double)size / ByteCountKB:.00} KB";
			return $"{size} B";
		}
	}
}
