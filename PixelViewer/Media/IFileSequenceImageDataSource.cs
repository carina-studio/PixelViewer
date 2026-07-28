using System.Collections.Generic;

namespace Carina.PixelViewer.Media;

/// <summary>
/// <see cref="IMultiFrameImageDataSource"/> which is backed by a sequence of files, one file per frame.
/// </summary>
interface IFileSequenceImageDataSource : IMultiFrameImageDataSource
{
	/// <summary>
	/// Get names of files which provide data of frames, ordered by frame index.
	/// </summary>
	IList<string> FileNames { get; }
}
