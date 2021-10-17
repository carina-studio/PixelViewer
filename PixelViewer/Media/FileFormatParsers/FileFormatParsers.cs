using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.IO;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers
{
    /// <summary>
    /// Predefined <see cref="IFileFormatParser"/>s.
    /// </summary>
    static class FileFormatParsers
    {
        // Fields.
        static volatile ILogger? logger;
        static readonly Dictionary<FileFormat, IFileFormatParser> parsers = new Dictionary<FileFormat, IFileFormatParser>();


        /// <summary>
        /// Initialize.
        /// </summary>
        public static void Initialize(IApplication app)
        {
            lock (typeof(FileFormatParsers))
            {
                if (logger != null)
                    throw new InvalidOperationException();
                logger = app.LoggerFactory.CreateLogger(nameof(FileFormatParsers));
            }
            new Yuv4Mpeg2FileFormatParser().Let(it => parsers[it.FileFormat] = it);
        }


        /// <summary>
        /// Parse <see cref="Profiles.ImageRenderingProfile"/> from file asynchronously.
        /// </summary>
        /// <param name="source">Image data source.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of parsing.</returns>
        public static async Task<Profiles.ImageRenderingProfile> ParseImageRenderingProfileAsync(IImageDataSource source, CancellationToken cancellationToken)
        {
            // parse by file name
            var remainingParsers = new HashSet<IFileFormatParser>(parsers.Values);
            if (source is FileImageDataSource fileSource
                && FileFormats.TryGetFormatsByFileName(fileSource.FileName, out var fileFormats))
            {
                foreach (var fileFormat in fileFormats)
                {
                    if (parsers.TryGetValue(fileFormat, out var parser) && parser != null)
                    {
                        remainingParsers.Remove(parser);
                        try
                        {
                            return await parser.ParseImageRenderingProfileAsync(source, cancellationToken);
                        }
                        catch
                        {
                            if (cancellationToken.IsCancellationRequested)
                                throw new TaskCanceledException();
                        }
                    }
                }
            }
            if (remainingParsers.IsEmpty())
                throw new ArgumentException("Unable to parse.");

            // open stream
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

            // parse by each parser
            try
            {
                foreach (var parser in parsers.Values)
                {
                    if (!remainingParsers.Remove(parser))
                        continue;
                    try
                    {
                        return await parser.ParseImageRenderingProfileAsync(source, cancellationToken);
                    }
                    catch
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw new TaskCanceledException();
                    }
                    finally
                    {
                        await Task.Run(() => stream.Position = 0);
                    }
                }
            }
            finally
            {
                Global.RunWithoutErrorAsync(stream.Dispose);
            }

            // unable to parse
            throw new ArgumentException("Unable to parse.");
        }
    }
}
