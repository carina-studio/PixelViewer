using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Carina.PixelViewer.Native
{
    /// <summary>
    /// Native functions and structures for macOS.
    /// </summary>
    static class MacOS
    {
        // Constants.
        const string CoreFoundationLib = "/System/Library/Frameworks/ApplicationServices.framework/Frameworks/CoreFoundation.framework/CoreFoundation";
        const string CoreGraphicsLib = "/System/Library/Frameworks/ApplicationServices.framework/Frameworks/CoreGraphics.framework/CoreGraphics";
        const string ImageIOLib = "/System/Library/Frameworks/ApplicationServices.framework/Frameworks/ImageIO.framework/ImageIO";


        public enum CFNumberType : long
        {
            SInt8Type = 1,
            SInt16Type = 2,
            SInt32Type = 3,
            SInt64Type = 4,
            Float32Type = 5,
            Float64Type = 6,
            CharType = 7,
            ShortType = 8,
            IntType = 9,
            LongType = 10,
            LongLongType = 11,
            FloatType = 12,
            DoubleType = 13,
        }


        [StructLayout(LayoutKind.Sequential)]
        public struct CFRange
        {
            public long location;
            public long length;
        }


        [Flags]
        public enum CFStringCompareFlags : long
        {
            CaseInsensitive = 1,	
            Backwards = 4,		/* Starting from the end of the string */
            Anchored = 8,		/* Only at the specified starting point */
            Nonliteral = 16,	/* If specified, loose equivalence is performed (o-umlaut == o, umlaut) */
            Localized = 32,		/* User's default locale is used for the comparisons */
            Numerically = 64,
        }


        public enum CGColorSpaceModel : int
        {
            Unknown = -1,
            Monochrome,
            RGB,
            CMYK,
            Lab,
            DeviceN,
            Indexed,
            Pattern,
            XYZ
        }


        public enum CGError : int
        {
            Success = 0,
            Failure = 1000,
            IllegalArgument = 1001,
            InvalidConnection = 1002,
            InvalidContext = 1003,
            CannotComplete = 1004,
            NotImplemented = 1006,
            RangeCheck = 1007,
            TypeCheck = 1008,
            InvalidOperation = 1010,
            NoneAvailable = 1011,
        }


        public enum CGImageAlphaInfo : uint
        {
            AlphaNone,               /* For example, RGB. */
            AlphaPremultipliedLast,  /* For example, premultiplied RGBA */
            AlphaPremultipliedFirst, /* For example, premultiplied ARGB */
            AlphaLast,               /* For example, non-premultiplied RGBA */
            AlphaFirst,              /* For example, non-premultiplied ARGB */
            AlphaNoneSkipLast,       /* For example, RGBX. */
            AlphaNoneSkipFirst,      /* For example, XRGB. */
            AlphaOnly                /* No color data, alpha data only */
        }


        public enum CGImageByteOrderInfo : uint 
        {
            ByteOrderMask     = 0x7000,
            ByteOrderDefault  = (0 << 12),
            ByteOrder16Little = (1 << 12),
            ByteOrder32Little = (2 << 12),
            ByteOrder16Big    = (3 << 12),
            ByteOrder32Big    = (4 << 12)
        }


        public enum CGImagePixelFormatInfo : uint
        {
            Mask = 0xF0000,
            Packed = (0 << 16),
            RGB555 = (1 << 16),
            RGB565 = (2 << 16),
            RGB101010 = (3 << 16),
            RGBCIF10 = (4 << 16),
        }


        public enum CGImageSourceStatus
        {
            UnexpectedEOF = -5,
            InvalidData = -4,
            UnknownType = -3,
            ReadingHeader = -2,
            Incomplete = -1,
            Complete = 0
        }


        [StructLayout(LayoutKind.Sequential)]
        public struct CGPoint 
        {
            public double X;
            public double Y;
        };


        [StructLayout(LayoutKind.Sequential)]
        public struct CGRect 
        {
            public CGPoint Origin;
            public CGSize Size;
        };


        [StructLayout(LayoutKind.Sequential)]
        public struct CGSize 
        {
            public double Width;
            public double Height;
        };


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
            var imageIOLibHandle = NativeLibrary.Load(ImageIOLib);
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


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFAllocatorGetDefault();


        [DllImport(CoreFoundationLib)]
		public static extern void CFDataAppendBytes(IntPtr theData, IntPtr bytes, long length);


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFDataCreate(IntPtr allocator, IntPtr data, long length);


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFDataCreate(IntPtr allocator, [MarshalAs(UnmanagedType.LPArray)] byte[] data, long length);


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFDataCreateMutable(IntPtr allocator, long capacity);


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFDataGetBytePtr(IntPtr theData);


        [DllImport(CoreFoundationLib)]
		public static extern ulong CFDataGetLength(IntPtr theData);


         [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFDataGetMutableBytePtr(IntPtr theData);


        [DllImport(CoreFoundationLib)]
		public static extern bool CFDictionaryContainsKey(IntPtr theDict, IntPtr key);


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFDictionaryGetValue(IntPtr theDict, IntPtr key);


        [DllImport(CoreFoundationLib)]
		public static extern void CFNumberGetValue(IntPtr number, CFNumberType theType, out int value);


        [DllImport(CoreFoundationLib)]
		public static extern void CFNumberGetValue(IntPtr number, CFNumberType theType, out uint value);


        [DllImport(CoreFoundationLib)]
		public static extern void CFNumberGetValue(IntPtr number, CFNumberType theType, out float value);


        [DllImport(CoreFoundationLib)]
		public static extern void CFNumberGetValue(IntPtr number, CFNumberType theType, out double value);


        [DllImport(CoreFoundationLib)]
		public static extern void CFRelease(IntPtr cf);


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFRetain(IntPtr cf);


        public static long CFStringCompare(IntPtr theString1, string theString2, CFStringCompareFlags compareOptions)
        {
            var cfString2 = CFStringCreate(theString2);
            var result = CFStringCompare(theString1, cfString2, compareOptions);
            CFRelease(cfString2);
            return result;
        }


        public static long CFStringCompare(string theString1, IntPtr theString2, CFStringCompareFlags compareOptions)
        {
            var cfString1 = CFStringCreate(theString1);
            var result = CFStringCompare(cfString1, theString2, compareOptions);
            CFRelease(cfString1);
            return result;
        }


        [DllImport(CoreFoundationLib)]
		public static extern long CFStringCompare(IntPtr theString1, IntPtr theString2, CFStringCompareFlags compareOptions);


        public static IntPtr CFStringCreate(string str) => 
            CFStringCreateWithCharacters(CFAllocatorGetDefault(), str, str.Length);


        [DllImport(CoreFoundationLib, CharSet = CharSet.Unicode)]
		public static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, string chars, long numChars);


        public static string CFStringGetCharacters(IntPtr theString)
        {
            var length = (int)CFStringGetLength(theString);
            var buffer = new StringBuilder(length + 1);
            CFStringGetCharacters(theString, new CFRange() { location = 0, length = length }, buffer);
            return buffer.ToString();
        }


        public static string CFStringGetCharacters(IntPtr theString, int index, int count)
        {
            var buffer = new StringBuilder(count + 1);
            CFStringGetCharacters(theString, new CFRange() { location = index, length = count }, buffer);
            return buffer.ToString();
        }


        [DllImport(CoreFoundationLib, CharSet = CharSet.Unicode)]
		public static extern void CFStringGetCharacters(IntPtr theString, CFRange range, StringBuilder buffer);


        [DllImport(CoreFoundationLib)]
		public static extern long CFStringGetLength(IntPtr theString);


        [DllImport(CoreGraphicsLib)]
		public static extern IntPtr CGColorSpaceCopyICCData(IntPtr space);


        [DllImport(CoreGraphicsLib)]
		public static extern CGColorSpaceModel CGColorSpaceGetModel(IntPtr space);


        [DllImport(CoreGraphicsLib)]
		public static extern IntPtr CGColorSpaceGetName(IntPtr space);


        [DllImport(CoreGraphicsLib)]
		public static extern IntPtr CGDataProviderCopyData(IntPtr provider);


        [DllImport(CoreGraphicsLib)]
		public static extern CGImageAlphaInfo CGImageGetAlphaInfo(IntPtr image);


        [DllImport(CoreGraphicsLib)]
		public static extern nuint CGImageGetBitsPerPixel(IntPtr image);


        [DllImport(CoreGraphicsLib)]
		public static extern CGImageByteOrderInfo CGImageGetByteOrderInfo(IntPtr image);


        [DllImport(CoreGraphicsLib)]
		public static extern nuint CGImageGetBytesPerRow(IntPtr image);


        [DllImport(CoreGraphicsLib)]
		public static extern IntPtr CGImageGetColorSpace(IntPtr image);


        [DllImport(CoreGraphicsLib)]
		public static extern IntPtr CGImageGetDataProvider(IntPtr image);


        [DllImport(CoreGraphicsLib)]
		public static extern nuint CGImageGetHeight(IntPtr image);


        [DllImport(CoreGraphicsLib)]
		public static extern CGImagePixelFormatInfo CGImageGetPixelFormatInfo(IntPtr image);


        [DllImport(CoreGraphicsLib)]
		public static extern nuint CGImageGetWidth(IntPtr image);


        [DllImport(ImageIOLib)]
        public static extern IntPtr CGImageSourceCopyPropertiesAtIndex(IntPtr isrc, nuint index, IntPtr options);


        [DllImport(ImageIOLib)]
        public static extern IntPtr CGImageSourceCreateImageAtIndex(IntPtr isrc, nuint index, IntPtr options);


        [DllImport(ImageIOLib)]
        public static extern IntPtr CGImageSourceCreateWithData(IntPtr data, IntPtr options);


        [DllImport(ImageIOLib)]
        public static extern nuint CGImageSourceGetCount(IntPtr isrc);


        [DllImport(ImageIOLib)]
        public static extern nuint CGImageSourceGetPrimaryImageIndex(IntPtr isrc);


        [DllImport(ImageIOLib)]
        public static extern CGImageSourceStatus CGImageSourceGetStatus(IntPtr isrc);


        [DllImport(CoreGraphicsLib)]
		public static extern IntPtr CGDisplayCopyColorSpace(uint display);


        [DllImport(CoreGraphicsLib)]
		public static extern CGError CGGetDisplaysWithPoint(CGPoint point, uint maxDisplays, [MarshalAs(UnmanagedType.LPArray)] uint[]? displays, out uint matchingDisplayCount);


        [DllImport(CoreGraphicsLib)]
		public static extern CGError CGGetDisplaysWithRect(CGRect rect, uint maxDisplays, [MarshalAs(UnmanagedType.LPArray)] uint[]? displays, out uint matchingDisplayCount);


        [DllImport(CoreGraphicsLib)]
		public static extern uint CGMainDisplayID();
    }
}