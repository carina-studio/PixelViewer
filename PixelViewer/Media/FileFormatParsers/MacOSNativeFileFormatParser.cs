using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.Native;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// Implementation of <see cref="IFileFormatParser"/> based-on macOS native API.
/// </summary>
abstract class MacOSNativeFileFormatParser : BaseFileFormatParser
{
    // Fields.
    readonly IImageRenderer imageRenderer;


    /// <summary>
    /// Initialize new <see cref="MacOSNativeFileFormatParser"/> instance.
    /// </summary>
    /// <param name="format">File format.</param>
    /// <param name="renderer">Image renderer.</param>
    protected MacOSNativeFileFormatParser(FileFormat format, IImageRenderer renderer) : base(format)
    { 
        this.imageRenderer = renderer;
    }


    /// <summary>
    /// Called to check file header.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if header of file is correct.</returns>
    protected abstract bool OnCheckFileHeader(Stream stream);


    /// <inheritdoc/>
    protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(Stream stream, CancellationToken cancellationToken)
    {
        // check file header first to prevent decoding image
        var position = stream.Position;
        if (!this.OnCheckFileHeader(stream))
            return null;
        stream.Position = position;

        // check data size
        var dataSize = Math.Max(0, stream.Length - position);
        if (dataSize > 256L << 20) // 256 MB
            return null;
        
        // load image
        var imageDataRef = IntPtr.Zero;
        var imageSourceRef = IntPtr.Zero;
        var imageRef = IntPtr.Zero;
        var iccProfileDataRef = IntPtr.Zero;
        try
        {
            // load data into memory
            imageDataRef = MacOS.CFDataCreateMutable(MacOS.CFAllocatorGetDefault(), dataSize);
            if (imageDataRef == IntPtr.Zero)
                throw new Exception($"Unable to allocate buffer with size {dataSize}.");
            var buffer = new byte[4096];
            unsafe
            {
                fixed (byte* bufferPtr = buffer)
                {
                    var readCount = stream.Read(buffer, 0, buffer.Length);
                    while (readCount > 0)
                    {
                        MacOS.CFDataAppendBytes(imageDataRef, new IntPtr(bufferPtr), readCount);
                        readCount = stream.Read(buffer, 0, buffer.Length);
                    }
                }
                }
            if ((long)MacOS.CFDataGetLength(imageDataRef) != dataSize)
                throw new Exception($"Inconsistent data size.");
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            
            // create image source
            imageSourceRef = MacOS.CGImageSourceCreateWithData(imageDataRef, IntPtr.Zero);
            if (imageSourceRef == IntPtr.Zero || MacOS.CGImageSourceGetStatus(imageSourceRef) != MacOS.CGImageSourceStatus.Complete)
                return null;
            var primaryImageIndex = MacOS.CGImageSourceGetPrimaryImageIndex(imageSourceRef);
            
            // load image
            imageRef = MacOS.CGImageSourceCreateImageAtIndex(imageSourceRef, primaryImageIndex, IntPtr.Zero);
            if (imageRef == IntPtr.Zero)
                throw new Exception($"Unable to load image.");
            
            // load ICC profile
            var colorSpaceFromIccProfile = (ColorSpace?)null;
            var colorSpaceRef = MacOS.CGImageGetColorSpace(imageRef);
            if (colorSpaceRef != IntPtr.Zero)
            {
                iccProfileDataRef = MacOS.CGColorSpaceCopyICCData(colorSpaceRef);
                if (iccProfileDataRef != IntPtr.Zero)
                {
                    var size = (int)MacOS.CFDataGetLength(iccProfileDataRef);
                    var iccData = new byte[size];
                    System.Runtime.InteropServices.Marshal.Copy(MacOS.CFDataGetBytePtr(iccProfileDataRef), iccData, 0, size);
                    try
                    {
                        using var iccDataStream = new MemoryStream(iccData);
                        colorSpaceFromIccProfile = await ColorSpace.LoadFromIccProfileAsync(iccDataStream, ColorSpaceSource.Embedded, cancellationToken);
                    }
                    catch
                    { }
                }
            }

            // create profile
            var profile = new ImageRenderingProfile(this.FileFormat, this.imageRenderer);
            if (colorSpaceFromIccProfile != null)
                profile.ColorSpace = colorSpaceFromIccProfile;
            profile.Width = (int)MacOS.CGImageGetWidth(imageRef);
            profile.Height = (int)MacOS.CGImageGetHeight(imageRef);
            return profile;
        }
        finally
        {
            if (iccProfileDataRef != IntPtr.Zero)
                MacOS.CFRelease(iccProfileDataRef);
            if (imageRef != IntPtr.Zero)
                MacOS.CFRelease(imageRef);
            if (imageSourceRef != IntPtr.Zero)
                MacOS.CFRelease(imageSourceRef);
            if (imageDataRef != IntPtr.Zero)
                MacOS.CFRelease(imageDataRef);
        }
    }
}