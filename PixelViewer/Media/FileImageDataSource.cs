using Carina.PixelViewer.IO;
using CarinaStudio;
using CarinaStudio.Collections;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Implementation of <see cref="IImageDataSource"/> based-on file.
	/// </summary>
	unsafe class FileImageDataSource : BaseShareableDisposable<FileImageDataSource>, IImageDataSource
	{
		// Holder.
		class HolderImpl : BaseResourceHolder
		{
			// Fields.
			public readonly FileStream BaseStream;

			// Constructor.
			public HolderImpl(string fileName)
			{
				logger.Debug($"Create source of '{fileName}'");
				this.BaseStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
			}

			// Release.
			protected override void Release()
			{
				logger.Debug($"Release source of '{this.BaseStream.Name}'");
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


		// Static fields.
		static readonly ILogger logger = LogManager.GetCurrentClassLogger();


		// Fields.
		readonly List<WeakReference<StreamImpl>> openedStreams = new List<WeakReference<StreamImpl>>();


		/// <summary>
		/// Initialize new <see cref="FileImageDataSource"/> instance.
		/// </summary>
		/// <param name="fileName">Name of file of image.</param>
		public FileImageDataSource(string fileName) : base(new HolderImpl(fileName))
		{ }


		// Constructor.
		FileImageDataSource(HolderImpl holder) : base(holder)
		{ }


		// Dispose.
		[MethodImpl(MethodImplOptions.Synchronized)]
		protected override void Dispose(bool disposing)
		{
			// close all opened streams
			if (disposing && this.openedStreams.IsNotEmpty())
			{
				logger.Warn($"Close {this.openedStreams.Count} opened stream(s) of '{this.FileName}'");
				foreach (var streamRef in this.openedStreams.ToArray())
				{
					try
					{
						if (streamRef.TryGetTarget(out var stream))
							stream.Close();
					}
					catch (Exception ex)
					{
						logger.Warn(ex, $"Error occurred while closing opened stream of '{this.FileName}'");
					}
				}
				this.openedStreams.Clear();
			}

			// call base
			base.Dispose(disposing);
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


		// Open stream to read.
		[MethodImpl(MethodImplOptions.Synchronized)]
		public Stream Open()
		{
			if (this.IsDisposed)
				throw new ObjectDisposedException(this.GetType().Name);
			var fileStream = new FileStream(this.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
			var stream = new StreamImpl(this, fileStream);
			this.openedStreams.Add(new WeakReference<StreamImpl>(stream));
			return stream;
		}


		/// <summary>
		/// Get name of file.
		/// </summary>
		public string FileName { get => this.GetResourceHolder<HolderImpl>().BaseStream.Name; }


		// Share.
		protected override FileImageDataSource Share(BaseResourceHolder holder) => new FileImageDataSource((HolderImpl)holder);
		IImageDataSource IShareableDisposable<IImageDataSource>.Share() => this.Share();


		// Size of image data.
		public long Size { get => this.GetResourceHolder<HolderImpl>().BaseStream.Length; }


		// To readable string.
		public override string ToString() => $"[{this.FileName}]";
	}
}
