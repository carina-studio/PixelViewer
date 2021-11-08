using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.IO;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Implementation of <see cref="IImageDataSource"/> based-on file.
	/// </summary>
	class FileImageDataSource : BaseShareableDisposable<FileImageDataSource>, IImageDataSource
	{
		// Holder.
		class HolderImpl : BaseResourceHolder
		{
			// Fields.
			public readonly IApplication Application;
			public readonly FileStream BaseStream;
			public readonly ILogger Logger;

			// Constructor.
			public HolderImpl(IApplication app, string fileName)
			{
				this.Logger = app.LoggerFactory.CreateLogger(nameof(FileImageDataSource));
				this.Logger.LogDebug($"Create source of '{fileName}'");
				this.Application = app;
				this.BaseStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
			}

			// Release.
			protected override void Release()
			{
				this.Logger.LogDebug($"Release source of '{this.BaseStream.Name}'");
				try
				{
					this.BaseStream.Close();
				}
				catch
				{ }
			}
		}


		// Stream to read data.
		class StreamImpl : StreamWrapper
		{
			// Fields.
			readonly FileImageDataSource source;

			// Constructor.
			public StreamImpl(FileImageDataSource source, FileStream fileStream) : base(fileStream)
			{
				this.source = source;
			}

			// Dispose.
			protected override void Dispose(bool disposing)
			{
				base.Dispose(disposing);
				if (disposing)
					this.source.OnStreamClosed(this);
			}
		}


		// Fields.
		readonly ILogger logger;
		readonly List<WeakReference<StreamImpl>> openedStreams = new List<WeakReference<StreamImpl>>();


		/// <summary>
		/// Initialize new <see cref="FileImageDataSource"/> instance.
		/// </summary>
		/// <param name="fileName">Name of file of image.</param>
		public FileImageDataSource(IApplication app, string fileName) : base(new HolderImpl(app, fileName))
		{
			var holder = this.GetResourceHolder<HolderImpl>();
			this.FileName = holder.BaseStream.Name;
			this.logger = holder.Logger;
		}


		// Constructor.
		FileImageDataSource(HolderImpl holder) : base(holder)
		{
			this.FileName = holder.BaseStream.Name;
			this.logger = holder.Logger;
		}


		// Check access.
		public bool CheckStreamAccess(StreamAccess access)
		{
			return !this.IsDisposed && access == StreamAccess.Read;
		}


		// Dispose.
		[MethodImpl(MethodImplOptions.Synchronized)]
		protected override void Dispose(bool disposing)
		{
			// close all opened streams
			if (disposing && this.openedStreams.IsNotEmpty())
			{
				this.logger.LogWarning($"Close {this.openedStreams.Count} opened stream(s) of '{this.FileName}'");
				foreach (var streamRef in this.openedStreams.ToArray())
				{
					if (streamRef.TryGetTarget(out var stream))
						Global.RunWithoutErrorAsync(stream.Dispose);
				}
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


		// Called when stream closed.
		[MethodImpl(MethodImplOptions.Synchronized)]
		void OnStreamClosed(StreamImpl stream)
		{
			for (var i = this.openedStreams.Count - 1; i >= 0; --i)
			{
				if (!this.openedStreams[i].TryGetTarget(out var checkingStream))
					this.openedStreams.RemoveAt(i);
				else if (checkingStream == stream)
				{
					this.openedStreams.RemoveAt(i);
					break;
				}
			}
		}


		public async Task<Stream> OpenStreamAsync(StreamAccess access, CancellationToken token)
		{
			// check access
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
				this.openedStreams.Add(new WeakReference<StreamImpl>(stream));
			}

			// complete
			return stream;
		}


		/// <summary>
		/// Get name of file.
		/// </summary>
		public string FileName { get; }


		// Share.
		protected override FileImageDataSource Share(BaseResourceHolder holder) => new FileImageDataSource((HolderImpl)holder);
		IImageDataSource IShareableDisposable<IImageDataSource>.Share() => this.Share();


		// Size of image data.
		public long Size { get => this.GetResourceHolder<HolderImpl>().BaseStream.Length; }


		// To readable string.
		public override string ToString() => $"[{this.FileName}]";
    }
}
