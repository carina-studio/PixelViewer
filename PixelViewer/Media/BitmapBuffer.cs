using CarinaStudio;
using System;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Default implementation of <see cref="IBitmapBuffer"/>.
	/// </summary>
	class BitmapBuffer : BaseShareableDisposable<BitmapBuffer>, IBitmapBuffer
	{
		// Holder.
		class HolderImpl : BaseResourceHolder
		{
			// Fields.
			public readonly byte[] Array;
			public readonly ColorSpace ColorSpace;
			public readonly BitmapFormat Format;
			public readonly int Height;
			public readonly int RowBytes;
			public readonly int Width;

			// Constructor.
			public HolderImpl(BitmapFormat format, ColorSpace colorSpace, int width, int height)
			{
				this.ColorSpace = colorSpace;
				this.Format = format;
				this.Width = width;
				this.Height = height;
				this.RowBytes = format.CalculateRowBytes(width);
				this.Array = new byte[this.RowBytes * height];
			}

			// Release.
			protected override void Release()
			{ }
		}


		// Fields.
		Memory<byte>? memory;


		/// <summary>
		/// Initialize new <see cref="BitmapBuffer"/> instance.
		/// </summary>
		/// <param name="format">Format.</param>
		/// <param name="colorSpace">Color space.</param>
		/// <param name="width">Width in pixel.</param>
		/// <param name="height">Height in pixel.</param>
		public BitmapBuffer(BitmapFormat format, ColorSpace colorSpace, int width, int height) : base(new HolderImpl(format, colorSpace, width, height))
		{
			if (width <= 0)
				throw new ArgumentOutOfRangeException(nameof(width));
			if (height <= 0)
				throw new ArgumentOutOfRangeException(nameof(height));
			this.memory = new Memory<byte>(this.GetResourceHolder<HolderImpl>().Array);
		}


		// Constructor.
		BitmapBuffer(HolderImpl holder) : base(holder)
		{
			this.memory = new Memory<byte>(holder.Array);
		}


		// Implementations.
		public ColorSpace ColorSpace => this.GetResourceHolder<HolderImpl>().ColorSpace;
		public BitmapFormat Format => this.GetResourceHolder<HolderImpl>().Format;
		protected override void Dispose(bool disposing)
		{
			this.memory = null;
			base.Dispose(disposing);
		}
		public int Height => this.GetResourceHolder<HolderImpl>().Height;
		IBitmapBuffer IShareableDisposable<IBitmapBuffer>.Share() => this.Share();
		public Memory<byte> Memory => this.memory.GetValueOrDefault();
		public int RowBytes => this.GetResourceHolder<HolderImpl>().RowBytes;
		protected override BitmapBuffer Share(BaseResourceHolder holder) => new BitmapBuffer((HolderImpl)holder);
		public int Width => this.GetResourceHolder<HolderImpl>().Width;
	}
}
