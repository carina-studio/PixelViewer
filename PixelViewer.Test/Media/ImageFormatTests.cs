using Carina.PixelViewer.Media;
using NUnit.Framework;
using System;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="ImageFormat"/>.
/// </summary>
[TestFixture]
class ImageFormatTests
{
	// Create a user-defined format with the given identifier and display name.
	static ImageFormat CreateUserDefinedFormat(string id, string displayName) =>
		new(id, displayName, false, [ new ImagePlaneDescriptor(1) ]);


	/// <summary>
	/// Test for the name of format for displaying to user.
	/// </summary>
	[Test]
	public void DisplayNameTest()
	{
		// check that the name given by user is returned for a user-defined format, whose name is the identifier
		var id = Guid.NewGuid().ToString();
		var format = CreateUserDefinedFormat(id, "My Sensor Raw");
		try
		{
			Assert.That(format.Name, Is.EqualTo(id));
			Assert.That(format.DisplayName, Is.EqualTo("My Sensor Raw"));
		}
		finally
		{
			ImageFormat.Unregister(format);
		}

		// check that a built-in format with no string resource falls back to its name, no application is available in this fixture
		var builtInFormat = new ImageFormat(ImageFormatCategory.Luminance, $"Built-In {id}", [ new ImagePlaneDescriptor(1) ]);
		Assert.That(builtInFormat.DisplayName, Is.EqualTo(builtInFormat.Name));
	}


	/// <summary>
	/// Test for registering a format which has the same name as an existing one.
	/// </summary>
	[Test]
	public void RegisteringDuplicateNameTest()
	{
		// check that a user-defined format is rejected when its identifier is still registered, editing one needs to unregister the previous instance first
		var id = Guid.NewGuid().ToString();
		var format = CreateUserDefinedFormat(id, "Registering");
		try
		{
			Assert.Throws<ArgumentException>(() => _ = CreateUserDefinedFormat(id, "Edited"));
			Assert.That(ImageFormat.TryGetByName(id, out var foundFormat), Is.True);
			Assert.That(foundFormat, Is.SameAs(format));
		}
		finally
		{
			ImageFormat.Unregister(format);
		}

		// check that the identifier can be reused once the previous instance has been unregistered
		var editedFormat = CreateUserDefinedFormat(id, "Edited");
		try
		{
			Assert.That(ImageFormat.TryGetByName(id, out var foundFormat), Is.True);
			Assert.That(foundFormat, Is.SameAs(editedFormat));
			Assert.That(editedFormat.DisplayName, Is.EqualTo("Edited"));
		}
		finally
		{
			ImageFormat.Unregister(editedFormat);
		}
	}


	/// <summary>
	/// Test for unregistering user-defined format.
	/// </summary>
	[Test]
	public void UnregisteringTest()
	{
		// check that the format can be found by its name after registration
		var id = Guid.NewGuid().ToString();
		var format = CreateUserDefinedFormat(id, "Unregistering");
		Assert.That(ImageFormat.TryGetByName(id, out var foundFormat), Is.True);
		Assert.That(foundFormat, Is.SameAs(format));

		// check that no registration is left behind after unregistration
		Assert.That(ImageFormat.Unregister(format), Is.True);
		Assert.That(ImageFormat.TryGetByName(id, out _), Is.False);

		// check that unregistering a format which has already been unregistered reports no removal
		Assert.That(ImageFormat.Unregister(format), Is.False);
	}


	/// <summary>
	/// Test for unregistering built-in format.
	/// </summary>
	[Test]
	public void UnregisteringBuiltInFormatTest()
	{
		// check that a built-in format is rejected and stays registered along with its keyword
		var id = Guid.NewGuid().ToString();
		var keyword = $"kw{id[..8]}";
		var format = new ImageFormat(ImageFormatCategory.Luminance, $"Built-In {id}", [ new ImagePlaneDescriptor(1) ], [ keyword ]);
		Assert.That(ImageFormat.Unregister(format), Is.False);
		Assert.That(ImageFormat.TryGetByName(format.Name, out var foundFormat), Is.True);
		Assert.That(foundFormat, Is.SameAs(format));
		Assert.That(ImageFormat.TryGetByFileName($"image_{keyword}.raw", out foundFormat), Is.True);
		Assert.That(foundFormat, Is.SameAs(format));
	}
}
