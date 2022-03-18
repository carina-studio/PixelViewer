using System.IO;
using System.Linq;
using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.Threading.Tasks;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
            readonly ColorSpace fromColorSpace;
            readonly bool isIdentical;
            readonly long[] matrix;
            readonly ColorSpace toColorSpace;

            /// <summary>
            /// Initialize new <see cref="Converter"/> instance.
            /// </summary>
            /// <param name="fromColorSpace">Source color space.</param>
            /// <param name="toColorSpace">Target color space.</param>
            public Converter(ColorSpace fromColorSpace, ColorSpace toColorSpace)
            {
                this.fromColorSpace = fromColorSpace;
                this.toColorSpace = toColorSpace;
                this.isIdentical = fromColorSpace.Equals(toColorSpace);
                if (!this.isIdentical)
                {
                    var m1 = this.fromColorSpace.skiaColorSpace.ToColorSpaceXyz();
                    var m2 = this.toColorSpace.skiaColorSpace.ToColorSpaceXyz().Invert();
                    this.matrix = Quantize(SKColorSpaceXyz.Concat(m1, m2));
                }
                else
                    this.matrix = new long[0];
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
                if (this.fromColorSpace.hasTransferFunc)
                {
                    qR = this.fromColorSpace.NumericalTransferFromRgb(qR);
                    qG = this.fromColorSpace.NumericalTransferFromRgb(qG);
                    qB = this.fromColorSpace.NumericalTransferFromRgb(qB);
                }
                var m = this.matrix;
                qR = Clip((m[0] * qR + m[1] * qG + m[2] * qB) >> 16);
                qG = Clip((m[3] * qR + m[4] * qG + m[5] * qB) >> 16);
                qB = Clip((m[6] * qR + m[7] * qG + m[8] * qB) >> 16);
                if (this.toColorSpace.hasTransferFunc)
                {
                    qR = this.toColorSpace.NumericalTransferToRgb(qR);
                    qG = this.toColorSpace.NumericalTransferToRgb(qG);
                    qB = this.toColorSpace.NumericalTransferToRgb(qB);
                }
                return (qR / 65536.0, qG / 65536.0, qB / 65536.0);
            }
        }


        /// <summary>
        /// Adobe RGB (1998).
        /// </summary>
        public static readonly ColorSpace AdobeRGB_1998 = new ColorSpace("Adobe-RGB-1998", null, SKColorSpace.CreateRgb(SKColorSpaceTransferFn.TwoDotTwo, SKColorSpaceXyz.AdobeRgb), true);
        /// <summary>
        /// ITU-R BT.2020.
        /// </summary>
        public static readonly ColorSpace BT_2020 = new ColorSpace("BT.2020", null, SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Rec2020, SKColorSpaceXyz.Rec2020), true);
        /// <summary>
        /// ITU-R BT.601 525-line.
        /// </summary>
        public static readonly ColorSpace BT_601_525Line = new ColorSpace("BT.601-525-line", null, SKColorSpace.CreateRgb(new SKColorSpaceTransferFn()
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
            )), true);
        /// <summary>
        /// ITU-R BT.601 625-line.
        /// </summary>
        public static readonly ColorSpace BT_601_625Line = new ColorSpace("BT.601-625-line", null, SKColorSpace.CreateRgb(new SKColorSpaceTransferFn()
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
            )), true);
        /// <summary>
        /// DCI-P3 (D63).
        /// </summary>
#pragma warning disable CS0618
        public static readonly ColorSpace DCI_P3 = new ColorSpace("DCI-P3", null, SKColorSpace.CreateRgb(new SKColorSpaceTransferFn() { G = 2.6f, A = 1.0f }, SKColorSpaceXyz.Dcip3), true);
#pragma warning restore CS0618
        /// <summary>
        /// Default color space.
        /// </summary>
        public static readonly ColorSpace Default;
        /// <summary>
        /// Display-P3 (P3-D65).
        /// </summary>
        public static readonly ColorSpace Display_P3 = new ColorSpace("Display-P3", null, SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3), true);
        /// <summary>
        /// Dolby vision.
        /// </summary>
        public static readonly ColorSpace Dolby_Vision = new ColorSpace("Dolby-Vision", null, SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Pq, SKColorSpaceXyz.Rec2020), true);
        /// <summary>
        /// HLG10.
        /// </summary>
        public static readonly ColorSpace Hlg10 = new ColorSpace("HLG10", null, SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Hlg, SKColorSpaceXyz.Rec2020), true);
        /// <summary>
        /// Linear sRGB.
        /// </summary>
        public static readonly ColorSpace LinearSrgb = new ColorSpace("Linear-sRGB", null, SKColorSpace.CreateSrgbLinear(), true);
        /// <summary>
        /// sRGB.
        /// </summary>
        public static readonly ColorSpace Srgb = new ColorSpace("sRGB", null, SKColorSpace.CreateSrgb(), true);


        // Static fields.
        static readonly Dictionary<string, ColorSpace> builtInColorSpaces = new()
        {
            { AdobeRGB_1998.Name, AdobeRGB_1998 },
            { BT_2020.Name, BT_2020 },
            { BT_601_525Line.Name, BT_601_525Line },
            { BT_601_625Line.Name, BT_601_625Line },
            { DCI_P3.Name, DCI_P3 },
            { Display_P3.Name, Display_P3 },
            { Dolby_Vision.Name, Dolby_Vision },
            { Hlg10.Name, Hlg10 },
            { LinearSrgb.Name, LinearSrgb },
            { Srgb.Name, Srgb },
        };
        static readonly TaskFactory ioTaskFactory = new TaskFactory(new FixedThreadsTaskScheduler(1));
        static readonly Random random = new();


        // Fields.
        string? customName;
        readonly bool hasTransferFunc;
        readonly long[] matrixFromXyz;
        readonly long[] matrixToXyz;
        readonly SKColorSpaceTransferFn numericalTransferFuncFromRgb;
        readonly SKColorSpaceTransferFn numericalTransferFuncToRgb;
        volatile long[]? numericalTransferTableFromRgb;
        volatile long[]? numericalTransferTableToRgb;
        readonly SKColorSpace skiaColorSpace;
        readonly SKColorSpaceXyz skiaColorSpaceXyz;


        // Static initializer.
        static ColorSpace()
        {
            BuiltInColorSpaces = builtInColorSpaces.Values.ToArray().AsReadOnly();
            Default = Srgb;
        }


        // Constructor.
        ColorSpace(string name, string? customName, SKColorSpace colorSpace, bool isBuiltIn)
        {
            this.customName = customName;
            this.skiaColorSpace = colorSpace;
            this.hasTransferFunc = colorSpace.GetNumericalTransferFunction(out this.numericalTransferFuncFromRgb);
            if (this.hasTransferFunc)
                this.numericalTransferFuncToRgb = this.numericalTransferFuncFromRgb.Invert();
            this.IsBuiltIn = isBuiltIn;
            this.skiaColorSpaceXyz = colorSpace.ToColorSpaceXyz();
            this.matrixToXyz = Quantize(this.skiaColorSpaceXyz);
            this.matrixFromXyz = Quantize(this.skiaColorSpaceXyz.Invert());
            this.Name = name;
        }


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
            if (color > 65536)
                return 65536;
            return color;
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
            }
        }


        /// <inheritdoc/>
        public bool Equals(ColorSpace? colorSpace) =>
            colorSpace is not null 
            && this.numericalTransferFuncFromRgb.Equals(colorSpace.numericalTransferFuncFromRgb)
            && this.skiaColorSpaceXyz.Equals(colorSpace.skiaColorSpaceXyz);


        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is ColorSpace colorSpace)
                return this.Equals(colorSpace);
            return false;
        }


        /// <summary>
        /// Convert from XYZ D50 color space.
        /// </summary>
        /// <param name="x">X.</param>
        /// <param name="y">Y.</param>
        /// <param name="z">Z.</param>
        /// <returns>Normalized RGB color.</returns>
        public (double, double, double) FromXyz(double x, double y, double z)
        {
            var m = this.matrixFromXyz;
            var qX = (long)(x * 65536 + 0.5);
            var qY = (long)(y * 65536 + 0.5);
            var qZ = (long)(z * 65536 + 0.5);
            var qR = Clip((m[0] * qX + m[1] * qY + m[2] * qZ) >> 16);
            var qG = Clip((m[3] * qX + m[4] * qY + m[5] * qZ) >> 16);
            var qB = Clip((m[6] * qX + m[7] * qY + m[8] * qZ) >> 16);
            if (this.hasTransferFunc)
            {
                qR = this.NumericalTransferToRgb(qR);
                qG = this.NumericalTransferToRgb(qG);
                qB = this.NumericalTransferToRgb(qB);
            }
            return (qR / 65536.0, qG / 65536.0, qB / 65536.0);
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
        /// Check whether the color space is buil-in or not.
        /// </summary>
        public bool IsBuiltIn { get; }


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
                return new ColorSpace(name, customName, SKColorSpace.CreateRgb(transferFunc, colorSpaceXyz), false);
            });
        });


        // Load color space from ICC profile.
        static ColorSpace LoadFromIccProfile(string? fileName, Stream stream)
        {
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
            
            // get name defined in profile
            var iccName = (string?)null;
            var offset = 132;
            for (var i = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(128)); i > 0; --i, offset += 12)
            {
                if (BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(offset)) == 0x64657363u) // description
                {
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
                        break;
                }
            }
            if (iccName == null && fileName != null)
                iccName = Path.GetFileName(fileName);

            // create color space
            return new ColorSpace(GenerateRandomName(), iccName, skiaColorSpace, false);
        }


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
            return stream.Use(it => LoadFromIccProfile(fileName, it));
        });
        

        /// <summary>
        /// Get name of color space.
        /// </summary>
        public string Name { get; }


        // Numerical transfer from RGB.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        long NumericalTransferFromRgb(long color)
        {
            var table = this.numericalTransferTableFromRgb;
            if (table == null)
            {
                table = new long[65537];
                var transferFunc = this.numericalTransferFuncFromRgb;
                for (var i = 65536; i >= 0; --i)
                    table[i] = (long)(transferFunc.Transform(i / 65536f) * 65536 + 0.5);
                this.numericalTransferTableFromRgb = table;
            }
            return table[color];
        }


        // Numerical transfer to RGB.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        long NumericalTransferToRgb(long color)
        {
            var table = this.numericalTransferTableToRgb;
            if (table == null)
            {
                table = new long[65537];
                var transferFunc = this.numericalTransferFuncToRgb;
                for (var i = 65536; i >= 0; --i)
                    table[i] = (long)(transferFunc.Transform(i / 65536f) * 65536 + 0.5);
                this.numericalTransferTableToRgb = table;
            }
            return table[color];
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
                return 65536;
            return (long)(color * 65536 + 0.5);
        }


        // Quantize matrix of XYZ color space.
        static long[] Quantize(SKColorSpaceXyz matrix) => new long[9]
        {
            (long)(matrix[0, 0] * 65536 + 0.5), (long)(matrix[1, 0] * 65536 + 0.5), (long)(matrix[2, 0] * 65536 + 0.5),
            (long)(matrix[0, 1] * 65536 + 0.5), (long)(matrix[1, 1] * 65536 + 0.5), (long)(matrix[2, 1] * 65536 + 0.5),
            (long)(matrix[0, 2] * 65536 + 0.5), (long)(matrix[1, 2] * 65536 + 0.5), (long)(matrix[2, 2] * 65536 + 0.5),
        };


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
            if (!CarinaStudio.IO.File.TryOpenWrite(fileName, 5000, out var stream) || stream == null)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                throw new IOException($"Unable to open file '{fileName}' to save color space.");
            }
            using (stream)
            {
                using var jsonWriter = new Utf8JsonWriter(stream, new JsonWriterOptions(){ Indented = true });
                jsonWriter.WriteStartObject();
                jsonWriter.WriteString(nameof(Name), this.Name);
                this.customName?.Let(it =>
                    jsonWriter.WriteString(nameof(CustomName), it));
                if (this.skiaColorSpace.GetNumericalTransferFunction(out var transferFunc))
                {
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
        /// Convert to L*a*b* D50 color space.
        /// </summary>
        /// <param name="r">Normalized R.</param>
        /// <param name="g">Normalized G.</param>
        /// <param name="b">Normalized B.</param>
        /// <returns>L*a*b* color.</returns>
        public (double, double, double) ToLab(double r, double g, double b)
        {
            var (x, y, z) = this.ToXyz(r, g, b);
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


        /// <inheritdoc/>
        public override string ToString() => this.CustomName ?? this.Name;


        /// <summary>
        /// Convert to XYZ D50 color space.
        /// </summary>
        /// <param name="r">Normalized R.</param>
        /// <param name="g">Normalized G.</param>
        /// <param name="b">Normalized B.</param>
        /// <returns>XYZ color.</returns>
        public (double, double, double) ToXyz(double r, double g, double b)
        {
            var qR = Quantize(r);
            var qG = Quantize(g);
            var qB = Quantize(b);
            if (this.hasTransferFunc)
            {
                qR = this.NumericalTransferFromRgb(qR);
                qG = this.NumericalTransferFromRgb(qG);
                qB = this.NumericalTransferFromRgb(qB);
            }
            var m = this.matrixToXyz;
            return (
                (m[0] * qR + m[1] * qG + m[2] * qB) / 4294967296.0,
                (m[3] * qR + m[4] * qG + m[5] * qB) / 4294967296.0,
                (m[6] * qR + m[7] * qG + m[8] * qB) / 4294967296.0
            );
        }


        /// <summary>
        /// Try get built-in color space by name.
        /// </summary>
        /// <param name="name">Name of color space.</param>
        /// <param name="colorSpace">Found color space, or <see cref="Default"/> if specific color space cannot be found.</param>
        /// <returns>True if specific color space can be found.</returns>
        public static bool TryGetBuiltInColorSpace(string name, out ColorSpace colorSpace)
        {
            if (builtInColorSpaces.TryGetValue(name, out var value))
            {
                colorSpace = value;
                return true;
            }
            colorSpace = Default;
            return false;
        }


        /// <summary>
        /// Try get built-in color space which is almost same as given color space.
        /// </summary>
        /// <param name="reference">Given color space.</param>
        /// <param name="colorSpace">Found color space, or <see cref="Default"/> if specific color space cannot be found.</param>
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
                if (candidate.Equals(reference))
                {
                    colorSpace = candidate;
                    return true;
                }
            }
            colorSpace = Default;
            return false;
        }
    }
}