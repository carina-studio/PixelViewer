using System.IO;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Source of raw image data.
	/// </summary>
	interface IImageDataSource : ISharableDisposable<IImageDataSource>
	{
		/// <summary>
		/// Open <see cref="Stream"/> to read raw image data.
		/// </summary>
		/// <returns><see cref="Stream"/>.</returns>
		Stream Open();


		/// <summary>
		/// Size of data in bytes.
		/// </summary>
		long Size { get; }
	}
}
