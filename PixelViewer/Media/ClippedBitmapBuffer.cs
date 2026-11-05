using Avalonia;
using System;

namespace Carina.PixelViewer.Media;

/// <summary>
/// <see cref="IBitmapBuffer"/> which exposes a region of another <see cref="IBitmapBuffer"/> without copying its data.
/// </summary>
/// <remarks>The rows keep the row stride of the source, so the data of a row is followed by the rest of the source row rather than by the next row.
/// The source is shared by the instance and released when the instance is disposed.</remarks>
sealed class ClippedBitmapBuffer : IBitmapBuffer
{
    // Fields.
    bool isDisposed;
    readonly PixelRect region;
    readonly IBitmapBuffer source;


    /// <summary>
    /// Initialize new <see cref="ClippedBitmapBuffer"/> instance.
    /// </summary>
    /// <param name="source">Source <see cref="IBitmapBuffer"/>, which is shared by the instance.</param>
    /// <param name="region">Region of source to expose.</param>
    public ClippedBitmapBuffer(IBitmapBuffer source, PixelRect region)
    {
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 || region.Right > source.Width || region.Bottom > source.Height)
            throw new ArgumentOutOfRangeException(nameof(region));
        this.region = region;
        this.source = source.Share();
    }


    /// <inheritdoc/>
    public ColorSpace ColorSpace => this.source.ColorSpace;


    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.isDisposed)
            return;
        this.isDisposed = true;
        this.source.Dispose();
    }


    /// <inheritdoc/>
    public BitmapFormat Format => this.source.Format;


    /// <inheritdoc/>
    public int Height => this.region.Height;


    /// <inheritdoc/>
    public bool IsBufferSharedWith(IBitmapBuffer buffer) =>
        this.source.IsBufferSharedWith(buffer);


    /// <inheritdoc/>
    public Memory<byte> Memory
    {
        get
        {
            var offset = this.region.Y * this.source.RowBytes + this.region.X * this.source.Format.GetByteSize();
            return this.source.Memory[offset..];
        }
    }


    /// <inheritdoc/>
    public int RowBytes => this.source.RowBytes;


    /// <inheritdoc/>
    public IBitmapBuffer Share()
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        return new ClippedBitmapBuffer(this.source, this.region);
    }


    /// <inheritdoc/>
    public int Width => this.region.Width;
}
