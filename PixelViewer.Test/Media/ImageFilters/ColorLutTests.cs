using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageFilters;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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
	/// Test for keeping a slight color cast by the safe bands of auto color adjustment.
	/// </summary>
	[Test]
	public void AutoWhiteBalanceBandsTest()
	{
		// inside the bands
		Assert.That(ColorLut.ApplyAutoWhiteBalanceBands(-0.3, 0.1), Is.EqualTo((0.0, 0.0)), "Slight warmth and tint should be kept.");
		Assert.That(ColorLut.ApplyAutoWhiteBalanceBands(0.1, -0.1), Is.EqualTo((0.0, 0.0)), "Slight coolness and tint should be kept.");

		// outside the bands
		var (warmth, tint) = ColorLut.ApplyAutoWhiteBalanceBands(-0.8, 0.4);
		Assert.That(warmth, Is.EqualTo(-0.8 + ColorLut.AutoWarmthCoolingBand).Within(1e-9), "Cooling should be reduced by the width of its band.");
		Assert.That(tint, Is.EqualTo(0.4 - ColorLut.AutoTintBand).Within(1e-9), "Tint should be reduced by the width of its band.");
		(warmth, tint) = ColorLut.ApplyAutoWhiteBalanceBands(0.8, -0.4);
		Assert.That(warmth, Is.EqualTo(0.8 - ColorLut.AutoWarmthWarmingBand).Within(1e-9), "Warming should be reduced by the width of its band.");
		Assert.That(tint, Is.EqualTo(-0.4 + ColorLut.AutoTintBand).Within(1e-9), "Tint should be reduced by the width of its band in both directions.");

		// warm image is kept more than cool one
		Assert.That(ColorLut.AutoWarmthCoolingBand, Is.GreaterThan(ColorLut.AutoWarmthWarmingBand), "Warm image should be kept more than cool one.");
		Assert.That(Math.Abs(ColorLut.ApplyAutoWhiteBalanceBands(-0.5, 0).Item1), Is.LessThan(ColorLut.ApplyAutoWhiteBalanceBands(0.5, 0).Item1), "Warm image should be cooled less than cool image is warmed.");

		// invalid values
		Assert.That(() => ColorLut.ApplyAutoWhiteBalanceBands(double.NaN, 0), Throws.InstanceOf<ArgumentException>(), "Warmth which is not finite should be rejected.");
	}


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
	/// Test for applying contrast around middle gray whatever the encoding of LUT is.
	/// </summary>
	[Test]
	public async Task ContrastPivotTest()
	{
		// transform LUT of linear colors and LUT of sRGB colors
		const double MiddleGray = 0.214;
		var colorSpace = ColorSpace.Srgb;
		Func<double, double> toLinear = colorSpace.NumericalTransferToLinear;
		Func<double, double> fromLinear = colorSpace.NumericalTransferFromLinear;
		foreach (var intensity in new[] { 0.5, -0.5 })
		{
			var linearLut = new double[65536];
			ColorLut.ResetToIdentity(linearLut);
			await ColorLut.ContrastTransformAsync(linearLut, intensity, null, null, CancellationToken.None);
			var srgbLut = new double[65536];
			ColorLut.ResetToIdentity(srgbLut);
			await ColorLut.ContrastTransformAsync(srgbLut, intensity, toLinear, fromLinear, CancellationToken.None);
			var maxColor = linearLut.Length - 1.0;
			double Linear(double color) => linearLut[(int)Math.Round(color * maxColor)] / maxColor;
			double Srgb(double color) => toLinear(srgbLut[(int)Math.Round(fromLinear(color) * maxColor)] / maxColor);
			var message = $"intensity {intensity}";

			// middle gray is kept, and colors move away from it or towards it
			Assert.That(Linear(MiddleGray), Is.EqualTo(MiddleGray).Within(0.002), $"Middle gray of linear LUT should be kept with {message}.");
			Assert.That(Srgb(MiddleGray), Is.EqualTo(MiddleGray).Within(0.002), $"Middle gray of sRGB LUT should be kept with {message}.");
			foreach (var color in new[] { 0.05, 0.1, 0.4, 0.6 })
			{
				var expectedSign = Math.Sign(intensity) * Math.Sign(color - MiddleGray);
				Assert.That(Math.Sign(Linear(color) - color), Is.EqualTo(expectedSign), $"Color {color} of linear LUT should move away from or towards middle gray with {message}.");
				Assert.That(Srgb(color), Is.EqualTo(Linear(color)).Within(0.002), $"Color {color} should be transformed in the same way whatever the encoding of LUT with {message}.");
			}

			// monotonic
			for (var i = 1; i < linearLut.Length; ++i)
				Assert.That(linearLut[i], Is.GreaterThanOrEqualTo(linearLut[i - 1]), $"LUT should be monotonic with {message}.");
		}
	}


	/// <summary>
	/// Test for rejecting invalid parameters of gain transformation.
	/// </summary>
	[Test]
	public void GainArgumentCheckingTest()
	{
		// invalid gain
		var lut = new double[256];
		ColorLut.ResetToIdentity(lut);
		Assert.That(() => ColorLut.GainTransformAsync(lut, double.NaN, null, null, CancellationToken.None), Throws.InstanceOf<ArgumentException>(), "Gain which is not finite should be rejected.");
		Assert.That(() => ColorLut.GainTransformAsync(lut, -1, null, null, CancellationToken.None), Throws.InstanceOf<ArgumentException>(), "Negative gain should be rejected.");

		// cancelled
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();
		Assert.That(() => ColorLut.GainTransformAsync(lut, 2, null, null, cancellationTokenSource.Token), Throws.InstanceOf<OperationCanceledException>(), "Cancelled transformation should throw.");
	}


	/// <summary>
	/// Test for scaling linear colors by gain and clipping them to white.
	/// </summary>
	[Test]
	public async Task GainCurveTest()
	{
		foreach (var colorSpace in new[] { null, ColorSpace.Srgb })
		{
			foreach (var size in LutSizes)
			{
				foreach (var gain in new[] { 0.5, 1.5, 2 })
				{
					var lut = await TransformGainAsync(size, gain, colorSpace);
					var maxColor = size - 1.0;
					for (var i = 0; i < size; ++i)
					{
						var expectedColor = Math.Min(ToLinear(colorSpace, i / maxColor) * gain, 1);
						var color = ToLinear(colorSpace, lut[i] / maxColor);
						Assert.That(color, Is.EqualTo(expectedColor).Within(Tolerance), $"Linear color of entry {i} of {size} entries should be scaled by {gain} ({colorSpace?.Name ?? "linear"}).");
					}
				}
			}
		}
	}


	/// <summary>
	/// Test for keeping LUT unchanged when gain is 1.
	/// </summary>
	[Test]
	public async Task GainIdentityTest()
	{
		foreach (var size in LutSizes)
		{
			var lut = await TransformGainAsync(size, 1, ColorSpace.Srgb);
			for (var i = size - 1; i >= 0; --i)
				Assert.That(lut[i], Is.EqualTo(i), $"Entry {i} of {size} entries should be kept.");
		}
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
	/// Test for transforming single color the same as the LUT does.
	/// </summary>
	[Test]
	public async Task PerValueTransformationTest()
	{
		var colorSpace = ColorSpace.Srgb;
		Func<double, double> toLinear = colorSpace.NumericalTransferToLinear;
		Func<double, double> fromLinear = colorSpace.NumericalTransferFromLinear;
		foreach (var size in LutSizes)
		{
			var maxColor = size - 1.0;
			foreach (var value in new[] { -1, -0.4, 0.3, 1 })
			{
				// transform LUTs, the transfer functions of sRGB are approximated so a color which is not transformed may not come back exactly
				var brightnessLut = await TransformBrightnessAsync(size, value * 2, colorSpace);
				var contrastLut = new double[size];
				ColorLut.ResetToIdentity(contrastLut);
				await ColorLut.ContrastTransformAsync(contrastLut, value, toLinear, fromLinear, CancellationToken.None);
				var highlightLut = await TransformHighlightAsync(size, value, colorSpace);
				var shadowLut = await TransformShadowAsync(size, value, colorSpace);
				for (var i = size - 1; i >= 0; i -= Math.Max(1, size / 512))
				{
					var color = i / maxColor;
					var message = $"value {value} at {i} of LUT with size {size}";
					Assert.That(brightnessLut[i] / maxColor, Is.EqualTo(fromLinear(ColorLut.TransformBrightness(toLinear(color), value * 2))).Within(1e-4), $"Brightness of {message}.");
					Assert.That(contrastLut[i] / maxColor, Is.EqualTo(fromLinear(ColorLut.TransformContrast(toLinear(color), value))).Within(1e-4), $"Contrast of {message}.");
					Assert.That(highlightLut[i] / maxColor, Is.EqualTo(fromLinear(ColorLut.TransformHighlight(toLinear(color), value))).Within(1e-4), $"Highlight of {message}.");
					Assert.That(shadowLut[i] / maxColor, Is.EqualTo(fromLinear(ColorLut.TransformShadow(toLinear(color), value))).Within(1e-4), $"Shadow of {message}.");
				}
			}
		}
	}


	/// <summary>
	/// Test for selecting means of colors weighted towards neutral colors.
	/// </summary>
	[Test]
	public void SelectNeutralMeansTest()
	{
		// warmth and tint which neutralize the given means
		static (double, double) SelectWarmthAndTint((double, double, double) means) =>
			ColorLut.SelectWarmthAndTint(1 / means.Item1, 1 / means.Item2, 1 / means.Item3);

		// large area of vivid blue beside neutral grey
		var colors = new List<(double, double, double)>();
		for (var i = 0; i < 700; ++i)
			colors.Add((0.05, 0.2, 0.6));
		for (var i = 0; i < 300; ++i)
			colors.Add((0.3, 0.3, 0.3));
		var (plainWarmth, _) = SelectWarmthAndTint((0.7 * 0.05 + 0.3 * 0.3, 0.7 * 0.2 + 0.3 * 0.3, 0.7 * 0.6 + 0.3 * 0.3));
		var (warmth, tint) = SelectWarmthAndTint(ColorLut.SelectNeutralMeans(colors));
		Assert.That(plainWarmth, Is.GreaterThan(0.5), "Plain means of blue scene should ask for strong warmth.");
		Assert.That(Math.Abs(warmth), Is.LessThan(0.1), "Vivid blue should not be taken as color cast.");
		Assert.That(Math.Abs(tint), Is.LessThan(0.1), "Vivid blue should not be taken as color cast of tint.");

		// color cast shifting every color is still found
		var castColors = new List<(double, double, double)>();
		foreach (var (r, g, b) in colors)
			castColors.Add((r * 0.8, g, Math.Min(b * 1.25, 0.95)));
		var (castWarmth, _) = SelectWarmthAndTint(ColorLut.SelectNeutralMeans(castColors));
		Assert.That(castWarmth, Is.GreaterThan(0.25), "Cool cast shifting every color should ask for warmth.");

		// black and clipped colors are skipped
		var skippedColors = new List<(double, double, double)>(colors);
		for (var i = 0; i < 1000; ++i)
		{
			skippedColors.Add((0.001, 0.001, 0.009));
			skippedColors.Add((1, 0.99, 0.5));
		}
		Assert.That(ColorLut.SelectNeutralMeans(skippedColors), Is.EqualTo(ColorLut.SelectNeutralMeans(colors)), "Black and clipped colors should be skipped.");
		Assert.That(ColorLut.SelectNeutralMeans([]), Is.EqualTo((0.0, 0.0, 0.0)), "Nothing should be selected without colors.");
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


	/// <summary>
	/// Test for selecting brightness, contrast, highlight and shadow by luminance of image.
	/// </summary>
	[Test]
	public void ToneAdjustmentSelectionTest()
	{
		// generate luminances which have the given ratios of the given values, and the given log-average after scaling the others
		List<double> Generate(double logAverage, double stops, params (double Ratio, double Value)[] fixedValues)
		{
			var luminances = new List<double>();
			var fixedRatio = 0.0;
			var fixedLogSum = 0.0;
			foreach (var (ratio, value) in fixedValues)
			{
				for (var i = (int)(ratio * 1000); i > 0; --i)
					luminances.Add(value);
				fixedRatio += ratio;
				fixedLogSum += ratio * Math.Log(Math.Max(value, 1e-4));
			}
			var center = Math.Exp((Math.Log(logAverage) - fixedLogSum) / (1 - fixedRatio));
			for (var i = (int)((1 - fixedRatio) * 1000) - 1; i >= 0; --i)
				luminances.Add(center * Math.Pow(2, stops * (i % 2 == 0 ? 1 : -1)));
			return luminances;
		}
		(double, double, double, double) Select(List<double> luminances) =>
			ColorLut.SelectToneAdjustments(luminances);

		// brightness, which brings median luminance of midtones into the range
		const double MidGray = 0.25;
		var (brightness, _, _, _) = Select(Generate(MidGray, 0.1));
		Assert.That(brightness, Is.EqualTo(0), "Brightness should be kept for image whose midtones are in the range.");
		(brightness, _, _, _) = Select(Generate(ColorLut.AutoBrightnessLowerLuminance / 2, 0.1));
		Assert.That(brightness, Is.EqualTo(1).Within(0.151), "Dark image should be brightened to the lower end of the range.");
		(brightness, _, _, _) = Select(Generate(ColorLut.AutoBrightnessUpperLuminance * 1.5, 0.1));
		Assert.That(brightness, Is.EqualTo(Math.Log2(1 / 1.5)).Within(0.151), "Bright image should be darkened to the upper end of the range.");
		(brightness, _, _, _) = Select(Generate(0.02, 0.1));
		Assert.That(brightness, Is.EqualTo(ColorLut.AutoBrightnessMaxEv), "Brightness should be limited.");
		var darkAreaLuminances = Generate(MidGray, 0.1);
		for (var i = 0; i < 800; ++i)
			darkAreaLuminances.Add(0.03);
		Assert.That(Select(darkAreaLuminances).Item1, Is.EqualTo(0), "Brightness should be kept for image whose median of midtones is in the range, beside a dark area smaller than half.");

		// brightness by midtones, leaving highlights and shadows to highlight and shadow
		var midtones = Generate(MidGray, 0.5);
		var hdrLuminances = new List<double>(midtones);
		for (var i = 0; i < 400; ++i)
			hdrLuminances.Add(0.95);
		for (var i = 0; i < 150; ++i)
		{
			hdrLuminances.Add(1);
			hdrLuminances.Add(0);
		}
		var (hdrBrightness, _, hdrHighlight, _) = Select(hdrLuminances);
		Assert.That(hdrBrightness, Is.EqualTo(Select(midtones).Item1), "Brightness should be selected by midtones only.");
		Assert.That(hdrBrightness, Is.EqualTo(0), "Brightness should be kept for image of midtones in the range and bright highlights.");
		Assert.That(hdrHighlight, Is.EqualTo(-ColorLut.AutoHighlightMax), "Highlight should be reduced for image of bright highlights.");
		var darkLuminances = new List<double>(midtones);
		for (var i = 0; i < 600; ++i)
			darkLuminances.Add(0.004);
		var (darkBrightness, _, _, darkShadow) = Select(darkLuminances);
		Assert.That(darkBrightness, Is.EqualTo(0), "Brightness should be kept for image of midtones in the range and deep shadows.");
		Assert.That(darkShadow, Is.EqualTo(ColorLut.AutoShadowMax), "Shadow should be lifted for image of deep shadows.");
		(brightness, _, _, _) = Select(Generate(0.9, 0.01));
		Assert.That(brightness, Is.EqualTo(0), "Brightness should be kept for image without midtones.");

		// positive brightness limited by highlights which it would push luminance into
		List<double> WithBrightParts(List<double> luminances, double value, int count)
		{
			for (var i = 0; i < count; ++i)
				luminances.Add(value);
			return luminances;
		}
		var darkWithBrightBrightness = Select(WithBrightParts(Generate(0.03, 0.5), 0.7, 50)).Item1;
		Assert.That(darkWithBrightBrightness, Is.GreaterThan(0).And.LessThanOrEqualTo(Math.Log2(ColorLut.AutoHighlightThreshold / 0.7)), "Brightness of dark image with bright parts should be limited so that the bright parts are not pushed into highlights.");
		Assert.That(Select(Generate(0.03, 0.5)).Item1, Is.EqualTo(ColorLut.AutoBrightnessMaxEv), "Brightness of dark image without bright parts should not be limited by highlights.");
		var (crowdedBrightness, _, crowdedHighlight, _) = Select(WithBrightParts(Generate(0.1, 0.5), 0.85, 50));
		Assert.That(crowdedBrightness, Is.EqualTo(0), "Brightness should not be increased for image whose highlights are already crowded.");
		Assert.That(crowdedHighlight, Is.LessThan(0), "Crowded highlights should be reduced.");

		// contrast
		var prevContrast = -1.0;
		foreach (var stops in new[] { 1.5, 1.25, 1, 0.75, 0.5 })
		{
			var (_, contrast, _, _) = Select(Generate(MidGray, stops));
			Assert.That(contrast, Is.GreaterThanOrEqualTo(prevContrast).And.InRange(0, ColorLut.AutoContrastMax), $"Contrast should rise as spread falls, at {stops} stops.");
			prevContrast = contrast;
		}
		var wideLuminances = new List<double>();
		for (var i = 0; i < 500; ++i)
		{
			wideLuminances.Add(0.02);
			wideLuminances.Add(MidGray);
			wideLuminances.Add(0.75);
		}
		Assert.That(Select(wideLuminances).Item2, Is.EqualTo(0), "Contrast should be kept for image with wide spread.");
		Assert.That(prevContrast, Is.GreaterThan(0.1), "Contrast should be increased for flat image.");
		Assert.That(Select(Generate(MidGray, 0)).Item2, Is.EqualTo(0), "Contrast should be kept for image of single luminance.");

		// highlight
		var prevHighlight = 1.0;
		foreach (var ratio in new[] { 0, 0.05, 0.1, 0.2 })
		{
			var (_, _, highlight, _) = Select(Generate(MidGray, 0.5, (ratio, 0.9)));
			Assert.That(highlight, Is.LessThanOrEqualTo(prevHighlight).And.InRange(-ColorLut.AutoHighlightMax, 0), $"Highlight should fall as highlights grow, at ratio {ratio}.");
			prevHighlight = highlight;
		}
		Assert.That(prevHighlight, Is.EqualTo(-ColorLut.AutoHighlightMax), "Highlight should be reduced for image with many highlights.");
		Assert.That(Select(Generate(MidGray, 0.5, (0.2, 1))).Item3, Is.EqualTo(0), "Clipped white should not be counted as highlights.");
		var clippedLuminances = Generate(MidGray * 4, 0.5);
		for (var i = 0; i < 300; ++i)
			clippedLuminances.Add(1);
		var (clippedBrightness, _, clippedHighlight, _) = Select(clippedLuminances);
		Assert.That(clippedBrightness, Is.LessThan(0), "Bright midtones should be darkened.");
		Assert.That(clippedHighlight, Is.EqualTo(0), "Clipped white which is darkened by brightness should not be counted as highlights.");

		// shadow
		var prevShadow = -1.0;
		foreach (var ratio in new[] { 0, 0.05, 0.1, 0.2, 0.3 })
		{
			var (_, _, _, shadow) = Select(Generate(MidGray, 0.5, (ratio, 0.004)));
			Assert.That(shadow, Is.GreaterThanOrEqualTo(prevShadow).And.InRange(0, ColorLut.AutoShadowMax), $"Shadow should rise as shadows grow, at ratio {ratio}.");
			prevShadow = shadow;
		}
		Assert.That(prevShadow, Is.EqualTo(ColorLut.AutoShadowMax), "Shadow should be lifted for image with many shadows.");
		Assert.That(Select(Generate(MidGray, 0.5, (0.2, 0))).Item4, Is.EqualTo(0), "Black should not be counted as shadows.");

		// no luminance
		Assert.That(Select([]), Is.EqualTo((0.0, 0.0, 0.0, 0.0)), "Nothing should be adjusted without luminance.");
	}


	// Transform brightness of identity LUT.
	static async Task<double[]> TransformBrightnessAsync(int size, double ev, ColorSpace? colorSpace)
	{
		var lut = new double[size];
		ColorLut.ResetToIdentity(lut);
		await ColorLut.BrightnessTransformAsync(lut, ev, colorSpace is not null ? colorSpace.NumericalTransferToLinear : null, colorSpace is not null ? colorSpace.NumericalTransferFromLinear : null, CancellationToken.None);
		return lut;
	}


	// Transform identity LUT by gain applied to linear color.
	static async Task<double[]> TransformGainAsync(int size, double gain, ColorSpace? colorSpace)
	{
		var lut = new double[size];
		ColorLut.ResetToIdentity(lut);
		await ColorLut.GainTransformAsync(lut, gain, colorSpace is not null ? colorSpace.NumericalTransferToLinear : null, colorSpace is not null ? colorSpace.NumericalTransferFromLinear : null, CancellationToken.None);
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


	/// <summary>
	/// Test for selecting warmth and tint which give back the ratios of the gains they were selected from.
	/// </summary>
	[Test]
	public void WarmthAndTintRoundTripTest()
	{
		var weights = ToneCurveImageFilter.Bt709LuminanceWeights;
		foreach (var warmth in new[] { -1, -0.4, 0, 0.25, 1 })
		{
			foreach (var tint in new[] { -1, -0.3, 0, 0.6, 1 })
			{
				// select gains and scale them arbitrarily
				var (gainR, gainG, gainB) = ColorLut.SelectWhiteBalanceGains(warmth, tint, weights);
				const double scale = 3.7;

				// select warmth and tint back
				var (selectedWarmth, selectedTint) = ColorLut.SelectWarmthAndTint(gainR * scale, gainG * scale, gainB * scale);
				Assert.That(selectedWarmth, Is.EqualTo(warmth).Within(1e-9), $"Warmth should be selected back from warmth {warmth} and tint {tint}.");
				Assert.That(selectedTint, Is.EqualTo(tint).Within(1e-9), $"Tint should be selected back from warmth {warmth} and tint {tint}.");
			}
		}
		Assert.That(() => ColorLut.SelectWarmthAndTint(0, 1, 1), Throws.InstanceOf<ArgumentException>(), "Gain which is not positive should be rejected.");
	}


	/// <summary>
	/// Test for direction of gains selected for warmth and tint, and for keeping the luminance of gray.
	/// </summary>
	[Test]
	public void WhiteBalanceGainsTest()
	{
		// neutral
		var weights = ToneCurveImageFilter.Bt709LuminanceWeights;
		var (gainR, gainG, gainB) = ColorLut.SelectWhiteBalanceGains(0, 0, weights);
		Assert.That(gainR, Is.EqualTo(1).Within(1e-9), "R should be kept without warmth and tint.");
		Assert.That(gainG, Is.EqualTo(1).Within(1e-9), "G should be kept without warmth and tint.");
		Assert.That(gainB, Is.EqualTo(1).Within(1e-9), "B should be kept without warmth and tint.");

		// warmth and tint
		foreach (var (warmth, tint) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0), (0.5, -0.5) })
		{
			(gainR, gainG, gainB) = ColorLut.SelectWhiteBalanceGains(warmth, tint, weights);
			var (weightR, weightG, weightB) = weights;
			var message = $"warmth {warmth} and tint {tint}";
			Assert.That(weightR * gainR + weightG * gainG + weightB * gainB, Is.EqualTo(1).Within(1e-9), $"Luminance of gray should be kept with {message}.");
			Assert.That(Math.Log2(gainR / gainB), Is.EqualTo(2 * warmth * ColorLut.WarmthMaxEv).Within(1e-9), $"Ratio of R to B should follow the warmth with {message}.");
			Assert.That(Math.Log2(Math.Sqrt(gainR * gainB) / gainG), Is.EqualTo(tint * ColorLut.TintMaxEv).Within(1e-9), $"Ratio of R and B to G should follow the tint with {message}.");
		}

		// clamped
		var clampedGains = ColorLut.SelectWhiteBalanceGains(3, -3, weights);
		Assert.That(clampedGains, Is.EqualTo(ColorLut.SelectWhiteBalanceGains(1, -1, weights)), "Warmth and tint should be clamped to [-1.0, 1.0].");
	}
}
