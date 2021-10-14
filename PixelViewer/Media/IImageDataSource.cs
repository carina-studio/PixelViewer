using CarinaStudio;
using CarinaStudio.IO;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Source of raw image data.
	/// </summary>
	interface IImageDataSource : IShareableDisposable<IImageDataSource>, IStreamProvider
	{
		/// <summary>
		/// Size of data in bytes.
		/// </summary>
		long Size { get; }
	}
}
