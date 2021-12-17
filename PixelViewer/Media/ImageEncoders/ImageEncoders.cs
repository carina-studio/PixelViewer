using CarinaStudio.Collections;
using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.Media.ImageEncoders
{
    /// <summary>
    /// Manager of <see cref="IImageEncoder"/>s.
    /// </summary>
    static class ImageEncoders
    {
        // Fields.
        static readonly Dictionary<FileFormat, IImageEncoder> encodersByFormat = new Dictionary<FileFormat, IImageEncoder>();
        static readonly Dictionary<string, IImageEncoder> encodersByName = new Dictionary<string, IImageEncoder>();


        // Static initializer.
        static ImageEncoders()
        {
            All = new IImageEncoder[]
            {
                new JpegImageEncoder(),
                new PngImageEncoder(),
                new RawBgraImageEncoder(),
            }.AsReadOnly();
            foreach(var encoder in All)
            {
                encodersByFormat[encoder.Format] = encoder;
                encodersByName[encoder.Name] = encoder;
            }
        }


        /// <summary>
        /// Get all supported encoders.
        /// </summary>
        public static IList<IImageEncoder> All { get; }


        /// <summary>
        /// Try get <see cref="IImageEncoder"/> by supported format.
        /// </summary>
        /// <param name="format">File format.</param>
        /// <param name="encoder">Found <see cref="IImageEncoder"/>.</param>
        /// <returns>True if <see cref="IImageEncoder"/> found.</returns>
        public static bool TryGetEncoderByFormat(FileFormat format, out IImageEncoder? encoder) =>
            encodersByFormat.TryGetValue(format, out encoder);


        /// <summary>
        /// Try get <see cref="IImageEncoder"/> by name.
        /// </summary>
        /// <param name="name">Name of encoder.</param>
        /// <param name="encoder">Found <see cref="IImageEncoder"/>.</param>
        /// <returns>True if <see cref="IImageEncoder"/> found.</returns>
        public static bool TryGetEncoderByName(string name, out IImageEncoder? encoder) =>
            encodersByName.TryGetValue(name, out encoder);
    }
}
