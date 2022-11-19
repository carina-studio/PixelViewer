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
        static readonly IList<uint> emptyBlackLevels = ListExtensions.AsReadOnly(new uint[4]);
        static readonly IList<int> emptyEffectiveBits = ListExtensions.AsReadOnly(new int[4]);
        static volatile ILogger? logger;


        // Fields.
        BayerPattern bayerPattern;
        IList<uint> blackLevels = emptyBlackLevels;
        double blueColorGain = 1.0;
        ByteOrdering byteOrdering = ByteOrdering.BigEndian;
        ColorSpace colorSpace = Media.ColorSpace.Default;
        long dataOffset;
        bool demosaicing = true;
        IList<int> effectiveBits = emptyEffectiveBits;
        readonly FileFormat? fileFormat;
        string? fileName;
        long framePaddingSize;
        double greenColorGain = 1.0;
        int height = 1;
        string name = "";
        IList<int> pixelStrides = emptyEffectiveBits;
        double redColorGain = 1.0;
        ImageRenderers.IImageRenderer? renderer;
        IList<int> rowStrides = emptyEffectiveBits;
        bool useLinearColorSpace;
        IList<uint> whiteLevels = emptyBlackLevels;
        int width = 1;
        YuvToBgraConverter yuvToBgraConverter = YuvToBgraConverter.Default;


        // Constructor.
        public ImageRenderingProfile(string name, ImageRenderers.IImageRenderer renderer) : this(ImageRenderingProfileType.UserDefined)
        {
            this.name = name;
            this.renderer = renderer;
            YuvToBgraConverter.TryGetByName(App.CurrentOrNull?.Settings?.GetValueOrDefault(SettingKeys.DefaultYuvToBgraConversion) ?? "", out this.yuvToBgraConverter);
        }
        public ImageRenderingProfile(FileFormat format, ImageRenderers.IImageRenderer renderer) : this(ImageRenderingProfileType.FileFormat)
        {
            this.fileFormat = format;
            this.fileFormat.PropertyChanged += this.OnFileFormatPropertyChanged;
            this.name = format.Name;
            this.renderer = renderer;
            YuvToBgraConverter.TryGetByName(App.CurrentOrNull?.Settings?.GetValueOrDefault(SettingKeys.DefaultYuvToBgraConversion) ?? "", out this.yuvToBgraConverter);
        }
        ImageRenderingProfile(ImageRenderingProfileType type)
        {
            Media.ColorSpace.TryGetColorSpace(this.Application.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName), out this.colorSpace);
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


        // Pattern of Bayer Filter.
        public BayerPattern BayerPattern
        {
            get => this.bayerPattern;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.bayerPattern == value)
                    return;
                this.bayerPattern = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BayerPattern)));
            }
        }


        // Black level.
        public IList<uint> BlackLevels
        {
            get => this.blackLevels;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.blackLevels.SequenceEqual(value))
                    return;
                if (value.Count != ImageFormat.MaxPlaneCount)
                    throw new ArgumentException("Number of element must be same as ImageFormat.MaxPlaneCount.");
                this.blackLevels = ListExtensions.AsReadOnly(value.ToArray());
                this.PropertyChanged?.Invoke(this, new(nameof(BlackLevels)));
            }
        }


        // Gain of blue color.
        public double BlueColorGain
        {
            get => this.blueColorGain;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                value = ImageRenderers.ImageRenderingOptions.GetValidRgbGain(value);
                if (Math.Abs(this.blueColorGain - value) < 0.001)
                    return;
                this.blueColorGain = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlueColorGain)));
            }
        }


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


        // Color space of rendered image.
        public ColorSpace ColorSpace
        {
            get => this.colorSpace;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.colorSpace == value)
                    return;
                this.colorSpace = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorSpace)));
            }
        }


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
            if (!disposing)
                return;
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
                this.effectiveBits = ListExtensions.AsReadOnly(value.ToArray());
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


        // Gain of green color.
        public double GreenColorGain
        {
            get => this.greenColorGain;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                value = ImageRenderers.ImageRenderingOptions.GetValidRgbGain(value);
                if (Math.Abs(this.greenColorGain - value) < 0.001)
                    return;
                this.greenColorGain = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GreenColorGain)));
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


        /// <summary>
        /// Check whether type of profile is <see cref="ImageRenderingProfileType.FileFormat"/> or not.
        /// </summary>
        public bool IsFileFormat { get => this.Type == ImageRenderingProfileType.FileFormat; }


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
                        case "BGGR_16":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.BGGR_2x2;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "BGGR_16_BE":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.BGGR_2x2;
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "BGGR_16_LE":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.BGGR_2x2;
                            profile.byteOrdering = ByteOrdering.LittleEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GBRG_16":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.GBRG_2x2;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GBRG_16_BE":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.GBRG_2x2;
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GBRG_16_LE":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.GBRG_2x2;
                            profile.byteOrdering = ByteOrdering.LittleEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GRBG_16":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.GRBG_2x2;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GRBG_16_BE":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.GRBG_2x2;
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "GRBG_16_LE":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.GRBG_2x2;
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
                        case "RGGB_16":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.RGGB_2x2;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "RGGB_16_BE":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.RGGB_2x2;
                            profile.byteOrdering = ByteOrdering.BigEndian;
                            profile.IsUpgradedWhenLoading = true;
                            break;
                        case "RGGB_16_BL":
                            formatName = "Bayer_Pattern_16";
                            profile.bayerPattern = BayerPattern.RGGB_2x2;
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

                // get bayer pattern
                if (profile.renderer?.Format?.Category == ImageFormatCategory.Bayer
                    && rootElement.TryGetProperty(nameof(BayerPattern), out jsonProperty)
                    && jsonProperty.ValueKind == JsonValueKind.String)
                {
                    Enum.TryParse(jsonProperty.GetString(), out profile.bayerPattern);
                }

                // YUV to RGB converter
                if (profile.renderer?.Format?.Category == ImageFormatCategory.YUV)
                {
                    if (rootElement.TryGetProperty(nameof(YuvToBgraConverter), out jsonProperty)
                        && jsonProperty.ValueKind == JsonValueKind.String)
                    {
                        if (!YuvToBgraConverter.TryGetByName(jsonProperty.GetString().AsNonNull(), out profile.yuvToBgraConverter))
                            profile.IsUpgradedWhenLoading = true;
                    }
                    else
                        profile.IsUpgradedWhenLoading = true;
                }

                // color space
                if (rootElement.TryGetProperty(nameof(ColorSpace), out jsonProperty)
                       && jsonProperty.ValueKind == JsonValueKind.String)
                {
                    var name = jsonProperty.GetString().AsNonNull().Let(it => it switch
                    {
                        "Adobe-RGB" => "Adobe-RGB-1998".Also(_ => profile.IsUpgradedWhenLoading = true),
                        "BT.601" => "BT.601-625-line".Also(_ => profile.IsUpgradedWhenLoading = true),
                        _ => it,
                    });
                    if (!Media.ColorSpace.TryGetColorSpace(name, out profile.colorSpace))
                        profile.IsUpgradedWhenLoading = true;
                }
                if (rootElement.TryGetProperty(nameof(UseLinearColorSpace), out jsonProperty) 
                    && jsonProperty.ValueKind == JsonValueKind.True)
                {
                    profile.useLinearColorSpace = true;
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
                    profile.effectiveBits = ListExtensions.AsReadOnly(array);
                }

                // get black levels
                if (rootElement.TryGetProperty(nameof(BlackLevels), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
                {
                    var array = new uint[ImageFormat.MaxPlaneCount];
                    var index = 0;
                    foreach (var jsonValue in jsonProperty.EnumerateArray())
                    {
                        if (jsonValue.TryGetUInt32(out var uintValue))
                            array[index] = uintValue;
                        ++index;
                        if (index >= array.Length)
                            break;
                    }
                    profile.blackLevels = ListExtensions.AsReadOnly(array);
                }

                // get white levels
                if (rootElement.TryGetProperty(nameof(WhiteLevels), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
                {
                    var array = new uint[ImageFormat.MaxPlaneCount];
                    var index = 0;
                    foreach (var jsonValue in jsonProperty.EnumerateArray())
                    {
                        if (jsonValue.TryGetUInt32(out var uintValue))
                            array[index] = uintValue;
                        ++index;
                        if (index >= array.Length)
                            break;
                    }
                    profile.whiteLevels = ListExtensions.AsReadOnly(array);
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
                    profile.pixelStrides = ListExtensions.AsReadOnly(array);
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
                    profile.rowStrides = ListExtensions.AsReadOnly(array);
                }

                // get RGB gain
                if (rootElement.TryGetProperty(nameof(RedColorGain), out jsonProperty) && jsonProperty.TryGetDouble(out profile.redColorGain))
                    profile.redColorGain = ImageRenderers.ImageRenderingOptions.GetValidRgbGain(profile.redColorGain);
                if (rootElement.TryGetProperty(nameof(GreenColorGain), out jsonProperty) && jsonProperty.TryGetDouble(out profile.greenColorGain))
                    profile.greenColorGain = ImageRenderers.ImageRenderingOptions.GetValidRgbGain(profile.greenColorGain);
                if (rootElement.TryGetProperty(nameof(BlueColorGain), out jsonProperty) && jsonProperty.TryGetDouble(out profile.blueColorGain))
                    profile.blueColorGain = ImageRenderers.ImageRenderingOptions.GetValidRgbGain(profile.blueColorGain);
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


        /// <summary>
        /// Orientation to display image. The property is used for displaying only.
        /// </summary>
        public int Orientation { get; set; }


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
                this.pixelStrides = ListExtensions.AsReadOnly(value.ToArray());
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PixelStrides)));
            }
        }


        // Gain of red color.
        public double RedColorGain
        {
            get => this.redColorGain;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                value = ImageRenderers.ImageRenderingOptions.GetValidRgbGain(value);
                if (Math.Abs(this.redColorGain - value) < 0.001)
                    return;
                this.redColorGain = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedColorGain)));
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
                this.rowStrides = ListExtensions.AsReadOnly(value.ToArray());
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
                if (format.Category == ImageFormatCategory.Bayer)
                    jsonWriter.WriteString(nameof(BayerPattern), this.bayerPattern.ToString());
                if (format.HasMultipleByteOrderings)
                    jsonWriter.WriteString(nameof(ByteOrdering), this.byteOrdering.ToString());
                if (format.Category == ImageFormatCategory.YUV)
                    jsonWriter.WriteString(nameof(YuvToBgraConverter), this.yuvToBgraConverter.Name);
                jsonWriter.WriteString(nameof(ColorSpace), this.colorSpace.Name);
                if (this.useLinearColorSpace)
                    jsonWriter.WriteBoolean(nameof(UseLinearColorSpace), true);
                jsonWriter.WriteBoolean(nameof(Demosaicing), this.demosaicing);
                jsonWriter.WriteNumber(nameof(Width), this.width);
                jsonWriter.WriteNumber(nameof(Height), this.height);
                jsonWriter.WritePropertyName(nameof(EffectiveBits));
                jsonWriter.WriteStartArray();
                for (var i = 0; i < format.PlaneCount; ++i)
                    jsonWriter.WriteNumberValue(this.effectiveBits[i]);
                jsonWriter.WriteEndArray();
                jsonWriter.WritePropertyName(nameof(BlackLevels));
                jsonWriter.WriteStartArray();
                for (var i = 0; i < format.PlaneCount; ++i)
                    jsonWriter.WriteNumberValue(this.blackLevels[i]);
                jsonWriter.WriteEndArray();
                jsonWriter.WritePropertyName(nameof(WhiteLevels));
                jsonWriter.WriteStartArray();
                for (var i = 0; i < format.PlaneCount; ++i)
                    jsonWriter.WriteNumberValue(this.whiteLevels[i]);
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
                jsonWriter.WriteNumber(nameof(RedColorGain), this.redColorGain);
                jsonWriter.WriteNumber(nameof(GreenColorGain), this.greenColorGain);
                jsonWriter.WriteNumber(nameof(BlueColorGain), this.blueColorGain);
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


        // Use linear color space.
        public bool UseLinearColorSpace
        {
            get => this.useLinearColorSpace;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.useLinearColorSpace == value)
                    return;
                this.useLinearColorSpace = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseLinearColorSpace)));
            }
        }


        // Throw exception is profile is default.
        void VerifyDefault()
        {
            if (this.Type == ImageRenderingProfileType.Default)
                throw new InvalidOperationException("Cannot modify default profile.");
        }


        // White level.
        public IList<uint> WhiteLevels
        {
            get => this.whiteLevels;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.whiteLevels.SequenceEqual(value))
                    return;
                if (value.Count != ImageFormat.MaxPlaneCount)
                    throw new ArgumentException("Number of element must be same as ImageFormat.MaxPlaneCount.");
                this.whiteLevels = ListExtensions.AsReadOnly(value.ToArray());
                this.PropertyChanged?.Invoke(this, new(nameof(WhiteLevels)));
            }
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


        // Conversion of YUV to RGB.
        public YuvToBgraConverter YuvToBgraConverter
        {
            get => this.yuvToBgraConverter;
            set
            {
                this.VerifyAccess();
                this.VerifyDisposed();
                this.VerifyDefault();
                if (this.yuvToBgraConverter == value)
                    return;
                this.yuvToBgraConverter = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(YuvToBgraConverter)));
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
