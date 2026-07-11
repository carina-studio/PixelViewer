using CarinaStudio;
using CarinaStudio.IO;
using System;
using System.Collections.Generic;
using System.IO;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Predefined file formats.
/// </summary>
static class FileFormats
{
    // Fields.
    static volatile IApplication? app;
    static volatile FileFormat? arw;
    static volatile FileFormat? bmp;
    static volatile FileFormat? cr2;
    static volatile FileFormat? dng;
    static readonly ISet<FileFormat> emptyFormats = new HashSet<FileFormat>().AsReadOnly();
    static readonly Dictionary<string, ISet<FileFormat>> formatsByExtensions = new(PathEqualityComparer.Default);
    static readonly Dictionary<string, FileFormat> formatsById = new();
    static volatile FileFormat? heif;
    static volatile FileFormat? jpeg;
    static volatile FileFormat? nef;
    static volatile FileFormat? png;
    static volatile FileFormat? rawBgra;
    static volatile FileFormat? tiff;
    static volatile FileFormat? yuv4mpeg2;
    static volatile FileFormat? webP;
    
    
    /// <summary>
    /// Sony RAW format.
    /// </summary>
    public static FileFormat Arw => arw ?? throw new InvalidOperationException("File format is not ready yet.");


    /// <summary>
    /// Windows Bitmap.
    /// </summary>
    public static FileFormat Bmp => bmp ?? throw new InvalidOperationException("File format is not ready yet.");
    
    
    /// <summary>
    /// Canon RAW v2.
    /// </summary>
    public static FileFormat Cr2 => cr2 ?? throw new InvalidOperationException("File format is not ready yet.");


    /// <summary>
    /// Digital Negative (DNG).
    /// </summary>
    public static FileFormat Dng => dng ?? throw new InvalidOperationException("File format is not ready yet.");


    /// <summary>
    /// Get all defined formats.
    /// </summary>
    public static IEnumerable<FileFormat> Formats => formatsById.Values;


    /// <summary>
    /// High Efficiency Image File Format (HEIF).
    /// </summary>
    public static FileFormat Heif => heif ?? throw new InvalidOperationException("File format is not ready yet.");


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
        arw = Register(new FileFormat(app, "Arw", [ ".arw" ]));
        bmp = Register(new FileFormat(app, "Bmp", [ ".bmp" ]));
        cr2 = Register(new FileFormat(app, "Cr2", [ ".cr2" ]));
        dng = Register(new FileFormat(app, "Dng", [ ".dng" ]));
        heif = Register(new FileFormat(app, "Heif", [ ".heif", ".heic" ]));
        jpeg = Register(new FileFormat(app, "Jpeg", [ ".jpg", ".jpeg", ".jpe", ".jfif" ]));
        nef = Register(new FileFormat(app, "Nef", [ ".nef" ]));
        png = Register(new FileFormat(app, "Png", [ ".png" ]));
        rawBgra = Register(new FileFormat(app, "RawBgra", [ ".bgra" ]));
        tiff = Register(new FileFormat(app, "Tiff", [ ".tif", ".tiff" ]));
        yuv4mpeg2 = Register(new FileFormat(app, "Yuv4Mpeg2", [ ".y4m" ]));
        webP = Register(new FileFormat(app, "WebP", [ ".webp" ]));
    }


    /// <summary>
    /// JPEG.
    /// </summary>
    public static FileFormat Jpeg => jpeg ?? throw new InvalidOperationException("File format is not ready yet.");
    
    
    /// <summary>
    /// NEF.
    /// </summary>
    public static FileFormat Nef => nef ?? throw new InvalidOperationException("File format is not ready yet.");


    /// <summary>
    /// Portable Network Graphic (PNG).
    /// </summary>
    public static FileFormat Png => png ?? throw new InvalidOperationException("File format is not ready yet.");


    /// <summary>
    /// Raw BGRA data.
    /// </summary>
    public static FileFormat RawBgra => rawBgra ?? throw new InvalidOperationException("File format is not ready yet.");


    /// <summary>
    /// Tag Image File Format (TIFF).
    /// </summary>
    public static FileFormat Tiff => tiff ?? throw new InvalidOperationException("File format is not ready yet.");


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
        if (formatsByExtensions.TryGetValue(Path.GetExtension(fileName).ToLower(), out var result))
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
    public static FileFormat Yuv4Mpeg2 => yuv4mpeg2 ?? throw new InvalidOperationException("File format is not ready yet.");
    
    
    /// <summary>
    /// WebP.
    /// </summary>
    public static FileFormat WebP => webP ?? throw new InvalidOperationException("File format is not ready yet.");
}
