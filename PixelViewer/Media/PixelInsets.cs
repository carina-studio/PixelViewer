using Avalonia;
using System.Text.Json;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Number of pixels taken in from each edge of an image.
/// </summary>
/// <remarks>A negative inset extends the image beyond its edge instead of taking it in.</remarks>
readonly record struct PixelInsets
{
    /// <summary>
    /// Insets which take in nothing.
    /// </summary>
    public static readonly PixelInsets Empty = new(0, 0, 0, 0);


    /// <summary>
    /// Initialize new <see cref="PixelInsets"/> structure.
    /// </summary>
    /// <param name="left">Number of pixels taken in from the left edge.</param>
    /// <param name="top">Number of pixels taken in from the top edge.</param>
    /// <param name="right">Number of pixels taken in from the right edge.</param>
    /// <param name="bottom">Number of pixels taken in from the bottom edge.</param>
    public PixelInsets(int left, int top, int right, int bottom)
    {
        this.Bottom = bottom;
        this.Left = left;
        this.Right = right;
        this.Top = top;
    }


    /// <summary>
    /// Get number of pixels taken in from the bottom edge.
    /// </summary>
    public int Bottom { get; }


    /// <summary>
    /// Get the region of an image of given size which is left after taking in the insets.
    /// </summary>
    /// <param name="width">Width of image.</param>
    /// <param name="height">Height of image.</param>
    /// <returns>Region left.</returns>
    /// <remarks>The region is not checked, so it is beyond the image when an inset is negative, and its width or height is not positive when the insets leave nothing.</remarks>
    public PixelRect GetRegion(int width, int height) =>
        new(this.Left, this.Top, width - this.Left - this.Right, height - this.Top - this.Bottom);


    /// <summary>
    /// Check whether the insets take in nothing or not.
    /// </summary>
    public bool IsEmpty => this.Left == 0 && this.Top == 0 && this.Right == 0 && this.Bottom == 0;


    /// <summary>
    /// Get number of pixels taken in from the left edge.
    /// </summary>
    public int Left { get; }


    /// <summary>
    /// Get number of pixels taken in from the right edge.
    /// </summary>
    public int Right { get; }


    /// <summary>
    /// Get number of pixels taken in from the top edge.
    /// </summary>
    public int Top { get; }


    /// <summary>
    /// Try loading <see cref="PixelInsets"/> from the JSON value written by <see cref="WriteToJson(Utf8JsonWriter)"/>.
    /// </summary>
    /// <param name="jsonValue">JSON value.</param>
    /// <param name="insets">Loaded insets.</param>
    /// <returns>True if the insets are loaded.</returns>
    /// <remarks>An inset which is not defined is taken as 0.</remarks>
    public static bool TryLoadFromJson(JsonElement jsonValue, out PixelInsets insets)
    {
        // check value
        insets = Empty;
        if (jsonValue.ValueKind != JsonValueKind.Object)
            return false;

        // read insets
        var left = ReadInset(jsonValue, nameof(Left));
        var top = ReadInset(jsonValue, nameof(Top));
        var right = ReadInset(jsonValue, nameof(Right));
        var bottom = ReadInset(jsonValue, nameof(Bottom));
        if (left is null || top is null || right is null || bottom is null)
            return false;
        insets = new PixelInsets(left.Value, top.Value, right.Value, bottom.Value);
        return true;

        // read inset of an edge, or return null if it is not an integer
        static int? ReadInset(JsonElement jsonValue, string name)
        {
            if (!jsonValue.TryGetProperty(name, out var jsonProperty))
                return 0;
            return jsonProperty.ValueKind == JsonValueKind.Number && jsonProperty.TryGetInt32(out var inset) ? inset : null;
        }
    }


    /// <summary>
    /// Write the insets as a JSON value which can be loaded by <see cref="TryLoadFromJson(JsonElement, out PixelInsets)"/>.
    /// </summary>
    /// <param name="jsonWriter"><see cref="Utf8JsonWriter"/> to write the insets. The name of property should be written by the caller.</param>
    public void WriteToJson(Utf8JsonWriter jsonWriter)
    {
        jsonWriter.WriteStartObject();
        jsonWriter.WriteNumber(nameof(Left), this.Left);
        jsonWriter.WriteNumber(nameof(Top), this.Top);
        jsonWriter.WriteNumber(nameof(Right), this.Right);
        jsonWriter.WriteNumber(nameof(Bottom), this.Bottom);
        jsonWriter.WriteEndObject();
    }
}
