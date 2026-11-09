using Carina.PixelViewer.Media;
using NUnit.Framework;
using System;
using System.IO;
using System.Text.Json;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="ToneCurve"/>.
/// </summary>
[TestFixture]
class ToneCurveTests : BaseTests
{
	/// <summary>
	/// Test for comparing curves by their points.
	/// </summary>
	[Test]
	public void EqualityTest()
	{
		var curve = new ToneCurve([ 0, 0.5, 1 ], [ 0, 0.75, 1 ]);
		Assert.That(new ToneCurve([ 0, 0.5, 1 ], [ 0, 0.75, 1 ]), Is.EqualTo(curve), "Curves of the same points should be equal.");
		Assert.That(new ToneCurve([ 0, 0.5, 1 ], [ 0, 0.75, 1 ]).GetHashCode(), Is.EqualTo(curve.GetHashCode()), "Curves of the same points should have the same hash code.");
		Assert.That(new ToneCurve([ 0, 0.5, 1 ], [ 0, 0.7, 1 ]), Is.Not.EqualTo(curve), "Curves of different outputs should not be equal.");
		Assert.That(new ToneCurve([ 0, 0.4, 1 ], [ 0, 0.75, 1 ]), Is.Not.EqualTo(curve), "Curves of different inputs should not be equal.");
	}


	/// <summary>
	/// Test for evaluating the curve through its points, monotonically between them and clamped outside of [0, 1].
	/// </summary>
	[Test]
	public void EvaluationTest()
	{
		// check points
		double[] inputs = [ 0, 0.01, 0.1, 0.3, 0.6, 1 ];
		double[] outputs = [ 0, 0.005, 0.2, 0.6, 0.6, 1 ];
		var curve = new ToneCurve(inputs, outputs);
		for (var i = inputs.Length - 1; i >= 0; --i)
			Assert.That(curve.Evaluate(inputs[i]), Is.EqualTo(outputs[i]).Within(1e-12), $"Curve should pass through point {i}.");

		// check monotonicity and the flat segment
		var previousOutput = 0.0;
		for (var i = 0; i <= 10000; ++i)
		{
			var input = i / 10000.0;
			var output = curve.Evaluate(input);
			Assert.That(output, Is.GreaterThanOrEqualTo(previousOutput), $"Curve should not decrease at {input}.");
			if (input >= 0.3 && input <= 0.6)
				Assert.That(output, Is.EqualTo(0.6).Within(1e-12), $"Curve should stay flat at {input} between points of the same output.");
			previousOutput = output;
		}

		// check clamping
		Assert.That(curve.Evaluate(-1), Is.Zero, "Input below 0 should be clamped.");
		Assert.That(curve.Evaluate(2), Is.EqualTo(1), "Input above 1 should be clamped.");
		Assert.That(curve.Evaluate(double.NaN), Is.Zero, "NaN should be clamped to 0.");
	}


	/// <summary>
	/// Test for refusing invalid points.
	/// </summary>
	[Test]
	public void InvalidPointsTest()
	{
		Assert.Throws<ArgumentException>(() => _ = new ToneCurve([ 0, 1 ], [ 0, 0.5, 1 ]), "Different numbers of inputs and outputs should be refused.");
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ToneCurve([ 0 ], [ 0 ]), "Single point should be refused.");
		Assert.Throws<ArgumentException>(() => _ = new ToneCurve([ 0.1, 1 ], [ 0, 1 ]), "Curve not starting from (0, 0) should be refused.");
		Assert.Throws<ArgumentException>(() => _ = new ToneCurve([ 0, 1 ], [ 0, 0.9 ]), "Curve not ending at (1, 1) should be refused.");
		Assert.Throws<ArgumentException>(() => _ = new ToneCurve([ 0, 0.5, 0.5, 1 ], [ 0, 0.4, 0.6, 1 ]), "Inputs which do not increase should be refused.");
		Assert.Throws<ArgumentException>(() => _ = new ToneCurve([ 0, 0.4, 0.6, 1 ], [ 0, 0.6, 0.4, 1 ]), "Outputs which decrease should be refused.");
		Assert.Throws<ArgumentException>(() => _ = new ToneCurve([ 0, double.NaN, 1 ], [ 0, 0.5, 1 ]), "NaN input should be refused.");
	}


	/// <summary>
	/// Test for saving and loading the curve as JSON.
	/// </summary>
	[Test]
	public void JsonTest()
	{
		// write the curve
		var curve = new ToneCurve([ 0, 0.0123, 0.25, 1 ], [ 0, 0.004, 0.5, 1 ]);
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
			curve.WriteToJson(writer);

		// load the curve
		using (var document = JsonDocument.Parse(stream.ToArray()))
		{
			Assert.That(ToneCurve.TryLoadFromJson(document.RootElement, out var loadedCurve), Is.True, "Curve should be loaded.");
			Assert.That(loadedCurve, Is.EqualTo(curve), "Loaded curve should be the same as the saved one.");
		}

		// load invalid curves
		foreach (var json in new[] { "[]", "{}", "{ \"Inputs\": [ 0, 1 ] }", "{ \"Inputs\": [ 0, \"a\" ], \"Outputs\": [ 0, 1 ] }", "{ \"Inputs\": [ 0, 1 ], \"Outputs\": [ 0, 2 ] }" })
		{
			using var document = JsonDocument.Parse(json);
			Assert.That(ToneCurve.TryLoadFromJson(document.RootElement, out _), Is.False, $"Invalid curve should not be loaded: {json}");
		}
	}
}
