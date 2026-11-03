using CarinaStudio;
using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Map of gains which corrects the shading of lens, defined as a grid of gains for each position of the repeating block of pixels.
/// </summary>
/// <remarks>
/// The map applies to the pixels inside the area from (<see cref="AreaLeft"/>, <see cref="AreaTop"/>) to (<see cref="AreaRight"/>, <see cref="AreaBottom"/>),
/// the right and bottom are exclusive. The pixel at (x, y) inside the area is at position ((x - <see cref="AreaLeft"/>) % <see cref="PitchX"/>, (y - <see cref="AreaTop"/>) % <see cref="PitchY"/>)
/// of the repeating block, and each position has a grid of gains of its own. The grid point (i, j) is placed at (<see cref="OriginX"/> + i * <see cref="SpacingX"/>, <see cref="OriginY"/> + j * <see cref="SpacingY"/>)
/// relative to the area, which is 0 at the left or top edge and 1 at the right or bottom edge. The instance cannot be modified after creation.
/// </remarks>
class LensShadingMap : IEquatable<LensShadingMap>
{
    /// <summary>
    /// Maximum pitch of positions in each direction.
    /// </summary>
    public const int MaxPitch = 8;
    /// <summary>
    /// Maximum number of grid points in each direction.
    /// </summary>
    public const int MaxPointCount = 4096;


    // Constants.
    const string GainsPropertyName = "Gains";


    // Fields.
    readonly float[] gains;


    /// <summary>
    /// Initialize new <see cref="LensShadingMap"/> instance.
    /// </summary>
    /// <param name="areaLeft">Left of the area in pixels.</param>
    /// <param name="areaTop">Top of the area in pixels.</param>
    /// <param name="areaRight">Exclusive right of the area in pixels.</param>
    /// <param name="areaBottom">Exclusive bottom of the area in pixels.</param>
    /// <param name="pitchX">Horizontal pitch of positions.</param>
    /// <param name="pitchY">Vertical pitch of positions.</param>
    /// <param name="pointCountX">Number of grid points in horizontal direction.</param>
    /// <param name="pointCountY">Number of grid points in vertical direction.</param>
    /// <param name="originX">Horizontal position of the first grid point relative to the area.</param>
    /// <param name="originY">Vertical position of the first grid point relative to the area.</param>
    /// <param name="spacingX">Horizontal spacing between grid points relative to the area.</param>
    /// <param name="spacingY">Vertical spacing between grid points relative to the area.</param>
    /// <param name="gains">Gains ordered by position, row of grid and column of grid, the positions are ordered by row first.</param>
    public LensShadingMap(int areaLeft, int areaTop, int areaRight, int areaBottom, int pitchX, int pitchY, int pointCountX, int pointCountY, double originX, double originY, double spacingX, double spacingY, ReadOnlySpan<float> gains)
    {
        // check area
        if (areaLeft < 0 || areaRight <= areaLeft)
            throw new ArgumentOutOfRangeException(nameof(areaRight));
        if (areaTop < 0 || areaBottom <= areaTop)
            throw new ArgumentOutOfRangeException(nameof(areaBottom));

        // check grid
        if (pitchX <= 0 || pitchX > MaxPitch)
            throw new ArgumentOutOfRangeException(nameof(pitchX));
        if (pitchY <= 0 || pitchY > MaxPitch)
            throw new ArgumentOutOfRangeException(nameof(pitchY));
        if (pointCountX <= 0 || pointCountX > MaxPointCount)
            throw new ArgumentOutOfRangeException(nameof(pointCountX));
        if (pointCountY <= 0 || pointCountY > MaxPointCount)
            throw new ArgumentOutOfRangeException(nameof(pointCountY));
        if (!double.IsFinite(originX))
            throw new ArgumentOutOfRangeException(nameof(originX));
        if (!double.IsFinite(originY))
            throw new ArgumentOutOfRangeException(nameof(originY));
        if (!double.IsFinite(spacingX) || spacingX <= 0)
            throw new ArgumentOutOfRangeException(nameof(spacingX));
        if (!double.IsFinite(spacingY) || spacingY <= 0)
            throw new ArgumentOutOfRangeException(nameof(spacingY));

        // check gains
        if (gains.Length != pitchX * pitchY * pointCountX * pointCountY)
            throw new ArgumentException("Number of gains does not match the positions and the grid.", nameof(gains));
        foreach (var gain in gains)
        {
            if (!float.IsFinite(gain) || gain < 0)
                throw new ArgumentException("Gain should be a finite and non-negative value.", nameof(gains));
        }

        // keep the map
        this.AreaBottom = areaBottom;
        this.AreaLeft = areaLeft;
        this.AreaRight = areaRight;
        this.AreaTop = areaTop;
        this.gains = gains.ToArray();
        this.OriginX = originX;
        this.OriginY = originY;
        this.PitchX = pitchX;
        this.PitchY = pitchY;
        this.PointCountX = pointCountX;
        this.PointCountY = pointCountY;
        this.SpacingX = spacingX;
        this.SpacingY = spacingY;
    }


    /// <summary>
    /// Get exclusive bottom of the area which the map applies to in pixels.
    /// </summary>
    public int AreaBottom { get; }


    /// <summary>
    /// Get left of the area which the map applies to in pixels.
    /// </summary>
    public int AreaLeft { get; }


    /// <summary>
    /// Get exclusive right of the area which the map applies to in pixels.
    /// </summary>
    public int AreaRight { get; }


    /// <summary>
    /// Get top of the area which the map applies to in pixels.
    /// </summary>
    public int AreaTop { get; }


    // Encode the gains into a Base64 string. Each gain is encoded as a little-endian 32-bit floating-point value and
    // the encoded values are compressed before being converted, as ColorTable does for its colors.
    string EncodeGains()
    {
        // convert the gains into little-endian bytes
        var bytes = new byte[this.gains.Length * sizeof(float)];
        for (var i = this.gains.Length - 1; i >= 0; --i)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), this.gains[i]);

        // compress the bytes, the gains of a map are usually smooth so they are compressed well
        using var compressedStream = new MemoryStream();
        using (var compressingStream = new ZLibStream(compressedStream, CompressionLevel.Optimal, true))
            compressingStream.Write(bytes);

        // convert the compressed bytes in place, the buffer of the stream is longer than the bytes written into it
        if (!compressedStream.TryGetBuffer(out var buffer))
            return Convert.ToBase64String(compressedStream.ToArray());
        return Convert.ToBase64String(buffer.AsSpan());
    }


    /// <inheritdoc/>
    public bool Equals(LensShadingMap? map)
    {
        if (map is null)
            return false;
        if (ReferenceEquals(this, map))
            return true;
        return this.AreaLeft == map.AreaLeft
            && this.AreaTop == map.AreaTop
            && this.AreaRight == map.AreaRight
            && this.AreaBottom == map.AreaBottom
            && this.PitchX == map.PitchX
            && this.PitchY == map.PitchY
            && this.PointCountX == map.PointCountX
            && this.PointCountY == map.PointCountY
            && this.OriginX.Equals(map.OriginX)
            && this.OriginY.Equals(map.OriginY)
            && this.SpacingX.Equals(map.SpacingX)
            && this.SpacingY.Equals(map.SpacingY)
            && this.gains.AsSpan().SequenceEqual(map.gains);
    }


    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is LensShadingMap map && this.Equals(map);


    /// <summary>
    /// Get all gains ordered by position, row of grid and column of grid, the positions are ordered by row first.
    /// </summary>
    public ReadOnlySpan<float> Gains => this.gains;


    /// <summary>
    /// Get the gain at given grid point of given position.
    /// </summary>
    /// <param name="positionX">Horizontal position, in the range of [0, <see cref="PitchX"/>).</param>
    /// <param name="positionY">Vertical position, in the range of [0, <see cref="PitchY"/>).</param>
    /// <param name="pointX">Column of grid point, in the range of [0, <see cref="PointCountX"/>).</param>
    /// <param name="pointY">Row of grid point, in the range of [0, <see cref="PointCountY"/>).</param>
    /// <returns>Gain.</returns>
    public float GetGain(int positionX, int positionY, int pointX, int pointY)
    {
        if (positionX < 0 || positionX >= this.PitchX)
            throw new ArgumentOutOfRangeException(nameof(positionX));
        if (positionY < 0 || positionY >= this.PitchY)
            throw new ArgumentOutOfRangeException(nameof(positionY));
        if (pointX < 0 || pointX >= this.PointCountX)
            throw new ArgumentOutOfRangeException(nameof(pointX));
        if (pointY < 0 || pointY >= this.PointCountY)
            throw new ArgumentOutOfRangeException(nameof(pointY));
        return this.gains[(((positionY * this.PitchX) + positionX) * this.PointCountY + pointY) * this.PointCountX + pointX];
    }


    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // combine the geometry
        var hashCode = new HashCode();
        hashCode.Add(this.AreaLeft);
        hashCode.Add(this.AreaTop);
        hashCode.Add(this.AreaRight);
        hashCode.Add(this.AreaBottom);
        hashCode.Add(this.PitchX);
        hashCode.Add(this.PitchY);
        hashCode.Add(this.PointCountX);
        hashCode.Add(this.PointCountY);

        // combine the gains
        hashCode.AddBytes(MemoryMarshal.AsBytes(this.gains.AsSpan()));
        return hashCode.ToHashCode();
    }


    /// <summary>
    /// Get horizontal position of the first grid point relative to the area.
    /// </summary>
    public double OriginX { get; }


    /// <summary>
    /// Get vertical position of the first grid point relative to the area.
    /// </summary>
    public double OriginY { get; }


    /// <summary>
    /// Get horizontal pitch of positions, which is the width of the repeating block of pixels.
    /// </summary>
    public int PitchX { get; }


    /// <summary>
    /// Get vertical pitch of positions, which is the height of the repeating block of pixels.
    /// </summary>
    public int PitchY { get; }


    /// <summary>
    /// Get number of grid points in horizontal direction.
    /// </summary>
    public int PointCountX { get; }


    /// <summary>
    /// Get number of grid points in vertical direction.
    /// </summary>
    public int PointCountY { get; }


    /// <summary>
    /// Get horizontal spacing between grid points relative to the area.
    /// </summary>
    public double SpacingX { get; }


    /// <summary>
    /// Get vertical spacing between grid points relative to the area.
    /// </summary>
    public double SpacingY { get; }


    // Try decoding the gains which were encoded by EncodeGains().
    static bool TryDecodeGains(string encodedGains, [NotNullWhen(true)] out float[]? gains)
    {
        gains = null;
        try
        {
            // decompress the gains
            using var decompressedStream = new MemoryStream();
            using (var compressedStream = new MemoryStream(Convert.FromBase64String(encodedGains)))
            {
                using var decompressingStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
                decompressingStream.CopyTo(decompressedStream);
            }

            // check size of decompressed gains, the buffer of the stream is read in place instead of being copied out
            if (!decompressedStream.TryGetBuffer(out var buffer))
                return false;
            if (buffer.Count == 0 || (buffer.Count % sizeof(float)) != 0)
                return false;

            // convert the little-endian bytes back into gains
            var count = buffer.Count / sizeof(float);
            var decodedGains = new float[count];
            var bytes = buffer.AsSpan();
            for (var i = count - 1; i >= 0; --i)
                decodedGains[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * sizeof(float))..]);
            gains = decodedGains;
            return true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Try creating <see cref="LensShadingMap"/> from the JSON value written by <see cref="WriteToJson(Utf8JsonWriter)"/>.
    /// </summary>
    /// <param name="jsonValue">JSON value to load the map from.</param>
    /// <param name="map">Created <see cref="LensShadingMap"/>.</param>
    /// <returns>True if the map was loaded successfully.</returns>
    public static bool TryLoadFromJson(JsonElement jsonValue, [NotNullWhen(true)] out LensShadingMap? map)
    {
        // get geometry
        map = null;
        if (jsonValue.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryGetInt32(nameof(AreaLeft), out var areaLeft)
            || !TryGetInt32(nameof(AreaTop), out var areaTop)
            || !TryGetInt32(nameof(AreaRight), out var areaRight)
            || !TryGetInt32(nameof(AreaBottom), out var areaBottom)
            || !TryGetInt32(nameof(PitchX), out var pitchX)
            || !TryGetInt32(nameof(PitchY), out var pitchY)
            || !TryGetInt32(nameof(PointCountX), out var pointCountX)
            || !TryGetInt32(nameof(PointCountY), out var pointCountY)
            || !TryGetDouble(nameof(OriginX), out var originX)
            || !TryGetDouble(nameof(OriginY), out var originY)
            || !TryGetDouble(nameof(SpacingX), out var spacingX)
            || !TryGetDouble(nameof(SpacingY), out var spacingY))
        {
            return false;
        }

        // decode the gains
        if (!jsonValue.TryGetProperty(GainsPropertyName, out var jsonProperty)
            || jsonProperty.ValueKind != JsonValueKind.String
            || !TryDecodeGains(jsonProperty.GetString().AsNonNull(), out var gains))
        {
            return false;
        }

        // create the map, the parameters are checked by the constructor
        try
        {
            map = new LensShadingMap(areaLeft, areaTop, areaRight, areaBottom, pitchX, pitchY, pointCountX, pointCountY, originX, originY, spacingX, spacingY, gains);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }

        // get double value of the property with given name
        bool TryGetDouble(string name, out double value)
        {
            value = 0;
            return jsonValue.TryGetProperty(name, out var jsonProperty)
                && jsonProperty.ValueKind == JsonValueKind.Number
                && jsonProperty.TryGetDouble(out value);
        }

        // get integer value of the property with given name
        bool TryGetInt32(string name, out int value)
        {
            value = 0;
            return jsonValue.TryGetProperty(name, out var jsonProperty)
                && jsonProperty.ValueKind == JsonValueKind.Number
                && jsonProperty.TryGetInt32(out value);
        }
    }


    /// <summary>
    /// Write the map as a JSON value which can be loaded by <see cref="TryLoadFromJson(JsonElement, out LensShadingMap?)"/>.
    /// </summary>
    /// <param name="jsonWriter"><see cref="Utf8JsonWriter"/> to write the map. The name of property should be written by the caller.</param>
    public void WriteToJson(Utf8JsonWriter jsonWriter)
    {
        jsonWriter.WriteStartObject();
        jsonWriter.WriteNumber(nameof(AreaLeft), this.AreaLeft);
        jsonWriter.WriteNumber(nameof(AreaTop), this.AreaTop);
        jsonWriter.WriteNumber(nameof(AreaRight), this.AreaRight);
        jsonWriter.WriteNumber(nameof(AreaBottom), this.AreaBottom);
        jsonWriter.WriteNumber(nameof(PitchX), this.PitchX);
        jsonWriter.WriteNumber(nameof(PitchY), this.PitchY);
        jsonWriter.WriteNumber(nameof(PointCountX), this.PointCountX);
        jsonWriter.WriteNumber(nameof(PointCountY), this.PointCountY);
        jsonWriter.WriteNumber(nameof(OriginX), this.OriginX);
        jsonWriter.WriteNumber(nameof(OriginY), this.OriginY);
        jsonWriter.WriteNumber(nameof(SpacingX), this.SpacingX);
        jsonWriter.WriteNumber(nameof(SpacingY), this.SpacingY);
        jsonWriter.WriteString(GainsPropertyName, this.EncodeGains());
        jsonWriter.WriteEndObject();
    }
}
