using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.IO;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Implementation of <see cref="IFileSequenceImageDataSource"/> which treats each of a set of files as one frame.
/// </summary>
class FileSequenceImageDataSource : BaseShareableDisposable<FileSequenceImageDataSource>, IFileSequenceImageDataSource
{
	// Holder.
	class HolderImpl : BaseResourceHolder
	{
		// Fields.
		public readonly IApplication App;
		public readonly string[] FileNames;
		public readonly long[] FileSizes;
		public readonly LinkedList<FileImageDataSource> FrameSourceCache = new();
		public readonly ILogger Logger;
		public readonly IList<string> ReadOnlyFileNames;
		public readonly long TotalSize;

		// Constructor.
		public HolderImpl(IApplication app, string[] fileNames)
		{
			// check parameter
			if (fileNames.IsEmpty())
				throw new ArgumentException("No file provided for frame sequence.", nameof(fileNames));

			// setup file names
			this.App = app;
			this.Logger = app.LoggerFactory.CreateLogger(nameof(FileSequenceImageDataSource));
			this.Logger.LogDebug("Create source of {count} file(s)", fileNames.Length);
			this.FileNames = fileNames;
			this.ReadOnlyFileNames = ListExtensions.AsReadOnly(fileNames);

			// collect size of file of each frame, size of inaccessible file is treated as zero
			this.FileSizes = new long[fileNames.Length];
			for (var i = fileNames.Length - 1; i >= 0; --i)
			{
				try
				{
					this.FileSizes[i] = new FileInfo(fileNames[i]).Length;
				}
				catch (Exception ex)
				{
					this.Logger.LogWarning(ex, "Unable to get size of file '{fileName}' of frame {frameIndex}", fileNames[i], i);
					this.FileSizes[i] = 0;
				}
				this.TotalSize += this.FileSizes[i];
			}
		}

		// Release.
		protected override void Release()
		{
			// dispose cached sources of frames
			lock (this.FrameSourceCache)
			{
				foreach (var frameSource in this.FrameSourceCache)
					Global.RunWithoutError(frameSource.Dispose);
				this.FrameSourceCache.Clear();
			}

			// complete
			this.Logger.LogDebug("Release source of {count} file(s)", this.FileNames.Length);
		}
	}


	// Constants.
	const int FrameSourceCacheCapacity = 16;


	// Fields.
	readonly HolderImpl holder;


	/// <summary>
	/// Initialize new <see cref="FileSequenceImageDataSource"/> instance.
	/// </summary>
	/// <param name="app">Application.</param>
	/// <param name="fileNames">Names of files, one per frame. They are sorted in natural order.</param>
	/// <remarks>Size of each file is captured when instance is created, further changes of files are not reflected. A file which is inaccessible is kept in the sequence, its size is zero and getting source of its frame fails.</remarks>
	public FileSequenceImageDataSource(IApplication app, IEnumerable<string> fileNames) : base(new HolderImpl(app, SortFiles(fileNames))) =>
		this.holder = this.GetResourceHolder<HolderImpl>();


	// Constructor for sharing.
	FileSequenceImageDataSource(HolderImpl holder) : base(holder) =>
		this.holder = holder;


	/// <inheritdoc/>
	public bool CheckStreamAccess(StreamAccess access) => false;


	// Compare files by their file names in natural order, full paths are compared when file names are same to keep the order stable.
	static int CompareFiles(string x, string y)
	{
		var result = CompareNatural(Path.GetFileName(x), Path.GetFileName(y));
		if (result != 0)
			return result;
		return string.CompareOrdinal(x, y);
	}


	// Compare two strings treating consecutive digits as numbers.
	static int CompareNatural(string a, string b)
	{
		int ia = 0, ib = 0;
		while (ia < a.Length && ib < b.Length)
		{
			var ca = a[ia];
			var cb = b[ib];
			if (char.IsDigit(ca) && char.IsDigit(cb))
			{
				var sa = ia;
				var sb = ib;
				while (ia < a.Length && char.IsDigit(a[ia]))
					++ia;
				while (ib < b.Length && char.IsDigit(b[ib]))
					++ib;
				var na = a.Substring(sa, ia - sa).TrimStart('0');
				var nb = b.Substring(sb, ib - sb).TrimStart('0');
				if (na.Length != nb.Length)
					return na.Length - nb.Length;
				var cmp = string.CompareOrdinal(na, nb);
				if (cmp != 0)
					return cmp;
			}
			else
			{
				var cmp = char.ToLowerInvariant(ca).CompareTo(char.ToLowerInvariant(cb));
				if (cmp != 0)
					return cmp;
				++ia;
				++ib;
			}
		}
		return (a.Length - ia) - (b.Length - ib);
	}


	/// <inheritdoc/>
	public IList<string> FileNames => this.holder.ReadOnlyFileNames;


	/// <inheritdoc/>
	public int FrameCount => this.holder.FileNames.Length;


	/// <inheritdoc/>
	public async Task<IImageDataSource> GetFrameAsync(int frameIndex, CancellationToken cancellationToken)
	{
		// check state
		if (this.IsDisposed)
			throw new ObjectDisposedException(nameof(FileSequenceImageDataSource));
		if (frameIndex < 0 || frameIndex >= this.holder.FileNames.Length)
			throw new ArgumentOutOfRangeException(nameof(frameIndex));
		cancellationToken.ThrowIfCancellationRequested();

		// use cached source of frame if it is available
		var fileName = this.holder.FileNames[frameIndex];
		var frameSourceCache = this.holder.FrameSourceCache;
		lock (frameSourceCache)
		{
			var node = frameSourceCache.First;
			while (node is not null)
			{
				if (PathEqualityComparer.Default.Equals(node.Value.FileName, fileName))
				{
					frameSourceCache.Remove(node);
					frameSourceCache.AddFirst(node);
					return node.Value.Share();
				}
				node = node.Next;
			}
		}

		// create source of frame
		var app = this.holder.App;
		var frameSource = await Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			return new FileImageDataSource(app, fileName);
		}, cancellationToken);

		// keep source of frame in cache and share it to caller
		lock (frameSourceCache)
		{
			if (this.IsDisposed)
			{
				Global.RunWithoutErrorAsync(frameSource.Dispose);
				throw new ObjectDisposedException(nameof(FileSequenceImageDataSource));
			}
			frameSourceCache.AddFirst(frameSource);
			while (frameSourceCache.Count > FrameSourceCacheCapacity)
			{
				var lastNode = frameSourceCache.Last.AsNonNull();
				frameSourceCache.Remove(lastNode);
				Global.RunWithoutErrorAsync(lastNode.Value.Dispose);
			}
			return frameSource.Share();
		}
	}


	/// <inheritdoc/>
	public Task<Stream> OpenStreamAsync(StreamAccess access, CancellationToken token) =>
		throw new InvalidOperationException($"Cannot open stream of {nameof(FileSequenceImageDataSource)}, open stream of source of frame instead.");


	/// <inheritdoc/>
	protected override FileSequenceImageDataSource Share(BaseResourceHolder holder) => new FileSequenceImageDataSource((HolderImpl)holder);
	/// <inheritdoc/>
	IImageDataSource IShareableDisposable<IImageDataSource>.Share() => this.Share();


	/// <inheritdoc/>
	public long Size => this.holder.TotalSize;


	/// <summary>
	/// Sort file names in natural (numeric-aware) order by file name.
	/// </summary>
	internal static string[] SortFiles(IEnumerable<string> fileNames)
	{
		var sortedFileNames = fileNames.ToArray();
		Array.Sort(sortedFileNames, CompareFiles);
		return sortedFileNames;
	}


	/// <inheritdoc/>
	public override string ToString() => $"[FrameSequence: {this.FrameCount} file(s)]";
}
