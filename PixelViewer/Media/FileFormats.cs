using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.IO;
using System;
using System.Collections.Generic;
using System.IO;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// Predefined file formats.
    /// </summary>
    static class FileFormats
    {
        // Fields.
        static volatile IApplication? app;
        static volatile FileFormat? bmp;
        static readonly ISet<FileFormat> emptyFormats = new HashSet<FileFormat>().AsReadOnly();
        static readonly Dictionary<string, ISet<FileFormat>> formatsByExtensions = new Dictionary<string, ISet<FileFormat>>(PathEqualityComparer.Default);
        static readonly Dictionary<string, FileFormat> formatsById = new Dictionary<string, FileFormat>();
        static volatile FileFormat? jpeg;
        static volatile FileFormat? png;
        static volatile FileFormat? rawBgra;
        static volatile FileFormat? yuv4mpeg2;


        /// <summary>
        /// Windows Bitmap.
        /// </summary>
        public static FileFormat Bmp { get => bmp ?? throw new InvalidOperationException("File format is not ready yet."); }


        /// <summary>
        /// Get all defined formats.
        /// </summary>
        public static IEnumerable<FileFormat> Formats { get => formatsById.Values; }


        /// <summary>
        /// Initialize.
        /// </summary>
        /// <param name="app">Application.</param>
        public static void Initialize(IApplication app)
        {
            lock (typeof(FileFormats))
            {
                if (FileFormats.app != null)
                    throw new InvalidOperationException();
                FileFormats.app = app;
            }
            bmp = Register(new FileFormat(app, "Bmp", new string[] { ".bmp" }));
            jpeg = Register(new FileFormat(app, "Jpeg", new string[] { ".jpg", ".jpeg", ".jpe", ".jfif" }));
            png = Register(new FileFormat(app, "Png", new string[] { ".png" }));
            rawBgra = Register(new FileFormat(app, "RawBgra", new string[] { ".bgra" }));
            yuv4mpeg2 = Register(new FileFormat(app, "Yuv4Mpeg2", new string[] { ".y4m" }));
        }


        /// <summary>
        /// JPEG.
        /// </summary>
        public static FileFormat Jpeg { get => jpeg ?? throw new InvalidOperationException("File format is not ready yet."); }


        /// <summary>
        /// Portable Network Graphic (PNG).
        /// </summary>
        public static FileFormat Png { get => png ?? throw new InvalidOperationException("File format is not ready yet."); }


        /// <summary>
        /// Raw BGRA data.
        /// </summary>
        public static FileFormat RawBgra { get => rawBgra ?? throw new InvalidOperationException("File format is not ready yet."); }


        // Register format.
        static FileFormat Register(FileFormat format)
        {
            foreach (var extension in format.Extensions)
            {
                if (!formatsByExtensions.TryGetValue(extension, out var formatSet))
                {
                    formatSet = new HashSet<FileFormat>();
                    formatsByExtensions[extension] = formatSet;
                }
                formatSet.Add(format);
            }
            formatsById[format.Id] = format;
            return format;
        }


        // Get format by ID.
        public static bool TryGetFormatById(string id, out FileFormat? format) => formatsById.TryGetValue(id, out format);


        // Select formats by file name.
        public static bool TryGetFormatsByFileName(string fileName, out ISet<FileFormat> formats)
        {
            if (formatsByExtensions.TryGetValue(Path.GetExtension(fileName), out var result))
            {
                formats = result.AsReadOnly();
                return true;
            }
            formats = emptyFormats;
            return false;
        }


        /// <summary>
        /// YUV4MPEG2.
        /// </summary>
        public static FileFormat Yuv4Mpeg2 { get => yuv4mpeg2 ?? throw new InvalidOperationException("File format is not ready yet."); }
    }
}
