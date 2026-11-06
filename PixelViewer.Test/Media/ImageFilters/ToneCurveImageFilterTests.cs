using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageFilters;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.Media.ImageFilters;

/// <summary>
/// Tests of <see cref="ToneCurveImageFilter"/>.
/// </summary>
[TestFixture]
class ToneCurveImageFilterTests : BaseTests
{
	// Constants.
	const double LinearTolerance = 2e-3;


	// Static fields.
	static readonly BitmapFormat[] Formats = [ BitmapFormat.Bgra32, BitmapFormat.Bgra64 ];


	// Apply filter to pixels given as normalized (R, G, B, A) and get the normalized result.
	static async Task<(double, double, double, double)[]> ApplyFilterAsync(BitmapFormat format, IList<double> lut, ColorSpace? colorSpace, (double, double, double, double)[] pixels)
	{
		// prepare source
		var maxColor = format == BitmapFormat.Bgra64 ? 65535.0 : 255.0;
		using var source = new BitmapBuffer(format, ColorSpace.Srgb, pixels.Length, 1);
		using var result = new BitmapBuffer(format, ColorSpace.Srgb, pixels.Length, 1);
		unsafe
		{
			source.Memory.Pin(address =>
			{
				for (var i = pixels.Length - 1; i >= 0; --i)
				{
					var (r, g, b, a) = pixels[i];
					if (format == BitmapFormat.Bgra64)
					{
						var pixelPtr = (ushort*)address + i * 4;
						pixelPtr[0] = (ushort)Math.Round(b * maxColor);
						pixelPtr[1] = (ushort)Math.Round(g * maxColor);
						pixelPtr[2] = (ushort)Math.Round(r * maxColor);
						pixelPtr[3] = (ushort)Math.Round(a * maxColor);
					}
					else
					{
						var pixelPtr = (byte*)address + i * 4;
						pixelPtr[0] = (byte)Math.Round(b * maxColor);
						pixelPtr[1] = (byte)Math.Round(g * maxColor);
						pixelPtr[2] = (byte)Math.Round(r * maxColor);
						pixelPtr[3] = (byte)Math.Round(a * maxColor);
					}
				}
			});
		}

		// apply filter
		var parameters = new ToneCurveImageFilter.Params()
		{
			ColorTransferFuncFromLinear = colorSpace is not null ? colorSpace.NumericalTransferFromLinear : null,
			ColorTransferFuncToLinear = colorSpace is not null ? colorSpace.NumericalTransferToLinear : null,
			LookupTable = lut,
		};
		await new ToneCurveImageFilter().ApplyFilterAsync(source, result, parameters, CancellationToken.None);

		// read result
		var resultPixels = new (double, double, double, double)[pixels.Length];
		unsafe
		{
			result.Memory.Pin(address =>
			{
				for (var i = pixels.Length - 1; i >= 0; --i)
				{
					if (format == BitmapFormat.Bgra64)
					{
						var pixelPtr = (ushort*)address + i * 4;
						resultPixels[i] = (pixelPtr[2] / maxColor, pixelPtr[1] / maxColor, pixelPtr[0] / maxColor, pixelPtr[3] / maxColor);
					}
					else
					{
						var pixelPtr = (byte*)address + i * 4;
						resultPixels[i] = (pixelPtr[2] / maxColor, pixelPtr[1] / maxColor, pixelPtr[0] / maxColor, pixelPtr[3] / maxColor);
					}
				}
			});
		}
		return resultPixels;
	}


	/// <summary>
	/// Test for keeping black pixels black.
	/// </summary>
	[Test]
	public async Task BlackTest()
	{
		foreach (var format in Formats)
		{
			var lut = await CreateLutAsync(format, async it =>
			{
				await ColorLut.BrightnessTransformAsync(it, 1, ColorSpace.Srgb.NumericalTransferToLinear, ColorSpace.Srgb.NumericalTransferFromLinear, CancellationToken.None);
				await ColorLut.ShadowTransformAsync(it, 1, ColorSpace.Srgb.NumericalTransferToLinear, ColorSpace.Srgb.NumericalTransferFromLinear, CancellationToken.None);
			});
			var result = await ApplyFilterAsync(format, lut, ColorSpace.Srgb, [ (0, 0, 0, 1) ]);
			Assert.That(result[0], Is.EqualTo((0.0, 0.0, 0.0, 1.0)), $"Black pixel should be kept ({format}).");
		}
	}


	/// <summary>
	/// Test for keeping hue and saturation of colored pixels.
	/// </summary>
	[Test]
	public async Task ColoredPixelTest()
	{
		// prepare pixels
		var colorSpace = ColorSpace.Srgb;
		(double, double, double, double)[] pixels = [ (0.48, 0.35, 0.25, 1), (0.2, 0.4, 0.6, 1), (0.7, 0.3, 0.5, 0.5) ];

		// check each tone curve
		foreach (var shadow in new[] { 1.0, -1.0 })
		{
			var lut = await CreateLutAsync(BitmapFormat.Bgra64, it => ColorLut.ShadowTransformAsync(it, shadow, colorSpace.NumericalTransferToLinear, colorSpace.NumericalTransferFromLinear, CancellationToken.None));
			var result = await ApplyFilterAsync(BitmapFormat.Bgra64, lut, colorSpace, pixels);
			for (var i = pixels.Length - 1; i >= 0; --i)
			{
				// check ratios of linear colors
				var (r, g, b, a) = pixels[i];
				var (resultR, resultG, resultB, resultA) = result[i];
				var (linearR, linearG, linearB) = (colorSpace.NumericalTransferToLinear(r), colorSpace.NumericalTransferToLinear(g), colorSpace.NumericalTransferToLinear(b));
				var (resultLinearR, resultLinearG, resultLinearB) = (colorSpace.NumericalTransferToLinear(resultR), colorSpace.NumericalTransferToLinear(resultG), colorSpace.NumericalTransferToLinear(resultB));
				var message = $"pixel {i} with shadow {shadow}";
				Assert.That(resultLinearR / resultLinearG, Is.EqualTo(linearR / linearG).Within(0.5).Percent, $"Ratio of R to G of {message} should be kept.");
				Assert.That(resultLinearB / resultLinearG, Is.EqualTo(linearB / linearG).Within(0.5).Percent, $"Ratio of B to G of {message} should be kept.");
				Assert.That(resultA, Is.EqualTo(a).Within(1.0 / 65535), $"Alpha of {message} should be kept.");

				// check luminance
				var (weightR, weightG, weightB) = ToneCurveImageFilter.Bt709LuminanceWeights;
				var luminance = weightR * linearR + weightG * linearG + weightB * linearB;
				var expectedLuminance = colorSpace.NumericalTransferToLinear(Interpolate(lut, colorSpace.NumericalTransferFromLinear(luminance)));
				Assert.That(weightR * resultLinearR + weightG * resultLinearG + weightB * resultLinearB, Is.EqualTo(expectedLuminance).Within(LinearTolerance), $"Luminance of {message} should follow the tone curve.");
			}
		}
	}


	// Create LUT of identity transformation and transform it.
	static async Task<double[]> CreateLutAsync(BitmapFormat format, Func<double[], Task> transform)
	{
		var lut = new double[format == BitmapFormat.Bgra64 ? 65536 : 256];
		ColorLut.ResetToIdentity(lut);
		await transform(lut);
		return lut;
	}


	/// <summary>
	/// Test for applying tone curve to gray pixels as it is.
	/// </summary>
	[Test]
	public async Task GrayPixelTest()
	{
		foreach (var colorSpace in new[] { null, ColorSpace.Srgb })
		{
			foreach (var format in Formats)
			{
				// apply
				var lut = await CreateLutAsync(format, it => ColorLut.ContrastTransformAsync(it, 0.5, CancellationToken.None));
				var maxColor = lut.Length - 1.0;
				var pixels = new (double, double, double, double)[11];
				for (var i = pixels.Length - 1; i >= 0; --i)
				{
					var color = Math.Round(i / 10.0 * maxColor) / maxColor;
					pixels[i] = (color, color, color, 1);
				}
				var result = await ApplyFilterAsync(format, lut, colorSpace, pixels);

				// check
				var tolerance = format == BitmapFormat.Bgra64 ? 1e-3 : 1.01 / 255;
				for (var i = pixels.Length - 1; i >= 0; --i)
				{
					var expected = lut[(int)Math.Round(pixels[i].Item1 * maxColor)] / maxColor;
					var (r, g, b, _) = result[i];
					var message = $"gray pixel {pixels[i].Item1} ({format}, {colorSpace?.Name ?? "linear"})";
					Assert.That(r, Is.EqualTo(expected).Within(tolerance), $"R of {message} should follow the tone curve.");
					Assert.That(g, Is.EqualTo(expected).Within(tolerance), $"G of {message} should follow the tone curve.");
					Assert.That(b, Is.EqualTo(expected).Within(tolerance), $"B of {message} should follow the tone curve.");
				}
			}
		}
	}


	// Interpolate value of normalized color in LUT.
	static double Interpolate(IList<double> lut, double color)
	{
		var maxColor = lut.Count - 1.0;
		var position = Math.Clamp(color, 0, 1) * maxColor;
		var index = Math.Min((int)position, lut.Count - 2);
		return (lut[index] + (lut[index + 1] - lut[index]) * (position - index)) / maxColor;
	}


	/// <summary>
	/// Test for rejecting LUT of wrong size.
	/// </summary>
	[Test]
	public void LutSizeCheckingTest()
	{
		Assert.That(async () => await ApplyFilterAsync(BitmapFormat.Bgra64, new double[256], null, [ (0.5, 0.5, 0.5, 1) ]), Throws.InstanceOf<ArgumentException>(), "LUT whose size does not match the format should be rejected.");
	}


	/// <summary>
	/// Test for mixing color with white when it exceeds the range.
	/// </summary>
	[Test]
	public async Task OverRangeTest()
	{
		// apply
		var colorSpace = ColorSpace.Srgb;
		var lut = await CreateLutAsync(BitmapFormat.Bgra64, it => ColorLut.BrightnessTransformAsync(it, 1, colorSpace.NumericalTransferToLinear, colorSpace.NumericalTransferFromLinear, CancellationToken.None));
		(double, double, double, double)[] pixels = [ (0.9, 0.3, 0.2, 1) ];
		var result = await ApplyFilterAsync(BitmapFormat.Bgra64, lut, colorSpace, pixels);

		// check largest channel and hue
		var (r, g, b, _) = pixels[0];
		var (resultR, resultG, resultB, _) = result[0];
		var (linearR, linearG, linearB) = (colorSpace.NumericalTransferToLinear(r), colorSpace.NumericalTransferToLinear(g), colorSpace.NumericalTransferToLinear(b));
		var (resultLinearR, resultLinearG, resultLinearB) = (colorSpace.NumericalTransferToLinear(resultR), colorSpace.NumericalTransferToLinear(resultG), colorSpace.NumericalTransferToLinear(resultB));
		Assert.That(resultR, Is.EqualTo(1).Within(1e-6), "Largest channel should reach white.");
		Assert.That((resultLinearG - resultLinearB) / (resultLinearR - resultLinearB), Is.EqualTo((linearG - linearB) / (linearR - linearB)).Within(1).Percent, "Hue should be kept.");

		// check luminance
		var (weightR, weightG, weightB) = ToneCurveImageFilter.Bt709LuminanceWeights;
		var luminance = weightR * linearR + weightG * linearG + weightB * linearB;
		var expectedLuminance = colorSpace.NumericalTransferToLinear(Interpolate(lut, colorSpace.NumericalTransferFromLinear(luminance)));
		Assert.That(weightR * resultLinearR + weightG * resultLinearG + weightB * resultLinearB, Is.EqualTo(expectedLuminance).Within(LinearTolerance), "Luminance should follow the tone curve.");
	}


	/// <summary>
	/// Test for selecting luminance weights of color space.
	/// </summary>
	[Test]
	public void SelectingLuminanceWeightsTest()
	{
		var (weightR, weightG, weightB) = ToneCurveImageFilter.SelectLuminanceWeights(ColorSpace.Srgb);
		var (bt709R, bt709G, bt709B) = ToneCurveImageFilter.Bt709LuminanceWeights;
		Assert.That(weightR + weightG + weightB, Is.EqualTo(1).Within(1e-9), "Weights should be normalized.");
		Assert.That(weightR, Is.EqualTo(bt709R).Within(0.015), "Weight of R of sRGB should be close to BT.709.");
		Assert.That(weightG, Is.EqualTo(bt709G).Within(0.015), "Weight of G of sRGB should be close to BT.709.");
		Assert.That(weightB, Is.EqualTo(bt709B).Within(0.015), "Weight of B of sRGB should be close to BT.709.");
	}
}
