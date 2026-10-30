using Carina.PixelViewer.Media;
using NUnit.Framework;
using SkiaSharp;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="ColorSpace"/>.
/// </summary>
[TestFixture]
class ColorSpaceTests
{
	// Constants.
	const double Tolerance16Bit = 16.0 / 65535;
	const double Tolerance8Bit = 1.5 / 255;


	// Static fields.
	static readonly SKColorSpaceXyz offDiagonalMatrixToXyz = new(
		0.7f, 0.2f, 0.0642f,
		0.3f, 0.6f, 0.1f,
		0.05f, 0.1f, 0.6749f);
	static readonly (double, double, double)[] sampleColors =
	[
		(1.0, 0.0, 0.0),
		(0.0, 1.0, 0.0),
		(0.0, 0.0, 1.0),
		(0.8, 0.4, 0.2),
		(0.2, 0.6, 0.9),
		(0.5, 0.5, 0.5),
		(1.0, 1.0, 1.0),
		(0.0, 0.0, 0.0)
	];


	// Clip a normalized color to the valid range.
	static double Clip(double color)
	{
		if (color < 0)
			return 0;
		if (color > 1)
			return 1;
		return color;
	}


	// Convert a normalized linear color of the off-diagonal color space to sRGB in floating point, as the reference of the fixed-point conversion.
	static (double, double, double) ConvertToSrgbByFloatingPoint(double r, double g, double b)
	{
		// transfer to CIE XYZ with the D50 white point
		var m = offDiagonalMatrixToXyz;
		var x = m[0, 0] * r + m[1, 0] * g + m[2, 0] * b;
		var y = m[0, 1] * r + m[1, 1] * g + m[2, 1] * b;
		var z = m[0, 2] * r + m[1, 2] * g + m[2, 2] * b;

		// transfer to linear sRGB
		var mInv = SKColorSpaceXyz.Srgb.Invert();
		var srgbR = mInv[0, 0] * x + mInv[1, 0] * y + mInv[2, 0] * z;
		var srgbG = mInv[0, 1] * x + mInv[1, 1] * y + mInv[2, 1] * z;
		var srgbB = mInv[0, 2] * x + mInv[1, 2] * y + mInv[2, 2] * z;

		// clip each channel to the valid range, as the conversion does
		return (Clip(srgbR), Clip(srgbG), Clip(srgbB));
	}


	// Create a linear color space with strongly off-diagonal primaries, so that mixing the channels up is visible.
	static ColorSpace CreateOffDiagonalLinearColorSpace() => ColorSpace.FromMatrixToXyz(
		ColorSpaceSource.Embedded,
		"Off-diagonal linear",
		offDiagonalMatrixToXyz,
		new SKColorSpaceTransferFn
		{
			G = 1f,
			A = 1f
		},
		ColorSpace.D50);


	/// <summary>
	/// Test for the numerical transfer of a linear color space being an identity.
	/// </summary>
	[Test]
	public void LinearNumericalTransferTest()
	{
		// the source transfer of a linear color space should change nothing, whether it is skipped or not
		var linearColorSpace = CreateOffDiagonalLinearColorSpace();
		var skippingSrcConverter = linearColorSpace.CreateConverter(ColorSpace.Srgb, true, true);
		var transferringSrcConverter = linearColorSpace.CreateConverter(ColorSpace.Srgb, false, true);
		foreach (var (r, g, b) in sampleColors)
		{
			var expected = skippingSrcConverter.Convert((ushort)(r * 65535 + 0.5), (ushort)(g * 65535 + 0.5), (ushort)(b * 65535 + 0.5));
			var actual = transferringSrcConverter.Convert((ushort)(r * 65535 + 0.5), (ushort)(g * 65535 + 0.5), (ushort)(b * 65535 + 0.5));
			Assert.That(actual, Is.EqualTo(expected), $"Source transfer of linear color space changed ({r}, {g}, {b}).");
		}

		// the destination transfer of a linear color space should change nothing either
		var skippingDestConverter = ColorSpace.Srgb.CreateConverter(linearColorSpace, true, true);
		var transferringDestConverter = ColorSpace.Srgb.CreateConverter(linearColorSpace, true, false);
		foreach (var (r, g, b) in sampleColors)
		{
			var expected = skippingDestConverter.Convert((ushort)(r * 65535 + 0.5), (ushort)(g * 65535 + 0.5), (ushort)(b * 65535 + 0.5));
			var actual = transferringDestConverter.Convert((ushort)(r * 65535 + 0.5), (ushort)(g * 65535 + 0.5), (ushort)(b * 65535 + 0.5));
			Assert.That(actual, Is.EqualTo(expected), $"Destination transfer of linear color space changed ({r}, {g}, {b}).");
		}
	}


	/// <summary>
	/// Test for converting 8-bit and 16-bit color to another color space.
	/// </summary>
	[Test]
	public void MatrixConversionTest()
	{
		// convert each sample color with both integer overloads and compare with the floating-point reference
		var srcColorSpace = CreateOffDiagonalLinearColorSpace();
		var converter = srcColorSpace.CreateConverter(ColorSpace.Srgb, true, true);
		foreach (var (r, g, b) in sampleColors)
		{
			var (expectedR, expectedG, expectedB) = ConvertToSrgbByFloatingPoint(r, g, b);

			var (r8, g8, b8) = converter.Convert((byte)(r * 255 + 0.5), (byte)(g * 255 + 0.5), (byte)(b * 255 + 0.5));
			Assert.That(r8 / 255.0, Is.EqualTo(expectedR).Within(Tolerance8Bit), $"8-bit R of ({r}, {g}, {b}).");
			Assert.That(g8 / 255.0, Is.EqualTo(expectedG).Within(Tolerance8Bit), $"8-bit G of ({r}, {g}, {b}).");
			Assert.That(b8 / 255.0, Is.EqualTo(expectedB).Within(Tolerance8Bit), $"8-bit B of ({r}, {g}, {b}).");

			var (r16, g16, b16) = converter.Convert((ushort)(r * 65535 + 0.5), (ushort)(g * 65535 + 0.5), (ushort)(b * 65535 + 0.5));
			Assert.That(r16 / 65535.0, Is.EqualTo(expectedR).Within(Tolerance16Bit), $"16-bit R of ({r}, {g}, {b}).");
			Assert.That(g16 / 65535.0, Is.EqualTo(expectedG).Within(Tolerance16Bit), $"16-bit G of ({r}, {g}, {b}).");
			Assert.That(b16 / 65535.0, Is.EqualTo(expectedB).Within(Tolerance16Bit), $"16-bit B of ({r}, {g}, {b}).");
		}
	}
}
