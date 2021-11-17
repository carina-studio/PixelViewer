using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.IO;
using CarinaStudio.Threading;
using CarinaStudio.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.Profiles
{
    /// <summary>
    /// Profile of image rendering.
    /// </summary>
    class ImageRenderingProfile : BaseDisposable, IApplicationObject, INotifyPropertyChanged
    {
        /// <summary>
        /// <see cref="TaskFactory"/> for I/O operations.
        /// </summary>
        public static readonly TaskFactory IOTaskFactory = new TaskFactory(new FixedThreadsTaskScheduler(1));


        // Static fields.
        static volatile IApplication? app;
        static volatile ImageRenderingProfile? defaultProfile;
        static volatile string? directoryPath;
        static readonly IList<int> emptyEffectiveBits = new int[4].AsReadOnly();
        static volatile ILogger? logger;


        // Fields.
        ByteOrdering byteOrdering = ByteOrdering.BigEndian;
        long dataOffset;
        bool demosaicing = true;
        IList<int> effectiveBits = emptyEffectiveBits;
        readonly FileFormat? fileFormat;
        string? fileName;
        long framePaddingSize;
        int height = 1;
        string name = "";
        IList<int> pixelStrides = emptyEffectiveBits;
        ImageRenderers.IImageRenderer? renderer;
        IList<int> rowStrides = emptyEffectiveBits;
        int width = 1;
        YuvConversionMode yuvConversionMode;


        // Constructor.
        public ImageRenderingProfile(string name, ImageRenderers.IImageRenderer renderer) : this(ImageRenderingProfileType.UserDefined)
        {
            this.name = name;
            this.renderer = renderer;
            this.yuvConversionMode = App.CurrentOrNull?.Settings?.GetValueOrDefault(SettingKeys.DefaultYuvConversionMode) ?? default;
        }
        public ImageRenderingProfile(FileFormat format, ImageRenderers.IImageRenderer renderer) : this(ImageRenderingProfileType.FileFormat)
        {
            this.fileFormat = format;
            this.fileFormat.PropertyChanged += this.OnFileFormatPropertyChanged;
            this.name = format.Name;
            this.renderer = renderer;
            this.yuvConversionMode = App.CurrentOrNull?.Settings?.GetValueOrDefault(SettingKeys.DefaultYuvConversionMode) ?? default;
        }
        ImageRenderingProfile(ImageRenderingProfileType type)
        {
            this.Type = type;
            if (type == ImageRenderingProfileType.Default)
            {
                this.name = this.Application.GetStringNonNull("ImageRenderingProfile.Default");
                this.Application.StringsUpdated += (_, e) =>
                {
                    this.name = this.Application.GetStringNonNull("ImageRenderingProfile.Default");
                    this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                };
            }
        }


        // Application.
        public IApplication Application { get => app ?? throw new InvalidOperationException("Profile is not ready yet."); }


        // Byte ordering.
        public ByteOrdering ByteOrdering
        {
            get => this.byteOrdering;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.byteOrdering == value)
                    return;
                if (value <= 0)
                    throw new ArgumentOutOfRangeException();
                this.byteOrdering = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ByteOrdering)));
            }
        }


        // Check thread.
        public bool CheckAccess() => this.Application.CheckAccess();


        // Data offset.
        public long DataOffset
        {
            get => this.dataOffset;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.dataOffset == value)
                    return;
                if (value <= 0)
                    throw new ArgumentOutOfRangeException();
                this.dataOffset = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataOffset)));
            }
        }


        // Delete related file.
        public Task DeleteFileAsync()
        {
            this.VerifyAccess();
            this.VerifyDisposed();
            var fileName = this.fileName;
            if (string.IsNullOrEmpty(fileName))
                return Task.CompletedTask;
            this.fileName = null;
            return IOTaskFactory.StartNew(() =>
            {
                try
                {
                    System.IO.File.Delete(fileName);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, $"Unable to delete file '{fileName}'");
                }
            });
        }


        // Default profile.
        public static ImageRenderingProfile Default { get => defaultProfile ?? throw new InvalidOperationException("Default profile is not ready yet."); }


        // Demosaicing
        public bool Demosaicing
        {
            get => this.demosaicing;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.demosaicing == value)
                    return;
                this.demosaicing = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Demosaicing)));
            }
        }


        // Path of directory to load/save profiles.
        public static string DirectoryPath { get => directoryPath ?? throw new InvalidOperationException("Profile is not ready yet."); }


        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            this.VerifyAccess();
            this.VerifyDefault();
            if (this.fileFormat != null)
                fileFormat.PropertyChanged -= this.OnFileFormatPropertyChanged;
        }


        // Effective bits for each plane.
        public IList<int> EffectiveBits
        {
            get => this.effectiveBits;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.effectiveBits.SequenceEqual(value))
                    return;
                if (value.Count != ImageFormat.MaxPlaneCount)
                    throw new ArgumentException("Number of element must be same as ImageFormat.MaxPlaneCount.");
                this.effectiveBits = value.ToArray().AsReadOnly();
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBits)));
            }
        }


        // Padding size between each frame.
        public long FramePaddingSize
        {
            get => this.framePaddingSize;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.framePaddingSize == value)
                    return;
                if (value <= 0)
                    throw new ArgumentOutOfRangeException();
                this.framePaddingSize = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FramePaddingSize)));
            }
        }


        // Height of image.
        public int Height
        {
            get => this.height;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.height == value)
                    return;
                if (value <= 0)
                    throw new ArgumentOutOfRangeException();
                this.height = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Height)));
            }
        }


        // Initialize.
        public static void Initialize(IApplication app)
        {
            lock (typeof(ImageRenderingProfile))
            {
                if (ImageRenderingProfile.app != null)
                    throw new InvalidOperationException("Unexpected initialization.");
                ImageRenderingProfile.app = app;
            }
            defaultProfile = new ImageRenderingProfile(ImageRenderingProfileType.Default);
            directoryPath = Path.Combine(app.RootPrivateDirectoryPath, "Profiles");
            logger = app.LoggerFactory.CreateLogger(nameof(ImageRenderingProfile));
        }


        // Check upgrading state.
        public bool IsUpgradedWhenLoading { get; private set; }


        // Load and create profile from file.
        public static async Task<ImageRenderingProfile> LoadAsync(string fileName)
        {
            // load from file
            var profile = new ImageRenderingProfile(ImageRenderingProfileType.UserDefined);
            await IOTaskFactory.StartNew(() =>
            {
                // parse file
                using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                using var jsonDocument = JsonDocument.Parse(stream);
                var rootElement = jsonDocument.RootElement;
                if (rootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Root element must be an object.");

                // get name
                if (rootElement.TryGetProperty(nameof(Name), out var jsonProperty) && jsonProperty.ValueKind == JsonValueKind.String)
                    profile.name = jsonProperty.GetString().AsNonNull();
                else
                    throw new JsonException("No name specified.");

                // get renderer
                if(rootElement.TryGetProperty("Format", out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.String)
                {
                    var formatName = jsonProperty.GetString().AsNonNull();
                    switch (formatName)
                    {
                        case "BGGR_16_BE":
                            formatName = "BGGR_16";
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "BGGR_16_LE":
                            formatName = "BGGR_16";
                            profile.byteOrdering = ByteOrdering.LittleEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GBRG_16_BE":
                            formatName = "GBRG_16";
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GBRG_16_LE":
                            formatName = "GBRG_16";
                            profile.byteOrdering = ByteOrdering.LittleEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GRBG_16_BE":
                            formatName = "GRBG_16";
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GRBG_16_LE":
                            formatName = "GRBG_16";
                            profile.byteOrdering = ByteOrdering.LittleEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "L16_BE":
                            formatName = "L16";
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "L16_LE":
                            formatName = "L16";
                            profile.byteOrdering = ByteOrdering.LittleEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "RGB_565_BE":
                            formatName = "RGB_565";
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "RGB_565_LE":
                            formatName = "RGB_565";
                            profile.byteOrdering = ByteOrdering.LittleEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "RGGB_16_BE":
                            formatName = "RGGB_16";
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "RGGB_16_BL":
                            formatName = "RGGB_16";
                            profile.byteOrdering = ByteOrdering.LittleEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                    }
                    if (ImageRenderers.ImageRenderers.TryFindByFormatName(formatName, out var renderer))
                        profile.renderer = renderer;
                    else
                        throw new JsonException($"Unknown format: {jsonProperty.GetString()}.");
                }
                else
                    throw new JsonException("No format specified.");

                // get data offset
                if (rootElement.TryGetProperty(nameof(DataOffset), out jsonProperty))
                    jsonProperty.TryGetInt64(out profile.dataOffset);

                // get frame padding size
                if (rootElement.TryGetProperty(nameof(FramePaddingSize), out jsonProperty))
                    jsonProperty.TryGetInt64(out profile.framePaddingSize);

                // get byte ordering
                if (profile.renderer?.Format?.HasMultipleByteOrderings == true
                    && rootElement.TryGetProperty(nameof(ByteOrdering), out jsonProperty)
                    && jsonProperty.ValueKind == JsonValueKind.String)
                {
                    Enum.TryParse(jsonProperty.GetString(), out profile.byteOrdering);
                }

                // YUV conversion mode
                if (profile.renderer?.Format?.Category == ImageFormatCategory.YUV)
                {
                    if (rootElement.TryGetProperty(nameof(YuvConversionMode), out jsonProperty)
                        && jsonProperty.ValueKind == JsonValueKind.String
                        && Enum.TryParse(jsonProperty.GetString(), out profile.yuvConversionMode))
                    {
                        if (profile.yuvConversionMode == YuvConversionMode.ITU_R)
                        {
                            profile.yuvConversionMode = YuvConversionMode.BT_601;
                            profile.IsUpgradedWhenLoading = true;
                        }
                    }
                    else
                        profile.IsUpgradedWhenLoading = true;
                }

                // get demosaicing
                if (rootElement.TryGetProperty(nameof(Demosaicing), out jsonProperty))
                    profile.demosaicing = jsonProperty.ValueKind != JsonValueKind.False;

                // get dimensions
                if (rootElement.TryGetProperty(nameof(Width), out jsonProperty) && jsonProperty.TryGetInt32(out var intValue))
                    profile.width = Math.Max(1, intValue);
                if (rootElement.TryGetProperty(nameof(Height), out jsonProperty) && jsonProperty.TryGetInt32(out intValue))
                    profile.height = Math.Max(1, intValue);

                // get effective bits
                if (rootElement.TryGetProperty(nameof(EffectiveBits), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
                {
                    var array = new int[ImageFormat.MaxPlaneCount];
                    var index = 0;
                    foreach (var jsonValue in jsonProperty.EnumerateArray())
                    {
                        if (jsonValue.TryGetInt32(out intValue))
                            array[index] = Math.Max(0, intValue);
                        ++index;
                        if (index >= array.Length)
                            break;
                    }
                    profile.effectiveBits = array.AsReadOnly();
                }

                // get pixel strides
                if (rootElement.TryGetProperty(nameof(PixelStrides), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
                {
                    var array = new int[ImageFormat.MaxPlaneCount];
                    var index = 0;
                    foreach (var jsonValue in jsonProperty.EnumerateArray())
                    {
                        if (jsonValue.TryGetInt32(out intValue))
                            array[index] = Math.Max(0, intValue);
                        ++index;
                        if (index >= array.Length)
                            break;
                    }
                    profile.pixelStrides = array.AsReadOnly();
                }

                // get row strides
                if (rootElement.TryGetProperty(nameof(RowStrides), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
                {
                    var array = new int[ImageFormat.MaxPlaneCount];
                    var index = 0;
                    foreach (var jsonValue in jsonProperty.EnumerateArray())
                    {
                        if (jsonValue.TryGetInt32(out intValue))
                            array[index] = Math.Max(0, intValue);
                        ++index;
                        if (index >= array.Length)
                            break;
                    }
                    profile.rowStrides = array.AsReadOnly();
                }
            });

            // setup profile
            profile.fileName = fileName;

            // complete
            return profile;
        }


        // Logger.
        ILogger Logger { get => logger ?? throw new InvalidOperationException("Instance is not ready yet."); }


        // Name of profile.
        public string Name
        {
            get => this.name;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                if (this.Type != ImageRenderingProfileType.UserDefined)
                    throw new InvalidOperationException();
                if (this.name == value)
                    return;
                this.name = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }


        // Property of file format changed.
        void OnFileFormatPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileFormat.Name))
            {
                this.name = this.fileFormat.AsNonNull().Name;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }


        // Pixel strides for each plane.
        public IList<int> PixelStrides
        {
            get => this.pixelStrides;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.pixelStrides.SequenceEqual(value))
                    return;
                if (value.Count != ImageFormat.MaxPlaneCount)
                    throw new ArgumentException("Number of element must be same as ImageFormat.MaxPlaneCount.");
                this.pixelStrides = value.ToArray().AsReadOnly();
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PixelStrides)));
            }
        }


        // Image renderer.
        public ImageRenderers.IImageRenderer Renderer
        {
            get => this.renderer ?? throw new InvalidOperationException("No renderer in the profile.");
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.renderer == value)
                    return;
                this.renderer = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Renderer)));
            }
        }


        // Row strides for each plane.
        public IList<int> RowStrides
        {
            get => this.rowStrides;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.rowStrides.SequenceEqual(value))
                    return;
                if (value.Count != ImageFormat.MaxPlaneCount)
                    throw new ArgumentException("Number of element must be same as ImageFormat.MaxPlaneCount.");
                this.rowStrides = value.ToArray().AsReadOnly();
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowStrides)));
            }
        }


        // Save profile to file.
        public async Task SaveAsync()
        {
            // check state
            this.VerifyAccess();
            this.VerifyDisposed();
            if (this.Type != ImageRenderingProfileType.UserDefined)
                throw new InvalidOperationException();

            // select file name
            var fileName = Path.Combine(DirectoryPath, WebUtility.UrlEncode(this.name) + ".json");

            // save to file
            var prevFileName = this.fileName;
            await IOTaskFactory.StartNew(() =>
            {
                // delete previous file
                if (!string.IsNullOrEmpty(prevFileName) && !PathEqualityComparer.Default.Equals(prevFileName, fileName))
                {
                    try
                    {
                        this.Logger.LogTrace($"Delete previous file '{prevFileName}'");
                        System.IO.File.Delete(prevFileName);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, $"Unable to delete previous file '{prevFileName}'");
                    }
                }

                // create directory
                Directory.CreateDirectory(DirectoryPath);

                // save to file
                this.Logger.LogTrace($"Save '{this.name}' to '{fileName}'");
                using var stream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite);
                using var jsonWriter = new Utf8JsonWriter(stream, new JsonWriterOptions() { Indented = true });
                var format = this.Renderer.Format;
                jsonWriter.WriteStartObject();
                jsonWriter.WriteString(nameof(Name), this.name);
                jsonWriter.WriteString("Format", format.Name);
                if (this.dataOffset != 0)
                    jsonWriter.WriteNumber(nameof(DataOffset), this.dataOffset);
                if (this.framePaddingSize != 0)
                    jsonWriter.WriteNumber(nameof(FramePaddingSize), this.framePaddingSize);
                if (format.HasMultipleByteOrderings)
                    jsonWriter.WriteString(nameof(ByteOrdering), this.byteOrdering.ToString());
                if (format.Category == ImageFormatCategory.YUV)
                    jsonWriter.WriteString(nameof(YuvConversionMode), this.yuvConversionMode.ToString());
                jsonWriter.WriteBoolean(nameof(Demosaicing), this.demosaicing);
                jsonWriter.WriteNumber(nameof(Width), this.width);
                jsonWriter.WriteNumber(nameof(Height), this.height);
                jsonWriter.WritePropertyName(nameof(EffectiveBits));
                jsonWriter.WriteStartArray();
                for (var i = 0; i < format.PlaneCount; ++i)
                    jsonWriter.WriteNumberValue(this.effectiveBits[i]);
                jsonWriter.WriteEndArray();
                jsonWriter.WritePropertyName(nameof(PixelStrides));
                jsonWriter.WriteStartArray();
                for (var i = 0; i < format.PlaneCount; ++i)
                    jsonWriter.WriteNumberValue(this.pixelStrides[i]);
                jsonWriter.WriteEndArray();
                jsonWriter.WritePropertyName(nameof(RowStrides));
                jsonWriter.WriteStartArray();
                for (var i = 0; i < format.PlaneCount; ++i)
                    jsonWriter.WriteNumberValue(this.rowStrides[i]);
                jsonWriter.WriteEndArray();
                jsonWriter.WriteEndObject();
            });

            // update state
            this.fileName = fileName;
            this.IsUpgradedWhenLoading = false;
        }


        // Synchronization context.
        public SynchronizationContext SynchronizationContext => this.Application.SynchronizationContext;


        // Raised when property changed.
        public event PropertyChangedEventHandler? PropertyChanged;


        // Type of profile.
        public ImageRenderingProfileType Type { get; }


        // Throw exception is profile is default.
        void VerifyDefault()
        {
            if (this.Type == ImageRenderingProfileType.Default)
                throw new InvalidOperationException("Cannot modify default profile.");
        }


        // Width of image.
        public int Width
        {
            get => this.width;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.width == value)
                    return;
                if (value <= 0)
                    throw new ArgumentOutOfRangeException();
                this.width = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
            }
        }


        // Conversion mode of YUV to RGB.
        public YuvConversionMode YuvConversionMode
        {
            get => this.yuvConversionMode;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.yuvConversionMode == value)
                    return;
                this.yuvConversionMode = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(YuvConversionMode)));
            }
        }
    }


    /// <summary>
    /// Type of <see cref="ImageRenderingProfile"/>.
    /// </summary>
    enum ImageRenderingProfileType
    {
        /// <summary>
        /// Default profile.
        /// </summary>
        Default,
        /// <summary>
        /// Generated for file format.
        /// </summary>
        FileFormat,
        /// <summary>
        /// User defined.
        /// </summary>
        UserDefined,
    }
}
