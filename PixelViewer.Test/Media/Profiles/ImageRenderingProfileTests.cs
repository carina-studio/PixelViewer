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
		(nameof(ImageRenderingProfile.UseLinearColorSpace), profile => profile.UseLinearColorSpace = !profile.UseLinearColorSpace),
		(nameof(ImageRenderingProfile.WhiteLevels), profile => profile.WhiteLevels = IncreaseFirstElement(profile.WhiteLevels)),
		(nameof(ImageRenderingProfile.Width), profile => profile.Width += 1),
		(nameof(ImageRenderingProfile.YuvToBgraConverter), profile => profile.YuvToBgraConverter = profile.YuvToBgraConverter == YuvToBgraConverter.BT_709 ? YuvToBgraConverter.BT_601 : YuvToBgraConverter.BT_709),
	];


	// Create a color table for testing.
	static ColorTable CreateColorTable()
	{
		var colorTable = new ColorTable(4, 8);
		var colors = colorTable.Memory.Span;
		for (var i = 3; i >= 0; --i)
			colors[i] = (uint)(i * 8);
		return colorTable;
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
}
