using CarinaStudio;
using CarinaStudio.Collections;
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
    class ImageRenderingProfile : IApplicationObject, INotifyPropertyChanged
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
        IList<int> effectiveBits = emptyEffectiveBits;
        string? fileName;
        int height = 1;
        string name = "";
        IList<int> pixelStrides = emptyEffectiveBits;
        ImageRenderers.IImageRenderer? renderer;
        IList<int> rowStrides = emptyEffectiveBits;
        int width = 1;


        // Constructor.
        public ImageRenderingProfile(string name, ImageRenderers.IImageRenderer renderer) : this(false)
        {
            this.name = name;
            this.renderer = renderer;
        }
        ImageRenderingProfile(bool isDefault)
        {
            this.IsDefault = isDefault;
            if (isDefault)
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


        // Delete related file.
        public Task DeleteFileAsync()
        {
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


        // Path of directory to load/save profiles.
        public static string DirectoryPath { get => directoryPath ?? throw new InvalidOperationException("Profile is not ready yet."); }


        // Effective bits for each plane.
        public IList<int> EffectiveBits
        {
            get => this.effectiveBits;
            set
            {
                this.VerifyAccess();
                this.VerifyDefault();
                if (this.effectiveBits.SequenceEqual(value))
                    return;
                if (value.Count != ImageFormat.MaxPlaneCount)
                    throw new ArgumentException("Number of element must be same as ImageFormat.MaxPlaneCount.");
                this.effectiveBits = value.ToArray().AsReadOnly();
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveBits)));
            }
        }


        // Height of image.
        public int Height
        {
            get => this.height;
            set
            {
                this.VerifyAccess();
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
            defaultProfile = new ImageRenderingProfile(true);
            directoryPath = Path.Combine(app.RootPrivateDirectoryPath, "Profiles");
            logger = app.LoggerFactory.CreateLogger(nameof(ImageRenderingProfile));
        }


        // Check whether profile is default one or not.
        public bool IsDefault { get; }


        // Check upgrading state.
        public bool IsUpgradedWhenLoading { get; private set; }


        // Load and create profile from file.
        public static async Task<ImageRenderingProfile> LoadAsync(string fileName)
        {
            // load from file
            var profile = new ImageRenderingProfile(false);
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

                // get byte ordering
                if (profile.renderer?.Format?.HasMultipleByteOrderings == true
                    && rootElement.TryGetProperty(nameof(ByteOrdering), out jsonProperty)
                    && jsonProperty.ValueKind == JsonValueKind.String)
                {
                    Enum.TryParse(jsonProperty.GetString(), out profile.byteOrdering);
                }

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
                this.VerifyDefault();
                if (this.name == value)
                    return;
                this.name = value;
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
            this.VerifyDefault();

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
                if (format.HasMultipleByteOrderings)
                    jsonWriter.WriteString(nameof(ByteOrdering), this.byteOrdering.ToString());
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


        // Throw exception is profile is default.
        void VerifyDefault()
        {
            if (this.IsDefault)
                throw new InvalidOperationException("Cannot modify default profile.");
        }


        // Width of image.
        public int Width
        {
            get => this.width;
            set
            {
                this.VerifyAccess();
                this.VerifyDefault();
                if (this.width == value)
                    return;
                if (value <= 0)
                    throw new ArgumentOutOfRangeException();
                this.width = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
            }
        }
    }


    /// <summary>
    /// Data for <see cref="ImageRenderingProfile"/> related events.
    /// </summary>
    class ImageRenderingProfileEventArgs : EventArgs
    {
        // Constructor.
        public ImageRenderingProfileEventArgs(ImageRenderingProfile profile) => this.Profile = profile;


        // Related profile.
        public ImageRenderingProfile Profile { get; }
    }
}
