using CarinaStudio.IO;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media;

/// <summary>
/// <see cref="IImageDataSource"/> which consists of multiple frames, data of each frame is provided by its own <see cref="IImageDataSource"/>.
/// </summary>
/// <remarks>The source provides no data by itself, <see cref="IStreamProvider.OpenStreamAsync"/> throws <see cref="InvalidOperationException"/> and <see cref="IImageDataSource.Size"/> reports the total size of all frames.</remarks>
interface IMultiFrameImageDataSource : IImageDataSource
{
	/// <summary>
	/// Get number of frames contained in the source.
	/// </summary>
	int FrameCount { get; }


	/// <summary>
	/// Get source which provides data of the given frame.
	/// </summary>
	/// <param name="frameIndex">0-based index of frame.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Task of getting source of data of the frame. The source should be disposed by caller.</returns>
	Task<IImageDataSource> GetFrameAsync(int frameIndex, CancellationToken cancellationToken);
}
