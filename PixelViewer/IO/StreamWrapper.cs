using System;
using System.IO;

namespace Carina.PixelViewer.IO
{
	/// <summary>
	/// <see cref="Stream"/> which wraps another <see cref="Stream"/>.
	/// </summary>
	abstract class StreamWrapper : Stream
	{
		/// <summary>
		/// Initialize new <see cref="StreamWrapper"/> instance.
		/// </summary>
		/// <param name="baseStream"><see cref="Stream"/> to be wrapped.</param>
		protected StreamWrapper(Stream baseStream)
		{
			this.BaseStream = baseStream;
		}


		/// <summary>
		/// Get the <see cref="Stream"/> wrapped by this instance.
		/// </summary>
		protected Stream BaseStream { get; }


		// Implementations.
		public override bool CanRead => this.BaseStream.CanRead;
		public override bool CanSeek => this.BaseStream.CanSeek;
		public override bool CanWrite => this.BaseStream.CanWrite;
		public override void Close()
		{
			this.BaseStream.Close();
			base.Close();
		}
		public override long Length => this.BaseStream.Length;
		public override long Position
		{
			get => this.BaseStream.Position;
			set
			{
				this.BaseStream.Position = value;
			}
		}
		public override void Flush() => this.BaseStream.Flush();
		public override int Read(byte[] buffer, int offset, int count) => this.BaseStream.Read(buffer, offset, count);
		public override long Seek(long offset, SeekOrigin origin) => this.BaseStream.Seek(offset, origin);
		public override void SetLength(long value) => this.BaseStream.SetLength(value);
		public override void Write(byte[] buffer, int offset, int count) => this.BaseStream.Write(buffer, offset, count);
	}
}
