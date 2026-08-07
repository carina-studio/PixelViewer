using CarinaStudio;
using System;
using System.Buffers;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Table which maps the value of a single color channel of source image to the color to be rendered.
/// </summary>
/// <remarks>The content of the table should be filled through <see cref="Memory"/> right after the instance is created,
/// and should not be modified after the instance is used for rendering image.</remarks>
class ColorTable : BaseShareableDisposable<ColorTable>, IMemoryOwner<uint>
{
    /// <summary>
    /// Maximum number of colors in the table.
    /// </summary>
    public const int MaxCount = 65536;


    /// <summary>
    /// Maximum bit depth of color in the table.
    /// </summary>
    public const int MaxColorBitDepth = 32;


    // Holder of the colors shared by all instances which refer to the same table.
    class HolderImpl(int count, int colorBitDepth) : BaseResourceHolder
    {
        // Fields.
        public readonly uint[] Colors = new uint[count];
        public readonly int ColorBitDepth = colorBitDepth;

        // Release.
        protected override void Release()
        { }
    }


    // Fields.
    Memory<uint>? memory;


    /// <summary>
    /// Initialize new <see cref="ColorTable"/> instance.
    /// </summary>
    /// <param name="count">Number of colors in the table.</param>
    /// <param name="colorBitDepth">Bit depth of each color in the table.</param>
    /// <remarks>The parameters are checked after the colors have been allocated, as <see cref="BitmapBuffer"/> does.
    /// Checking them earlier would leave a half-constructed instance to be finalized when they are invalid.</remarks>
    public ColorTable(int count, int colorBitDepth) : base(new HolderImpl(count, colorBitDepth))
    {
        if (count <= 0 || count > MaxCount)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (colorBitDepth <= 0 || colorBitDepth > MaxColorBitDepth)
            throw new ArgumentOutOfRangeException(nameof(colorBitDepth));
        this.memory = new Memory<uint>(this.GetResourceHolder<HolderImpl>().Colors);
    }


    // Constructor for sharing the table with another instance.
    ColorTable(HolderImpl holder) : base(holder)
    {
        this.memory = new Memory<uint>(holder.Colors);
    }


    /// <summary>
    /// Get bit depth of each color in the table.
    /// </summary>
    public int ColorBitDepth => this.GetResourceHolder<HolderImpl>().ColorBitDepth;


    /// <summary>
    /// Get number of colors in the table.
    /// </summary>
    /// <remarks>The number is not necessary to be a power of 2. Rendering an image which refers to a color out of
    /// the range of the table will fail instead of being detected in advance.</remarks>
    public int Count => this.GetResourceHolder<HolderImpl>().Colors.Length;


    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        this.memory = null;
        base.Dispose(disposing);
    }


    /// <summary>
    /// Check whether the colors in the table are shared with the given <see cref="ColorTable"/> or not.
    /// </summary>
    /// <param name="colorTable"><see cref="ColorTable"/> to check.</param>
    /// <returns>True if the colors are shared with <paramref name="colorTable"/>.</returns>
    /// <remarks>True is also returned if <paramref name="colorTable"/> is the same instance. None of the instances should be disposed before calling the method.</remarks>
    public bool IsContentSharedWith(ColorTable colorTable) =>
        this.GetResourceHolder<HolderImpl>() == colorTable.GetResourceHolder<HolderImpl>();


    /// <inheritdoc/>
    public Memory<uint> Memory => this.memory.GetValueOrDefault();


    /// <inheritdoc/>
    protected override ColorTable Share(BaseResourceHolder holder) => new((HolderImpl)holder);
}
