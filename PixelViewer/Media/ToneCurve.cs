using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Tone curve which maps a linear color to a linear color, defined by points which are interpolated by monotone cubic interpolation.
/// </summary>
/// <remarks>
/// The inputs and the outputs are normalized to [0, 1]. The first point is (0, 0) and the last one is (1, 1), the inputs are strictly increasing and the outputs are non-decreasing,
/// so the curve is monotone between the points as well. The instance cannot be modified after creation.
/// </remarks>
class ToneCurve : IEquatable<ToneCurve>
{
    /// <summary>
    /// Maximum number of points.
    /// </summary>
    public const int MaxPointCount = 4096;


    // Fields.
    readonly double[] inputs;
    readonly double[] outputs;
    readonly double[] tangents;


    /// <summary>
    /// Initialize new <see cref="ToneCurve"/> instance.
    /// </summary>
    /// <param name="inputs">Inputs of points, which start from 0, end at 1 and are strictly increasing.</param>
    /// <param name="outputs">Outputs of points, which start from 0, end at 1 and are non-decreasing.</param>
    public ToneCurve(ReadOnlySpan<double> inputs, ReadOnlySpan<double> outputs)
    {
        // check number of points
        if (inputs.Length != outputs.Length)
            throw new ArgumentException("Numbers of inputs and outputs are different.", nameof(outputs));
        if (inputs.Length < 2 || inputs.Length > MaxPointCount)
            throw new ArgumentOutOfRangeException(nameof(inputs));

        // check end points
        if (inputs[0] != 0 || outputs[0] != 0)
            throw new ArgumentException("First point should be (0, 0).", nameof(inputs));
        if (inputs[^1] != 1 || outputs[^1] != 1)
            throw new ArgumentException("Last point should be (1, 1).", nameof(inputs));

        // check order of points
        for (var i = inputs.Length - 1; i > 0; --i)
        {
            if (!(inputs[i] > inputs[i - 1]))
                throw new ArgumentException("Inputs should be strictly increasing.", nameof(inputs));
            if (!(outputs[i] >= outputs[i - 1]))
                throw new ArgumentException("Outputs should be non-decreasing.", nameof(outputs));
        }

        // keep the points
        this.inputs = inputs.ToArray();
        this.outputs = outputs.ToArray();
        this.tangents = SelectTangents(this.inputs, this.outputs);
    }


    /// <summary>
    /// Get number of points.
    /// </summary>
    public int Count => this.inputs.Length;


    /// <inheritdoc/>
    public bool Equals(ToneCurve? curve)
    {
        if (curve is null)
            return false;
        if (ReferenceEquals(this, curve))
            return true;
        return this.inputs.AsSpan().SequenceEqual(curve.inputs)
            && this.outputs.AsSpan().SequenceEqual(curve.outputs);
    }


    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ToneCurve curve && this.Equals(curve);


    /// <summary>
    /// Evaluate the curve.
    /// </summary>
    /// <param name="input">Normalized input, which is clamped to [0, 1].</param>
    /// <returns>Normalized output.</returns>
    public double Evaluate(double input)
    {
        // clamp input
        if (!(input > 0))
            return 0;
        if (input >= 1)
            return 1;

        // find the segment which contains the input
        var index = Array.BinarySearch(this.inputs, input);
        if (index >= 0)
            return this.outputs[index];
        index = ~index - 1;

        // interpolate by cubic Hermite spline
        var x0 = this.inputs[index];
        var width = this.inputs[index + 1] - x0;
        var t = (input - x0) / width;
        var t2 = t * t;
        var t3 = t2 * t;
        var output = (2 * t3 - 3 * t2 + 1) * this.outputs[index]
            + (t3 - 2 * t2 + t) * width * this.tangents[index]
            + (-2 * t3 + 3 * t2) * this.outputs[index + 1]
            + (t3 - t2) * width * this.tangents[index + 1];
        return Math.Clamp(output, this.outputs[index], this.outputs[index + 1]);
    }


    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.AddBytes(MemoryMarshal.AsBytes(this.inputs.AsSpan()));
        hashCode.AddBytes(MemoryMarshal.AsBytes(this.outputs.AsSpan()));
        return hashCode.ToHashCode();
    }


    /// <summary>
    /// Get inputs of points.
    /// </summary>
    public ReadOnlySpan<double> Inputs => this.inputs;


    /// <summary>
    /// Get outputs of points.
    /// </summary>
    public ReadOnlySpan<double> Outputs => this.outputs;


    // Select the tangent at each point by the method of Fritsch and Carlson, which keeps the interpolated curve monotone.
    static double[] SelectTangents(double[] inputs, double[] outputs)
    {
        // calculate slopes of segments
        var count = inputs.Length;
        var slopes = new double[count - 1];
        for (var i = count - 2; i >= 0; --i)
            slopes[i] = (outputs[i + 1] - outputs[i]) / (inputs[i + 1] - inputs[i]);

        // select initial tangents, the tangent at a local extremum or beside a flat segment is 0
        var tangents = new double[count];
        tangents[0] = slopes[0];
        tangents[count - 1] = slopes[count - 2];
        for (var i = count - 2; i > 0; --i)
            tangents[i] = (slopes[i - 1] * slopes[i] > 0) ? (slopes[i - 1] + slopes[i]) / 2 : 0;

        // limit tangents so that each segment stays monotone
        for (var i = 0; i < count - 1; ++i)
        {
            if (slopes[i] == 0)
            {
                tangents[i] = 0;
                tangents[i + 1] = 0;
                continue;
            }
            var a = tangents[i] / slopes[i];
            var b = tangents[i + 1] / slopes[i];
            var squaredLength = a * a + b * b;
            if (squaredLength > 9)
            {
                var scale = 3 / Math.Sqrt(squaredLength);
                tangents[i] = scale * a * slopes[i];
                tangents[i + 1] = scale * b * slopes[i];
            }
        }
        return tangents;
    }


    /// <summary>
    /// Try creating <see cref="ToneCurve"/> from the JSON value written by <see cref="WriteToJson(Utf8JsonWriter)"/>.
    /// </summary>
    /// <param name="jsonValue">JSON value to load the curve from.</param>
    /// <param name="curve">Created <see cref="ToneCurve"/>.</param>
    /// <returns>True if the curve was loaded successfully.</returns>
    public static bool TryLoadFromJson(JsonElement jsonValue, [NotNullWhen(true)] out ToneCurve? curve)
    {
        // get points
        curve = null;
        if (jsonValue.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryGetValues(nameof(Inputs), out var inputs) || !TryGetValues(nameof(Outputs), out var outputs))
            return false;

        // create the curve, the points are checked by the constructor
        try
        {
            curve = new ToneCurve(inputs.ToArray(), outputs.ToArray());
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }

        // get values of the array property with given name
        bool TryGetValues(string name, out List<double> values)
        {
            values = [];
            if (!jsonValue.TryGetProperty(name, out var jsonProperty) || jsonProperty.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var jsonItem in jsonProperty.EnumerateArray())
            {
                if (jsonItem.ValueKind != JsonValueKind.Number || !jsonItem.TryGetDouble(out var value))
                    return false;
                values.Add(value);
            }
            return true;
        }
    }


    /// <summary>
    /// Write the curve as a JSON value which can be loaded by <see cref="TryLoadFromJson(JsonElement, out ToneCurve?)"/>.
    /// </summary>
    /// <param name="jsonWriter"><see cref="Utf8JsonWriter"/> to write the curve. The name of property should be written by the caller.</param>
    public void WriteToJson(Utf8JsonWriter jsonWriter)
    {
        // write inputs
        jsonWriter.WriteStartObject();
        jsonWriter.WritePropertyName(nameof(Inputs));
        jsonWriter.WriteStartArray();
        foreach (var input in this.inputs)
            jsonWriter.WriteNumberValue(input);
        jsonWriter.WriteEndArray();

        // write outputs
        jsonWriter.WritePropertyName(nameof(Outputs));
        jsonWriter.WriteStartArray();
        foreach (var output in this.outputs)
            jsonWriter.WriteNumberValue(output);
        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
    }
}
