using Carina.PixelViewer.Media;
using NUnit.Framework;
using System;
using System.IO;
using System.Text.Json;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="LensShadingMap"/>.
/// </summary>
[TestFixture]
class LensShadingMapTests : BaseTests
{
	/// <summary>
	/// Test for rejecting invalid parameters when creating a map.
	/// </summary>
	[Test]
	public void ArgumentCheckingTest()
	{
		// valid parameters, which are a 2x2 block of positions with a 3x2 grid each
		var gains = CreateGains(2, 2, 3, 2);
		Assert.That(() => new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, gains), Throws.Nothing, "Valid parameters should be accepted.");

		// invalid area
		Assert.That(() => new LensShadingMap(-1, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, gains), Throws.InstanceOf<ArgumentException>(), "Negative left should be rejected.");
		Assert.That(() => new LensShadingMap(0, 0, 0, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, gains), Throws.InstanceOf<ArgumentException>(), "Empty width should be rejected.");
		Assert.That(() => new LensShadingMap(0, 8, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, gains), Throws.InstanceOf<ArgumentException>(), "Empty height should be rejected.");

		// invalid grid
		Assert.That(() => new LensShadingMap(0, 0, 16, 8, 0, 2, 3, 2, 0, 0, 0.5, 1, gains), Throws.InstanceOf<ArgumentException>(), "Zero pitch should be rejected.");
		Assert.That(() => new LensShadingMap(0, 0, 16, 8, LensShadingMap.MaxPitch + 1, 1, 1, 1, 0, 0, 1, 1, new float[LensShadingMap.MaxPitch + 1]), Throws.InstanceOf<ArgumentException>(), "Pitch beyond the maximum should be rejected.");
		Assert.That(() => new LensShadingMap(0, 0, 16, 8, 1, 1, LensShadingMap.MaxPointCount + 1, 1, 0, 0, 1, 1, new float[LensShadingMap.MaxPointCount + 1]), Throws.InstanceOf<ArgumentException>(), "Number of grid points beyond the maximum should be rejected.");
		Assert.That(() => new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, double.NaN, 0, 0.5, 1, gains), Throws.InstanceOf<ArgumentException>(), "Origin which is not finite should be rejected.");
		Assert.That(() => new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0, 1, gains), Throws.InstanceOf<ArgumentException>(), "Zero spacing should be rejected.");
		Assert.That(() => new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, double.PositiveInfinity, gains), Throws.InstanceOf<ArgumentException>(), "Spacing which is not finite should be rejected.");

		// invalid gains
		Assert.That(() => new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, gains.AsSpan(1)), Throws.InstanceOf<ArgumentException>(), "Gains which do not fill the grids should be rejected.");
		foreach (var invalidGain in new[] { float.NaN, float.PositiveInfinity, -0.5f })
		{
			var invalidGains = (float[])gains.Clone();
			invalidGains[5] = invalidGain;
			Assert.That(() => new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, invalidGains), Throws.InstanceOf<ArgumentException>(), $"Gain {invalidGain} should be rejected.");
		}
	}


	// Create gains whose values tell the position and the grid point they belong to.
	static float[] CreateGains(int pitchX, int pitchY, int pointCountX, int pointCountY)
	{
		var gains = new float[pitchX * pitchY * pointCountX * pointCountY];
		var index = 0;
		for (var positionY = 0; positionY < pitchY; ++positionY)
		{
			for (var positionX = 0; positionX < pitchX; ++positionX)
			{
				for (var pointY = 0; pointY < pointCountY; ++pointY)
				{
					for (var pointX = 0; pointX < pointCountX; ++pointX)
						gains[index++] = GetExpectedGain(positionX, positionY, pointX, pointY);
				}
			}
		}
		return gains;
	}


	/// <summary>
	/// Test for comparing maps.
	/// </summary>
	[Test]
	public void EqualityTest()
	{
		// maps with same parameters are equal
		var gains = CreateGains(2, 2, 3, 2);
		var map = new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, gains);
		var sameMap = new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, gains);
		Assert.That(map.Equals(sameMap), Is.True, "Maps with same parameters should be equal.");
		Assert.That(map.GetHashCode(), Is.EqualTo(sameMap.GetHashCode()), "Maps with same parameters should have same hash code.");
		Assert.That(Equals(map, null), Is.False, "Map should not be equal to null.");

		// difference of each parameter is reported
		var differentGains = (float[])gains.Clone();
		differentGains[^1] += 0.5f;
		var differentMaps = new (string Parameter, LensShadingMap Map)[]
		{
			("area", new LensShadingMap(2, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, gains)),
			("pitch", new LensShadingMap(0, 0, 16, 8, 4, 1, 3, 2, 0, 0, 0.5, 1, gains)),
			("grid points", new LensShadingMap(0, 0, 16, 8, 2, 2, 2, 3, 0, 0, 0.5, 1, gains)),
			("origin", new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0.1, 0, 0.5, 1, gains)),
			("spacing", new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 0.9, gains)),
			("gains", new LensShadingMap(0, 0, 16, 8, 2, 2, 3, 2, 0, 0, 0.5, 1, differentGains)),
		};
		foreach (var (parameter, differentMap) in differentMaps)
			Assert.That(map.Equals(differentMap), Is.False, $"Difference of {parameter} should be reported.");
	}


	/// <summary>
	/// Test for getting gains of grid points.
	/// </summary>
	[Test]
	public void GainsTest()
	{
		// check the gain of every grid point of every position
		const int pitchX = 2;
		const int pitchY = 4;
		const int pointCountX = 5;
		const int pointCountY = 3;
		var gains = CreateGains(pitchX, pitchY, pointCountX, pointCountY);
		var map = new LensShadingMap(0, 0, 64, 32, pitchX, pitchY, pointCountX, pointCountY, 0, 0, 0.25, 0.5, gains);
		for (var positionY = 0; positionY < pitchY; ++positionY)
		{
			for (var positionX = 0; positionX < pitchX; ++positionX)
			{
				for (var pointY = 0; pointY < pointCountY; ++pointY)
				{
					for (var pointX = 0; pointX < pointCountX; ++pointX)
						Assert.That(map.GetGain(positionX, positionY, pointX, pointY), Is.EqualTo(GetExpectedGain(positionX, positionY, pointX, pointY)), $"Unexpected gain of point ({pointX}, {pointY}) at position ({positionX}, {positionY}).");
				}
			}
		}

		// the gains are copied, so changing the source of gains does not change the map
		gains[0] = 100;
		Assert.That(map.GetGain(0, 0, 0, 0), Is.EqualTo(GetExpectedGain(0, 0, 0, 0)), "Map should not be changed by the source of gains.");
		Assert.That(map.Gains.Length, Is.EqualTo(gains.Length), "All gains should be kept.");

		// grid points out of range are rejected
		Assert.That(() => map.GetGain(pitchX, 0, 0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(() => map.GetGain(0, pitchY, 0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(() => map.GetGain(0, 0, pointCountX, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(() => map.GetGain(0, 0, 0, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
	}


	// Get the gain which CreateGains() generates for given position and grid point.
	static float GetExpectedGain(int positionX, int positionY, int pointX, int pointY) =>
		1 + positionY * 0.5f + positionX * 0.25f + pointY * 0.0625f + pointX * 0.015625f;


	/// <summary>
	/// Test for writing the map to JSON and loading it back.
	/// </summary>
	[Test]
	public void JsonSerializationTest()
	{
		// load the map back and check that it is the same as the original one
		var map = new LensShadingMap(1, 2, 4097, 3074, 2, 2, 40, 30, 0.01, 0.02, 1.0 / 39, 1.0 / 29, CreateGains(2, 2, 40, 30));
		var json = WriteToJson(map);
		Assert.That(LensShadingMap.TryLoadFromJson(JsonDocument.Parse(json).RootElement, out var loadedMap), Is.True, "Map should be loaded.");
		Assert.That(loadedMap, Is.EqualTo(map), "Loaded map should be the same as the original one.");

		// a map whose gains cannot be decoded is not loaded
		var gainsJson = $"\"{Convert.ToBase64String(new byte[] { 1, 2, 3 })}\"";
		Assert.That(LensShadingMap.TryLoadFromJson(JsonDocument.Parse(ReplaceGains(json, gainsJson)).RootElement, out _), Is.False, "Map with gains which cannot be decoded should not be loaded.");
		Assert.That(LensShadingMap.TryLoadFromJson(JsonDocument.Parse(ReplaceGains(json, "\"Not Base64\"")).RootElement, out _), Is.False, "Map with gains which are not Base64 should not be loaded.");

		// a map whose gains do not fill the grids is not loaded
		var smallerMapJson = WriteToJson(new LensShadingMap(0, 0, 16, 8, 1, 1, 2, 2, 0, 0, 1, 1, CreateGains(1, 1, 2, 2)));
		Assert.That(LensShadingMap.TryLoadFromJson(JsonDocument.Parse(ReplaceGains(json, GetGainsJson(smallerMapJson))).RootElement, out _), Is.False, "Map whose gains do not fill the grids should not be loaded.");

		// a map which misses a parameter or carries an invalid one is not loaded
		foreach (var (parameter, replacement) in new[] { (nameof(LensShadingMap.PitchX), "\"PitchXX\""), (nameof(LensShadingMap.SpacingY), "\"SpacingY\": -1, \"Unused\"") })
		{
			var invalidJson = json.Replace($"\"{parameter}\"", replacement);
			Assert.That(LensShadingMap.TryLoadFromJson(JsonDocument.Parse(invalidJson).RootElement, out _), Is.False, $"Map with missing or invalid {parameter} should not be loaded.");
		}
		Assert.That(LensShadingMap.TryLoadFromJson(JsonDocument.Parse("[]").RootElement, out _), Is.False, "Map should not be loaded from value which is not an object.");

		// get the JSON value of gains
		static string GetGainsJson(string json)
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.GetProperty("Gains").GetRawText();
		}

		// replace the gains in JSON with the given JSON value
		static string ReplaceGains(string json, string gainsJson) =>
			json.Replace(GetGainsJson(json), gainsJson);
	}


	// Write the given map as a JSON string.
	static string WriteToJson(LensShadingMap map)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
			map.WriteToJson(writer);
		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}
}
