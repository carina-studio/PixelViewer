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
        public readonly IApplication Application;
        public TimeSpan Duration;
        public readonly string FFmpegDirectory;
        public ByteOrdering? FrameByteOrdering;
        public ColorSpace FrameColorSpace = ColorSpace.Srgb;
        public ImageFormat? FrameFormat;
        public readonly string FileName;
        public double FrameRate = double.NaN;
        public PixelSize FrameSize;
        public readonly string Id;
        public readonly ILogger Logger;


        // Constructor.
        public HolderImpl(IApplication app, string fileName)
        {
            this.Application = app;
            this.FFmpegDirectory = Path.Combine(app.RootPrivateDirectoryPath, "FFmpeg");
            this.FileName = fileName;
            this.Id = new string(new char[8].Also(it =>
            {
                for (var i = it.Length - 1; i >= 0; --i)
                {
                    var n = Random.Next(36);
                    it[i] = n <= 9 ? (char)('0' + n) : (char)('a' + (n - 10));
                }
            }));
            this.Logger = app.LoggerFactory.CreateLogger(nameof(FFmpegVideoDataSource));
            this.Logger.LogDebug($"Create source of '{fileName}' ({this.Id})");
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


    // Constants.
    const int SharedFrameCacheCapacity = 32;


    // Static fields.
    static readonly Regex DurationRegex = new("^[\\s]*Duration:[\\s]*(?<Duration>[\\d:\\.]+),");
    static readonly Random Random = new();
    static readonly LinkedList<FileImageDataSource> SharedFrameCache = new();
    static readonly Regex StreamHeaderRegex = new("^[\\s]*Stream #[\\d]+:[\\d]+[^:]*:[\\s]*(?<Type>[\\w]+):[\\s]*(?<Info>.*)");
    static readonly Regex VideoFrameExtractedRegex = new("^\\s*video:\\d+");
    static readonly Regex VideoStreamHeaderInfoRegex = new("(?<Encoder>[\\w\\d]+)\\s*\\((?<EncodingProfile>[^\\)]*)\\)(\\s*\\([^\\)]*\\))?,\\s*(?<PixelFormat>[\\w\\d]+)\\([^,]*(,\\s*(?<ColorSpace>[^\\)]*))?\\),\\s*(?<FrameWidth>[\\d]+)x(?<FrameHeight>[\\d]+)(\\s\\[[^\\]]+\\])?(,[^,]*)?,\\s*(?<FrameRate>[\\d\\.]+)\\s+fps");


    // Fields.
    readonly string id;
    readonly ILogger logger;
	readonly List<StreamImpl> openedStreams = new();


    // Static initializer.
    static FFmpegVideoDataSource()
    {
        Task.Run(() =>
        {
            try
            {
                var directory = Path.Combine(App.Current.RootPrivateDirectoryPath, "FFmpeg");
                if (Directory.Exists(directory))
                {
                    foreach (var frameFileName in Directory.GetFiles(directory))
                    {
                        if (Path.GetFileName(frameFileName)?.StartsWith("Frame_") == true)
                            Global.RunWithoutError(() => System.IO.File.Delete(frameFileName));
                    }
                }
            }
            catch
            { }
        });
    }


    // Constructor.
    FFmpegVideoDataSource(IApplication app, string fileName) : base(new HolderImpl(app, fileName))
    {
        // setup logger
        var holder = this.GetResourceHolder<HolderImpl>();
        this.logger = holder.Logger;

        // get ID
        this.id = holder.Id;

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
        this.id = holder.Id;
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

        // clear cached frame files
        lock (SharedFrameCache)
        {
            var node = SharedFrameCache.First;
            var fileNameKeyword = $"_{this.id}_";
            while (node != null)
            {
                var nextNode = node.Next;
                if (Path.GetFileName(node.Value.FileName)?.Contains(fileNameKeyword) == true)
                {
                    SharedFrameCache.Remove(node);
                    Global.RunWithoutErrorAsync(node.Value.Dispose);
                }
                node = nextNode;
            }
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
    public async Task<IImageDataSource> GetFrameAsync(TimeSpan position, CancellationToken cancellationToken = default)
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
        
        // select output format
        var holder = this.GetResourceHolder<HolderImpl>();
        var frameFileExtension = holder.FrameFormat?.Let(it =>
        {
            if (it.Category == ImageFormatCategory.YUV)
                return ".yuv";
            throw new NotSupportedException($"Unsupported video frame format: {it.Name}.");
        }) ?? throw new NotSupportedException($"Unknown video frame format.");

        // use cached frame directly
        var frameId = $"Frame_{holder.Id}_{position.TotalMilliseconds}{frameFileExtension}";
        var frameFileName = Path.Combine(holder.FFmpegDirectory, frameId);
        var cachedFrameFile = SharedFrameCache.Lock(it =>
        {
            this.logger.LogTrace($"Get frame at {position}, frame ID: {frameId}");
            var node = it.First;
            while (node != null)
            {
                if (PathEqualityComparer.Default.Equals(node.Value.FileName, frameFileName))
                {
                    if (node.Previous != null)
                    {
                        it.Remove(node);
                        it.AddFirst(node);
                    }
                    this.logger.LogTrace($"Use cached frame, frame ID: {frameId}");
                    return node.Value.Share();
                }
                node = node.Next;
            }
            return null;
        });
        if (cachedFrameFile != null)
            return cachedFrameFile;
        
        // extract video frame
        var videoFileName = holder.FileName;
        var ffmpegDirectory = holder.FFmpegDirectory;
        var frameFile = await Task.Run(() =>
        {
            var ffmpegProcess = (Process?)null;
            try
            {
                // extract frame
                ffmpegProcess = this.LaunchFFmpeg($"-ss {(int)position.TotalHours:D2}:{position.ToString("mm\\:ss\\.fff")} -i \"{videoFileName}\" -vframes 1 -y \"{frameFileName}\"");
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
                using var reader = ffmpegProcess.StandardError;
                var completed = false;
                var line = reader.ReadLine();
                while (line != null)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new TaskCanceledException();
                    if (VideoFrameExtractedRegex.IsMatch(line))
                        completed = true;
                    line = reader.ReadLine();
                }
                if (!completed)
                    throw new Exception($"Failed to exteact frame from '{videoFileName}'.");

                // complete
                return new FileImageDataSource(holder.Application, frameFileName, true);
            }
            catch
            {
                Global.RunWithoutError(() => System.IO.File.Delete(frameFileName));
                throw;
            }
            finally
            {
                Global.RunWithoutError(() => ffmpegProcess?.Kill());
            }
        });
        if (this.IsDisposed)
        {
            Global.RunWithoutErrorAsync(() => System.IO.File.Delete(frameFileName));
            throw new ObjectDisposedException(nameof(FFmpegVideoDataSource));
        }
        if (cancellationToken.IsCancellationRequested)
        {
            Global.RunWithoutErrorAsync(() => System.IO.File.Delete(frameFileName));
            throw new TaskCanceledException();
        }

        // add frame to cache
        lock (SharedFrameCache)
        {
            while (SharedFrameCache.Count >= SharedFrameCacheCapacity)
            {
                var node = SharedFrameCache.Last;
                SharedFrameCache.RemoveLast();
                this.logger.LogTrace($"Drop frame from cache, frame ID: {Path.GetFileName(node?.Value?.FileName)}");
                node?.Value?.Dispose();
            }
            SharedFrameCache.AddFirst(frameFile.Share());
        }

        // complete
        return frameFile;
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