using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.ViewModels;

/// <summary>
/// Tests of interrupting the image rendering of <see cref="Session"/> by requesting rendering again before the current rendering completes.
/// </summary>
[TestFixture]
class SessionRenderingInterruptionTests : BaseTests
{
	// Implementation of IImageRenderer which parks in RenderAsync until the gate is opened by the test, so that a rendering can be interrupted deterministically.
	// The interface is implemented directly instead of extending BaseImageRenderer so that parking is asynchronous, the rendering thread pool of BaseImageRenderer
	// has 2 threads only and a blocked thread would be shared with the renderings of other tests.
	class GateImageRenderer(ImageFormat format) : IImageRenderer
	{
		// Fields.
		int concurrentRenderingCount;
		readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource renderingCancelledSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource renderingCompletedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource renderingStartedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

		// Number of renderings which have been cancelled.
		public int CancelledRenderingCount { get; private set; }

		// Number of renderings which have been completed without cancellation.
		public int CompletedRenderingCount { get; private set; }

		/// <inheritdoc/>
		public IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height) => [ new(1, width) ];

		/// <inheritdoc/>
		public int EvaluatePixelCount(IImageDataSource source) => (int)source.Size;

		/// <inheritdoc/>
		public long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions) =>
			width <= 0 || height <= 0 ? 0 : (long)Math.Max(width, planeOptions[0].RowStride) * height;

		/// <inheritdoc/>
		public ImageFormat Format { get; } = format;

		/// <inheritdoc/>
		public bool IsBuiltIn => true;
		public bool IsColorTableSupported => false;

		// Height of the image rendered by the latest completed rendering.
		public int LastRenderedHeight { get; private set; }

		// Width of the image rendered by the latest completed rendering.
		public int LastRenderedWidth { get; private set; }

		// Maximum number of renderings which were performed at the same time.
		public int MaxConcurrentRenderingCount { get; private set; }

		// Let the renderings which are waiting for the gate complete. Renderings requested afterwards are completed without parking.
		public void OpenGate() =>
			this.gate.TrySetResult();

		/// <inheritdoc/>
		/// <remarks>The format of this renderer never changes, so the event is never raised.</remarks>
		public event PropertyChangedEventHandler? PropertyChanged
		{
			add { }
			remove { }
		}

		/// <inheritdoc/>
		public async Task<ImageRenderingResult> RenderAsync(IImageDataSource source, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// report the cancellation of this rendering, which lets the test open the gate only after the interruption has actually reached the renderer
			using var cancellationRegistration = cancellationToken.Register(() => this.renderingCancelledSource.TrySetResult());

			// report that a rendering has started and keep track of overlapped renderings
			++this.StartedRenderingCount;
			++this.concurrentRenderingCount;
			if (this.concurrentRenderingCount > this.MaxConcurrentRenderingCount)
				this.MaxConcurrentRenderingCount = this.concurrentRenderingCount;
			this.renderingStartedSource.TrySetResult();
			try
			{
				// park until the gate is opened, pixels are not written because the session reports the allocated buffer no matter what it contains
				// cancellation is deliberately not observed while parking, a real renderer keeps working until it reaches its next cancellation check so
				// its rendering is still in progress for a while after it has been cancelled, which is what the interruption tests need to reproduce
				await this.gate.Task;

				// give up if the rendering has been cancelled while parking
				if (cancellationToken.IsCancellationRequested)
				{
					++this.CancelledRenderingCount;
					cancellationToken.ThrowIfCancellationRequested();
				}

				// report the parameters of the completed rendering so that the test can tell which rendering produced the image
				this.LastRenderedWidth = bitmapBuffer.Width;
				this.LastRenderedHeight = bitmapBuffer.Height;
				++this.CompletedRenderingCount;
				this.renderingCompletedSource.TrySetResult();
				return new ImageRenderingResult();
			}
			finally
			{
				--this.concurrentRenderingCount;
			}
		}

		// Task which completes when a rendering is cancelled after the latest Reset().
		public Task RenderingCancelledTask => this.renderingCancelledSource.Task;

		// Task which completes when a rendering completes without cancellation after the latest Reset().
		public Task RenderingCompletedTask => this.renderingCompletedSource.Task;

		// Task which completes when a rendering starts after the latest Reset().
		public Task RenderingStartedTask => this.renderingStartedSource.Task;

		// Reset the tasks so that they wait for the renderings requested afterwards.
		public void Reset()
		{
			this.renderingCancelledSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
			this.renderingCompletedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
			this.renderingStartedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		/// <inheritdoc/>
		public Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
			Task.FromResult(BitmapFormat.Bgra32);

		// Number of renderings which have been started.
		public int StartedRenderingCount { get; private set; }
	}


	// Dimensions of the image used by the test of continuous filtering, they are large enough for a filtering pass to overlap the next parameter change.
	const int FilteringImageSize = 1024;
	// Number of times the filtering parameter is changed by the test of continuous filtering, which is what dragging a slider does.
	// The changes need to span several filtering passes, the requests of filtering are coalesced by RenderImageDelay.
	const int FilteringParameterChangeCount = 40;
	// Interval between the changes of the filtering parameter made by the test of continuous filtering.
	const int FilteringParameterChangeInterval = 100;
	// Size of the generated source file, it is large enough for the dimensions evaluated for the gate renderer to be changeable by tests.
	const int SourceFileSize = 1 << 16;


	// Fields.
	GateImageRenderer? gateRenderer;
	bool? restoredEvaluateImageRendererByFileName;
	Session? session;
	IDisposable? sessionActivationToken;
	readonly List<string> sourceFilePaths = [];


	/// <summary>
	/// Test for activating the session while the hibernation requested by the previous deactivation has not completed yet.
	/// </summary>
	/// <remarks>
	/// The rendering is parked before any image has been rendered, so deactivating the session requests hibernation while the rendering is still in progress.
	/// Activating the session in the same call stack makes sure that the activation happens before the hibernation completes, the session should render the
	/// image for the activation instead of being left with no image and nothing scheduled to render one.
	/// </remarks>
	[Test]
	public void ActivatingDuringPendingHibernationTest()
	{
		var session = this.session ?? throw new AssertionException("No session for testing.");
		var renderer = this.gateRenderer ?? throw new AssertionException("No renderer for testing.");
		this.TestOnApplicationThread(async () =>
		{
			// select the gate renderer before opening the source so that the first rendering of the source parks and no image is ever rendered
			session.ImageRenderer = renderer;
			var filePath = this.GenerateSourceFile();
			session.OpenSourceFileCommand.Execute(filePath);
			Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 5000), Is.True, "Cannot open source file.");
			Assert.That(session.ImageRenderer, Is.SameAs(renderer), "Opening the source file should keep the selected renderer.");
			Assert.That(await WaitForTaskAsync(renderer.RenderingStartedTask, 5000), Is.True, "Rendering by the gate renderer should have started.");
			Assert.That(session.HasRenderedImage, Is.False, "No image should have been rendered before the parked rendering completes.");

			// deactivate and activate the session without leaving the call stack, so the activation is guaranteed to happen while the hibernation is pending
			renderer.Reset();
			this.sessionActivationToken = this.sessionActivationToken.DisposeAndReturnNull();
			this.sessionActivationToken = session.Activate();

			// let the parked rendering complete only after the hibernation has cancelled it, so that the hibernation is really pending while the session is activated
			Assert.That(await WaitForTaskAsync(renderer.RenderingCancelledTask, 5000), Is.True, "Hibernation should cancel the rendering which is in progress.");
			renderer.OpenGate();

			// the session should be rendering for the activation instead of staying blank
			Assert.That(await WaitForTaskAsync(renderer.RenderingCompletedTask, 5000), Is.True, "Rendering should be performed for the activation which interrupted the hibernation.");
			Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), false, 5000), Is.True, "Rendering should complete.");
			Assert.That(session.IsHibernated, Is.False, "Session should not be hibernated after being activated.");
			Assert.That(session.RenderedImage, Is.Not.Null, "Rendered image should be reported after the session has been activated.");
			Assert.That(session.HasRenderingError, Is.False, "Rendering should not fail.");
			Assert.That(renderer.MaxConcurrentRenderingCount, Is.EqualTo(1), "Renderings should never be performed at the same time.");
		});
	}


	/// <summary>
	/// Test for keeping the reported image stable while the filtering parameters are changed continuously, which is what dragging a slider does.
	/// </summary>
	/// <remarks>The image being cleared in the middle of the flow makes it flicker, so the reported image is expected to be replaced by the next one instead of being cleared first.</remarks>
	[Test]
	public void ContinuousFilteringStabilityTest()
	{
		var session = this.session ?? throw new AssertionException("No session for testing.");
		var renderer = this.gateRenderer ?? throw new AssertionException("No renderer for testing.");
		this.TestOnApplicationThread(async () =>
		{
			// open source file and render an image with the gate renderer, the gate is opened so that every rendering completes
			renderer.OpenGate();
			var filePath = this.GenerateSourceFile(FilteringImageSize * FilteringImageSize);
			session.OpenSourceFileCommand.Execute(filePath);
			Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 5000), Is.True, "Cannot open source file.");
			Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), false, 10000), Is.True, "Rendering for the opened source file should complete.");
			session.ImageRenderer = renderer;
			session.ImageWidth = FilteringImageSize;
			session.ImageHeight = FilteringImageSize;
			Assert.That(await WaitForConditionAsync(() => session.RenderedImage is not null && !session.IsRenderingImage, 10000), Is.True, "Image should be rendered before the filtering parameters are changed.");

			// count how many times the reported image is cleared while the filtering parameter keeps being changed
			var clearedCount = 0;
			var filteringCount = 0;
			var handler = new PropertyChangedEventHandler((_, e) =>
			{
				if (e.PropertyName == nameof(Session.RenderedImage) && session.RenderedImage is null)
					++clearedCount;
				else if (e.PropertyName == nameof(Session.IsFilteringRenderedImage) && session.IsFilteringRenderedImage)
					++filteringCount;
			});
			session.PropertyChanged += handler;
			try
			{
				// change the filtering parameter continuously, the value stays away from zero so that filtering keeps being needed and
				// each change interrupts the filtering requested by the previous one
				for (var i = 1; i <= FilteringParameterChangeCount; ++i)
				{
					session.BrightnessAdjustment = 0.1 + (0.9 * i / FilteringParameterChangeCount);
					await Task.Delay(FilteringParameterChangeInterval, CancellationToken.None);
				}

				// wait for the filtering of the final parameter to complete
				Assert.That(await WaitForConditionAsync(() => session.RenderedImage is not null && !session.IsFilteringRenderedImage && !session.IsRenderingImage, 10000), Is.True, "Filtered image should be reported after the changes of the filtering parameter.");
			}
			finally
			{
				session.PropertyChanged -= handler;
			}

			// the image should never have been cleared, every filtering replaces the reported image with the next one
			Assert.That(clearedCount, Is.EqualTo(0), $"Reported image should not be cleared while the filtering parameters are changed continuously, cleared {clearedCount} times in {filteringCount} filterings.");
			Assert.That(session.HasRenderingError, Is.False, "Rendering and filtering should not fail.");
		});
	}


	/// <summary>
	/// Release the resources created by the completed test.
	/// </summary>
	[TearDown]
	public void CleanUp()
	{
		this.TestOnApplicationThread(async () =>
		{
			// let the parked rendering complete so that it does not keep the source file locked
			this.gateRenderer?.OpenGate();

			// close the source
			var session = this.session;
			if (session is not null)
			{
				if (session.IsSourceOpened)
				{
					session.ClearSourceCommand.Execute(null);
					await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 5000);
				}
				this.sessionActivationToken = this.sessionActivationToken.DisposeAndReturnNull();
				session.Dispose();
				this.session = null;
			}

			// unregister the gate renderer
			if (this.gateRenderer is not null)
			{
				ImageRenderers.Remove(this.gateRenderer);
				ImageFormat.Unregister(this.gateRenderer.Format);
				this.gateRenderer = null;
			}

			// delete the generated source files
			foreach (var filePath in this.sourceFilePaths)
				Global.RunWithoutError(() => File.Delete(filePath));
			this.sourceFilePaths.Clear();

			// restore the settings overridden for the test
			if (this.restoredEvaluateImageRendererByFileName.HasValue)
			{
				this.Application.Settings.SetValue(SettingKeys.EvaluateImageRendererByFileName, this.restoredEvaluateImageRendererByFileName.GetValueOrDefault());
				this.restoredEvaluateImageRendererByFileName = null;
			}
		});
	}


	/// <summary>
	/// Create the <see cref="Session"/> and the renderer for the test to run.
	/// </summary>
	/// <remarks>Every test owns its own session because the tests change the activation state and the rendering parameters of the session.</remarks>
	[SetUp]
	public void CreateSession()
	{
		this.TestOnApplicationThread(async () =>
		{
			// initialize the sub-systems required to construct and run a session
			await this.InitializeSubSystemsAsync();

			// keep the renderer selected by the test, the name of the generated source file must not be able to select another one
			this.restoredEvaluateImageRendererByFileName = this.Application.Settings.GetValueOrDefault(SettingKeys.EvaluateImageRendererByFileName);
			this.Application.Settings.SetValue(SettingKeys.EvaluateImageRendererByFileName, false);

			// register the renderer which parks its renderings, its format needs a unique name because formats are registered globally
			this.gateRenderer = new GateImageRenderer(new ImageFormat(ImageFormatCategory.Luminance, $"Gate-{Guid.NewGuid()}", new ImagePlaneDescriptor(1)));
			ImageRenderers.Add(this.gateRenderer);

			// create session for testing and activate it so image rendering is performed
			this.session = new Session(this.Application, null);
			this.sessionActivationToken = this.session.Activate();
		});
	}


	/// <summary>
	/// Test for filtering the image which is rendered by the rendering that interrupted another rendering.
	/// </summary>
	[Test]
	public void FilteringAfterInterruptedRenderingTest()
	{
		var session = this.session ?? throw new AssertionException("No session for testing.");
		var renderer = this.gateRenderer ?? throw new AssertionException("No renderer for testing.");
		this.TestOnApplicationThread(async () =>
		{
			// park the rendering by the gate renderer
			await this.PrepareParkedRenderingAsync(session, renderer);

			// request filtering while the current rendering is parked, the need of filtering is reported by a scheduled action so it is waited for before
			// interrupting the rendering, otherwise whether the rendering sees the need when it completes would be a race
			session.BrightnessAdjustment = 0.5;
			Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsFilteringRenderedImageNeeded), true, 5000), Is.True, "Filtering should be needed after the adjustment has been set.");

			// request re-rendering while the current rendering is parked
			renderer.Reset();
			session.RenderImageCommand.Execute(null);

			// let the parked rendering complete only after the re-rendering has cancelled it
			Assert.That(await WaitForTaskAsync(renderer.RenderingCancelledTask, 5000), Is.True, "Re-rendering should cancel the rendering which is in progress.");
			renderer.OpenGate();

			// the image rendered by the re-rendering should be filtered and reported
			Assert.That(await WaitForTaskAsync(renderer.RenderingCompletedTask, 5000), Is.True, "Re-rendering should be performed after the interrupted rendering has been cancelled.");
			Assert.That(await WaitForConditionAsync(() => session.RenderedImage is not null && !session.IsFilteringRenderedImage, 10000), Is.True, "Filtered image should be reported after the filtering completes.");
			Assert.That(session.HasRenderingError, Is.False, "Rendering and filtering should not fail.");
		});
	}


	// Generate source image file filled with random data, its file format is unidentifiable so the renderer selected by the session is kept after opening it.
	string GenerateSourceFile(int size = SourceFileSize)
	{
		// get the directory to place the file, its name needs to be controlled because a name which carries the keyword of a format or a pair of
		// numbers makes the session select the renderer or evaluate the dimensions by the name instead of keeping what the test has set
		string directoryPath;
		using (var stream = this.CreateCacheFile())
			directoryPath = Path.GetDirectoryName(stream.Name).AsNonNull();

		// fill the file with random data
		var data = new byte[size];
		for (var i = data.Length - 1; i >= 0; --i)
			data[i] = (byte)this.Random.Next(0, 256);
		var filePath = Path.Combine(directoryPath, $"source-of-{TestContext.CurrentContext.Test.Name}");
		File.WriteAllBytes(filePath, data);
		this.sourceFilePaths.Add(filePath);
		return filePath;
	}


	/// <summary>
	/// Test for interrupting the rendering by changing a rendering parameter.
	/// </summary>
	/// <remarks>The image needs to be rendered with the parameter which was changed while the previous rendering was interrupted, reporting the image rendered with the parameter before the change is a stale image.</remarks>
	[Test]
	public void InterruptingRenderingByParameterChangeTest()
	{
		var session = this.session ?? throw new AssertionException("No session for testing.");
		var renderer = this.gateRenderer ?? throw new AssertionException("No renderer for testing.");
		this.TestOnApplicationThread(async () =>
		{
			// park the rendering by the gate renderer
			await this.PrepareParkedRenderingAsync(session, renderer);

			// change the width of image while the current rendering is parked
			var newWidth = session.ImageWidth + 8;
			renderer.Reset();
			session.ImageWidth = newWidth;

			// let the parked rendering complete only after the rendering for the new width has cancelled it
			Assert.That(await WaitForTaskAsync(renderer.RenderingCancelledTask, 5000), Is.True, "Rendering for the changed width should cancel the rendering which is in progress.");
			renderer.OpenGate();

			// the image should be rendered with the width set while the previous rendering was interrupted
			Assert.That(await WaitForTaskAsync(renderer.RenderingCompletedTask, 5000), Is.True, "Rendering should be performed for the changed width.");
			Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), false, 5000), Is.True, "Rendering should complete.");
			Assert.That(renderer.LastRenderedWidth, Is.EqualTo(newWidth), "Image should be rendered with the width changed while the previous rendering was interrupted.");
			Assert.That(session.RenderedImage, Is.Not.Null, "Rendered image should be reported.");
			Assert.That(session.HasRenderingError, Is.False, "Rendering should not fail.");
			Assert.That(renderer.MaxConcurrentRenderingCount, Is.EqualTo(1), "Renderings should never be performed at the same time.");
		});
	}


	/// <summary>
	/// Test for interrupting the rendering by requesting rendering again, which is what pressing the refresh button on the toolbar does.
	/// </summary>
	[Test]
	public void InterruptingRenderingByRefreshTest()
	{
		var session = this.session ?? throw new AssertionException("No session for testing.");
		var renderer = this.gateRenderer ?? throw new AssertionException("No renderer for testing.");
		this.TestOnApplicationThread(async () =>
		{
			// park the rendering by the gate renderer
			await this.PrepareParkedRenderingAsync(session, renderer);

			// request rendering again while the current rendering is parked
			renderer.Reset();
			session.RenderImageCommand.Execute(null);

			// let the parked rendering complete only after the re-rendering has cancelled it
			Assert.That(await WaitForTaskAsync(renderer.RenderingCancelledTask, 5000), Is.True, "Re-rendering should cancel the rendering which is in progress.");
			renderer.OpenGate();

			// the re-rendering should be performed after the cancellation of the interrupted rendering completes
			Assert.That(await WaitForTaskAsync(renderer.RenderingCompletedTask, 5000), Is.True, "Re-rendering should be performed after the interrupted rendering has been cancelled.");
			Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), false, 5000), Is.True, "Rendering should complete.");
			Assert.That(session.RenderedImage, Is.Not.Null, "Rendered image should be reported by the re-rendering.");
			Assert.That(session.HasRenderingError, Is.False, "Re-rendering should not fail.");
			Assert.That(renderer.MaxConcurrentRenderingCount, Is.EqualTo(1), "Renderings should never be performed at the same time.");
		});
	}


	// Open a source file and park the rendering by the gate renderer, so that the test can interrupt the rendering.
	async Task PrepareParkedRenderingAsync(Session session, GateImageRenderer renderer)
	{
		// open source file
		var filePath = this.GenerateSourceFile();
		session.OpenSourceFileCommand.Execute(filePath);
		Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 5000), Is.True, "Cannot open source file.");

		// wait for the rendering requested by opening the file to complete before selecting the gate renderer, so that selecting it is not an interruption itself
		Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), true, 5000), Is.True, "Rendering should start after the source file has been opened.");
		Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), false, 10000), Is.True, "Rendering for the opened source file should complete.");

		// select the gate renderer, the rendering requested by the selection parks until the gate is opened
		session.ImageRenderer = renderer;
		Assert.That(await WaitForTaskAsync(renderer.RenderingStartedTask, 5000), Is.True, "Rendering by the gate renderer should have started.");
	}


	/// <summary>
	/// Test for interrupting the rendering repeatedly before it completes.
	/// </summary>
	[Test]
	public void RepeatedRenderingInterruptionTest()
	{
		var session = this.session ?? throw new AssertionException("No session for testing.");
		var renderer = this.gateRenderer ?? throw new AssertionException("No renderer for testing.");
		this.TestOnApplicationThread(async () =>
		{
			// park the rendering by the gate renderer
			await this.PrepareParkedRenderingAsync(session, renderer);

			// request rendering again repeatedly while the current rendering is parked
			renderer.Reset();
			for (var i = 3; i > 0; --i)
				session.RenderImageCommand.Execute(null);

			// let the parked rendering complete only after the re-renderings have cancelled it
			Assert.That(await WaitForTaskAsync(renderer.RenderingCancelledTask, 5000), Is.True, "Re-rendering should cancel the rendering which is in progress.");
			renderer.OpenGate();

			// the requests should be collapsed into a rendering which reports the image
			Assert.That(await WaitForTaskAsync(renderer.RenderingCompletedTask, 5000), Is.True, "Re-rendering should be performed after the interrupted renderings have been cancelled.");
			Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), false, 5000), Is.True, "Rendering should complete.");
			Assert.That(session.RenderedImage, Is.Not.Null, "Rendered image should be reported by the re-rendering.");
			Assert.That(session.HasRenderingError, Is.False, "Re-rendering should not fail.");
			Assert.That(renderer.MaxConcurrentRenderingCount, Is.EqualTo(1), "Renderings should never be performed at the same time.");
		});
	}
}
