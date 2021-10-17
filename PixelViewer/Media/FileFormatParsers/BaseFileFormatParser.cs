using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using CarinaStudio.IO;
using CarinaStudio.Threading;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers
{
    /// <summary>
    /// Base implementation of <see cref="IFileFormatParser"/>.
    /// </summary>
    abstract class BaseFileFormatParser : IFileFormatParser
    {
        /// <summary>
        /// Initialize new <see cref="BaseFileFormatParser"/> instance.
        /// </summary>
        /// <param name="format">Supported file format.</param>
        protected BaseFileFormatParser(FileFormat format)
        {
            this.Application = format.Application;
            this.FileFormat = format;
        }


        /// <inheritdoc/>
        public IApplication Application { get; }


        /// <inheritdoc/>
        public bool CheckAccess() => this.Application.CheckAccess();


        /// <inheritdoc/>
        public FileFormat FileFormat { get; }


        /// <inheritdoc/>
        public SynchronizationContext SynchronizationContext => this.Application.SynchronizationContext;


        /// <inheritdoc/>
        public async Task<ImageRenderingProfile> ParseImageRenderingProfileAsync(IImageDataSource source, CancellationToken cancellationToken)
        {
            // open stream
            this.VerifyAccess();
            var stream = (Stream?)null;
            try
            {
                stream = await source.OpenStreamAsync(StreamAccess.Read, cancellationToken);
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                throw;
            }

            // parse
            try
            {
                return await ParseImageRenderingProfileAsyncCore(stream, cancellationToken);
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                throw;
            }
            finally
            {
                Global.RunWithoutErrorAsync(stream.Close);
            }
        }


        /// <summary>
        /// Called to parse <see cref="ImageRenderingProfile"/> from source.
        /// </summary>
        /// <param name="stream">Stream to read data.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of parsing <see cref="ImageRenderingProfile"/>.</returns>
        protected abstract Task<ImageRenderingProfile> ParseImageRenderingProfileAsyncCore(Stream stream, CancellationToken cancellationToken);


        /// <summary>
        /// Throw <see cref="ArgumentException"/> for invalid file format.
        /// </summary>
        protected void ThrowInvalidFormatException() => throw new ArgumentException("Invalid format.");
    }
}
