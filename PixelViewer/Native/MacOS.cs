using CarinaStudio.MacOS;
using CarinaStudio.MacOS.CoreFoundation;
using System;
using System.Runtime.InteropServices;

namespace Carina.PixelViewer.Native
{
    /// <summary>
    /// Native functions and structures for macOS.
    /// </summary>
    static class MacOS
    {
        public static readonly IntPtr kCGImagePropertyColorModel; // CFStringRef
        public static readonly IntPtr kCGImagePropertyDepth; // CFNumberRef
        public static readonly IntPtr kCGImagePropertyHasAlpha; // CFBooleanRef
        public static readonly IntPtr kCGImagePropertyOrientation; // CFNumberRef
        public static readonly IntPtr kCGImagePropertyPixelHeight; // CFNumberRef
        public static readonly IntPtr kCGImagePropertyPixelWidth; // CFNumberRef


        // Static initializer.
        static unsafe MacOS()
        {
            if (CarinaStudio.Platform.IsNotMacOS)
                return;

            // load symbols in ImageIO.Framework
            var imageIOLibHandle = NativeLibrary.Load(NativeLibraryNames.ImageIO);
            if (imageIOLibHandle != IntPtr.Zero)
            {
                kCGImagePropertyColorModel = new IntPtr(*(nint*)NativeLibrary.GetExport(imageIOLibHandle, "kCGImagePropertyColorModel"));
                kCGImagePropertyDepth = new IntPtr(*(nint*)NativeLibrary.GetExport(imageIOLibHandle, "kCGImagePropertyDepth"));
                kCGImagePropertyHasAlpha = new IntPtr(*(nint*)NativeLibrary.GetExport(imageIOLibHandle, "kCGImagePropertyHasAlpha"));
                kCGImagePropertyOrientation = new IntPtr(*(nint*)NativeLibrary.GetExport(imageIOLibHandle, "kCGImagePropertyOrientation"));
                kCGImagePropertyPixelHeight = new IntPtr(*(nint*)NativeLibrary.GetExport(imageIOLibHandle, "kCGImagePropertyPixelHeight"));
                kCGImagePropertyPixelWidth = new IntPtr(*(nint*)NativeLibrary.GetExport(imageIOLibHandle, "kCGImagePropertyPixelWidth"));
            }
        }


        [DllImport(NativeLibraryNames.CoreFoundation)]
		public static extern bool CFDictionaryContainsKey(IntPtr theDict, IntPtr key);


        public static bool CFDictionaryGetValue(IntPtr theDict, IntPtr key, out int value)
        {
            var valueRef = CFDictionaryGetValue(theDict, key);
            if (valueRef == IntPtr.Zero)
            {
                value = default;
                return false;
            }
            using var n = CFObject.FromHandle<CFNumber>(valueRef);
            value = n.ToInt32();
            return true;
        }


        public static bool CFDictionaryGetValue(IntPtr theDict, IntPtr key, out uint value)
        {
            var valueRef = CFDictionaryGetValue(theDict, key);
            if (valueRef == IntPtr.Zero)
            {
                value = default;
                return false;
            }
            using var n = CFObject.FromHandle<CFNumber>(valueRef);
            value = n.ToUInt32();
            return true;
        }


        public static bool CFDictionaryGetValue(IntPtr theDict, IntPtr key, out float value)
        {
            var valueRef = CFDictionaryGetValue(theDict, key);
            if (valueRef == IntPtr.Zero)
            {
                value = default;
                return false;
            }
            using var n = CFObject.FromHandle<CFNumber>(valueRef);
            value = n.ToSingle();
            return true;
        }


        public static bool CFDictionaryGetValue(IntPtr theDict, IntPtr key, out double value)
        {
            var valueRef = CFDictionaryGetValue(theDict, key);
            if (valueRef == IntPtr.Zero)
            {
                value = default;
                return false;
            }
            using var n = CFObject.FromHandle<CFNumber>(valueRef);
            value = n.ToDouble();
            return true;
        }


        public static bool CFDictionaryGetValue(IntPtr theDict, IntPtr key, out string? value)
        {
            var valueRef = CFDictionaryGetValue(theDict, key);
            if (valueRef == IntPtr.Zero)
            {
                value = default;
                return false;
            }
            using var s = CFObject.FromHandle<CFString>(valueRef);
            value = s.ToString();
            return true;
        }


        [DllImport(NativeLibraryNames.CoreFoundation)]
		public static extern IntPtr CFDictionaryGetValue(IntPtr theDict, IntPtr key);


        [DllImport(NativeLibraryNames.ImageIO)]
        public static extern IntPtr CGImageSourceCopyPropertiesAtIndex(IntPtr isrc, nuint index, IntPtr options);
    }
}