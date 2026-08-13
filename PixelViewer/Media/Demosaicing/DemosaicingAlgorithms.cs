using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio.AppSuite;
using CarinaStudio.Collections;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.Demosaicing;

/// <summary>
/// Class to hold all available <see cref="DemosaicingAlgorithm"/>s.
/// </summary>
static partial class DemosaicingAlgorithms
{
	/// <summary>
	/// Algorithm which interpolates each missing color component of pixel by averaging the same component of its neighbors.
	/// </summary>
	public static readonly DemosaicingAlgorithm Bilinear = new BilinearDemosaicingAlgorithm();
	/// <summary>
	/// Algorithm which keeps the mosaic of image without demosaicing.
	/// </summary>
	public static readonly DemosaicingAlgorithm Bypass = new BypassDemosaicingAlgorithm();
	/// <summary>
	/// Placeholder which stands for no algorithm having been chosen at all.
	/// </summary>
	/// <remarks>It is not one of <see cref="All"/> and it cannot demosaic anything, it only lets a profile which nobody has chosen an algorithm for say so instead of naming one which would then look like a choice. Whoever reads it is expected to replace it by an algorithm of their own, and every member which would actually interpolate throws so that failing to do so is reported instead of demosaicing by something arbitrary.</remarks>
	public static readonly DemosaicingAlgorithm Undefined = new UndefinedDemosaicingAlgorithm();


	// Placeholder algorithm which throws whenever it is asked to do anything but describe itself.
	class UndefinedDemosaicingAlgorithm() : DemosaicingAlgorithm("Undefined")
	{
		/// <inheritdoc/>
		public override OutputBufferRequirement CheckOutputBufferRequirement(ImageRenderingOptions renderingOptions, int width, int height) =>
			throw new InvalidOperationException("No demosaicing algorithm has been chosen.");

		/// <inheritdoc/>
		public override void Demosaic(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, Memory<byte> workingBuffer, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("No demosaicing algorithm has been chosen.");

		/// <inheritdoc/>
		/// <remarks>No pattern is supported, so a caller which selects an algorithm by the pattern replaces this one even if it forgets to check for it.</remarks>
		public override bool IsBayerPatternSupported(BayerPattern pattern) => false;
	}


	// Static fields.
	static readonly ObservableList<DemosaicingAlgorithm> all = [ Bypass, Bilinear ];
	static volatile IAppSuiteApplication? app;
	static volatile ILogger? logger;


	/// <summary>
	/// Get all available <see cref="DemosaicingAlgorithm"/>s.
	/// </summary>
	/// <remarks><see cref="Bypass"/> is the first algorithm in the list.</remarks>
	public static IList<DemosaicingAlgorithm> All { get; } = ListExtensions.AsReadOnly(all);


	/// <summary>
	/// Get default <see cref="DemosaicingAlgorithm"/>.
	/// </summary>
	/// <remarks>The algorithm is the one which every build can always run, so it is what an unknown identifier resolves to and what a session falls back to when the algorithm it would use cannot be used.</remarks>
	public static DemosaicingAlgorithm Default => Bilinear;


	/// <summary>
	/// Initialize.
	/// </summary>
	/// <param name="app">Application.</param>
	/// <returns>Task of initialization.</returns>
	/// <remarks>Built-in algorithms are registered when <see cref="All"/> is first accessed, so this method only sets up the shared state that runtime-managed algorithms rely on. It must be called before any runtime algorithm is added. Initializing again by the same application does nothing, so a caller needn't track whether initialization has already happened; only another application initializing them is an error.</remarks>
	public static Task InitializeAsync(IAppSuiteApplication app)
	{
		// check state
		if (DemosaicingAlgorithms.app is not null)
		{
			if (DemosaicingAlgorithms.app != app)
				throw new InvalidOperationException("Demosaicing algorithms have been initialized by another application.");
			return Task.CompletedTask;
		}

		// setup application and logger
		DemosaicingAlgorithms.app = app;
		logger = app.LoggerFactory.CreateLogger(nameof(DemosaicingAlgorithms));
		logger.LogDebug("Initialize");
		return Task.CompletedTask;
	}


	/// <summary>
	/// Get the <see cref="DemosaicingAlgorithm"/> which a new session starts with unless the settings name another one.
	/// </summary>
	/// <remarks>The algorithm is allowed to be one which the current build or the current state of the application cannot run, in which case the session falls back to <see cref="Default"/> instead of failing to render.</remarks>
	public static DemosaicingAlgorithm Preferred => Bilinear;


	/// <summary>
	/// Try getting <see cref="DemosaicingAlgorithm"/> with given identifier.
	/// </summary>
	/// <param name="id">Identifier of algorithm.</param>
	/// <param name="algorithm">Algorithm with given identifier, or <see cref="Default"/> if algorithm cannot be found.</param>
	/// <returns>True if algorithm with given identifier has been found.</returns>
	/// <remarks><see cref="Undefined"/> is found by its identifier as well, even though it is not one of <see cref="All"/>, so that a profile which was saved without a choice of algorithm still says so after being loaded again.</remarks>
	public static bool TryGetById(string? id, out DemosaicingAlgorithm algorithm)
	{
		if (id is not null)
		{
			if (id == Undefined.Id)
			{
				algorithm = Undefined;
				return true;
			}
			foreach (var candidate in all)
			{
				if (candidate.Id == id)
				{
					algorithm = candidate;
					return true;
				}
			}
		}
		algorithm = Default;
		return false;
	}
}
