using System;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Default implementation of <see cref="IBitmapBuffer"/>.
	/// </summary>
	class BitmapBuffer : BaseSharableDisposable<BitmapBuffer>, IBitmapBuffer
	{
		// Holder.
		class HolderImpl : BaseHolder
		{
			// Fields.
			public readonly byte[] Array;
			public readonly BitmapFormat Format;
			public readonly int Height;
			public readonly int RowBytes;
			public readonly int Width;

			// Constructor.
			public HolderImpl(BitmapFormat format, int width, int height)
			{
				this.Format = format;
				this.Width = width;
				this.Height = height;
				this.RowBytes = format.CalculateRowBytes(width);
				this.Array = new byte[this.RowBytes * height];
			}
		}


		// Fields.
		Memory<byte>? memory;


		/// <summary>
		/// Initialize new <see cref="BitmapBuffer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		/// <param name="width">Width in pixel.</param>
		/// <param name="height">Height in pixel.</param>
		public BitmapBuffer(BitmapFormat format, int width, int height) : base(new HolderImpl(format, width, height))
		{
			if (width <= 0)
				throw new ArgumentOutOfRangeException(nameof(width));
			if (height <= 0)
				throw new ArgumentOutOfRangeException(nameof(height));
			this.memory = new Memory<byte>(((HolderImpl)this.Holder).Array);
		}


		// Constructor.
		BitmapBuffer(HolderImpl holder) : base(holder)
		{
			this.memory = new Memory<byte>(holder.Array);
		}


		// Implementations.
		public BitmapFormat Format => ((HolderImpl)this.Holder).Format;
		protected override void Dispose(bool disposing)
		{
			this.memory = null;
			base.Dispose(disposing);
		}
		public int Height => ((HolderImpl)this.Holder).Height;
		IBitmapBuffer ISharableDisposable<IBitmapBuffer>.Share() => this.Share();
		public Memory<byte> Memory => this.memory.GetValueOrDefault();
		protected override BitmapBuffer OnShare(BaseHolder holder) => new BitmapBuffer((HolderImpl)holder);
		public int RowBytes => ((HolderImpl)this.Holder).RowBytes;
		public int Width => ((HolderImpl)this.Holder).Width;
	}
}
