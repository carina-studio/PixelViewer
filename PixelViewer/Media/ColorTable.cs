using CarinaStudio;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

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


    // Constants.
    const string ColorsPropertyName = "Colors";


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


    // Encode the colors in the table into a Base64 string. Each color is encoded as a little-endian 32-bit value and
    // the encoded values are compressed before being converted, little-endian is used explicitly so that the string
    // can be decoded by every platform no matter which byte ordering it uses natively.
    string EncodeColors()
    {
        // convert the colors into little-endian bytes
        var colors = this.GetResourceHolder<HolderImpl>().Colors;
        var bytes = new byte[colors.Length * sizeof(uint)];
        for (var i = colors.Length - 1; i >= 0; --i)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint)), colors[i]);

        // compress the bytes, the colors of a table are usually smooth so they are compressed well
        using var compressedStream = new MemoryStream();
        using (var compressingStream = new ZLibStream(compressedStream, CompressionLevel.Optimal, true))
            compressingStream.Write(bytes);

        // convert the compressed bytes in place, the buffer of the stream is longer than the bytes written into it
        if (!compressedStream.TryGetBuffer(out var buffer))
            return Convert.ToBase64String(compressedStream.ToArray());
        return Convert.ToBase64String(buffer.AsSpan());
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


    // Try decoding the colors which were encoded by EncodeColors().
    static bool TryDecodeColors(string encodedColors, int colorBitDepth, [NotNullWhen(true)] out ColorTable? colorTable)
    {
        colorTable = null;
        try
        {
            // decompress the colors
            using var decompressedStream = new MemoryStream();
            using (var compressedStream = new MemoryStream(Convert.FromBase64String(encodedColors)))
            {
                using var decompressingStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
                decompressingStream.CopyTo(decompressedStream);
            }

            // check size of decompressed colors, the buffer of the stream is read in place instead of being copied out
            if (!decompressedStream.TryGetBuffer(out var buffer))
                return false;
            var count = buffer.Count / sizeof(uint);
            if (count <= 0 || count > MaxCount || (buffer.Count % sizeof(uint)) != 0)
                return false;

            // convert the little-endian bytes back into colors
            var decodedColorTable = new ColorTable(count, colorBitDepth);
            var colors = decodedColorTable.Memory.Span;
            var bytes = buffer.AsSpan();
            for (var i = count - 1; i >= 0; --i)
                colors[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * sizeof(uint))..]);
            colorTable = decodedColorTable;
            return true;
        }
        catch
        {
            colorTable = colorTable.DisposeAndReturnNull();
            return false;
        }
    }


    /// <summary>
    /// Try creating <see cref="ColorTable"/> from the JSON value written by <see cref="WriteToJson(Utf8JsonWriter)"/>.
    /// </summary>
    /// <param name="jsonValue">JSON value to load the table from.</param>
    /// <param name="colorTable">Created <see cref="ColorTable"/>.</param>
    /// <returns>True if the table was loaded successfully.</returns>
    public static bool TryLoadFromJson(JsonElement jsonValue, [NotNullWhen(true)] out ColorTable? colorTable)
    {
        // check the bit depth of colors
        colorTable = null;
        if (jsonValue.ValueKind != JsonValueKind.Object)
            return false;
        if (!jsonValue.TryGetProperty(nameof(ColorBitDepth), out var jsonProperty)
            || !jsonProperty.TryGetInt32(out var colorBitDepth)
            || colorBitDepth <= 0
            || colorBitDepth > MaxColorBitDepth)
        {
            return false;
        }

        // decode the colors
        if (!jsonValue.TryGetProperty(ColorsPropertyName, out jsonProperty) || jsonProperty.ValueKind != JsonValueKind.String)
            return false;
        return TryDecodeColors(jsonProperty.GetString().AsNonNull(), colorBitDepth, out colorTable);
    }


    /// <summary>
    /// Write the table as a JSON value which can be loaded by <see cref="TryLoadFromJson(JsonElement, out ColorTable?)"/>.
    /// </summary>
    /// <param name="jsonWriter"><see cref="Utf8JsonWriter"/> to write the table. The name of property should be written by the caller.</param>
    /// <remarks>The number of colors is not written because it is defined by the encoded colors themselves.</remarks>
    public void WriteToJson(Utf8JsonWriter jsonWriter)
    {
        jsonWriter.WriteStartObject();
        jsonWriter.WriteNumber(nameof(ColorBitDepth), this.ColorBitDepth);
        jsonWriter.WriteString(ColorsPropertyName, this.EncodeColors());
        jsonWriter.WriteEndObject();
    }
}


/// <summary>
/// Extensions for <see cref="ColorTable"/>.
/// </summary>
static class ColorTableExtensions
{
    extension(ColorTable? colorTable)
    {
        /// <summary>
        /// Check whether the given <see cref="ColorTable"/> defines the same colors as this one or not.
        /// </summary>
        /// <param name="anotherColorTable"><see cref="ColorTable"/> to check.</param>
        /// <returns>True if both of them are null, or both of them share the same colors.</returns>
        /// <remarks>None of the instances should be disposed before calling the method.</remarks>
        public bool IsSameAs(ColorTable? anotherColorTable)
        {
            if (colorTable is null)
                return anotherColorTable is null;
            if (anotherColorTable is null)
                return false;
            return colorTable.IsContentSharedWith(anotherColorTable);
        }
    }
}
