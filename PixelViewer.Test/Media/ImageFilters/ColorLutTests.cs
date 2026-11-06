using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageFilters;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.Media.ImageFilters;

/// <summary>
/// Tests of <see cref="ColorLut"/>.
/// </summary>
[TestFixture]
class ColorLutTests : BaseTests
{
	// Constants.
	const double Tolerance = 5e-4;


	// Static fields.
	static readonly int[] LutSizes = [ 256, 65536 ];


	/// <summary>
	/// Test for rejecting invalid parameters of brightness transformation.
	/// </summary>
	[Test]
	public void BrightnessArgumentCheckingTest()
	{
		// invalid EV
		var lut = new double[256];
		ColorLut.ResetToIdentity(lut);
		Assert.That(() => ColorLut.BrightnessTransformAsync(lut, double.NaN, null, null, CancellationToken.None), Throws.InstanceOf<ArgumentException>(), "EV which is not finite should be rejected.");

		// cancelled
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();
		Assert.That(() => ColorLut.BrightnessTransformAsync(lut, 1, null, null, cancellationTokenSource.Token), Throws.InstanceOf<OperationCanceledException>(), "Cancelled transformation should throw.");
	}


	/// <summary>
	/// Test for keeping LUT unchanged when brightness is zero.
	/// </summary>
	[Test]
	public async Task BrightnessIdentityTest()
	{
		foreach (var size in LutSizes)
		{
			foreach (var ev in new[] { 0.0, 0.005, -0.005 })
			{
				var lut = await TransformBrightnessAsync(size, ev, ColorSpace.Srgb);
				for (var i = size - 1; i >= 0; --i)
					Assert.That(lut[i], Is.EqualTo(i), $"Entry {i} of {size} entries should be kept with {ev} EV.");
			}
		}
	}


	/// <summary>
	/// Test for keeping the slope of brightness curve continuous at the knee of shoulder.
	/// </summary>
	[Test]
	public async Task BrightnessKneeContinuityTest()
	{
		// transform
		const double ev = 1;
		const int size = 65536;
		var gain = Math.Pow(2, ev);
		var lut = await TransformBrightnessAsync(size, ev, null);

		// compare slopes on both sides of the knee
		var kneeIndex = (int)(ColorLut.BrightnessShoulderKnee / gain * (size - 1));
		var slopeBelow = lut[kneeIndex - 1] - lut[kneeIndex - 2];
		var slopeAbove = lut[kneeIndex + 2] - lut[kneeIndex + 1];
		Assert.That(slopeAbove, Is.EqualTo(slopeBelow).Within(0.1).Percent, "Slope should be continuous at the knee.");
	}


	/// <summary>
	/// Test for scaling linear colors by negative EV.
	/// </summary>
	[Test]
	public async Task BrightnessNegativeEvTest()
	{
		foreach (var colorSpace in new[] { null, ColorSpace.Srgb })
		{
			foreach (var size in LutSizes)
			{
				foreach (var ev in new[] { -0.5, -1, -2 })
				{
					var lut = await TransformBrightnessAsync(size, ev, colorSpace);
					var gain = Math.Pow(2, ev);
					var maxColor = size - 1.0;
					for (var i = size - 1; i >= 0; --i)
					{
						var expected = ToLinear(colorSpace, i / maxColor) * gain;
						Assert.That(ToLinear(colorSpace, lut[i] / maxColor), Is.EqualTo(expected).Within(Tolerance), $"Linear color of entry {i} of {size} entries should be scaled by {ev} EV ({colorSpace?.Name ?? "linear"}).");
					}
				}
			}
		}
	}


	/// <summary>
	/// Test for scaling linear colors by positive EV and compressing them above the knee.
	/// </summary>
	[Test]
	public async Task BrightnessPositiveEvTest()
	{
		foreach (var colorSpace in new[] { null, ColorSpace.Srgb })
		{
			foreach (var size in LutSizes)
			{
				foreach (var ev in new[] { 0.5, 1, 2 })
				{
					// transform
					var lut = await TransformBrightnessAsync(size, ev, colorSpace);
					var gain = Math.Pow(2, ev);
					var maxColor = size - 1.0;

					// check each entry
					var prevColor = -1.0;
					for (var i = 0; i < size; ++i)
					{
						var scaledColor = ToLinear(colorSpace, i / maxColor) * gain;
						var color = ToLinear(colorSpace, lut[i] / maxColor);
						var message = $"entry {i} of {size} entries with {ev} EV ({colorSpace?.Name ?? "linear"})";
						if (scaledColor <= ColorLut.BrightnessShoulderKnee)
							Assert.That(color, Is.EqualTo(scaledColor).Within(Tolerance), $"Linear color of {message} should be scaled.");
						else
						{
							Assert.That(color, Is.GreaterThanOrEqualTo(ColorLut.BrightnessShoulderKnee - Tolerance), $"Linear color of {message} should be above the knee.");
							Assert.That(color, Is.LessThan(scaledColor), $"Linear color of {message} should be compressed.");
							Assert.That(color, Is.LessThanOrEqualTo(1 + Tolerance), $"Linear color of {message} should not exceed white.");
						}
						if (colorSpace is null)
							Assert.That(lut[i], Is.GreaterThanOrEqualTo(prevColor), $"Curve should be monotonic at {message}.");
						prevColor = lut[i];
					}
				}
			}
		}
	}


	/// <summary>
	/// Test for treating colors as linear when only one transfer function is given.
	/// </summary>
	[Test]
	public async Task BrightnessWithSingleTransferFunctionTest()
	{
		// transform
		const int size = 256;
		var lut1 = new double[size];
		var lut2 = new double[size];
		var lut3 = new double[size];
		ColorLut.ResetToIdentity(lut1);
		ColorLut.ResetToIdentity(lut2);
		ColorLut.ResetToIdentity(lut3);
		await ColorLut.BrightnessTransformAsync(lut1, -1, null, null, CancellationToken.None);
		await ColorLut.BrightnessTransformAsync(lut2, -1, ColorSpace.Srgb.NumericalTransferToLinear, null, CancellationToken.None);
		await ColorLut.BrightnessTransformAsync(lut3, -1, null, ColorSpace.Srgb.NumericalTransferFromLinear, CancellationToken.None);

		// compare
		Assert.That(lut2, Is.EqualTo(lut1), "Colors should be linear without the transfer function from linear.");
		Assert.That(lut3, Is.EqualTo(lut1), "Colors should be linear without the transfer function to linear.");
	}


	/// <summary>
	/// Test for rejecting invalid parameters of highlight transformation.
	/// </summary>
	[Test]
	public void HighlightArgumentCheckingTest()
	{
		// invalid intensity
		var lut = new double[256];
		ColorLut.ResetToIdentity(lut);
		Assert.That(() => ColorLut.HighlightTransformAsync(lut, double.NaN, null, null, CancellationToken.None), Throws.InstanceOf<ArgumentException>(), "Intensity which is not finite should be rejected.");

		// cancelled
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();
		Assert.That(() => ColorLut.HighlightTransformAsync(lut, 1, null, null, cancellationTokenSource.Token), Throws.InstanceOf<OperationCanceledException>(), "Cancelled transformation should throw.");
	}


	/// <summary>
	/// Test for scaling distance from linear colors of highlights to white and keeping colors below the pivot.
	/// </summary>
	[Test]
	public async Task HighlightCurveTest()
	{
		var pivotDistance = 1 - ColorLut.HighlightPivot;
		foreach (var colorSpace in new[] { null, ColorSpace.Srgb })
		{
			foreach (var size in LutSizes)
			{
				foreach (var intensity in new[] { -1, -0.5, 0.5, 1 })
				{
					// transform
					var lut = await TransformHighlightAsync(size, intensity, colorSpace);
					var maxColor = size - 1.0;

					// check each entry
					var prevColor = -1.0;
					for (var i = 0; i < size; ++i)
					{
						var originalDistance = 1 - ToLinear(colorSpace, i / maxColor);
						var distance = 1 - ToLinear(colorSpace, lut[i] / maxColor);
						var message = $"entry {i} of {size} entries with intensity {intensity} ({colorSpace?.Name ?? "linear"})";
						if (originalDistance <= 0 || originalDistance >= pivotDistance)
							Assert.That(lut[i], Is.EqualTo(i), $"Color of {message} should be kept.");
						else
						{
							var stops = Math.Log2(pivotDistance / originalDistance);
							var ev = ColorLut.HighlightMaxEv * (1 - Math.Exp(-ColorLut.HighlightStrength * (stops - ColorLut.HighlightRampStops / 2) / ColorLut.HighlightMaxEv));
							var fullGain = Math.Pow(2, -intensity * ev);
							if (stops >= ColorLut.HighlightRampStops)
								Assert.That(distance, Is.EqualTo(originalDistance * fullGain).Within(Tolerance), $"Distance to white of {message} should be fully scaled.");
							else if (intensity > 0)
								Assert.That(distance, Is.InRange(originalDistance * Math.Pow(2, -intensity * ColorLut.HighlightStrength * stops) - Tolerance, originalDistance + Tolerance), $"Linear color of {message} should be partially brightened.");
							else
								Assert.That(distance, Is.InRange(originalDistance - Tolerance, originalDistance * Math.Pow(2, -intensity * ColorLut.HighlightStrength * stops) + Tolerance), $"Linear color of {message} should be partially darkened.");
						}
						if (colorSpace is null)
							Assert.That(lut[i], Is.GreaterThanOrEqualTo(prevColor), $"Curve should be monotonic at {message}.");
						prevColor = lut[i];
					}
				}
			}
		}
	}


	/// <summary>
	/// Test for keeping the slope of highlight curve continuous at the pivot and at the end of the ramp.
	/// </summary>
	[Test]
	public async Task HighlightFadeContinuityTest()
	{
		// transform
		const int size = 65536;
		var lut = await TransformHighlightAsync(size, -1, null);

		// compare slopes on both sides of the pivot and of the end of the ramp
		foreach (var end in new[] { ColorLut.HighlightPivot, 1 - (1 - ColorLut.HighlightPivot) / Math.Pow(2, ColorLut.HighlightRampStops) })
		{
			var index = (int)(end * (size - 1));
			var slopeBelow = lut[index - 1] - lut[index - 2];
			var slopeAbove = lut[index + 2] - lut[index + 1];
			Assert.That(slopeAbove, Is.EqualTo(slopeBelow).Within(1).Percent, $"Slope should be continuous at linear color {end}.");
		}
	}


	/// <summary>
	/// Test for keeping LUT unchanged when highlight intensity is zero.
	/// </summary>
	[Test]
	public async Task HighlightIdentityTest()
	{
		foreach (var size in LutSizes)
		{
			foreach (var intensity in new[] { 0.0, 0.0005, -0.0005 })
			{
				var lut = await TransformHighlightAsync(size, intensity, ColorSpace.Srgb);
				for (var i = size - 1; i >= 0; --i)
					Assert.That(lut[i], Is.EqualTo(i), $"Entry {i} of {size} entries should be kept with intensity {intensity}.");
			}
		}
	}


	/// <summary>
	/// Test for bounding the slope of highlight curve in linear light by the maximum EV.
	/// </summary>
	[Test]
	public async Task HighlightSlopeLimitTest()
	{
		var maxSlope = Math.Pow(2, ColorLut.HighlightMaxEv);
		foreach (var size in LutSizes)
		{
			var lut = await TransformHighlightAsync(size, -1, null);
			for (var i = size - 2; i >= 0; --i)
				Assert.That(lut[i + 1] - lut[i], Is.LessThanOrEqualTo(maxSlope * (1 + 1e-9)), $"Step between entry {i} and {i + 1} of {size} entries should be bounded.");
		}
	}


	/// <summary>
	/// Test for rejecting invalid parameters of shadow transformation.
	/// </summary>
	[Test]
	public void ShadowArgumentCheckingTest()
	{
		// invalid intensity
		var lut = new double[256];
		ColorLut.ResetToIdentity(lut);
		Assert.That(() => ColorLut.ShadowTransformAsync(lut, double.NaN, null, null, CancellationToken.None), Throws.InstanceOf<ArgumentException>(), "Intensity which is not finite should be rejected.");

		// cancelled
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();
		Assert.That(() => ColorLut.ShadowTransformAsync(lut, 1, null, null, cancellationTokenSource.Token), Throws.InstanceOf<OperationCanceledException>(), "Cancelled transformation should throw.");
	}


	/// <summary>
	/// Test for scaling linear colors of shadows and keeping colors above the pivot.
	/// </summary>
	[Test]
	public async Task ShadowCurveTest()
	{
		var fadeStart = ColorLut.ShadowPivot / Math.Pow(2, ColorLut.ShadowRangeStops);
		foreach (var colorSpace in new[] { null, ColorSpace.Srgb })
		{
			foreach (var size in LutSizes)
			{
				foreach (var intensity in new[] { -1, -0.5, 0.5, 1 })
				{
					// transform
					var lut = await TransformShadowAsync(size, intensity, colorSpace);
					var gain = Math.Pow(2, intensity * ColorLut.ShadowMaxEv);
					var maxColor = size - 1.0;

					// check each entry
					var prevColor = -1.0;
					for (var i = 0; i < size; ++i)
					{
						var originalColor = ToLinear(colorSpace, i / maxColor);
						var color = ToLinear(colorSpace, lut[i] / maxColor);
						var message = $"entry {i} of {size} entries with intensity {intensity} ({colorSpace?.Name ?? "linear"})";
						if (originalColor <= 0 || originalColor >= ColorLut.ShadowPivot)
							Assert.That(lut[i], Is.EqualTo(i), $"Color of {message} should be kept.");
						else if (originalColor <= fadeStart)
							Assert.That(color, Is.EqualTo(originalColor * gain).Within(Tolerance), $"Linear color of {message} should be fully scaled.");
						else if (intensity > 0)
							Assert.That(color, Is.InRange(originalColor - Tolerance, Math.Min(1, originalColor * gain) + Tolerance), $"Linear color of {message} should be partially lifted.");
						else
							Assert.That(color, Is.InRange(originalColor * gain - Tolerance, originalColor + Tolerance), $"Linear color of {message} should be partially darkened.");
						if (colorSpace is null)
							Assert.That(lut[i], Is.GreaterThanOrEqualTo(prevColor), $"Curve should be monotonic at {message}.");
						prevColor = lut[i];
					}
				}
			}
		}
	}


	/// <summary>
	/// Test for keeping the slope of shadow curve continuous at both ends of the fade.
	/// </summary>
	[Test]
	public async Task ShadowFadeContinuityTest()
	{
		// transform
		const int size = 65536;
		var lut = await TransformShadowAsync(size, 1, null);

		// compare slopes on both sides of each end of the fade
		foreach (var end in new[] { ColorLut.ShadowPivot / Math.Pow(2, ColorLut.ShadowRangeStops), ColorLut.ShadowPivot })
		{
			var index = (int)(end * (size - 1));
			var slopeBelow = lut[index - 1] - lut[index - 2];
			var slopeAbove = lut[index + 2] - lut[index + 1];
			Assert.That(slopeAbove, Is.EqualTo(slopeBelow).Within(1).Percent, $"Slope should be continuous at linear color {end}.");
		}
	}


	/// <summary>
	/// Test for keeping LUT unchanged when shadow intensity is zero.
	/// </summary>
	[Test]
	public async Task ShadowIdentityTest()
	{
		foreach (var size in LutSizes)
		{
			foreach (var intensity in new[] { 0.0, 0.0005, -0.0005 })
			{
				var lut = await TransformShadowAsync(size, intensity, ColorSpace.Srgb);
				for (var i = size - 1; i >= 0; --i)
					Assert.That(lut[i], Is.EqualTo(i), $"Entry {i} of {size} entries should be kept with intensity {intensity}.");
			}
		}
	}


	// Transfer normalized color to linear.
	static double ToLinear(ColorSpace? colorSpace, double color) =>
		colorSpace?.NumericalTransferToLinear(color) ?? color;


	// Transform brightness of identity LUT.
	static async Task<double[]> TransformBrightnessAsync(int size, double ev, ColorSpace? colorSpace)
	{
		var lut = new double[size];
		ColorLut.ResetToIdentity(lut);
		await ColorLut.BrightnessTransformAsync(lut, ev, colorSpace is not null ? colorSpace.NumericalTransferToLinear : null, colorSpace is not null ? colorSpace.NumericalTransferFromLinear : null, CancellationToken.None);
		return lut;
	}


	// Transform highlight of identity LUT.
	static async Task<double[]> TransformHighlightAsync(int size, double intensity, ColorSpace? colorSpace)
	{
		var lut = new double[size];
		ColorLut.ResetToIdentity(lut);
		await ColorLut.HighlightTransformAsync(lut, intensity, colorSpace is not null ? colorSpace.NumericalTransferToLinear : null, colorSpace is not null ? colorSpace.NumericalTransferFromLinear : null, CancellationToken.None);
		return lut;
	}


	// Transform shadow of identity LUT.
	static async Task<double[]> TransformShadowAsync(int size, double intensity, ColorSpace? colorSpace)
	{
		var lut = new double[size];
		ColorLut.ResetToIdentity(lut);
		await ColorLut.ShadowTransformAsync(lut, intensity, colorSpace is not null ? colorSpace.NumericalTransferToLinear : null, colorSpace is not null ? colorSpace.NumericalTransferFromLinear : null, CancellationToken.None);
		return lut;
	}
}
