using CarinaStudio.IO;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageEncoders
{
    /// <summary>
    /// IMage encoder.
    /// </summary>
    interface IImageEncoder
    {
        /// <summary>
        /// Encode image asynchronously.
        /// </summary>
        /// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> contains the data of image to be encoded.</param>
        /// <param name="outputStreamProvider"><see cref="IStreamProvider"/> to provide stream to output encoded data.</param>
        /// <param name="options">Options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of encoding.</returns>
        Task EncodeAsync(IBitmapBuffer bitmapBuffer, IStreamProvider outputStreamProvider, ImageEncodingOptions options, CancellationToken cancellationToken);


        /// <summary>
        /// Get file format which supported by this encoder.
        /// </summary>
        FileFormat Format { get; }


        /// <summary>
        /// Get unique name of encoder.
        /// </summary>
        string Name { get; }
    }


    /// <summary>
    /// Options to encode image.
    /// </summary>
    struct ImageEncodingOptions
    {
        /// <summary>
        /// Color space.
        /// </summary>
        public ColorSpace? ColorSpace { get; set; }


        /// <summary>
        /// Orientation to be applied on saved image.
        /// </summary>
        public int Orientation { get; set; }


        /// <summary>
        /// Quality level for encoding. Range is [1, 100].
        /// </summary>
        public int QualityLevel { get; set; }
    }
}
