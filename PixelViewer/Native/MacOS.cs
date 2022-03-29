using System;
using System.Runtime.InteropServices;

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


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFDataGetBytePtr(IntPtr theData);


        [DllImport(CoreFoundationLib)]
		public static extern ulong CFDataGetLength(IntPtr theData);


        [DllImport(CoreFoundationLib)]
		public static extern void CFRelease(IntPtr cf);


        [DllImport(CoreFoundationLib)]
		public static extern IntPtr CFRetain(IntPtr cf);


        [DllImport(CoreGraphicsLib)]
		public static extern IntPtr CGColorSpaceCopyICCData(IntPtr space);


        [DllImport(CoreGraphicsLib)]
		public static extern CGColorSpaceModel CGColorSpaceGetModel(IntPtr space);


        [DllImport(CoreGraphicsLib)]
		public static extern IntPtr CGColorSpaceGetName(IntPtr space);


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