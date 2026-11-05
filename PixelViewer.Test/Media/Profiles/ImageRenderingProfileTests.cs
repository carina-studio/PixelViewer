using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.Demosaicing;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

namespace Carina.PixelViewer.Test.Media.Profiles;

/// <summary>
/// Tests of <see cref="ImageRenderingProfile"/>.
/// </summary>
[TestFixture]
class ImageRenderingProfileTests : BaseTests
{
	// Parameters of profile which are not applied to render image, they are not compared by HasSameRenderingParameters().
	static readonly ISet<string> NonRenderingParameters = new HashSet<string>
	{
		nameof(ImageRenderingProfile.MediaMetadata),
		nameof(ImageRenderingProfile.Name),
	};


	// Mutation of each parameter of profile which is applied to render image, each of them should be reported as difference.
	static readonly IList<(string Parameter, Action<ImageRenderingProfile> Mutate)> ParameterMutations =
	[
		(nameof(ImageRenderingProfile.AlphaColorTable), profile =>
		{
			using var colorTable = CreateColorTable();
			profile.AlphaColorTable = colorTable;
		}),
		(nameof(ImageRenderingProfile.BaseExposure), profile => profile.BaseExposure += 1),
		(nameof(ImageRenderingProfile.BayerPattern), profile => profile.BayerPattern = profile.BayerPattern == BayerPattern.BGGR_2x2 ? BayerPattern.GBRG_4x4 : BayerPattern.BGGR_2x2),
		(nameof(ImageRenderingProfile.BlackLevels), profile => profile.BlackLevels = IncreaseFirstElement(profile.BlackLevels)),
		(nameof(ImageRenderingProfile.BlueColorGain), profile => profile.BlueColorGain += 1),
		(nameof(ImageRenderingProfile.BlueColorTable), profile =>
		{
			using var colorTable = CreateColorTable();
			profile.BlueColorTable = colorTable;
		}),
		(nameof(ImageRenderingProfile.ByteOrdering), profile => profile.ByteOrdering = profile.ByteOrdering == ByteOrdering.BigEndian ? ByteOrdering.LittleEndian : ByteOrdering.BigEndian),
		(nameof(ImageRenderingProfile.ColorSpace), profile => profile.ColorSpace = profile.ColorSpace.Equals(ColorSpace.Srgb) ? ColorSpace.AdobeRGB_1998 : ColorSpace.Srgb),
		(nameof(ImageRenderingProfile.Compression), profile => profile.Compression = profile.Compression == ImageDataCompression.None ? ImageDataCompression.LosslessJpeg : ImageDataCompression.None),
		(nameof(ImageRenderingProfile.DataOffset), profile => profile.DataOffset += 1),
		(nameof(ImageRenderingProfile.DemosaicingAlgorithm), profile => profile.DemosaicingAlgorithm = profile.DemosaicingAlgorithm is null ? DemosaicingAlgorithms.Bilinear : DemosaicingAlgorithms.Bypass),
		(nameof(ImageRenderingProfile.EffectiveBits), profile => profile.EffectiveBits = IncreaseFirstElement(profile.EffectiveBits)),
		(nameof(ImageRenderingProfile.FlipX), profile => profile.FlipX = !profile.FlipX),
		(nameof(ImageRenderingProfile.FlipY), profile => profile.FlipY = !profile.FlipY),
		(nameof(ImageRenderingProfile.FramePaddingSize), profile => profile.FramePaddingSize += 1),
		(nameof(ImageRenderingProfile.GreenColorGain), profile => profile.GreenColorGain += 1),
		(nameof(ImageRenderingProfile.GreenColorTable), profile =>
		{
			using var colorTable = CreateColorTable();
			profile.GreenColorTable = colorTable;
		}),
		(nameof(ImageRenderingProfile.Height), profile => profile.Height += 1),
		(nameof(ImageRenderingProfile.LensShading), profile => profile.LensShading = CreateLensShadingMap(profile.LensShading is null ? 1.5f : profile.LensShading.Gains[0] + 1)),
		(nameof(ImageRenderingProfile.Orientation), profile => profile.Orientation += 90),
		(nameof(ImageRenderingProfile.PixelStrides), profile => profile.PixelStrides = IncreaseFirstElement(profile.PixelStrides)),
		(nameof(ImageRenderingProfile.RedColorGain), profile => profile.RedColorGain += 1),
		(nameof(ImageRenderingProfile.RedColorTable), profile =>
		{
			using var colorTable = CreateColorTable();
			profile.RedColorTable = colorTable;
		}),
		(nameof(ImageRenderingProfile.Renderer), profile => profile.Renderer = ImageRenderers.All.First(it => it != profile.Renderer)),
		(nameof(ImageRenderingProfile.RowStrides), profile => profile.RowStrides = IncreaseFirstElement(profile.RowStrides)),
		(nameof(ImageRenderingProfile.SourceInsets), profile => profile.SourceInsets = new PixelInsets(profile.SourceInsets.Left + 1, 0, 0, 0)),
		(nameof(ImageRenderingProfile.TileHeight), profile => profile.TileHeight += 1),
		(nameof(ImageRenderingProfile.TileWidth), profile => profile.TileWidth += 1),
		(nameof(ImageRenderingProfile.UseLinearColorSpace), profile => profile.UseLinearColorSpace = !profile.UseLinearColorSpace),
		(nameof(ImageRenderingProfile.WhiteLevels), profile => profile.WhiteLevels = IncreaseFirstElement(profile.WhiteLevels)),
		(nameof(ImageRenderingProfile.Width), profile => profile.Width += 1),
		(nameof(ImageRenderingProfile.YuvToBgraConverter), profile => profile.YuvToBgraConverter = profile.YuvToBgraConverter == YuvToBgraConverter.BT_709 ? YuvToBgraConverter.BT_601 : YuvToBgraConverter.BT_709),
	];


	/// <summary>
	/// Test for saving and loading base exposure.
	/// </summary>
	[Test]
	public void BaseExposurePersistenceTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			// save a profile which uses base exposure
			await this.InitializeSubSystemsAsync();
			ImageRenderers.TryFindByFormatName("Bayer_Pattern_16", out var renderer);
			var name = $"Base Exposure Test {Guid.NewGuid()}";
			var profile = new ImageRenderingProfile(name, renderer.AsNonNull())
			{
				BaseExposure = 1.37,
			};
			await profile.SaveAsync();
			var fileName = Path.Combine(ImageRenderingProfile.DirectoryPath, $"{WebUtility.UrlEncode(name)}.json");
			Assert.That(File.Exists(fileName), Is.True, "The profile for testing should have been saved.");
			try
			{
				// check that base exposure is loaded
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
					Assert.That(loadedProfile.BaseExposure, Is.EqualTo(1.37), "Base exposure should be loaded.");

				// check that no base exposure is written, so the file stays the same as one written before it was supported
				profile.BaseExposure = 0;
				await profile.SaveAsync();
				var json = await File.ReadAllTextAsync(fileName);
				Assert.That(json, Does.Not.Contain($"\"{nameof(ImageRenderingProfile.BaseExposure)}\":"), "No base exposure should not be written.");
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
					Assert.That(loadedProfile.BaseExposure, Is.Zero, "No base exposure should be loaded.");

				// check that base exposure out of range is coerced when loading
				foreach (var (savedValue, expectedValue) in new[] { (25.0, ImageRenderingOptions.MaxBaseExposure), (-25.0, ImageRenderingOptions.MinBaseExposure) })
				{
					var jsonWithExposure = json.Replace($"\"{nameof(ImageRenderingProfile.Width)}\"", $"\"{nameof(ImageRenderingProfile.BaseExposure)}\": {savedValue}, \"{nameof(ImageRenderingProfile.Width)}\"");
					Assert.That(jsonWithExposure, Does.Contain($"\"{nameof(ImageRenderingProfile.BaseExposure)}\":"), "The base exposure out of range should have been written.");
					await File.WriteAllTextAsync(fileName, jsonWithExposure);
					using var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName);
					Assert.That(loadedProfile.BaseExposure, Is.EqualTo(expectedValue), "Base exposure out of range should be coerced.");
				}
			}
			finally
			{
				profile.Dispose();
				await DeleteFileAsync(fileName);
			}
		});
	}


	/// <summary>
	/// Test for saving and loading compression and tile size of data.
	/// </summary>
	[Test]
	public void CompressionAndTilePersistenceTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			// save a profile which uses compression and tiles
			await this.InitializeSubSystemsAsync();
			ImageRenderers.TryFindByFormatName("Bayer_Pattern_16", out var renderer);
			var name = $"Compression Test {Guid.NewGuid()}";
			var profile = new ImageRenderingProfile(name, renderer.AsNonNull())
			{
				Compression = ImageDataCompression.LosslessJpeg,
				TileWidth = 256,
				TileHeight = 128,
			};
			await profile.SaveAsync();
			var fileName = Path.Combine(ImageRenderingProfile.DirectoryPath, $"{WebUtility.UrlEncode(name)}.json");
			Assert.That(File.Exists(fileName), Is.True, "The profile for testing should have been saved.");
			try
			{
				// check that compression and tile size are loaded
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
				{
					Assert.That(loadedProfile.Compression, Is.EqualTo(ImageDataCompression.LosslessJpeg), "Compression should be loaded.");
					Assert.That(loadedProfile.TileWidth, Is.EqualTo(256), "Tile width should be loaded.");
					Assert.That(loadedProfile.TileHeight, Is.EqualTo(128), "Tile height should be loaded.");
				}

				// check that neither compression nor tiles are written, so the file stays the same as one written before they were supported
				profile.Compression = ImageDataCompression.None;
				profile.TileWidth = 0;
				profile.TileHeight = 0;
				await profile.SaveAsync();
				var json = await File.ReadAllTextAsync(fileName);
				Assert.That(json, Does.Not.Contain($"\"{nameof(ImageRenderingProfile.Compression)}\":"), "No compression should not be written.");
				Assert.That(json, Does.Not.Contain($"\"{nameof(ImageRenderingProfile.TileWidth)}\":"), "No tile width should not be written.");
				Assert.That(json, Does.Not.Contain($"\"{nameof(ImageRenderingProfile.TileHeight)}\":"), "No tile height should not be written.");
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
				{
					Assert.That(loadedProfile.Compression, Is.EqualTo(ImageDataCompression.None), "No compression should be loaded.");
					Assert.That(loadedProfile.TileWidth, Is.Zero, "No tile width should be loaded.");
					Assert.That(loadedProfile.TileHeight, Is.Zero, "No tile height should be loaded.");
				}

				// check that an unknown compression is treated as no compression
				json = json.Replace($"\"{nameof(ImageRenderingProfile.Width)}\"", $"\"{nameof(ImageRenderingProfile.Compression)}\": \"UnknownCompression\", \"{nameof(ImageRenderingProfile.Width)}\"");
				Assert.That(json, Does.Contain("UnknownCompression"), "The unknown compression should have been written.");
				await File.WriteAllTextAsync(fileName, json);
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
					Assert.That(loadedProfile.Compression, Is.EqualTo(ImageDataCompression.None), "Unknown compression should be treated as no compression.");
			}
			finally
			{
				profile.Dispose();
				await DeleteFileAsync(fileName);
			}
		});
	}


	// Create a color table for testing.
	static ColorTable CreateColorTable()
	{
		var colorTable = new ColorTable(4, 8);
		var colors = colorTable.Memory.Span;
		for (var i = 3; i >= 0; --i)
			colors[i] = (uint)(i * 8);
		return colorTable;
	}


	// Create a lens shading map of a 2x2 block of positions whose gains are all the given value.
	static LensShadingMap CreateLensShadingMap(float gain)
	{
		var gains = new float[2 * 2 * 3 * 3];
		Array.Fill(gains, gain);
		return new LensShadingMap(0, 0, 16, 16, 2, 2, 3, 3, 0, 0, 0.5, 0.5, gains);
	}


	// Create profile for testing.
	ImageRenderingProfile CreateProfile() => new(FileFormats.Png, ImageRenderers.All[0]);


	/// <summary>
	/// Test for checking whether profiles define the same parameters to render image or not.
	/// </summary>
	/// <summary>
	/// Test for keeping the identifier of a demosaicing algorithm which cannot be resolved when the profile is loaded.
	/// </summary>
	[Test]
	public void UnresolvedDemosaicingAlgorithmTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			// save a profile which uses a demosaicing algorithm
			await this.InitializeSubSystemsAsync();
			ImageRenderers.TryFindByFormatName("Bayer_Pattern_8", out var renderer);
			var name = $"Unresolved Test {Guid.NewGuid()}";
			var profile = new ImageRenderingProfile(name, renderer.AsNonNull())
			{
				DemosaicingAlgorithm = DemosaicingAlgorithms.Bilinear,
			};
			await profile.SaveAsync();
			var fileName = Path.Combine(ImageRenderingProfile.DirectoryPath, $"{WebUtility.UrlEncode(name)}.json");
			Assert.That(File.Exists(fileName), Is.True, "The profile for testing should have been saved.");
			try
			{
				// replace the algorithm in file by an identifier which no algorithm carries
				const string unresolvedId = "00000000-0000-0000-0000-00000000dead";
				var json = await File.ReadAllTextAsync(fileName);
				json = json.Replace($"\"{DemosaicingAlgorithms.Bilinear.Id}\"", $"\"{unresolvedId}\"");
				Assert.That(json, Does.Contain(unresolvedId), "The identifier of algorithm should have been replaced.");
				await File.WriteAllTextAsync(fileName, json);

				// check that loading falls back to another algorithm, the unknown one cannot be used
				using var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName);
				Assert.That(loadedProfile.DemosaicingAlgorithm, Is.Not.Null);
				Assert.That(loadedProfile.DemosaicingAlgorithm.AsNonNull().Id, Is.Not.EqualTo(unresolvedId));

				// check that saving the profile keeps the identifier which was read instead of the one it fell back to
				await loadedProfile.SaveAsync();
				json = await File.ReadAllTextAsync(fileName);
				Assert.That(json, Does.Contain(unresolvedId), "The identifier which cannot be resolved should be kept in file.");

				// check that selecting another algorithm replaces the identifier, user made a choice of their own
				loadedProfile.DemosaicingAlgorithm = DemosaicingAlgorithms.Bilinear;
				await loadedProfile.SaveAsync();
				json = await File.ReadAllTextAsync(fileName);
				Assert.That(json, Does.Not.Contain(unresolvedId));
				Assert.That(json, Does.Contain(DemosaicingAlgorithms.Bilinear.Id));
			}
			finally
			{
				await DeleteFileAsync(fileName);
			}
		});
	}


	[Test]
	public void HasSameRenderingParametersTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			// initialize the sub-systems required to create profiles
			await this.InitializeSubSystemsAsync();

			// check that every parameter of profile is either compared or excluded from comparison explicitly
			var mutatedParameters = new HashSet<string>(ParameterMutations.Select(it => it.Parameter));
			foreach (var property in typeof(ImageRenderingProfile).GetProperties(BindingFlags.Instance | BindingFlags.Public))
			{
				if (property.SetMethod is null || !property.SetMethod.IsPublic)
					continue;
				var parameter = property.Name;
				Assert.That(mutatedParameters.Contains(parameter) || NonRenderingParameters.Contains(parameter), Is.True,
					$"'{parameter}' is neither compared by {nameof(ImageRenderingProfile.HasSameRenderingParameters)}() nor excluded from comparison explicitly.");
			}

			// check that profiles with same parameters are reported as same
			using var profile = this.CreateProfile();
			using (var sameProfile = this.CreateProfile())
			{
				Assert.That(profile.HasSameRenderingParameters(profile), Is.True, "Profile should be same as itself.");
				Assert.That(profile.HasSameRenderingParameters(sameProfile), Is.True, "Profiles with same parameters should be reported as same.");
			}

			// check that difference of each parameter is reported
			foreach (var (parameter, mutate) in ParameterMutations)
			{
				using var mutatedProfile = this.CreateProfile();
				mutate(mutatedProfile);
				Assert.That(profile.HasSameRenderingParameters(mutatedProfile), Is.False, $"Difference of '{parameter}' should be reported.");
			}
		});
	}


	// Copy the given list and increase its first element.
	static IList<int> IncreaseFirstElement(IList<int> list)
	{
		var array = list.ToArray();
		++array[0];
		return array;
	}
	static IList<uint> IncreaseFirstElement(IList<uint> list)
	{
		var array = list.ToArray();
		++array[0];
		return array;
	}


	/// <summary>
	/// Test for saving and loading lens shading.
	/// </summary>
	[Test]
	public void LensShadingPersistenceTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			// save a profile which carries lens shading
			await this.InitializeSubSystemsAsync();
			ImageRenderers.TryFindByFormatName("Bayer_Pattern_16", out var renderer);
			var name = $"Lens Shading Test {Guid.NewGuid()}";
			var lensShading = CreateLensShadingMap(2.5f);
			var profile = new ImageRenderingProfile(name, renderer.AsNonNull())
			{
				LensShading = lensShading,
			};
			await profile.SaveAsync();
			var fileName = Path.Combine(ImageRenderingProfile.DirectoryPath, $"{WebUtility.UrlEncode(name)}.json");
			Assert.That(File.Exists(fileName), Is.True, "The profile for testing should have been saved.");
			try
			{
				// check that lens shading is loaded
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
				{
					Assert.That(loadedProfile.LensShading, Is.EqualTo(lensShading), "Lens shading should be loaded.");
					Assert.That(loadedProfile.HasSameRenderingParameters(profile), Is.True, "Loaded profile should have same rendering parameters.");
				}

				// check that no lens shading is written, so the file stays the same as one written before it was supported
				profile.LensShading = null;
				await profile.SaveAsync();
				var json = await File.ReadAllTextAsync(fileName);
				Assert.That(json, Does.Not.Contain($"\"{nameof(ImageRenderingProfile.LensShading)}\":"), "No lens shading should not be written.");
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
					Assert.That(loadedProfile.LensShading, Is.Null, "No lens shading should be loaded.");

				// check that invalid lens shading is ignored when loading
				var jsonWithInvalidLensShading = json.Replace($"\"{nameof(ImageRenderingProfile.Width)}\"", $"\"{nameof(ImageRenderingProfile.LensShading)}\": {{ \"PitchX\": 2 }}, \"{nameof(ImageRenderingProfile.Width)}\"");
				await File.WriteAllTextAsync(fileName, jsonWithInvalidLensShading);
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
				{
					Assert.That(loadedProfile.LensShading, Is.Null, "Invalid lens shading should be ignored.");
					Assert.That(loadedProfile.Width, Is.EqualTo(profile.Width), "Other parameters should still be loaded.");
				}
			}
			finally
			{
				profile.Dispose();
				await DeleteFileAsync(fileName);
			}
		});
	}


	/// <summary>
	/// Test for saving and loading the insets of source image.
	/// </summary>
	[Test]
	public void SourceInsetsPersistenceTest()
	{
		this.TestOnApplicationThread(async () =>
		{
			// save a profile which carries insets of source image
			await this.InitializeSubSystemsAsync();
			ImageRenderers.TryFindByFormatName("Bayer_Pattern_16", out var renderer);
			var name = $"Source Insets Test {Guid.NewGuid()}";
			var sourceInsets = new PixelInsets(144, 114, 17, 15);
			var profile = new ImageRenderingProfile(name, renderer.AsNonNull())
			{
				SourceInsets = sourceInsets,
			};
			await profile.SaveAsync();
			var fileName = Path.Combine(ImageRenderingProfile.DirectoryPath, $"{WebUtility.UrlEncode(name)}.json");
			Assert.That(File.Exists(fileName), Is.True, "The profile for testing should have been saved.");
			try
			{
				// check that insets are loaded
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
				{
					Assert.That(loadedProfile.SourceInsets, Is.EqualTo(sourceInsets), "Insets of source image should be loaded.");
					Assert.That(loadedProfile.HasSameRenderingParameters(profile), Is.True, "Loaded profile should have same rendering parameters.");
				}

				// check that empty insets are not written, so the file stays the same as one written before they were supported
				profile.SourceInsets = PixelInsets.Empty;
				await profile.SaveAsync();
				var json = await File.ReadAllTextAsync(fileName);
				Assert.That(json, Does.Not.Contain($"\"{nameof(ImageRenderingProfile.SourceInsets)}\":"), "Empty insets should not be written.");
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
					Assert.That(loadedProfile.SourceInsets.IsEmpty, Is.True, "Empty insets should be loaded.");

				// check that insets of an edge which is not written are taken as 0, and invalid insets are ignored
				var jsonWithPartialInsets = json.Replace($"\"{nameof(ImageRenderingProfile.Width)}\"", $"\"{nameof(ImageRenderingProfile.SourceInsets)}\": {{ \"Left\": 8, \"Bottom\": -2 }}, \"{nameof(ImageRenderingProfile.Width)}\"");
				await File.WriteAllTextAsync(fileName, jsonWithPartialInsets);
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
					Assert.That(loadedProfile.SourceInsets, Is.EqualTo(new PixelInsets(8, 0, 0, -2)), "Undefined insets should be 0 and negative ones should be kept.");
				var jsonWithInvalidInsets = json.Replace($"\"{nameof(ImageRenderingProfile.Width)}\"", $"\"{nameof(ImageRenderingProfile.SourceInsets)}\": 8, \"{nameof(ImageRenderingProfile.Width)}\"");
				await File.WriteAllTextAsync(fileName, jsonWithInvalidInsets);
				using (var loadedProfile = await ImageRenderingProfile.LoadAsync(fileName))
				{
					Assert.That(loadedProfile.SourceInsets.IsEmpty, Is.True, "Invalid insets should be ignored.");
					Assert.That(loadedProfile.Width, Is.EqualTo(profile.Width), "Other parameters should still be loaded.");
				}
			}
			finally
			{
				profile.Dispose();
				await DeleteFileAsync(fileName);
			}
		});
	}
}
