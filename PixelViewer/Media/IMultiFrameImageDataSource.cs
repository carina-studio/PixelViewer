using CarinaStudio.IO;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// <see cref="IImageDataSource"/> which is composed of multiple frames, each frame being served independently.
	/// </summary>
	interface IMultiFrameImageDataSource : IImageDataSource
	{
		/// <summary>
		/// Get the file name of the currently selected frame.
		/// </summary>
		string CurrentFileName { get; }

		/// <summary>
		/// Get number of frames contained in the source.
		/// </summary>
		int FrameCount { get; }

		/// <summary>
		/// Select the frame whose data will be served by <see cref="IImageDataSource.Size"/> and
		/// <see cref="IStreamProvider.OpenStreamAsync"/>.
		/// </summary>
		/// <param name="frameIndex">0-based index of frame to select.</param>
		void SelectFrame(int frameIndex);
	}
}
