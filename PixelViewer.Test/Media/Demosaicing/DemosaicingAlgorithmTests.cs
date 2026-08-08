using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.Demosaicing;
using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Threading;

namespace Carina.PixelViewer.Test.Media.Demosaicing;

/// <summary>
/// Tests of <see cref="DemosaicingAlgorithm"/>.
/// </summary>
/// <remarks>Every test is driven by <see cref="DemosaicingAlgorithms.All"/> instead of naming an algorithm, so an algorithm added later is covered without touching the tests.</remarks>
[TestFixture]
class DemosaicingAlgorithmTests : BaseTests
{
	// Constants. The minimum ratios are placed slightly below the ratios measured from the built-in bilinear algorithm, which is the algorithm interpolating least accurately, so that an algorithm interpolating even worse is reported instead of being accepted silently. Bilinear reaches 38.64 dB and 27.29 dB in the worst case. The ratios of one algorithm spread by no more than 0.7 dB over the patterns of one format and size, which is what makes the patterns comparable to each other, while the 2 sizes differ by up to 1.6 dB because the smaller one is not a multiple of the color block and gives the border a larger share of the image. Measure them again after changing the content of the ground truth.
	const double Min2x2PeakSignalNoiseRatio = 37;
	const double Min4x4PeakSignalNoiseRatio = 26;
	const int PeakSignalNoiseRatioBorderSize = 4;


	// Static fields.
	static readonly BayerPattern[] bayerPatterns = Enum.GetValues<BayerPattern>();
	static readonly BitmapFormat[] bitmapFormats = Enum.GetValues<BitmapFormat>();
	static readonly (int Width, int Height)[] imageSizes = [ (66, 34), (35, 29) ];
	static readonly (int Width, int Height)[] smallImageSizes = [ (1, 1), (1, 2), (2, 1), (2, 2) ];


	// Assert that the pixels in the 2 given buffers are exactly the same.
	static unsafe void AssertBuffersEqual(IBitmapBuffer x, IBitmapBuffer y, string message)
	{
		// check the layout of buffers
		Assert.That(y.Format, Is.EqualTo(x.Format), message);
		Assert.That(y.Width, Is.EqualTo(x.Width), message);
		Assert.That(y.Height, Is.EqualTo(x.Height), message);

		// compare the pixels row by row, the padding at the end of each row is ignored
		var byteSize = x.Format.GetByteSize();
		var rowSize = x.Format.CalculateRowBytes(x.Width);
		var xRowStride = x.RowBytes;
		var yRowStride = y.RowBytes;
		var height = x.Height;
		x.Memory.Pin(xBaseAddress =>
		{
			y.Memory.Pin(yBaseAddress =>
			{
				for (var rowIndex = 0; rowIndex < height; ++rowIndex)
				{
					var xRowPtr = (byte*)xBaseAddress + xRowStride * rowIndex;
					var yRowPtr = (byte*)yBaseAddress + yRowStride * rowIndex;
					for (var i = 0; i < rowSize; ++i)
					{
						if (xRowPtr[i] != yRowPtr[i])
							Assert.Fail($"{message} Byte {i % byteSize} of pixel ({i / byteSize}, {rowIndex}) is {xRowPtr[i]} and {yRowPtr[i]}.");
					}
				}
			});
		});
	}


	// Assert that every pixel of the destination buffer is filled by the given algorithm.
	static void AssertDestinationFilled(DemosaicingAlgorithm algorithm, BayerPattern bayerPattern, BitmapFormat format, int width, int height)
	{
		// prepare the mosaic to be demosaiced
		using var groundTruth = CreateGroundTruthImage(format, width, height);
		using var mosaic = CreateMosaic(groundTruth, bayerPattern);

		// demosaic into the destination buffers which are filled with different data before
		using var clearedResult = new BitmapBuffer(format, ColorSpace.Default, width, height);
		using var filledResult = new BitmapBuffer(format, ColorSpace.Default, width, height);
		FillBuffer(clearedResult, 0x00);
		FillBuffer(filledResult, 0xff);
		Demosaic(algorithm, mosaic, clearedResult, bayerPattern);
		Demosaic(algorithm, mosaic, filledResult, bayerPattern);

		// the result should be independent of the data in the destination buffer before demosaicing
		AssertBuffersEqual(clearedResult, filledResult, $"Destination buffer is not filled completely. {Describe(algorithm, bayerPattern, format, width, height)}");
	}


	// Assert that demosaicing in place produces the same result as demosaicing into another buffer.
	static void AssertInPlaceDemosaicingConsistent(DemosaicingAlgorithm algorithm, BayerPattern bayerPattern, BitmapFormat format, int width, int height)
	{
		// the algorithm is asked to work in place only when it needs no dedicated buffer at all. An algorithm which merely prefers one interpolates differently in each arrangement by definition, so comparing the 2 results would report the reason it states the preference as a failure
		if (algorithm.CheckOutputBufferRequirement(bayerPattern, width, height) != OutputBufferRequirement.NotRequired)
			return;

		// demosaic with separate source and destination buffers
		using var groundTruth = CreateGroundTruthImage(format, width, height);
		using var mosaic = CreateMosaic(groundTruth, bayerPattern);
		using var result = new BitmapBuffer(format, ColorSpace.Default, width, height);
		Demosaic(algorithm, mosaic, result, bayerPattern);

		// demosaic with the buffer which is shared as both source and destination, which is how Session performs in-place demosaicing
		using var inPlaceResult = CreateMosaic(groundTruth, bayerPattern);
		using var sharedInPlaceResult = inPlaceResult.Share();
		Assert.That(inPlaceResult.IsBufferSharedWith(sharedInPlaceResult), Is.True, "Shared buffer should be reported as the same buffer.");
		Demosaic(algorithm, inPlaceResult, sharedInPlaceResult, bayerPattern);

		// working in place should not change the result at all
		AssertBuffersEqual(result, inPlaceResult, $"Demosaicing in place produces a different result. {Describe(algorithm, bayerPattern, format, width, height)}");
	}


	// Assert that the algorithm which only prefers a dedicated buffer still interpolates accurately enough with the same buffer as both source and destination, which is what it falls back to when there is not enough memory for a dedicated one.
	static void AssertInPlaceInterpolationQuality(DemosaicingAlgorithm algorithm, BayerPattern bayerPattern, BitmapFormat format, int width, int height)
	{
		// only the algorithm which prefers a dedicated buffer interpolates in place in a way of its own. The algorithm which needs no dedicated buffer is already covered by the consistency of its 2 arrangements together with the quality of the one with separate buffers, and the algorithm which requires one is never asked to work in place at all
		if (algorithm.CheckOutputBufferRequirement(bayerPattern, width, height) != OutputBufferRequirement.Preferred)
			return;

		// demosaic with the buffer which is shared as both source and destination, which is how Session performs in-place demosaicing
		using var groundTruth = CreateGroundTruthImage(format, width, height);
		using var result = CreateMosaic(groundTruth, bayerPattern);
		using var sharedResult = result.Share();
		Assert.That(result.IsBufferSharedWith(sharedResult), Is.True, "Shared buffer should be reported as the same buffer.");
		Demosaic(algorithm, result, sharedResult, bayerPattern);

		// the interpolated image should be close enough to the image the mosaic is sampled from
		var peakSignalNoiseRatio = ComputePeakSignalNoiseRatio(result, groundTruth, PeakSignalNoiseRatioBorderSize);
		var minPeakSignalNoiseRatio = bayerPattern.BlockWidth > 2 ? Min4x4PeakSignalNoiseRatio : Min2x2PeakSignalNoiseRatio;
		Assert.That(peakSignalNoiseRatio, Is.GreaterThanOrEqualTo(minPeakSignalNoiseRatio), $"Image interpolated in place is only {peakSignalNoiseRatio:F2} dB away from the ground truth. {Describe(algorithm, bayerPattern, format, width, height)}");
	}


	// Compute the peak signal-to-noise ratio between the demosaiced image and the ground truth in dB. Only the color components are compared, and the pixels near the border are excluded because interpolating them is inaccurate for every algorithm.
	static unsafe double ComputePeakSignalNoiseRatio(IBitmapBuffer image, IBitmapBuffer groundTruth, int borderSize)
	{
		// prepare
		var width = image.Width;
		var height = image.Height;
		var imageRowStride = image.RowBytes;
		var groundTruthRowStride = groundTruth.RowBytes;
		var maxValue = image.Format == BitmapFormat.Bgra32 ? 255.0 : 65535.0;
		var squaredErrorSum = 0.0;
		var sampleCount = 0L;

		// accumulate the squared error of every color component
		image.Memory.Pin(imageBaseAddress =>
		{
			groundTruth.Memory.Pin(groundTruthBaseAddress =>
			{
				for (var y = borderSize; y < height - borderSize; ++y)
				{
					for (var x = borderSize; x < width - borderSize; ++x)
					{
						for (var i = 0; i < 3; ++i)
						{
							var error = image.Format == BitmapFormat.Bgra32
								? ((byte*)imageBaseAddress + imageRowStride * y)[x * 4 + i] - (double)((byte*)groundTruthBaseAddress + groundTruthRowStride * y)[x * 4 + i]
								: ((ushort*)((byte*)imageBaseAddress + imageRowStride * y))[x * 4 + i] - (double)((ushort*)((byte*)groundTruthBaseAddress + groundTruthRowStride * y))[x * 4 + i];
							squaredErrorSum += error * error;
							++sampleCount;
						}
					}
				}
			});
		});

		// convert the mean squared error to the ratio in dB
		if (sampleCount <= 0)
			return double.PositiveInfinity;
		var meanSquaredError = squaredErrorSum / sampleCount;
		if (meanSquaredError <= 0)
			return double.PositiveInfinity;
		return 10 * Math.Log10(maxValue * maxValue / meanSquaredError);
	}


	// Create the image which the mosaic is sampled from. The content is detailed enough to tell the algorithms apart, and it is generated analytically so that a failure can be reproduced.
	static unsafe IBitmapBuffer CreateGroundTruthImage(BitmapFormat format, int width, int height) =>
		new BitmapBuffer(format, ColorSpace.Default, width, height).Setup(buffer =>
		{
			var rowStride = buffer.RowBytes;
			buffer.Memory.Pin(baseAddress =>
			{
				for (var y = 0; y < height; ++y)
				{
					// generate the content which carries its detail in the luminance shared by every channel, and lets each channel drift away from it only slowly, which is how the channels of a natural image relate to each other. An algorithm correcting a missing component by the component the pixel itself provides relies on that relation, so content whose channels vary independently would measure something other than the quality of demosaicing.
					var normalizedY = height > 1 ? (double)y / (height - 1) : 0.0;
					for (var x = 0; x < width; ++x)
					{
						// carry the fine detail in 4 oblique gratings instead of one, so that no direction of the image is easier to interpolate than another. A single grating makes the ratios of the patterns incomparable to each other: 2 of the 16 pixels of a 4x4 color block are rearranged along a diagonal, along the main one for a chroma-leading pattern and along the anti-diagonal for a green-leading one, so a lone grating which one diagonal crosses faster than the other ranks the patterns by its own orientation. The 4 directions are the mirror images of each other and they share one period, which is what equalizes the directions - giving each of them a period of its own would reintroduce the bias it removes.
						var normalizedX = width > 1 ? (double)x / (width - 1) : 0.0;
						var detail = Math.Sin(2 * Math.PI * (x + 2 * y) / 17.0)
							+ Math.Sin(2 * Math.PI * (x - 2 * y) / 17.0)
							+ Math.Sin(2 * Math.PI * (2 * x + y) / 17.0)
							+ Math.Sin(2 * Math.PI * (2 * x - y) / 17.0);
						var luminance = 0.5 + 0.31 * Math.Sin(2 * Math.PI * (normalizedX + 0.35 * normalizedY)) + 0.045 * detail;
						var blue = luminance * 0.92 + 0.03 + 0.04 * Math.Sin(2 * Math.PI * normalizedY);
						var green = luminance + 0.03 * Math.Cos(2 * Math.PI * normalizedX);
						var red = luminance * 1.06 - 0.03 + 0.04 * Math.Sin(2 * Math.PI * (normalizedX + normalizedY));

						// write the pixel as an opaque color
						if (format == BitmapFormat.Bgra32)
						{
							var pixelPtr = (byte*)baseAddress + rowStride * y + x * 4;
							pixelPtr[0] = ImageProcessing.ClipToByte(blue * 255);
							pixelPtr[1] = ImageProcessing.ClipToByte(green * 255);
							pixelPtr[2] = ImageProcessing.ClipToByte(red * 255);
							pixelPtr[3] = 255;
						}
						else
						{
							var pixelPtr = (ushort*)((byte*)baseAddress + rowStride * y) + x * 4;
							pixelPtr[0] = ImageProcessing.ClipToUInt16(blue * 65535);
							pixelPtr[1] = ImageProcessing.ClipToUInt16(green * 65535);
							pixelPtr[2] = ImageProcessing.ClipToUInt16(red * 65535);
							pixelPtr[3] = 65535;
						}
					}
				}
			});
		});


	// Create the image which each of its pixels provides only the color component selected by the given pattern of Bayer Filter, which is what a Bayer renderer produces.
	static unsafe IBitmapBuffer CreateMosaic(IBitmapBuffer groundTruth, BayerPattern bayerPattern) =>
		new BitmapBuffer(groundTruth.Format, ColorSpace.Default, groundTruth.Width, groundTruth.Height).Setup(buffer =>
		{
			var colorComponentSelector = bayerPattern.CreateColorComponentSelector();
			var format = groundTruth.Format;
			var width = groundTruth.Width;
			var height = groundTruth.Height;
			var groundTruthRowStride = groundTruth.RowBytes;
			var rowStride = buffer.RowBytes;
			groundTruth.Memory.Pin(groundTruthBaseAddress =>
			{
				buffer.Memory.Pin(baseAddress =>
				{
					for (var y = 0; y < height; ++y)
					{
						for (var x = 0; x < width; ++x)
						{
							// keep the color component provided by the pixel itself and the alpha only, the other components are dropped by the color filter
							var component = (int)colorComponentSelector(x, y);
							if (format == BitmapFormat.Bgra32)
							{
								var groundTruthPixelPtr = (byte*)groundTruthBaseAddress + groundTruthRowStride * y + x * 4;
								var pixelPtr = (byte*)baseAddress + rowStride * y + x * 4;
								pixelPtr[0] = 0;
								pixelPtr[1] = 0;
								pixelPtr[2] = 0;
								pixelPtr[component] = groundTruthPixelPtr[component];
								pixelPtr[3] = groundTruthPixelPtr[3];
							}
							else
							{
								var groundTruthPixelPtr = (ushort*)((byte*)groundTruthBaseAddress + groundTruthRowStride * y) + x * 4;
								var pixelPtr = (ushort*)((byte*)baseAddress + rowStride * y) + x * 4;
								pixelPtr[0] = 0;
								pixelPtr[1] = 0;
								pixelPtr[2] = 0;
								pixelPtr[component] = groundTruthPixelPtr[component];
								pixelPtr[3] = groundTruthPixelPtr[3];
							}
						}
					}
				});
			});
		});


	// Perform demosaicing by the given algorithm, the arguments are prepared in the same way as Session does.
	static void Demosaic(DemosaicingAlgorithm algorithm, IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, BayerPattern bayerPattern) =>
		algorithm.Demosaic(srcBuffer, destBuffer, bayerPattern, bayerPattern.CreateColorComponentSelector(), new ImageRenderingOptions { BayerPattern = bayerPattern }, CancellationToken.None);


	// Describe the combination which is being tested for the message of assertion.
	static string Describe(DemosaicingAlgorithm algorithm, BayerPattern bayerPattern, BitmapFormat format, int width, int height) =>
		$"Algorithm: {algorithm.Id}, pattern: {bayerPattern}, format: {format}, size: {width}x{height}.";


	// Fill every byte of the given buffer with the given value.
	static unsafe void FillBuffer(IBitmapBuffer buffer, byte value)
	{
		var height = buffer.Height;
		var rowStride = buffer.RowBytes;
		buffer.Memory.Pin(baseAddress =>
		{
			for (var y = 0; y < height; ++y)
			{
				var rowPtr = (byte*)baseAddress + rowStride * y;
				for (var i = 0; i < rowStride; ++i)
					rowPtr[i] = value;
			}
		});
	}


	// Perform the given test on every combination of algorithm, pattern of Bayer Filter, format and size of image which is supported.
	static void RunOnEachSupportedCombination((int Width, int Height)[] sizes, Action<DemosaicingAlgorithm, BayerPattern, BitmapFormat, int, int> test)
	{
		foreach (var algorithm in DemosaicingAlgorithms.All)
		{
			foreach (var bayerPattern in bayerPatterns)
			{
				if (!algorithm.IsBayerPatternSupported(bayerPattern))
					continue;
				foreach (var format in bitmapFormats)
				{
					foreach (var (width, height) in sizes)
						test(algorithm, bayerPattern, format, width, height);
				}
			}
		}
	}


	/// <summary>
	/// Test for keeping the mosaic as-is by the sentinel which represents no demosaicing.
	/// </summary>
	[Test]
	public void TestBypassKeepsMosaic()
	{
		foreach (var bayerPattern in bayerPatterns)
		{
			foreach (var format in bitmapFormats)
			{
				foreach (var (width, height) in imageSizes)
				{
					// prepare the mosaic to be passed through
					using var groundTruth = CreateGroundTruthImage(format, width, height);
					using var mosaic = CreateMosaic(groundTruth, bayerPattern);

					// pass the mosaic through to another buffer
					using var result = new BitmapBuffer(format, ColorSpace.Default, width, height);
					Demosaic(DemosaicingAlgorithms.Bypass, mosaic, result, bayerPattern);

					// the mosaic should reach the destination without being interpolated
					AssertBuffersEqual(mosaic, result, $"Mosaic is changed by the sentinel. {Describe(DemosaicingAlgorithms.Bypass, bayerPattern, format, width, height)}");
				}
			}
		}
	}


	/// <summary>
	/// Test for supporting every pattern of Bayer Filter by the sentinel which represents no demosaicing.
	/// </summary>
	/// <remarks>The sentinel is the last resort of <c>Session.SelectDefaultDemosaicingAlgorithm()</c>, which can select a supported algorithm for every pattern only if the sentinel supports all of them.</remarks>
	[Test]
	public void TestBypassSupportsEveryBayerPattern()
	{
		foreach (var bayerPattern in bayerPatterns)
			Assert.That(DemosaicingAlgorithms.Bypass.IsBayerPatternSupported(bayerPattern), Is.True, $"Pattern '{bayerPattern}' is not supported by the sentinel.");
	}


	/// <summary>
	/// Test for filling every pixel of the destination buffer, which may be a newly allocated buffer.
	/// </summary>
	[Test]
	public void TestFillingDestination() =>
		RunOnEachSupportedCombination(imageSizes, AssertDestinationFilled);


	/// <summary>
	/// Test for performing demosaicing with the same buffer as both source and destination.
	/// </summary>
	/// <remarks>The algorithm which needs no dedicated buffer must produce the same result in both arrangements, while the algorithm which only prefers one is allowed to interpolate differently in place and is checked for interpolating accurately enough instead.</remarks>
	[Test]
	public void TestInPlaceDemosaicing()
	{
		RunOnEachSupportedCombination(imageSizes, AssertInPlaceDemosaicingConsistent);
		RunOnEachSupportedCombination(imageSizes, AssertInPlaceInterpolationQuality);
	}


	/// <summary>
	/// Test for interpolating the image which is close enough to the image the mosaic is sampled from.
	/// </summary>
	[Test]
	public void TestInterpolationQuality()
	{
		RunOnEachSupportedCombination(imageSizes, (algorithm, bayerPattern, format, width, height) =>
		{
			// the sentinel keeps the mosaic instead of interpolating it
			if (algorithm == DemosaicingAlgorithms.Bypass)
				return;

			// demosaic the mosaic which is sampled from the ground truth
			using var groundTruth = CreateGroundTruthImage(format, width, height);
			using var mosaic = CreateMosaic(groundTruth, bayerPattern);
			using var result = new BitmapBuffer(format, ColorSpace.Default, width, height);
			Demosaic(algorithm, mosaic, result, bayerPattern);

			// the interpolated image should be close enough to the ground truth
			var peakSignalNoiseRatio = ComputePeakSignalNoiseRatio(result, groundTruth, PeakSignalNoiseRatioBorderSize);
			var minPeakSignalNoiseRatio = bayerPattern.BlockWidth > 2 ? Min4x4PeakSignalNoiseRatio : Min2x2PeakSignalNoiseRatio;
			Assert.That(peakSignalNoiseRatio, Is.GreaterThanOrEqualTo(minPeakSignalNoiseRatio), $"Interpolated image is only {peakSignalNoiseRatio:F2} dB away from the ground truth. {Describe(algorithm, bayerPattern, format, width, height)}");
		});
	}


	/// <summary>
	/// Test for demosaicing the image which is smaller than the sub block or the kernel of algorithm.
	/// </summary>
	[Test]
	public void TestSmallImages()
	{
		RunOnEachSupportedCombination(smallImageSizes, AssertDestinationFilled);
		RunOnEachSupportedCombination(smallImageSizes, AssertInPlaceDemosaicingConsistent);
	}
}
