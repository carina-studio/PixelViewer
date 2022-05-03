using Avalonia;
using CarinaStudio;
using CarinaStudio.IO;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Source of video data.
/// </summary>
interface IVideoDataSource : IShareableDisposable<IVideoDataSource>, IStreamProvider
{
    /// <summary>
    /// Get duration of video.
    /// </summary>
    TimeSpan Duration { get; }


    /// <summary>
    /// Get byte ordering of raw data of video frame.
    /// </summary>
    ByteOrdering? FrameByteOrdering { get; }


    /// <summary>
    /// Get color space of video frame.
    /// </summary>
    ColorSpace? FrameColorSpace { get; }


    /// <summary>
    /// Get format of raw video frame.
    /// </summary>
    ImageFormat? FrameFormat { get; }


    /// <summary>
    /// Get video frame rate in FPS.
    /// </summary>
    double FrameRate { get; }


    /// <summary>
    /// Get dimension of video frame.
    /// </summary>
    PixelSize FrameSize { get; }


    /// <summary>
    /// Get video frame asynchronously.
    /// </summary>
    /// <param name="position">Position of video frame.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of getting video frame.</returns>
    Task<IImageDataSource> GetFrameAsync(TimeSpan position, CancellationToken cancellationToken = default);
}