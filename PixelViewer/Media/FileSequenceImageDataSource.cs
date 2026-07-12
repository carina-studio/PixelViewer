using CarinaStudio;
using CarinaStudio.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Implementation of <see cref="IMultiFrameImageDataSource"/> which treats each of a set of files as one frame.
	/// </summary>
	class FileSequenceImageDataSource : BaseShareableDisposable<FileSequenceImageDataSource>, IImageDataSource, IMultiFrameImageDataSource
	{
		// Holder.
		class HolderImpl : BaseResourceHolder
		{
			// Fields.
			public volatile int CurrentIndex;
			public readonly long[] FileSizes;
			public readonly string[] FileNames;

			// Constructor.
			public HolderImpl(string[] fileNames)
			{
				this.FileNames = fileNames;
				this.FileSizes = new long[fileNames.Length];
				for (var i = fileNames.Length - 1; i >= 0; --i)
					this.FileSizes[i] = new FileInfo(fileNames[i]).Length;
			}

			// Release.
			protected override void Release()
			{ }
		}


		// Fields.
		readonly HolderImpl holder;


		/// <summary>
		/// Initialize new <see cref="FileSequenceImageDataSource"/> instance.
		/// </summary>
		/// <param name="fileNames">Names of files, one per frame. They are sorted in natural order.</param>
		public FileSequenceImageDataSource(IEnumerable<string> fileNames) : base(new HolderImpl(SortFiles(fileNames)))
		{
			this.holder = this.GetResourceHolder<HolderImpl>();
			if (this.holder.FileNames.Length == 0)
				throw new ArgumentException("No file provided for frame sequence.");
		}


		// Constructor for sharing.
		FileSequenceImageDataSource(HolderImpl holder) : base(holder) =>
			this.holder = holder;


		// Check access.
		public bool CheckStreamAccess(StreamAccess access) =>
			!this.IsDisposed && access == StreamAccess.Read;


		/// <summary>
		/// Get the file name of the currently selected frame.
		/// </summary>
		public string CurrentFileName => this.holder.FileNames[this.holder.CurrentIndex];


		/// <summary>
		/// Get number of frames (files) contained in the source.
		/// </summary>
		public int FrameCount => this.holder.FileNames.Length;


		// Open stream of the currently selected frame.
		public async Task<Stream> OpenStreamAsync(StreamAccess access, CancellationToken token)
		{
			// check access
			if (!this.CheckStreamAccess(access))
				throw new ArgumentException($"Cannot open stream with {access} access.");

			// open stream of current frame file
			var fileName = this.holder.FileNames[this.holder.CurrentIndex];
			return await Task.Run<Stream>(() =>
			{
				if (this.IsDisposed)
					throw new ObjectDisposedException(nameof(FileSequenceImageDataSource));
				if (token.IsCancellationRequested)
					throw new TaskCanceledException();
				try
				{
					return new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
				}
				catch
				{
					if (token.IsCancellationRequested)
						throw new TaskCanceledException();
					throw;
				}
			}, token);
		}


		/// <summary>
		/// Select the frame whose data will be served.
		/// </summary>
		/// <param name="frameIndex">0-based frame index.</param>
		public void SelectFrame(int frameIndex) =>
			this.holder.CurrentIndex = Math.Max(0, Math.Min(this.holder.FileNames.Length - 1, frameIndex));


		// Share.
		protected override FileSequenceImageDataSource Share(BaseResourceHolder holder) => new FileSequenceImageDataSource((HolderImpl)holder);
		IImageDataSource IShareableDisposable<IImageDataSource>.Share() => this.Share();


		// Size of the currently selected frame.
		public long Size => this.holder.FileSizes[this.holder.CurrentIndex];


		/// <summary>
		/// Sort file names in natural (numeric-aware) order by file name.
		/// </summary>
		internal static string[] SortFiles(IEnumerable<string> fileNames)
		{
			var array = fileNames.ToArray();
			Array.Sort(array, (x, y) => CompareNatural(Path.GetFileName(x), Path.GetFileName(y)));
			return array;
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


		// To readable string.
		public override string ToString() => $"[FrameSequence: {this.FrameCount} file(s)]";
	}
}
