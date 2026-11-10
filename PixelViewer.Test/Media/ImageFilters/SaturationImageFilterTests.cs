using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageFilters;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.Media.ImageFilters;

/// <summary>
/// Tests of <see cref="SaturationImageFilter"/>.
/// </summary>
[TestFixture]
class SaturationImageFilterTests : BaseTests
{
	// Constants.
	const double Tolerance = 2.5 / 255;


	// Static fields.
	static readonly BitmapFormat[] Formats = [ BitmapFormat.Bgra32, BitmapFormat.Bgra64 ];
	static readonly (double, double, double, double) SkinTone = (0.8, 0.6, 0.47, 1);


	// Apply filter to pixels given as normalized (R, G, B, A) and get the normalized result.
	static async Task<(double, double, double, double)[]> ApplyFilterAsync(BitmapFormat format, double saturation, double vibrance, (double, double, double, double)[] pixels)
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
		var parameters = new SaturationImageFilter.Params()
		{
			Saturation = saturation,
			Vibrance = vibrance,
		};
		await new SaturationImageFilter().ApplyFilterAsync(source, result, parameters, CancellationToken.None);

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


	// Get change of chroma, which is max - min, from source pixel to result pixel.
	static double GetChromaChange((double, double, double, double) source, (double, double, double, double) result)
	{
		static double GetChroma((double, double, double, double) pixel)
		{
			var (r, g, b, _) = pixel;
			return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
		}
		return GetChroma(result) - GetChroma(source);
	}


	/// <summary>
	/// Test for keeping gray pixels and alpha.
	/// </summary>
	[Test]
	public async Task GrayAndAlphaTest()
	{
		foreach (var format in Formats)
		{
			(double, double, double, double)[] pixels = [ (0.5, 0.5, 0.5, 0.25), (0.8, 0.3, 0.2, 0.5) ];
			var sourcePixels = await ApplyFilterAsync(format, 0, 0, pixels);
			var resultPixels = await ApplyFilterAsync(format, 0.5, 0.5, pixels);
			Assert.That(resultPixels[0], Is.EqualTo(sourcePixels[0]), $"Gray pixel should be kept ({format}).");
			Assert.That(resultPixels[1].Item4, Is.EqualTo(sourcePixels[1].Item4), $"Alpha should be kept ({format}).");
		}
	}


	/// <summary>
	/// Test for applying saturation to skin tone the same as to other colors.
	/// </summary>
	[Test]
	public async Task SaturationOnSkinToneTest()
	{
		foreach (var format in Formats)
		{
			// skin tone and the color with its R and B swapped, which has the same chroma and the same gray
			var (r, g, b, a) = SkinTone;
			(double, double, double, double)[] pixels = [ SkinTone, (b, g, r, a) ];
			var resultPixels = await ApplyFilterAsync(format, 0.5, 0, pixels);

			// compare
			var skinToneChange = GetChromaChange(pixels[0], resultPixels[0]);
			var otherChange = GetChromaChange(pixels[1], resultPixels[1]);
			Assert.That(otherChange, Is.GreaterThan(0.03), $"Saturation should increase chroma ({format}).");
			Assert.That(skinToneChange, Is.EqualTo(otherChange).Within(Tolerance), $"Saturation should change skin tone as other colors ({format}).");
		}
	}


	/// <summary>
	/// Test for weight of skin tone.
	/// </summary>
	[Test]
	public void SkinToneWeightTest()
	{
		// inside
		Assert.That(SaturationImageFilter.GetSkinToneWeight(25, 0.35), Is.EqualTo(1), "Typical skin tone should be fully weighted.");

		// outside
		Assert.That(SaturationImageFilter.GetSkinToneWeight(SaturationImageFilter.SkinToneHueStart, 0.35), Is.EqualTo(0), "Red should not be weighted.");
		Assert.That(SaturationImageFilter.GetSkinToneWeight(-20, 0.35), Is.EqualTo(0), "Magenta-red should not be weighted.");
		Assert.That(SaturationImageFilter.GetSkinToneWeight(SaturationImageFilter.SkinToneHueEnd, 0.35), Is.EqualTo(0), "Yellow should not be weighted.");
		Assert.That(SaturationImageFilter.GetSkinToneWeight(25, SaturationImageFilter.SkinToneSaturationEnd), Is.EqualTo(0), "Highly saturated color should not be weighted.");
		Assert.That(SaturationImageFilter.GetSkinToneWeight(25, SaturationImageFilter.SkinToneSaturationStart), Is.EqualTo(0), "Nearly gray color should not be weighted.");

		// monotonic fading
		var prevWeight = 0.0;
		for (var hue = SaturationImageFilter.SkinToneHueStart; hue <= SaturationImageFilter.SkinToneHueFullStart; hue += 0.5)
		{
			var weight = SaturationImageFilter.GetSkinToneWeight(hue, 0.35);
			Assert.That(weight, Is.GreaterThanOrEqualTo(prevWeight), $"Weight should fade in by hue at {hue}.");
			prevWeight = weight;
		}
	}


	/// <summary>
	/// Test for protecting skin tone from vibrance, while other red-dominant colors are not protected.
	/// </summary>
	[Test]
	public async Task VibranceOnSkinToneTest()
	{
		foreach (var format in Formats)
		{
			foreach (var vibrance in new[] { 0.5, -0.5 })
			{
				// skin tone, red, and the colors with their R and B swapped, which have the same chroma and the same gray
				var (r, g, b, a) = SkinTone;
				(double, double, double, double)[] pixels = [ SkinTone, (b, g, r, a), (0.8, 0.3, 0.3, 1), (0.3, 0.3, 0.8, 1) ];
				var resultPixels = await ApplyFilterAsync(format, 0, vibrance, pixels);
				var message = $"vibrance {vibrance} ({format})";

				// skin tone is protected
				var skinToneChange = Math.Abs(GetChromaChange(pixels[0], resultPixels[0]));
				var otherChange = Math.Abs(GetChromaChange(pixels[1], resultPixels[1]));
				Assert.That(otherChange, Is.GreaterThan(0.03), $"Vibrance should change chroma with {message}.");
				Assert.That(skinToneChange, Is.LessThanOrEqualTo(otherChange * (1 - SaturationImageFilter.SkinToneProtection) + Tolerance), $"Skin tone should be protected with {message}.");

				// red is not protected
				var redChange = GetChromaChange(pixels[2], resultPixels[2]);
				var blueChange = GetChromaChange(pixels[3], resultPixels[3]);
				Assert.That(Math.Abs(blueChange), Is.GreaterThan(0.01), $"Vibrance should change chroma of blue with {message}.");
				Assert.That(redChange, Is.EqualTo(blueChange).Within(Tolerance), $"Red should not be protected with {message}.");
			}
		}
	}
}
