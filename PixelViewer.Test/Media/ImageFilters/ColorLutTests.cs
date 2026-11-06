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
}
