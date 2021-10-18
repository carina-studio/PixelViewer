using CarinaStudio;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers
{
    /// <summary>
    /// Parser of file format.
    /// </summary>
    interface IFileFormatParser : IApplicationObject
    {
        /// <summary>
        /// Get file format supported by the parser.
        /// </summary>
        FileFormat FileFormat { get; }


        /// <summary>
        /// Parse <see cref="Profiles.ImageRenderingProfile"/> from file asynchronously.
        /// </summary>
        /// <param name="source">Image data source.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of parsing.</returns>
        Task<Profiles.ImageRenderingProfile?> ParseImageRenderingProfileAsync(IImageDataSource source, CancellationToken cancellationToken);
    }
}
