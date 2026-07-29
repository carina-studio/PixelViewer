using CarinaStudio.AppSuite;
using System.Collections.Generic;

namespace Carina.PixelViewer.Test;

/// <summary>
/// Application for testing. <see cref="MockAppSuiteApplication"/> has no string resource, so the strings
/// needed by the code under testing are provided here.
/// </summary>
class TestApplication : MockAppSuiteApplication
{
	// Static fields.
	static readonly IDictionary<string, string> Strings = new Dictionary<string, string>
	{
		{ "Session.EmptyTitle", "Empty Tab" },
		{ "Session.FrameSequence", "Frame Sequence ({0})" },
		{ "Session.MultipleFiles", "{0} Files" },
	};


	/// <inheritdoc/>
	public override string? GetString(string key, string? defaultValue = null) =>
		Strings.TryGetValue(key, out var str) ? str : base.GetString(key, defaultValue);
}
