using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="ImageRenderers"/>.
/// </summary>
[TestFixture]
class ImageRenderersTests : BaseTests
{
	// Implementation of IImageRenderer which renders nothing, only used to test the registry.
	class DummyImageRenderer(ImageFormat format) : BaseImageRenderer(format)
	{
		// Change the format supported by this renderer.
		public void ChangeFormatForTest(ImageFormat format) =>
			this.ChangeFormat(format);

		/// <inheritdoc/>
		public override IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) =>
			[ new(1, width) ];

		/// <inheritdoc/>
		public override int EvaluatePixelCount(IImageDataSource source) => 0;

		/// <inheritdoc/>
		public override long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions) => 0;

		/// <inheritdoc/>
		protected override ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken) =>
			new();
	}


	// Create a user-defined format with the given identifier and display name.
	static ImageFormat CreateUserDefinedFormat(string id, string displayName) =>
		new(id, displayName, false, [ new ImagePlaneDescriptor(1) ]);


	/// <summary>
	/// Test for adding and removing renderer at runtime.
	/// </summary>
	[Test]
	public void AddingAndRemovingRendererTest()
	{
		this.TestOnApplicationThread(() =>
		{
			// prepare
			var id = Guid.NewGuid().ToString();
			var format = CreateUserDefinedFormat(id, "Adding and Removing");
			var renderer = new DummyImageRenderer(format);
			var builtInRendererCount = ImageRenderers.All.Count;
			var collectionChangedCount = 0;
			void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ++collectionChangedCount;
			((INotifyCollectionChanged)ImageRenderers.All).CollectionChanged += OnCollectionChanged;

			// add renderer and check that the list reports the change
			try
			{
				ImageRenderers.Add(renderer);
				Assert.That(ImageRenderers.All, Does.Contain(renderer));
				Assert.That(ImageRenderers.All.Count, Is.EqualTo(builtInRendererCount + 1));
				Assert.That(collectionChangedCount, Is.EqualTo(1));

				// check that the renderer can be resolved by the name of its format
				Assert.That(ImageRenderers.TryFindByFormatName(format.Name, out var foundRenderer), Is.True);
				Assert.That(foundRenderer, Is.SameAs(renderer));
				Assert.That(ImageFormat.TryGetByName(format.Name, out var foundFormat), Is.True);
				Assert.That(foundFormat, Is.SameAs(format));
			}
			finally
			{
				ImageRenderers.Remove(renderer);
				ImageFormat.Unregister(format);
				((INotifyCollectionChanged)ImageRenderers.All).CollectionChanged -= OnCollectionChanged;
			}

			// check that removing the renderer leaves no registration behind
			Assert.That(ImageRenderers.All, Does.Not.Contain(renderer));
			Assert.That(ImageRenderers.All.Count, Is.EqualTo(builtInRendererCount));
			Assert.That(collectionChangedCount, Is.EqualTo(2));
			Assert.That(ImageFormat.TryGetByName(format.Name, out _), Is.False);
			Assert.That(ImageRenderers.TryFindByFormatName(format.Name, out _), Is.False);
		});
	}


	/// <summary>
	/// Test for replacing the format of renderer after the user edited it.
	/// </summary>
	[Test]
	public void ChangingFormatTest()
	{
		this.TestOnApplicationThread(() =>
		{
			// prepare
			var id = Guid.NewGuid().ToString();
			var format = CreateUserDefinedFormat(id, "Before Renaming");
			var renderer = new DummyImageRenderer(format);
			var changedPropertyNames = new List<string?>();
			void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => changedPropertyNames.Add(e.PropertyName);
			renderer.PropertyChanged += OnPropertyChanged;
			ImageRenderers.Add(renderer);
			try
			{
				// rename the format, the caller unregisters the previous format to release its identifier before building the new one carrying the same identifier
				Assert.That(renderer.IsBuiltIn, Is.False);
				ImageFormat.Unregister(format);
				var renamedFormat = CreateUserDefinedFormat(id, "After Renaming");
				renderer.ChangeFormatForTest(renamedFormat);

				// check that the change is reported
				Assert.That(renderer.Format, Is.SameAs(renamedFormat));
				Assert.That(renderer.Format.DisplayName, Is.EqualTo("After Renaming"));
				Assert.That(changedPropertyNames, Is.EqualTo(new[] { nameof(IImageRenderer.Format) }));

				// check that the identifier is kept, so the new format took over the registration of the previous one
				Assert.That(renderer.Format.Name, Is.EqualTo(id));
				Assert.That(ImageFormat.TryGetByName(id, out var foundFormat), Is.True);
				Assert.That(foundFormat, Is.SameAs(renamedFormat));

				// check that the name persisted before renaming still resolves to the renderer, it is the identifier which never changes
				Assert.That(ImageRenderers.TryFindByFormatName(id, out var foundRenderer), Is.True);
				Assert.That(foundRenderer, Is.SameAs(renderer));
			}
			finally
			{
				renderer.PropertyChanged -= OnPropertyChanged;
				ImageRenderers.Remove(renderer);
				ImageFormat.Unregister(renderer.Format);
			}
		});
	}


	/// <summary>
	/// Test for rejecting the change of format of a built-in renderer.
	/// </summary>
	[Test]
	public void ChangingFormatOfBuiltInRendererTest()
	{
		this.TestOnApplicationThread(() =>
		{
			// prepare a renderer which supports a built-in format, such a format cannot be unregistered so its unique name avoids collision across runs
			var builtInFormat = new ImageFormat(ImageFormatCategory.Luminance, $"Built-In {Guid.NewGuid()}", [ new ImagePlaneDescriptor(1) ]);
			var renderer = new DummyImageRenderer(builtInFormat);

			// check that the renderer is reported as built-in and its format cannot be changed
			Assert.That(renderer.IsBuiltIn, Is.True);
			var userDefinedFormat = CreateUserDefinedFormat(Guid.NewGuid().ToString(), "Rejected");
			try
			{
				Assert.Throws<InvalidOperationException>(() => renderer.ChangeFormatForTest(userDefinedFormat));
				Assert.That(renderer.Format, Is.SameAs(builtInFormat));
			}
			finally
			{
				ImageFormat.Unregister(userDefinedFormat);
			}
		});
	}
}
