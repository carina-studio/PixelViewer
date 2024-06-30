using CarinaStudio;
using CarinaStudio.IO;
using CarinaStudio.Threading.Tasks;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageEncoders;

/// <summary>
/// Base implementation of <see cref="IImageEncoder"/>.
/// </summary>
abstract class BaseImageEncoder : IImageEncoder
{
    // Static fields.
    static readonly TaskFactory EncodingTaskFactory = new TaskFactory(new FixedThreadsTaskScheduler(1));


    /// <summary>
    /// Initialize new <see cref="BaseImageEncoder"/> instance.
    /// </summary>
    /// <param name="name">Name of encoder.</param>
    /// <param name="format">Supported format.</param>
    protected BaseImageEncoder(string name, FileFormat format)
    {
        this.Format = format;
        this.Name = name;
    }


    /// <inheritdoc/>
    public async Task EncodeAsync(IBitmapBuffer bitmapBuffer, IStreamProvider outputStreamProvider, ImageEncodingOptions options, CancellationToken cancellationToken)
    {
        // open stream
        using var sharedBitmapBuffer = bitmapBuffer.Share();
        var stream = await outputStreamProvider.OpenStreamAsync(StreamAccess.ReadWrite, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            Global.RunWithoutErrorAsync(() => stream.Close());
            throw new TaskCanceledException();
        }

        // encode
        try
        {
            await EncodingTaskFactory.StartNew(() => this.OnEncode(sharedBitmapBuffer, stream, options, cancellationToken), cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not TaskCanceledException && cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            throw;
        }
        finally
        {
            Global.RunWithoutErrorAsync(() => stream.Close());
        }
    }


    /// <inheritdoc/>
    public FileFormat Format { get; }


    /// <inheritdoc/>
    public string Name { get; }


    /// <summary>
    /// Called to encode image and write to stream.
    /// </summary>
    /// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> contains data of image to be encoded.</param>
    /// <param name="stream"><see cref="Stream"/> to write encoded data to.</param>
    /// <param name="options">Options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract void OnEncode(IBitmapBuffer bitmapBuffer, Stream stream, ImageEncodingOptions options, CancellationToken cancellationToken);
}
