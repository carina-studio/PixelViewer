using Avalonia;
using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.IO;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Implementation of <see cref="IVideoDataSource"/> based-on FFmpeg.
/// </summary>
class FFmpegVideoDataSource : BaseShareableDisposable<FFmpegVideoDataSource>, IVideoDataSource
{
    // Holder of source.
    class HolderImpl : BaseResourceHolder
    {
        // Fields.
        public TimeSpan Duration;
        public readonly string FFmpegDirectory;
        public ByteOrdering? FrameByteOrdering;
        public ColorSpace FrameColorSpace = ColorSpace.Srgb;
        public ImageFormat? FrameFormat;
        public readonly string FileName;
        public double FrameRate = double.NaN;
        public PixelSize FrameSize;
        public readonly ILogger Logger;


        // Constructor.
        public HolderImpl(IApplication app, string fileName)
        {
            this.FFmpegDirectory = Path.Combine(app.RootPrivateDirectoryPath, "FFmpeg");
            this.FileName = fileName;
            this.Logger = app.LoggerFactory.CreateLogger(nameof(FFmpegVideoDataSource));
            this.Logger.LogDebug($"Create source of '{fileName}'");
        }

        // Release.
        protected override void Release()
        { }
    }


    // Stream to read data.
    class StreamImpl : StreamWrapper
    {
        // Fields.
        readonly FFmpegVideoDataSource source;

        // Constructor.
        public StreamImpl(FFmpegVideoDataSource source, FileStream fileStream) : base(fileStream) =>
            this.source = source;

        // Dispose.
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                this.source.OnStreamClosed(this);
        }
    }


    // Static fields.
    static readonly Regex DurationRegex = new("^[\\s]*Duration:[\\s]*(?<Duration>[\\d:\\.]+),");
    static readonly Regex StreamHeaderRegex = new("^[\\s]*Stream #[\\d]+:[\\d]+[^:]*:[\\s]*(?<Type>[\\w]+):[\\s]*(?<Info>.*)");
    static readonly Regex VideoStreamHeaderInfoRegex = new("(?<Encoder>[\\w\\d]+)\\s*\\((?<EncodingProfile>[^\\)]*)\\)(\\s*\\([^\\)]*\\))?,\\s*(?<PixelFormat>[\\w\\d]+)\\([^,]*(,\\s*(?<ColorSpace>[^\\)]*))?\\),\\s*(?<FrameWidth>[\\d]+)x(?<FrameHeight>[\\d]+)(\\s\\[[^\\]]+\\])?(,[^,]*)?,\\s*(?<FrameRate>[\\d\\.]+)\\s+fps");


    // Fields.
    readonly ILogger logger;
	readonly List<StreamImpl> openedStreams = new();


    // Constructor.
    FFmpegVideoDataSource(IApplication app, string fileName) : base(new HolderImpl(app, fileName))
    {
        // setup logger
        var holder = this.GetResourceHolder<HolderImpl>();
        this.logger = holder.Logger;

        // get video information
        using var process = this.LaunchFFmpeg($"-i \"{fileName}\"");
        try
        {
            // parse information from FFmpeg
            using var reader = process.StandardError;
            var line = reader.ReadLine();
            var isVideoStream = false;
            var pixelFormat = "";
            var colorSpace = "";
            while (line != null)
            {
                if (holder.Duration == default)
                {
                    var match = DurationRegex.Match(line);
                    if (match.Success)
                        TimeSpan.TryParse(match.Groups["Duration"].Value, out holder.Duration);
                }
                else
                {
                    var match = StreamHeaderRegex.Match(line);
                    if (match.Success)
                    {
                        isVideoStream = match.Groups["Type"].Value == "Video";
                        if (isVideoStream)
                        {
                            var info = match.Groups["Info"].Value;
                            match = VideoStreamHeaderInfoRegex.Match(info);
                            if (match.Success)
                            {
                                holder.FrameRate = double.Parse(match.Groups["FrameRate"].Value);
                                holder.FrameSize = new(int.Parse(match.Groups["FrameWidth"].Value), int.Parse(match.Groups["FrameHeight"].Value));
                                pixelFormat = match.Groups["PixelFormat"].Value;
                                colorSpace = match.Groups["ColorSpace"].Let(it =>
                                    it.Success ? it.Value : "");
                            }
                        }
                    }
                    else if (isVideoStream)
                    {
                        //
                    }
                }
                line = reader.ReadLine();
            }

            // check parsed information
            if (holder.Duration == default || holder.FrameSize == default)
                throw new ArgumentException($"Unable to parse video information from file '{fileName}'.");
            
            // select frame format
            switch (pixelFormat)
            {
                case "yuv420p10be":
                case "yuv420p10le":
                    ImageFormat.TryGetByName("P010", out holder.FrameFormat);
                    break;
            }

            // select byte ordering
            if (pixelFormat.EndsWith("le"))
                holder.FrameByteOrdering = ByteOrdering.LittleEndian;
            else if (pixelFormat.EndsWith("be"))
                holder.FrameByteOrdering = ByteOrdering.BigEndian;
            
            // select proper color space
            if (string.IsNullOrWhiteSpace(colorSpace))
            {
                holder.FrameColorSpace = pixelFormat switch
                {
                    "yuv420p10be" 
                    or "yuv420p10le" => ColorSpace.BT_2020,
                    _ => ColorSpace.Srgb,
                };
            }
            else if (colorSpace.Contains("bt2020"))
            {
                if (colorSpace.Contains("arib-std-b67"))
                    holder.FrameColorSpace = ColorSpace.BT_2100_HLG;
                else
                    holder.FrameColorSpace = ColorSpace.BT_2020;
            }
        }
        finally
        {
            Global.RunWithoutError(process.Kill);
        }
    }
    FFmpegVideoDataSource(HolderImpl holder) : base(holder)
    { 
        this.logger = holder.Logger;
    }


    /// <inheritdoc/>
    public bool CheckStreamAccess(StreamAccess access) =>
        !this.IsDisposed && access == StreamAccess.Read;
    

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.Synchronized)]
    protected override void Dispose(bool disposing)
    {
        // close all opened streams
        if (disposing && this.openedStreams.IsNotEmpty())
        {
            this.logger.LogWarning($"Close {this.openedStreams.Count} opened stream(s) of '{this.FileName}'");
            foreach (var stream in this.openedStreams.ToArray())
                Global.RunWithoutErrorAsync(stream.Dispose);
            this.openedStreams.Clear();
        }

        // call base
        if (disposing)
            base.Dispose(disposing);
        else
        {
            // [Workaround] prevent NRE if disposing from finalizer caused by error in constructor
            Global.RunWithoutError(() => base.Dispose(disposing));
        }
    }


    /// <inheritdoc/>
    public TimeSpan Duration { get => this.GetResourceHolder<HolderImpl>().Duration; }


    /// <summary>
    /// Get file name of video.
    /// </summary>
    public string FileName { get => this.GetResourceHolder<HolderImpl>().FileName; }


    /// <inheritdoc/>
    public ByteOrdering? FrameByteOrdering { get => this.GetResourceHolder<HolderImpl>().FrameByteOrdering; }


    /// <inheritdoc/>
    public ColorSpace? FrameColorSpace { get => this.GetResourceHolder<HolderImpl>().FrameColorSpace; }


    /// <inheritdoc/>
    public ImageFormat? FrameFormat { get => this.GetResourceHolder<HolderImpl>().FrameFormat; }


    /// <inheritdoc/>
    public double FrameRate { get => this.GetResourceHolder<HolderImpl>().FrameRate; }


    /// <inheritdoc/>
    public PixelSize FrameSize { get => this.GetResourceHolder<HolderImpl>().FrameSize; }


    /// <inheritdoc/>
    public Task<IImageDataSource> GetFrameAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        // check state
        this.VerifyDisposed();
        if (cancellationToken.IsCancellationRequested)
            throw new TaskCanceledException();

        // correct position
        if (position.Ticks < 0)
            position = TimeSpan.Zero;
        else if (position > this.Duration)
            position = this.Duration;
        
        // extract video frame
        throw new NotImplementedException();
    }


    // Launch FFmpeg.
    Process LaunchFFmpeg(string args) =>
        Process.Start(new ProcessStartInfo()
        {
            Arguments = args,
            CreateNoWindow = true,
            FileName = Path.Combine(this.GetResourceHolder<HolderImpl>().FFmpegDirectory, "ffmpeg"),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new Exception("Unable to launch FFmpeg.");


    // Called when stream closed.
    [MethodImpl(MethodImplOptions.Synchronized)]
    void OnStreamClosed(StreamImpl stream) =>
        this.openedStreams.Remove(stream);
    

    /// <inheritdoc/>
    public async Task<Stream> OpenStreamAsync(StreamAccess access, CancellationToken token)
    {
        // check access
        this.VerifyDisposed();
        if (!this.CheckStreamAccess(access))
            throw new ArgumentException($"Cannot open stream with {access} access.");

        // open stream
        var stream = await Task.Run(() =>
        {
            // check state
            if (this.IsDisposed)
                throw new ObjectDisposedException(nameof(FileImageDataSource));
            if (token.IsCancellationRequested)
                throw new TaskCanceledException();

            // open stream
            var fileStream = (FileStream?)null;
            try
            {
                fileStream = new FileStream(this.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch
            {
                if(token.IsCancellationRequested)
                    throw new TaskCanceledException();
                throw;
            }
            return new StreamImpl(this, fileStream);
        });

        // check state
        lock (this)
        {
            if (this.IsDisposed)
            {
                Global.RunWithoutErrorAsync(stream.Dispose);
                throw new ObjectDisposedException(nameof(FileImageDataSource));
            }
            if (token.IsCancellationRequested)
            {
                Global.RunWithoutErrorAsync(stream.Dispose);
                throw new TaskCanceledException();
            }
            this.openedStreams.Add(stream);
        }

        // complete
        return stream;
    }


    /// <inheritdoc/>
    protected override FFmpegVideoDataSource Share(BaseResourceHolder resourceHolder) =>
        new((HolderImpl)resourceHolder);
    

    // Interface implementation.
    IVideoDataSource IShareableDisposable<IVideoDataSource>.Share() =>
        this.Share();
    

    /// <summary>
    /// Try creating <see cref="FFmpegVideoDataSource"/> instance asynchronously.
    /// </summary>
    /// <param name="app">Application.</param>
    /// <param name="fileName">File name of video.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task of creating instance.</returns>
    public static async Task<FFmpegVideoDataSource?> TryCreateAsync(IApplication app, string fileName, CancellationToken cancellationToken = default)
    {
        var source = (FFmpegVideoDataSource?)null;
        try
        {
            source = await Task.Run(() =>
                new FFmpegVideoDataSource(app, fileName));
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                source?.Dispose();
                throw new TaskCanceledException();
            }
        }
    }
}