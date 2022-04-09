using Avalonia;
using Carina.PixelViewer.Native;
using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Threading;
using CarinaStudio.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// RGB color space.
    /// </summary>
    class ColorSpace : IEquatable<ColorSpace>, INotifyPropertyChanged
    {
        /// <summary>
        /// Convert RGB color between color spaces.
        /// </summary>
        public class Converter
        {
            // Fields.
            readonly ColorSpace destColorSpace;
            readonly bool isIdentical;
            readonly long[] matrix;
            readonly bool skipDestNumericalTransfer;
            readonly bool skipSrcNumericalTransfer;
            readonly ColorSpace srcColorSpace;

            /// <summary>
            /// Initialize new <see cref="Converter"/> instance.
            /// </summary>
            /// <param name="srcColorSpace">Source color space.</param>
            /// <param name="skipSrcNumericalTransfer">Skip numerical transfer from source color space.</param>
            /// <param name="destColorSpace">Target color space.</param>
            /// <param name="skipDestNumericalTransfer">Skip numerical transfer to target color space.</param>
            public Converter(ColorSpace srcColorSpace, bool skipSrcNumericalTransfer, ColorSpace destColorSpace, bool skipDestNumericalTransfer)
            {
                this.srcColorSpace = srcColorSpace;
                this.destColorSpace = destColorSpace;
                this.isIdentical = srcColorSpace.Equals(destColorSpace);
                if (!this.isIdentical)
                {
                    var m1 = this.srcColorSpace.skiaColorSpaceXyz;
                    var m2 = this.destColorSpace.skiaColorSpaceXyz.Invert();
                    this.matrix = Quantize(SKColorSpaceXyz.Concat(m2, m1));
                }
                else
                    this.matrix = new long[0];
                this.skipSrcNumericalTransfer = skipSrcNumericalTransfer;
                this.skipDestNumericalTransfer = skipDestNumericalTransfer;
            }

            /// <summary>
            /// Convert 8-bit RGB color.
            /// </summary>
            /// <param name="r">Normalized R.</param>
            /// <param name="g">Normalized G.</param>
            /// <param name="b">Normalized B.</param>
            /// <returns>Converted normalized RGB color.</returns>
            public unsafe (byte, byte, byte) Convert(byte r, byte g, byte b)
            {
                if (this.isIdentical)
                    return (r, g, b);
                var qR = mappingTableFrom8Bit[r];
                var qG = mappingTableFrom8Bit[g];
                var qB = mappingTableFrom8Bit[b];
                if (!this.skipSrcNumericalTransfer && this.srcColorSpace.hasTransferFunc)
                {
                    qR = this.srcColorSpace.NumericalTransferToLinear(qR);
                    qG = this.srcColorSpace.NumericalTransferToLinear(qG);
                    qB = this.srcColorSpace.NumericalTransferToLinear(qB);
                }
                var m = this.matrix;
                qR = Clip((m[0] * qR + m[1] * qG + m[2] * qB) >> QuantizationBits);
                qG = Clip((m[3] * qR + m[4] * qG + m[5] * qB) >> QuantizationBits);
                qB = Clip((m[6] * qR + m[7] * qG + m[8] * qB) >> QuantizationBits);
                if (!this.skipDestNumericalTransfer && this.destColorSpace.hasTransferFunc)
                {
                    qR = this.destColorSpace.NumericalTransferFromLinear(qR);
                    qG = this.destColorSpace.NumericalTransferFromLinear(qG);
                    qB = this.destColorSpace.NumericalTransferFromLinear(qB);
                }
                return (mappingTableTo8Bit[qR], mappingTableTo8Bit[qG], mappingTableTo8Bit[qB]);
            }

            /// <summary>
            /// Convert 16-bit RGB color.
            /// </summary>
            /// <param name="r">Normalized R.</param>
            /// <param name="g">Normalized G.</param>
            /// <param name="b">Normalized B.</param>
            /// <returns>Converted normalized RGB color.</returns>
            public unsafe (ushort, ushort, ushort) Convert(ushort r, ushort g, ushort b)
            {
                if (this.isIdentical)
                    return (r, g, b);
                var qR = mappingTableFrom16Bit[r];
                var qG = mappingTableFrom16Bit[g];
                var qB = mappingTableFrom16Bit[b];
                if (!this.skipSrcNumericalTransfer && this.srcColorSpace.hasTransferFunc)
                {
                    qR = this.srcColorSpace.NumericalTransferToLinear(qR);
                    qG = this.srcColorSpace.NumericalTransferToLinear(qG);
                    qB = this.srcColorSpace.NumericalTransferToLinear(qB);
                }
                var m = this.matrix;
                qR = Clip((m[0] * qR + m[1] * qG + m[2] * qB) >> QuantizationBits);
                qG = Clip((m[3] * qR + m[4] * qG + m[5] * qB) >> QuantizationBits);
                qB = Clip((m[6] * qR + m[7] * qG + m[8] * qB) >> QuantizationBits);
                if (!this.skipDestNumericalTransfer && this.destColorSpace.hasTransferFunc)
                {
                    qR = this.destColorSpace.NumericalTransferFromLinear(qR);
                    qG = this.destColorSpace.NumericalTransferFromLinear(qG);
                    qB = this.destColorSpace.NumericalTransferFromLinear(qB);
                }
                return (mappingTableTo16Bit[qR], mappingTableTo16Bit[qG], mappingTableTo16Bit[qB]);
            }

            /// <summary>
            /// Convert RGB color.
            /// </summary>
            /// <param name="r">Normalized R.</param>
            /// <param name="g">Normalized G.</param>
            /// <param name="b">Normalized B.</param>
            /// <returns>Converted normalized RGB color.</returns>
            public (double, double, double) Convert(double r, double g, double b)
            {
                if (this.isIdentical)
                    return (r, g, b);
                var qR = Quantize(r);
                var qG = Quantize(g);
                var qB = Quantize(b);
                if (!this.skipSrcNumericalTransfer && this.srcColorSpace.hasTransferFunc)
                {
                    qR = this.srcColorSpace.NumericalTransferToLinear(qR);
                    qG = this.srcColorSpace.NumericalTransferToLinear(qG);
                    qB = this.srcColorSpace.NumericalTransferToLinear(qB);
                }
                var m = this.matrix;
                qR = Clip((m[0] * qR + m[1] * qG + m[2] * qB) >> QuantizationBits);
                qG = Clip((m[3] * qR + m[4] * qG + m[5] * qB) >> QuantizationBits);
                qB = Clip((m[6] * qR + m[7] * qG + m[8] * qB) >> QuantizationBits);
                if (!this.skipDestNumericalTransfer && this.destColorSpace.hasTransferFunc)
                {
                    qR = this.destColorSpace.NumericalTransferFromLinear(qR);
                    qG = this.destColorSpace.NumericalTransferFromLinear(qG);
                    qB = this.destColorSpace.NumericalTransferFromLinear(qB);
                }
                return ((double)qR / QuantizationBits, (double)qG / QuantizationBits, (double)qB / QuantizationBits);
            }
        }


        /// <summary>
        /// CIE standard illuminant D50.
        /// </summary>
        public static (double, double, double) D50 = (0.9642, 1.0000, 0.8249);
        /// <summary>
        /// CIE standard illuminant D65.
        /// </summary>
        public static (double, double, double) D65 = (0.9504, 1.0000, 1.0889);


        /// <summary>
        /// Adobe RGB (1998).
        /// </summary>
        public static readonly ColorSpace AdobeRGB_1998 = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "Adobe-RGB-1998", 
            null, 
            SKColorSpaceTransferFn.TwoDotTwo,
            SKColorSpaceXyz.AdobeRgb,
            D65, 
            new Uri("https://en.wikipedia.org/wiki/Adobe_RGB_color_space"));

        /// <summary>
        /// ITU-R BT.2020.
        /// </summary>
        public static readonly ColorSpace BT_2020 = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "BT.2020", 
            null, 
            SKColorSpaceTransferFn.Rec2020,
            SKColorSpaceXyz.Rec2020,
            D65, 
            new Uri("https://en.wikipedia.org/wiki/Rec._2020"));

        /// <summary>
        /// ITU-R BT.2100 with HLG transfer.
        /// </summary>
        public static readonly ColorSpace BT_2100_HLG = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "BT.2100-HLG",
            null,
            SKColorSpaceTransferFn.Hlg,
            SKColorSpaceXyz.Rec2020,
            D65,
            new Uri("https://en.wikipedia.org/wiki/Rec._2100"));

        /// <summary>
        /// ITU-R BT.2100 with PQ transfer.
        /// </summary>
        public static readonly ColorSpace BT_2100_PQ = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "BT.2100-PQ",
            null,
            SKColorSpaceTransferFn.Pq,
            SKColorSpaceXyz.Rec2020,
            D65,
            new Uri("https://en.wikipedia.org/wiki/Rec._2100"));

        /// <summary>
        /// ITU-R BT.601 525-line.
        /// </summary>
        public static readonly ColorSpace BT_601_525Line = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "BT.601-525-line", 
            null, 
            new SKColorSpaceTransferFn()
            {
                G = 1 / 0.45f,
                A = 1 / 1.099f,
                B = 0.099f / 1.099f,
                C = 1 / 4.5f, 
                D = 0.081f, 
                E = 0.0f, 
                F = 0.0f
            },
            new SKColorSpaceXyz(
                0.3935f, 0.3653f, 0.1917f,
                0.2124f, 0.7011f, 0.0866f,
                0.0187f, 0.1119f, 0.9584f
            ), 
            D65, 
            new Uri("https://en.wikipedia.org/wiki/Rec._601"));

        /// <summary>
        /// ITU-R BT.601 625-line.
        /// </summary>
        public static readonly ColorSpace BT_601_625Line = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "BT.601-625-line", 
            null, 
            new SKColorSpaceTransferFn()
            {
                G = 1 / 0.45f,
                A = 1 / 1.099f,
                B = 0.099f / 1.099f,
                C = 1 / 4.5f, 
                D = 0.081f, 
                E = 0.0f, 
                F = 0.0f
            },
            new SKColorSpaceXyz(
                0.4306f, 0.3415f, 0.1784f,
                0.2220f, 0.7067f, 0.0713f,
                0.0202f, 0.1296f, 0.9393f
            ),
            D65, 
            new Uri("https://en.wikipedia.org/wiki/Rec._601"));

#pragma warning disable CS0618
        /// <summary>
        /// DCI-P3 (D63).
        /// </summary>
        public static readonly ColorSpace DCI_P3 = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "DCI-P3", 
            null, 
            new SKColorSpaceTransferFn()
            {
                G = 2.6f,
                A = 1f
            },
            SKColorSpaceXyz.Dcip3,
            (0.894587, 1, 0.954416), 
            new Uri("https://en.wikipedia.org/wiki/DCI-P3"));
#pragma warning restore CS0618

        /// <summary>
        /// Default color space.
        /// </summary>
        public static readonly ColorSpace Default;

        /// <summary>
        /// Display-P3 (P3-D65).
        /// </summary>
        public static readonly ColorSpace Display_P3 = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "Display-P3", 
            null, 
            SKColorSpaceTransferFn.Srgb,
            new SKColorSpaceXyz(
                0.51512146f, 0.29197693f, 0.15710449f,
                0.24119568f, 0.6922455f, 0.0665741f,
                -0.0010528564f, 0.041885376f, 0.7840729f
            ),
            D65, 
            new Uri("https://en.wikipedia.org/wiki/DCI-P3"));
        
        /// <summary>
        /// sRGB.
        /// </summary>
        public static readonly ColorSpace Srgb = new ColorSpace(
            ColorSpaceSource.BuiltIn,
            "sRGB", 
            null, 
            SKColorSpaceTransferFn.Srgb,
            SKColorSpaceXyz.Srgb,
            D65, 
            new Uri("https://en.wikipedia.org/wiki/SRGB"));


        // Constants.
        const int QuantizationBits = 20;
        const int QuantizationSteps = 0x1 << QuantizationBits;
        const double QuantizationSteps2 = (double)QuantizationSteps * QuantizationSteps;


        // Static fields.
        static readonly SortedObservableList<ColorSpace> allColorSpaceList = new(Compare);
        static CarinaStudio.AppSuite.IAppSuiteApplication? app;
        static readonly Dictionary<string, ColorSpace> builtInColorSpaces = new()
        {
            { AdobeRGB_1998.Name, AdobeRGB_1998 },
            { BT_2020.Name, BT_2020 },
            { BT_2100_HLG.Name, BT_2100_HLG },
            { BT_2100_PQ.Name, BT_2100_PQ },
            { BT_601_525Line.Name, BT_601_525Line },
            { BT_601_625Line.Name, BT_601_625Line },
            { DCI_P3.Name, DCI_P3 },
            { Display_P3.Name, Display_P3 },
            { Srgb.Name, Srgb },
        };
        static volatile ILogger? logger;
        static readonly TaskFactory ioTaskFactory = new TaskFactory(new FixedThreadsTaskScheduler(1));
        static readonly unsafe long* mappingTableFrom16Bit = (long*)NativeMemory.Alloc(sizeof(long) * 65536);
        static readonly unsafe long* mappingTableFrom8Bit = (long*)NativeMemory.Alloc(sizeof(long) * 256);
        static readonly unsafe ushort* mappingTableTo16Bit = (ushort*)NativeMemory.Alloc(sizeof(ushort) * (QuantizationSteps + 1));
        static readonly unsafe byte* mappingTableTo8Bit = (byte*)NativeMemory.Alloc(sizeof(byte) * (QuantizationSteps + 1));
        static readonly Random random = new();
        static readonly SortedObservableList<ColorSpace> userDefinedColorSpaceList = new(Compare);
        static readonly Dictionary<string, ColorSpace> userDefinedColorSpaces = new();


        // Fields.
        string? customName;
        readonly bool hasTransferFunc;
        readonly long[] matrixFromXyz;
        readonly long[] matrixToXyz;
        readonly SKColorSpaceTransferFn numericalTransferFuncFromLinear;
        readonly SKColorSpaceTransferFn numericalTransferFuncToLinear;
        volatile unsafe long* numericalTransferTableFromLinear;
        volatile unsafe long* numericalTransferTableToLinear;
        readonly SKColorSpaceXyz skiaColorSpaceXyz;


        // Static initializer.
        static unsafe ColorSpace()
        {
            allColorSpaceList.AddAll(builtInColorSpaces.Values);
            AllColorSpaces = allColorSpaceList.AsReadOnly();
            BuiltInColorSpaces = builtInColorSpaces.Values.ToList().Also(it =>
                it.Sort(Compare)).AsReadOnly();
            Default = Srgb;
            for (var input = 0; input < 256; ++input)
                mappingTableFrom8Bit[input] = (long)(input / 255.0 * QuantizationSteps + 0.5);
            for (var input = 0; input < 65536; ++input)
                mappingTableFrom16Bit[input] = (long)(input / 65535.0 * QuantizationSteps + 0.5);
            for (var input = QuantizationSteps; input >= 0; --input)
            {
                mappingTableTo8Bit[input] = (byte)((double)input / QuantizationSteps * 255 + 0.5);
                mappingTableTo16Bit[input] = (ushort)((double)input / QuantizationSteps * 65535 + 0.5);
            }
            UserDefinedColorSpaces = userDefinedColorSpaceList.AsReadOnly();
        }


        // Constructor.
        ColorSpace(ColorSpaceSource source, string name, string? customName, SKColorSpaceTransferFn transferFunc, SKColorSpaceXyz matrixToXyz, (double, double, double)? whitePoint, Uri? uri)
        {
            this.customName = customName;
            this.hasTransferFunc = !IsLinearTransferFunc(transferFunc);
            if (this.hasTransferFunc)
            {
                this.numericalTransferFuncToLinear = transferFunc;
                this.numericalTransferFuncFromLinear = transferFunc.Invert();
            }
            if (whitePoint.HasValue)
            {
                this.IsD65WhitePoint = AreEquivalentWhitePoints(whitePoint.Value, D65);
                this.IsD50WhitePoint = !this.IsD65WhitePoint && AreEquivalentWhitePoints(whitePoint.Value, D50);
            }
            this.IsLinear = !this.hasTransferFunc;
            this.skiaColorSpaceXyz = matrixToXyz;
            this.matrixToXyz = Quantize(this.skiaColorSpaceXyz);
            this.matrixFromXyz = Quantize(this.skiaColorSpaceXyz.Invert());
            this.Name = name;
            this.Source = source;
            this.Uri = uri;
            this.WhitePoint = whitePoint;
        }


        // Finalizer.
        unsafe ~ColorSpace()
        {
            if (this.numericalTransferTableFromLinear != null)
            {
                NativeMemory.Free(this.numericalTransferTableFromLinear);
                this.numericalTransferTableFromLinear = null;
            }
            if (this.numericalTransferTableToLinear != null)
            {
                NativeMemory.Free(this.numericalTransferTableToLinear);
                this.numericalTransferTableToLinear = null;
            }
        }


        /// <summary>
        /// Add user-defined color space.
        /// </summary>
        /// <param name="colorSpace">Color space to add.</param>
        /// <returns>True if color space has been added successfully.</returns>
        public static bool AddUserDefinedColorSpace(ColorSpace colorSpace)
        {
            if (!colorSpace.IsUserDefined)
                return false;
            var app = ColorSpace.app;
            if (app == null)
                return false;
            if (userDefinedColorSpaces.TryAdd(colorSpace.Name, colorSpace))
            {
                colorSpace.PropertyChanged += OnCustomColorSpacePropertyChanged;
                _ = colorSpace.SaveToFileAsync(Path.Combine(app.RootPrivateDirectoryPath, "ColorSpaces", $"{colorSpace.Name}.json"));
                allColorSpaceList.Add(colorSpace);
                userDefinedColorSpaceList.Add(colorSpace);
                return true;
            }
            return false;
        }


        // Check whether two white points are equivalent or not.
        static bool AreEquivalentWhitePoints((double, double, double) x, (double, double, double) y) =>
            Math.Abs(x.Item1 - y.Item1) <= 0.0001
            && Math.Abs(x.Item2 - y.Item2) <= 0.0001
            && Math.Abs(x.Item3 - y.Item3) <= 0.0001;


        /// <summary>
        /// Get all color spaces.
        /// </summary>
        /// <value></value>
        public static IList<ColorSpace> AllColorSpaces { get; }


        /// <summary>
        /// Get all built-in color spaces.
        /// </summary>
        public static IList<ColorSpace> BuiltInColorSpaces { get; }


        // Clip quantized color to valid range.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long Clip(long color)
        {
            if (color < 0)
                return 0;
            if (color > QuantizationSteps)
                return QuantizationSteps;
            return color;
        }


        // Compare color spaces.
        static int Compare(ColorSpace? lhs, ColorSpace? rhs)
        {
            if (lhs == null)
                return rhs == null ? 0 : 1;
            if (rhs == null)
                return -1;
            if (lhs.IsBuiltIn)
                return rhs.IsBuiltIn ? string.Compare(lhs.Name, rhs.Name) : -1;
            if (rhs.IsBuiltIn)
                return 1;
            if (lhs.IsSystemDefined)
                return rhs.IsSystemDefined ? string.Compare(lhs.Name, rhs.Name) : -1;
            if (rhs.IsSystemDefined)
                return 1;
            return string.Compare(lhs?.Name, rhs?.Name);
        }


        /// <summary>
        /// Get ot set custom name of color space.
        /// </summary>
        public string? CustomName 
        { 
            get => this.customName;
            set
            {
                if (this.IsBuiltIn)
                    throw new InvalidOperationException();
                if (this.customName == value)
                    return;
                this.customName = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomName)));
                CustomNameChanged?.Invoke(null, new ColorSpaceEventArgs(this));
            }
        }


        /// <summary>
        /// Raise when custom name of one of custom color space has been changed.
        /// </summary>
        public static event EventHandler<ColorSpaceEventArgs>? CustomNameChanged;


        /// <inheritdoc/>
        public bool Equals(ColorSpace? colorSpace) =>
            colorSpace is not null 
            && this.Source == colorSpace.Source
            && (this.Source != ColorSpaceSource.SystemDefined || this.customName == colorSpace.customName)
            && this.numericalTransferFuncToLinear.Equals(colorSpace.numericalTransferFuncToLinear)
            && this.skiaColorSpaceXyz.Equals(colorSpace.skiaColorSpaceXyz);


        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is ColorSpace colorSpace)
                return this.Equals(colorSpace);
            return false;
        }


        /// <summary>
        /// Create <see cref="ColorSpace"/> instance from <see cref="SKColorSpace"/>.
        /// </summary>
        /// <param name="source">Source of color space.</param>
        /// <param name="customName">Custom name.</param>
        /// <param name="skColorSpace"><see cref="SKColorSpace"/>.</param>
        /// <param name="whitePoint">XYZ of white point.</param>
        /// <returns><see cref="ColorSpace"/>.</returns>
        public static ColorSpace FromSkiaColorSpace(ColorSpaceSource source, string? customName, SKColorSpace skColorSpace, (double, double, double)? whitePoint)
        {
            if (source == ColorSpaceSource.BuiltIn)
                throw new ArgumentException();
            var hasTransferFunc = skColorSpace.GetNumericalTransferFunction(out var transferFunc);
            if (!hasTransferFunc)
                transferFunc = SKColorSpaceTransferFn.Linear;
            return new ColorSpace(source, GenerateRandomName(), customName, transferFunc, skColorSpace.ToColorSpaceXyz(), whitePoint, null);
        }


        // Generate random name for color space.
        static string GenerateRandomName() => new char[8].Let(it =>
        {
            for (var i = it.Length - 1; i >= 0; --i)
            {
                var n = random.Next(36);
                if (n <= 9)
                    it[i] = (char)('0' + n);
                else
                    it[i] = (char)('a' + (n - 10));
            }
            return $"{new string(it)}-{(uint)DateTime.Now.ToBinary()}";
        });


        /// <inheritdoc/>
        public override int GetHashCode() => 
            (int)this.matrixToXyz[0];
        

        /// <summary>
        /// Get screen color space defined by system.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of getting screen color space.</returns>
        public static Task<ColorSpace> GetSystemScreenColorSpaceAsync(CancellationToken cancellationToken = default) =>
            GetSystemScreenColorSpaceAsync(null, cancellationToken);


        /// <summary>
        /// Get screen color space defined by system.
        /// </summary>
        /// <param name="window">Window to get screen.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of getting screen color space.</returns>
        public static async Task<ColorSpace> GetSystemScreenColorSpaceAsync(Avalonia.Controls.Window? window, CancellationToken cancellationToken = default)
        {
            // check state
            if (!IsSystemScreenColorSpaceSupported)
                throw new NotSupportedException();
            
            // get screen color space
            var systemColorSpace = await Task.Run(() =>
            {
                if (CarinaStudio.Platform.IsWindows)
                {
                    // use new API to get color profile
                    if (CarinaStudio.Platform.IsWindows10OrAbove)
                    {
                        try
                        {
                            // find monitor
                            var monitorInfo = new Win32.MONITORINFOEX() { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFOEX>() };
                            var windowBounds = (window?.Bounds).GetValueOrDefault();
                            var windowRect = new Win32.RECT()
                            {
                                left = (int)(windowBounds.Left + 0.5),
                                top = (int)(windowBounds.Top + 0.5),
                                right = (int)(windowBounds.Right + 0.5),
                                bottom = (int)(windowBounds.Bottom + 0.5),
                            };
                            var hMonitor = Win32.MonitorFromRect(ref windowRect, Win32.MONITOR.DEFAULTTONEAREST);
                            if (hMonitor == IntPtr.Zero)
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get monitor which contains window.");
                            if (!Win32.GetMonitorInfo(hMonitor, ref monitorInfo))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get info of monitor which contains window.");

                            // get device info
                            var displayDevice = new Win32.DISPLAY_DEVICE() { cb = (uint)Marshal.SizeOf<Win32.DISPLAY_DEVICE>() };
                            if (!Win32.EnumDisplayDevices(monitorInfo.szDevice, 0, ref displayDevice, Win32.EDD.GET_DEVICE_INTERFACE_NAME))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get display device which contains window.");

                            // check color profile scope
                            var colorProfileScope = Win32.WCS_PROFILE_MANAGEMENT_SCOPE.SYSTEM_WIDE;
                            if (Win32.WcsGetUsePerUserProfiles(displayDevice.DeviceKey, Win32.CLASS.MONITOR, out var usePerUserProfiles) && usePerUserProfiles)
                                colorProfileScope = Win32.WCS_PROFILE_MANAGEMENT_SCOPE.CURRENT_USER;

                            // get color profile name
                            if (!Win32.WcsGetDefaultColorProfileSize(colorProfileScope, displayDevice.DeviceKey, Win32.COLORPROFILETYPE.ICC, Win32.COLORPROFILESUBTYPE.RGB_WORKING_SPACE, 0, out var colorProfileNameSize))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get size of name of color profile.");
                            var colorProfileName = new StringBuilder((int)colorProfileNameSize);
                            if (!Win32.WcsGetDefaultColorProfile(colorProfileScope, displayDevice.DeviceKey, Win32.COLORPROFILETYPE.ICC, Win32.COLORPROFILESUBTYPE.RGB_WORKING_SPACE, 0, colorProfileNameSize, colorProfileName))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get name of color profile.");

                            // get color directory
                            var colorDirPathSize = 512u << 1;
                            var colorDirPath = new StringBuilder((int)colorDirPathSize >> 1);
                            if (!Win32.GetColorDirectory(null, colorDirPath, ref colorDirPathSize))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get color directory.");

                            // load color profile
                            using var stream = new FileStream(Path.Combine(colorDirPath.ToString(), colorProfileName.ToString()), FileMode.Open, FileAccess.Read);
                            return LoadFromIccProfile(colorProfileName.ToString(), stream, ColorSpaceSource.SystemDefined);
                        }
                        catch (Exception ex)
                        {
                            logger?.LogError(ex, "Unable to get system color space on Windows 10+.");
                        }
                    }

                    // get DC
                    var hWnd = (window?.PlatformImpl?.Handle?.Handle).GetValueOrDefault();
                    var hdc = Win32.GetWindowDC(hWnd);
                    if (hdc == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get DC of window or desktop.");

                    // load ICC profile
                    try
                    {
                        // get ICC profile name
                        var fileNameBufferSize = 512u;
                        var fileNameBuffer = new StringBuilder((int)fileNameBufferSize);
                        if (!Win32.GetICMProfile(hdc, ref fileNameBufferSize, fileNameBuffer))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to path of ICC profile of window or desktop.");

                        // load ICC profile
                        using var stream = new FileStream(fileNameBuffer.ToString(), FileMode.Open, FileAccess.Read);
                        return LoadFromIccProfile(fileNameBuffer.ToString(), stream, ColorSpaceSource.SystemDefined);
                    }
                    finally
                    {
                        Win32.ReleaseDC(hWnd, hdc);
                    }
                }
                else if (CarinaStudio.Platform.IsMacOS)
                {
                    var colorSpaceRef = IntPtr.Zero;
                    var iccDataRef = IntPtr.Zero;
                    try
                    {
                        // get display ID
                        var displayIdArray = new uint[1];
                        var windowBounds = (window?.Bounds).GetValueOrDefault();
                        if (window == null)
                            displayIdArray[0] = MacOS.CGMainDisplayID();
                        else if (MacOS.CGGetDisplaysWithRect(new MacOS.CGRect() 
                            { 
                                Origin = { X = windowBounds.Left, Y = windowBounds.Top },
                                Size = { Width = windowBounds.Width, Height = windowBounds.Height },
                            }, 
                            1, displayIdArray, out var displayCount) != MacOS.CGError.Success || displayCount != 1)
                        {
                            logger?.LogError("Unable to get screen which contains given window, use default screen");
                            displayIdArray[0] = MacOS.CGMainDisplayID();
                        }

                        // get color space
                        colorSpaceRef = MacOS.CGDisplayCopyColorSpace(displayIdArray[0]);
                        if (colorSpaceRef == IntPtr.Zero)
                            throw new Exception("No color space defined for main display.");
                        var colorModel = MacOS.CGColorSpaceGetModel(colorSpaceRef);
                        if (colorModel != MacOS.CGColorSpaceModel.RGB)
                            throw new NotSupportedException($"Unsupported color model: {colorModel}.");
                        iccDataRef = MacOS.CGColorSpaceCopyICCData(colorSpaceRef);
                        if (iccDataRef == IntPtr.Zero)
                            throw new Exception("Unable to get ICC profile from color space.");
                        var iccDataCount = MacOS.CFDataGetLength(iccDataRef);
                        if (iccDataCount == 0)
                            throw new Exception("Empty ICC profile from color space.");
                        var iccData = new byte[(int)iccDataCount];
                        Marshal.Copy(MacOS.CFDataGetBytePtr(iccDataRef), iccData, 0, iccData.Length);
                        return new MemoryStream(iccData).Use(it =>
                            LoadFromIccProfile(null, it, ColorSpaceSource.SystemDefined));
                    }
                    finally
                    {
                        if (iccDataRef != IntPtr.Zero)
                            MacOS.CFRelease(iccDataRef);
                        if (colorSpaceRef != IntPtr.Zero)
                            MacOS.CFRelease(colorSpaceRef);
                    }
                }
                throw new NotSupportedException();
            });

            // use built-in color space instead
            foreach (var builtInColorSpace in builtInColorSpaces.Values)
            {
                if (builtInColorSpace.Equals(systemColorSpace))
                    return builtInColorSpace;
            }

            // complete
            return systemColorSpace;
        }


        // [Workaroung] Apply HLG transfer.
        // Please refer to third_party/skcms/skcms.cc in Skia source code.
        static double HlgNumericalTransferToLinear(SKColorSpaceTransferFn fn, double value)
        {
            var sign = value >= 0 ? 1f : -1f;
            var K = fn.F + 1;
            return (K * sign * (value * fn.A <= 1
                ? Math.Pow(value * fn.A, fn.B)
                : Math.Pow(Math.E, (value - fn.E) * fn.C) + fn.D));
        }
        

        /// <summary>
        /// Initialize color space.
        /// </summary>
        /// <param name="app">Application.</param>
        /// <returns>Task of initialization.</returns>
        public static async Task InitializeAsync(CarinaStudio.AppSuite.IAppSuiteApplication app)
        {
            // check state
            if (ColorSpace.app != null)
                throw new InvalidOperationException();
            app.VerifyAccess();

            // attach to application
            ColorSpace.app = app;
            logger = app.LoggerFactory.CreateLogger(nameof(ColorSpace));

            logger.LogDebug("Initialize");

            // find color space files
            var directory = Path.Combine(app.RootPrivateDirectoryPath, "ColorSpaces");
            var fileNames = await ioTaskFactory.StartNew(() =>
            {
                try
                {
                    if (Directory.Exists(directory))
                        return Directory.GetFiles(directory, "*.json");
                    return new string[0];
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Failed to get color space files in '{directory}'");
                    return new string[0];
                }
            });
            logger.LogDebug($"Found {fileNames.Length} color space files");

            // load color space files
            foreach (var fileName in fileNames)
            {
                logger.LogTrace($"Load color space file '{fileName}'");
                var colorSpace = (ColorSpace?)null;
                try
                {
                    colorSpace = await LoadFromFileAsync(fileName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Failed to load color space file '{fileName}'");
                    continue;
                }
                if (userDefinedColorSpaces.TryAdd(colorSpace.Name, colorSpace))
                {
                    colorSpace.PropertyChanged += OnCustomColorSpacePropertyChanged;
                    allColorSpaceList.Add(colorSpace);
                    userDefinedColorSpaceList.Add(colorSpace);
                }
            }
            logger.LogDebug($"{userDefinedColorSpaces.Count} color space(s) loaded");
        }
        

        /// <summary>
        /// Check whether the source of color space is <see cref="ColorSpaceSource.BuiltIn"/> or not.
        /// </summary>
        public bool IsBuiltIn { get => this.Source == ColorSpaceSource.BuiltIn; }


        /// <summary>
        /// Check whether the white point is D50 or not.
        /// </summary>
        public bool IsD50WhitePoint { get; }


        /// <summary>
        /// Check whether the white point is D65 or not.
        /// </summary>
        public bool IsD65WhitePoint { get; }


        /// <summary>
        /// Check whether the source of color space is <see cref="ColorSpaceSource.Embedded"/> or not.
        /// </summary>
        public bool IsEmbedded { get => this.Source == ColorSpaceSource.Embedded; }


        // [Workaroung] Check whether given transfer function is HLG or not.
        // Please refer to third_party/skcms/skcms.cc in Skia source code.
        static bool IsHlgTransferFunc(SKColorSpaceTransferFn fn) =>
            fn.G < 0 && Math.Abs((int)fn.G - SKColorSpaceTransferFn.Hlg.G) <= 0.0000001;


        /// <summary>
        /// Check whether RGB in the color space is linear RGB or not.
        /// </summary>
        public bool IsLinear { get; }


        // Check whether given transfer function is linear or not.
        static bool IsLinearTransferFunc(SKColorSpaceTransferFn fn) =>
            Math.Abs(fn.G - 1) < 0.000001 && Math.Abs(fn.A - 1) < 0.000001 && fn.B == 0 && fn.C == 0 && fn.D == 0 && fn.E == 0 && fn.F == 0;


        /// <summary>
        /// Check whether the source of color space is <see cref="ColorSpaceSource.UserDefined"/> or not.
        /// </summary>
        public bool IsUserDefined { get => this.Source == ColorSpaceSource.UserDefined; }


        /// <summary>
        /// Check whether system defined screen color space is supported or not. 
        /// </summary>
        public static bool IsSystemScreenColorSpaceSupported { get; } = CarinaStudio.Platform.IsWindows || CarinaStudio.Platform.IsMacOS;


        /// <summary>
        /// Check whether the source of color space is <see cref="ColorSpaceSource.SystemDefined"/> or not.
        /// </summary>
        public bool IsSystemDefined { get => this.Source == ColorSpaceSource.SystemDefined; }


        /// <summary>
        /// Load color space from file.
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of loading from file.</returns>
        public static Task<ColorSpace> LoadFromFileAsync(string fileName, CancellationToken cancellationToken = default) => ioTaskFactory.StartNew(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            if (!CarinaStudio.IO.File.TryOpenRead(fileName, 5000, out var stream) || stream == null)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                throw new IOException($"Unable to open file '{fileName}' to load color space.");
            }
            return stream.Use(_ =>
            {
                // check root object
                using var jsonDocument = JsonDocument.Parse(stream);
                var rootObject = jsonDocument.RootElement;
                if (rootObject.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Invalid color space file.");
                
                // get name and custom name
                var name = (string?)null;
                var customName = (string?)null;
                if (rootObject.TryGetProperty(nameof(Name), out var jsonProperty) && jsonProperty.ValueKind == JsonValueKind.String)
                    name = jsonProperty.GetString();
                if (rootObject.TryGetProperty(nameof(CustomName), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.String)
                    customName = jsonProperty.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("No name of color space specified.");
                
                // get white point
                var whitePoint = ((double, double, double)?)null;
                if (rootObject.TryGetProperty(nameof(WhitePoint), out jsonProperty) 
                    && jsonProperty.ValueKind == JsonValueKind.Array
                    && jsonProperty.GetArrayLength() == 3)
                {
                    double[] values = new double[3];
                    var index = 0;
                    foreach (var jsonValue in jsonProperty.EnumerateArray())
                        values[index++] = jsonValue.GetDouble();
                    whitePoint = (values[0], values[1], values[2]);
                }
                
                // get transfer function
                var transferFunc = SKColorSpaceTransferFn.Empty;
                if (rootObject.TryGetProperty("NumericalTransferFunction", out jsonProperty) 
                    && jsonProperty.ValueKind == JsonValueKind.Array
                    && jsonProperty.GetArrayLength() == 7)
                {
                    float[] values = new float[7];
                    var index = 0;
                    foreach (var jsonValue in jsonProperty.EnumerateArray())
                        values[index++] = jsonValue.GetSingle();
                    transferFunc = new SKColorSpaceTransferFn(values);
                }

                // get matrix to XYZ D50
                var colorSpaceXyz = new SKColorSpaceXyz();
                if (rootObject.TryGetProperty("MatrixToXyzD50", out jsonProperty) 
                    && jsonProperty.ValueKind == JsonValueKind.Array
                    && jsonProperty.GetArrayLength() == 9)
                {
                    float[] values = new float[9];
                    var index = 0;
                    foreach (var jsonValue in jsonProperty.EnumerateArray())
                        values[index++] = jsonValue.GetSingle();
                    colorSpaceXyz = new SKColorSpaceXyz(values);
                }
                else
                    throw new ArgumentException("No matrix to XYZ D50 of color space specified.");
                
                // create color space
                return new ColorSpace(ColorSpaceSource.UserDefined, name, customName, transferFunc, colorSpaceXyz, whitePoint, null);
            });
        });


        // Load color space from ICC profile.
        static ColorSpace LoadFromIccProfile(string? fileName, Stream stream, ColorSpaceSource source)
        {
            // check parameter
            if (source == ColorSpaceSource.BuiltIn)
                throw new ArgumentException();
            
            // read header
            var header = new byte[128];
            if (stream.Read(header, 0, 128) < 128)
                throw new ArgumentException("Invalid ICC profile header.");
            var profileSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan());
            if (profileSize >= 1L << 20)
                throw new ArgumentException($"Size of ICC profile is too large: {profileSize}.");
            
            // read profile to memory
            var profile = new byte[profileSize];
            Array.Copy(header, 0, profile, 0, 128);
            if (stream.Read(profile, 128, profile.Length - 128) < profile.Length - 128)
                throw new ArgumentException("Invalid ICC profile.");
            
            // parse profile
            var skiaColorSpace = SKColorSpace.CreateIcc(profile);
            if (skiaColorSpace == null)
                throw new ArgumentException("Unsupported ICC profile.");
            
            // prepare data reading functions
            /*
            double ReadSFixed16Number(ReadOnlySpan<byte> buffer)
            {
                var value = BinaryPrimitives.ReadUInt32BigEndian(buffer);
                if (value == 0)
                    return 0;
                var integer = (short)(value >> 16);
                var fractional = (value & 0xffff) / 65536.0;
                return integer + fractional;
            }
            */
            
            // get white point and name defined in profile
            var iccName = (string?)null;
            var whitePoint = ((double, double, double)?)null;
            var offset = 132;
            for (var i = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(128)); i > 0; --i, offset += 12)
            {
                var tag = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(offset));
                switch (tag)
                {
                    case 0x64657363u: // 'desc' description
                        {
                            // skip reading name
                            if (iccName != null)
                                continue;
                            
                            // move to data block
                            var dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(offset + 4));
                            var dataSize = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(offset + 8));
                            if (dataOffset < 0 || dataOffset + dataSize > profileSize)
                                continue;
                            
                            // read name
                            switch (BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(dataOffset)))
                            {
                                case 0x64657363u: // 'desc'
                                    {
                                        var strLength = (int)BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(dataOffset + 8));
                                        if (strLength > 1)
                                            iccName = Encoding.ASCII.GetString(profile, dataOffset + 12, strLength - 1);
                                    }
                                    break;
                                case 0x6d6c7563u: // 'mluc'
                                    {
                                        var langCount = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(dataOffset + 8));
                                        var enUsName = (string?)null;
                                        var enName = (string?)null;
                                        dataOffset += 16;
                                        for (var langIndex = langCount; langIndex > 0; --langIndex)
                                        {
                                            var lang = Encoding.ASCII.GetString(profile, dataOffset, 4);
                                            var strLength = (int)BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(dataOffset + 4)) >> 1;
                                            if (strLength <= 0)
                                                break;
                                            if (BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(dataOffset + 8)) != 0x1cu)
                                                break;
                                            var str = new char[strLength].Let(it =>
                                            {
                                                var charDataOffset = dataOffset + 12;
                                                for (var cIndex = 0; cIndex < strLength; ++cIndex, charDataOffset += 2)
                                                    it[cIndex] = (char)BinaryPrimitives.ReadUInt16BigEndian(profile.AsSpan(charDataOffset));
                                                return new string(it);
                                            });
                                            if (lang == "enUS")
                                                enUsName = str;
                                            else if (lang.StartsWith("en"))
                                                enName = str;
                                            else if (iccName == null)
                                                iccName = str;
                                        }
                                        if (iccName == null)
                                        {
                                            if (enUsName != null)
                                                iccName = enUsName;
                                            else if (enName != null)
                                                iccName = enName;
                                        }
                                    }
                                    break;
                            }
                            if (iccName != null)
                                iccName = iccName.Trim();
                        }
                        break;
                    
                    case 0x77747074: // 'wtpt' white point
                        /*
                        {
                            // move to data block
                            var dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(offset + 4));
                            var dataSize = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(offset + 8));
                            if (dataOffset < 0 || dataOffset + dataSize > profileSize)
                                continue;
                            
                            // read XYZ
                            if (BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(dataOffset)) == 0x58595a20) // 'XYZ '
                            {
                                var wpX = ReadSFixed16Number(profile.AsSpan(dataOffset + 8));
                                var wpY = ReadSFixed16Number(profile.AsSpan(dataOffset + 12));
                                var wpZ = ReadSFixed16Number(profile.AsSpan(dataOffset + 16));
                                whitePoint = (wpX, wpY, wpZ);
                            }
                        }
                        */
                        break;
                }
            }
            if (iccName == null && fileName != null)
                iccName = Path.GetFileNameWithoutExtension(fileName);
            
            // check whit point
            /*
            if (!whitePoint.HasValue)
            {
                logger?.LogWarning("No white point defined in ICC profile, use D65 as default");
                whitePoint = D65;
            }
            */

            // create color space
            var hasTransferFunc = skiaColorSpace.GetNumericalTransferFunction(out var transferFunc);
            if (!hasTransferFunc)
                transferFunc = SKColorSpaceTransferFn.Linear;
            return new ColorSpace(source, GenerateRandomName(), iccName, transferFunc, skiaColorSpace.ToColorSpaceXyz(), whitePoint, null);
        }


        /// <summary>
        /// Load ICC profile and create <see cref"ColorSpace"/>.
        /// </summary>
        /// <param name="stream">Stream to read ICC profile.</param>
        /// <param name="source">Source of color space.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of loading ICC profile.</returns>
        public static Task<ColorSpace> LoadFromIccProfileAsync(Stream stream, ColorSpaceSource source, CancellationToken cancellationToken = default) => ioTaskFactory.StartNew(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            return LoadFromIccProfile((stream as FileStream)?.Name, stream, source);
        });


        /// <summary>
        /// Load ICC profile and create <see cref"ColorSpace"/>.
        /// </summary>
        /// <param name="fileName">File name of ICC profile.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of loading ICC profile.</returns>
        public static Task<ColorSpace> LoadFromIccProfileAsync(string fileName, CancellationToken cancellationToken = default) => ioTaskFactory.StartNew(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            if (!CarinaStudio.IO.File.TryOpenRead(fileName, 5000, out var stream) || stream == null)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                throw new IOException($"Unable to open file '{fileName}'.");
            }
            return stream.Use(it => LoadFromIccProfile(fileName, it, ColorSpaceSource.UserDefined));
        });
        

        /// <summary>
        /// Get name of color space.
        /// </summary>
        public string Name { get; }


        // Numerical transfer to non-linear color.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe long NumericalTransferFromLinear(long color)
        {
            var table = this.numericalTransferTableFromLinear;
            if (table == null)
            {
                lock (this)
                {
                    table = this.numericalTransferTableFromLinear;
                    if (table == null)
                    {
                        table = (long*)NativeMemory.Alloc(sizeof(long) * (QuantizationSteps + 1));
                        var transferFunc = this.numericalTransferFuncFromLinear;
                        for (var i = QuantizationSteps; i >= 0; --i)
                            table[i] = (long)(transferFunc.Transform((float)i / QuantizationSteps) * QuantizationSteps + 0.5);
                        this.numericalTransferTableFromLinear = table;
                    }
                }
            }
            return table[color];
        }


        // Numerical transfer to linear color. 
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe long NumericalTransferToLinear(long color)
        {
            var table = this.numericalTransferTableToLinear;
            if (table == null)
            {
                lock (this)
                {
                    table = this.numericalTransferTableToLinear;
                    if (table == null)
                    {
                        table = (long*)NativeMemory.Alloc(sizeof(long) * (QuantizationSteps + 1));
                        var transferFunc = this.numericalTransferFuncToLinear;
                        for (var i = QuantizationSteps; i >= 0; --i)
                            table[i] = (long)(transferFunc.Transform((float)i / QuantizationSteps) * QuantizationSteps + 0.5);
                        this.numericalTransferTableToLinear = table;
                    }
                }
            }
            return table[color];
        }


        // Called when property of custom color space changed.
        static void OnCustomColorSpacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ColorSpace colorSpace || !userDefinedColorSpaces.ContainsKey(colorSpace.Name))
                return;
            var app = ColorSpace.app;
            if (app == null)
                return;
            if (e.PropertyName == nameof(CustomName))
                _ = colorSpace.SaveToFileAsync(Path.Combine(app.RootPrivateDirectoryPath, "ColorSpaces", $"{colorSpace.Name}.json"));
        }


        /// <summary>
        /// Raised when property changed.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;


        // Quantize color.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long Quantize(double color)
        {
            if (color < 0)
                return 0;
            if (color > 1)
                return QuantizationSteps;
            return (long)(color * QuantizationSteps + 0.5);
        }


        // Quantize matrix of XYZ color space.
        static long[] Quantize(SKColorSpaceXyz matrix) => new long[9]
        {
            (long)((double)matrix[0, 0] * QuantizationSteps + 0.5), (long)((double)matrix[1, 0] * QuantizationSteps + 0.5), (long)((double)matrix[2, 0] * QuantizationSteps + 0.5),
            (long)((double)matrix[0, 1] * QuantizationSteps + 0.5), (long)((double)matrix[1, 1] * QuantizationSteps + 0.5), (long)((double)matrix[2, 1] * QuantizationSteps + 0.5),
            (long)((double)matrix[0, 2] * QuantizationSteps + 0.5), (long)((double)matrix[1, 2] * QuantizationSteps + 0.5), (long)((double)matrix[2, 2] * QuantizationSteps + 0.5),
        };


        /// <summary>
        /// Remove user-defined color space.
        /// </summary>
        /// <param name="colorSpace">Color space to remove.</param>
        /// <returns>True if color space has been removed successfully.</returns>
        public static bool RemoveUserDefinedColorSpace(ColorSpace colorSpace)
        {
            var app = ColorSpace.app;
            if (app == null)
                return false;
            if (userDefinedColorSpaces.Remove(colorSpace.Name))
            {
                // reset settings
                if (app.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName) == colorSpace.Name)
                    app.Settings.ResetValue(SettingKeys.DefaultColorSpaceName);
                if (app.Settings.GetValueOrDefault(SettingKeys.ScreenColorSpaceName) == colorSpace.Name)
                    app.Settings.ResetValue(SettingKeys.ScreenColorSpaceName);

                // detach from color space
                colorSpace.PropertyChanged -= OnCustomColorSpacePropertyChanged;

                // raise event
                RemovingUserDefinedColorSpace?.Invoke(null, new ColorSpaceEventArgs(colorSpace));

                // delete file
                var fileName = Path.Combine(app.RootPrivateDirectoryPath, "ColorSpaces", $"{colorSpace.Name}.json");
                _ = ioTaskFactory.StartNew(() =>
                    Global.RunWithoutError(() => File.Delete(fileName)));
                
                // remove from list
                userDefinedColorSpaceList.Remove(colorSpace);
                allColorSpaceList.Remove(colorSpace);
                return true;
            }
            return false;
        }


        /// <summary>
        /// Raise before removing user-defined color space.
        /// </summary>
        public static event EventHandler<ColorSpaceEventArgs>? RemovingUserDefinedColorSpace;


        /// <summary>
        /// Convert to L*a*b* D50 color space.
        /// </summary>
        /// <param name="r">R.</param>
        /// <param name="g">G.</param>
        /// <param name="b">B.</param>
        /// <returns>L*a*b* color.</returns>
        public (double, double, double) RgbToLab(byte r, byte g, byte b) =>
            XyzToLab(this.RgbToXyz(r, g, b));
        

        /// <summary>
        /// Convert to L*a*b* D50 color space.
        /// </summary>
        /// <param name="r">R.</param>
        /// <param name="g">G.</param>
        /// <param name="b">B.</param>
        /// <returns>L*a*b* color.</returns>
        public (double, double, double) RgbToLab(ushort r, ushort g, ushort b) =>
            XyzToLab(this.RgbToXyz(r, g, b));


        /// <summary>
        /// Convert to L*a*b* D50 color space.
        /// </summary>
        /// <param name="r">Normalized R.</param>
        /// <param name="g">Normalized G.</param>
        /// <param name="b">Normalized B.</param>
        /// <returns>L*a*b* color.</returns>
        public (double, double, double) RgbToLab(double r, double g, double b) =>
            XyzToLab(this.RgbToXyz(r, g, b));
        

        /// <summary>
        /// Convert to CIE xy chromaticity.
        /// </summary>
        /// <param name="r">R.</param>
        /// <param name="g">G.</param>
        /// <param name="b">B.</param>
        /// <returns>xy chromaticity.</returns>
        public (double, double) RgbToXyChromaticity(byte r, byte g, byte b) =>
            XyzToXyChromaticity(this.RgbToXyz(r, g, b));
        

        /// <summary>
        /// Convert to CIE xy chromaticity.
        /// </summary>
        /// <param name="r">R.</param>
        /// <param name="g">G.</param>
        /// <param name="b">B.</param>
        /// <returns>xy chromaticity.</returns>
        public (double, double) RgbToXyChromaticity(ushort r, ushort g, ushort b) =>
            XyzToXyChromaticity(this.RgbToXyz(r, g, b));


        /// <summary>
        /// Convert to CIE xy chromaticity.
        /// </summary>
        /// <param name="r">Normalized R.</param>
        /// <param name="g">Normalized G.</param>
        /// <param name="b">Normalized B.</param>
        /// <returns>xy chromaticity.</returns>
        public (double, double) RgbToXyChromaticity(double r, double g, double b) =>
            XyzToXyChromaticity(this.RgbToXyz(r, g, b));
        

        /// <summary>
        /// Convert from 8-bit RGB to XYZ D50 color space.
        /// </summary>
        /// <param name="r">R.</param>
        /// <param name="g">G.</param>
        /// <param name="b">B.</param>
        /// <returns>XYZ color.</returns>
        public unsafe (double, double, double) RgbToXyz(byte r, byte g, byte b)
        {
            var qR = mappingTableFrom8Bit[r];
            var qG = mappingTableFrom8Bit[g];
            var qB = mappingTableFrom8Bit[b];
            if (this.hasTransferFunc)
            {
                qR = this.NumericalTransferToLinear(qR);
                qG = this.NumericalTransferToLinear(qG);
                qB = this.NumericalTransferToLinear(qB);
            }
            var m = this.matrixToXyz;
            return (
                (m[0] * qR + m[1] * qG + m[2] * qB) / QuantizationSteps2,
                (m[3] * qR + m[4] * qG + m[5] * qB) / QuantizationSteps2,
                (m[6] * qR + m[7] * qG + m[8] * qB) / QuantizationSteps2
            );
        }


        /// <summary>
        /// Convert from 16-bit RGB to XYZ D50 color space.
        /// </summary>
        /// <param name="r">R.</param>
        /// <param name="g">G.</param>
        /// <param name="b">B.</param>
        /// <returns>XYZ color.</returns>
        public unsafe (double, double, double) RgbToXyz(ushort r, ushort g, ushort b)
        {
            var qR = mappingTableFrom16Bit[r];
            var qG = mappingTableFrom16Bit[g];
            var qB = mappingTableFrom16Bit[b];
            if (this.hasTransferFunc)
            {
                qR = this.NumericalTransferToLinear(qR);
                qG = this.NumericalTransferToLinear(qG);
                qB = this.NumericalTransferToLinear(qB);
            }
            var m = this.matrixToXyz;
            return (
                (m[0] * qR + m[1] * qG + m[2] * qB) / QuantizationSteps2,
                (m[3] * qR + m[4] * qG + m[5] * qB) / QuantizationSteps2,
                (m[6] * qR + m[7] * qG + m[8] * qB) / QuantizationSteps2
            );
        }


        /// <summary>
        /// Convert from RGB to XYZ D50 color space.
        /// </summary>
        /// <param name="r">Normalized R.</param>
        /// <param name="g">Normalized G.</param>
        /// <param name="b">Normalized B.</param>
        /// <returns>XYZ color.</returns>
        public (double, double, double) RgbToXyz(double r, double g, double b)
        {
            var qR = Quantize(r);
            var qG = Quantize(g);
            var qB = Quantize(b);
            if (this.hasTransferFunc)
            {
                qR = this.NumericalTransferToLinear(qR);
                qG = this.NumericalTransferToLinear(qG);
                qB = this.NumericalTransferToLinear(qB);
            }
            var m = this.matrixToXyz;
            return (
                (m[0] * qR + m[1] * qG + m[2] * qB) / 4294967296.0,
                (m[3] * qR + m[4] * qG + m[5] * qB) / 4294967296.0,
                (m[6] * qR + m[7] * qG + m[8] * qB) / 4294967296.0
            );
        }


        /// <summary>
        /// Save color space to file.
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task of saving to file.</returns>
        public Task SaveToFileAsync(string fileName, CancellationToken cancellationToken = default) => ioTaskFactory.StartNew(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();
            var directoryName = Path.GetDirectoryName(fileName);
            if (directoryName != null && !Directory.Exists(directoryName))
                Directory.CreateDirectory(directoryName);
            if (!CarinaStudio.IO.File.TryOpenWrite(fileName, 5000, out var stream) || stream == null)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                throw new IOException($"Unable to open file '{fileName}' to save color space.");
            }
            logger?.LogTrace($"Save color space '{this.Name}' to '{fileName}'");
            using (stream)
            {
                using var jsonWriter = new Utf8JsonWriter(stream, new JsonWriterOptions(){ Indented = true });
                jsonWriter.WriteStartObject();
                jsonWriter.WriteString(nameof(Name), this.Name);
                this.customName?.Let(it =>
                    jsonWriter.WriteString(nameof(CustomName), it));
                this.WhitePoint?.Let(wp =>
                {
                    jsonWriter.WritePropertyName(nameof(WhitePoint));
                    jsonWriter.WriteStartArray();
                    jsonWriter.WriteNumberValue(wp.Item1);
                    jsonWriter.WriteNumberValue(wp.Item2);
                    jsonWriter.WriteNumberValue(wp.Item3);
                    jsonWriter.WriteEndArray();
                });
                if (this.hasTransferFunc)
                {
                    var transferFunc = this.numericalTransferFuncToLinear;
                    jsonWriter.WritePropertyName("NumericalTransferFunction");
                    jsonWriter.WriteStartArray();
                    jsonWriter.WriteNumberValue(transferFunc.G);
                    jsonWriter.WriteNumberValue(transferFunc.A);
                    jsonWriter.WriteNumberValue(transferFunc.B);
                    jsonWriter.WriteNumberValue(transferFunc.C);
                    jsonWriter.WriteNumberValue(transferFunc.D);
                    jsonWriter.WriteNumberValue(transferFunc.E);
                    jsonWriter.WriteNumberValue(transferFunc.F);
                    jsonWriter.WriteEndArray();
                }
                this.skiaColorSpaceXyz.Let(it =>
                {
                    jsonWriter.WritePropertyName("MatrixToXyzD50");
                    jsonWriter.WriteStartArray();
                    foreach (var value in it.Values)
                        jsonWriter.WriteNumberValue(value);
                    jsonWriter.WriteEndArray();
                });
                jsonWriter.WriteEndObject();
            }
        });


        /// <summary>
        /// Get source of color space.
        /// </summary>
        public ColorSpaceSource Source { get; }


        /// <summary>
        /// Get all user-defined color spaces.
        /// </summary>
        /// <value></value>
        public static IList<ColorSpace> UserDefinedColorSpaces { get; }


        /// <summary>
        /// Convert to <see cref="SKColorSpace"/>.
        /// </summary>
        /// <returns><see cref="SKColorSpace"/>.</returns>
        public SKColorSpace ToSkiaColorSpace() => 
            SKColorSpace.CreateRgb(this.numericalTransferFuncToLinear, this.skiaColorSpaceXyz);


        /// <inheritdoc/>
        public override string ToString() => this.CustomName ?? this.Name;


        /// <summary>
        /// Try get built-in color space which is almost same as given color space.
        /// </summary>
        /// <param name="reference">Given color space.</param>
        /// <param name="colorSpace">Found built-in color space, or <see cref="Default"/> if specific color space cannot be found.</param>
        /// <returns>True if specific color space can be found.</returns>
        public static bool TryGetBuiltInColorSpace(ColorSpace reference, out ColorSpace colorSpace)
        {
            if (reference.IsBuiltIn)
            {
                colorSpace = reference;
                return true;
            }
            foreach (var candidate in builtInColorSpaces.Values)
            {
                if (candidate.numericalTransferFuncToLinear.Equals(reference.numericalTransferFuncToLinear)
                    && candidate.skiaColorSpaceXyz.Equals(reference.skiaColorSpaceXyz))
                {
                    colorSpace = candidate;
                    return true;
                }
            }
            colorSpace = Default;
            return false;
        }


        /// <summary>
        /// Try get color space by name.
        /// </summary>
        /// <param name="name">Name of color space.</param>
        /// <param name="colorSpace">Found color space, or <see cref="Default"/> if specific color space cannot be found.</param>
        /// <returns>True if specific color space can be found.</returns>
        public static bool TryGetColorSpace(string name, out ColorSpace colorSpace)
        {
            if (builtInColorSpaces.TryGetValue(name, out var value))
            {
                colorSpace = value;
                return true;
            }
            if (userDefinedColorSpaces.TryGetValue(name, out value))
            {
                colorSpace = value;
                return true;
            }
            colorSpace = Default;
            return false;
        }


        /// <summary>
        /// Try get color space which is almost same as given color space.
        /// </summary>
        /// <param name="reference">Given color space.</param>
        /// <param name="colorSpace">Found color space, or <see cref="Default"/> if specific color space cannot be found.</param>
        /// <returns>True if specific color space can be found.</returns>
        public static bool TryGetColorSpace(ColorSpace reference, out ColorSpace colorSpace)
        {
            if (TryGetBuiltInColorSpace(reference, out colorSpace))
                return true;
            foreach (var candidate in userDefinedColorSpaceList)
            {
                if (candidate.numericalTransferFuncToLinear.Equals(reference.numericalTransferFuncToLinear)
                    && candidate.skiaColorSpaceXyz.Equals(reference.skiaColorSpaceXyz))
                {
                    colorSpace = candidate;
                    return true;
                }
            }
            colorSpace = Default;
            return false;
        }


        /// <summary>
        /// Get URI to document of color space.
        /// </summary>
        public Uri? Uri { get; }


        /// <summary>
        /// Wait for IO tasks complete.
        /// </summary>
        /// <returns>Task of waiting.</returns>
        public static Task WaitForIOTasksAsync() => ioTaskFactory.StartNew(() => logger?.LogDebug("All I/O tasks completed"));


        /// <summary>
        /// Get XYZ of white point.
        /// </summary>
        public (double, double, double)? WhitePoint { get; }


        /// <summary>
        /// Convert from CIE xy chromaticity to Correlated Color Temperature.
        /// </summary>
        /// <param name="xy">xy chromaticity.</param>
        /// <returns>Correlated Color Temperature.</returns>
        public static double XyChromaticityToCct((double, double) xy) =>
            XyChromaticityToCct(xy.Item1, xy.Item2);


        /// <summary>
        /// Convert from CIE xy chromaticity to Correlated Color Temperature.
        /// </summary>
        /// <param name="x">x.</param>
        /// <param name="y">y.</param>
        /// <returns>Correlated Color Temperature.</returns>
        public static double XyChromaticityToCct(double x, double y)
        {
            var n = (x - 0.3320) / (y - 0.1858);
            var n2 = n * n;
            return Math.Floor(-449 * n2 * n + 3525 * n2 - 6823.3 * n + 5520.33);
        }


        /// <summary>
        /// Convert from XYZ D50 color space to L*a*b* D50 color space.
        /// </summary>
        /// <param name="xyz">XYZ color.</param>
        /// <returns>L*a*b* color.</returns>
        public static (double, double, double) XyzToLab((double, double, double) xyz) =>
            XyzToLab(xyz.Item1, xyz.Item2, xyz.Item3);


        /// <summary>
        /// Convert from XYZ D50 color space to L*a*b* D50 color space.
        /// </summary>
        /// <param name="x">X.</param>
        /// <param name="y">Y.</param>
        /// <param name="z">Z.</param>
        /// <returns>L*a*b* color.</returns>
        public static (double, double, double) XyzToLab(double x, double y, double z)
        {
            double Convert(double t) // https://en.wikipedia.org/wiki/CIELAB_color_space
            {
                if (t > 0.008856451679036)
                    return Math.Pow(t, 0.3333);
                return (t / 0.128418549346017) + 0.137931034482759;
            }
            var labL = 116 * Convert(y) - 16; // [0, 100]
            var labA = 500 * (Convert(x / 0.964212) - Convert(y)); // [-128, 128]
            var labB = 200 * (Convert(y) - Convert(z / 0.825188)); // [-128, 128]
            return (labL / 100, labA / 128, labB / 128);
        }


        /// <summary>
        /// Convert from XYZ D50 color space to RGB.
        /// </summary>
        /// <param name="x">X.</param>
        /// <param name="y">Y.</param>
        /// <param name="z">Z.</param>
        /// <returns>Normalized RGB color.</returns>
        public (double, double, double) XyzToRgb(double x, double y, double z)
        {
            var m = this.matrixFromXyz;
            var qX = (long)(x * QuantizationSteps + 0.5);
            var qY = (long)(y * QuantizationSteps + 0.5);
            var qZ = (long)(z * QuantizationSteps + 0.5);
            var qR = Clip((m[0] * qX + m[1] * qY + m[2] * qZ) >> QuantizationBits);
            var qG = Clip((m[3] * qX + m[4] * qY + m[5] * qZ) >> QuantizationBits);
            var qB = Clip((m[6] * qX + m[7] * qY + m[8] * qZ) >> QuantizationBits);
            if (this.hasTransferFunc)
            {
                qR = this.NumericalTransferFromLinear(qR);
                qG = this.NumericalTransferFromLinear(qG);
                qB = this.NumericalTransferFromLinear(qB);
            }
            return ((double)qR / QuantizationBits, (double)qG / QuantizationBits, (double)qB / QuantizationBits);
        }


        /// <summary>
        /// Convert from CIE XYZ to CIE xy chromaticity.
        /// </summary>
        /// <param name="xyz">XYZ color.</param>
        /// <returns>xy chromaticity.</returns>
        public static (double, double) XyzToXyChromaticity((double, double, double) xyz) =>
            XyzToXyChromaticity(xyz.Item1, xyz.Item2, xyz.Item3);


        /// <summary>
        /// Convert from CIE XYZ to CIE xy chromaticity.
        /// </summary>
        /// <param name="x">X.</param>
        /// <param name="y">Y.</param>
        /// <param name="z">Z.</param>
        /// <returns>xy chromaticity.</returns>
        public static (double, double) XyzToXyChromaticity(double x, double y, double z)
        {
            var xyz = (x + y + z);
            if (Math.Abs(xyz) >= 0.00000001)
                return (x / xyz, y / xyz);
            xyz = D50.Item1 + D50.Item2 + D50.Item3;
            return (D50.Item1 / xyz, D50.Item2 / xyz);
        }
    }


    /// <summary>
    /// Data for events relate to <see cref="ColorSpace"/>.
    /// </summary>
    class ColorSpaceEventArgs : EventArgs
    {
        /// <summary>
        /// Initialize new <see cref="ColorSpaceEventArgs"/> instance.
        /// </summary>
        /// <param name="colorSpace"><see cref="ColorSpace"/>.</param>
        public ColorSpaceEventArgs(ColorSpace colorSpace) =>
            this.ColorSpace = colorSpace;
        

        /// <summary>
        /// Get <see cref="ColorSpace"/>.
        /// </summary>
        public ColorSpace ColorSpace { get; }
    }


    /// <summary>
    /// Source of <see cref="ColorSpace"/>.
    /// </summary>
    enum ColorSpaceSource
    {
        /// <summary>
        /// Built-in.
        /// </summary>
        BuiltIn,
        /// <summary>
        /// System.
        /// </summary>
        SystemDefined,
        /// <summary>
        /// Embedded in file.
        /// </summary>
        Embedded,
        /// <summary>
        /// User defined.
        /// </summary>
        UserDefined,
    }
}