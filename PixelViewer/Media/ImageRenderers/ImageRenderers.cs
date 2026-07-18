using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.Collections;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Class to hold all available <see cref="IImageRenderer"/>s.
/// </summary>
static partial class ImageRenderers
{
	// Static fields.
	static readonly ObservableList<IImageRenderer> all = new ObservableList<IImageRenderer>().Also(it =>
	{
		it.AddRange([
			new L8ImageRenderer(),
			new L16ImageRenderer(),
			new NV12ImageRenderer(),
			new NV21ImageRenderer(),
			//new Y010ImageRenderer(), // Not standardized by Microsoft
			//new Y016ImageRenderer(), // Not standardized by Microsoft
			new I420ImageRenderer(),
			new P010ImageRenderer(),
			//new P012ImageRenderer(), // Not standardized by Microsoft
			new P016ImageRenderer(),
			new YV12ImageRenderer(),
			new Yuv422pImageRenderer(),
			new P210ImageRenderer(),
			//new P212ImageRenderer(), // Not standardized by Microsoft
			new P216ImageRenderer(),
			new UyvyImageRenderer(),
			new YuvyImageRenderer(),
			new YuyvImageRenderer(),
			new YvyuImageRenderer(),
			new Yuv444pImageRenderer(),
			new P410ImageRenderer(),
			//new P412ImageRenderer(), // Not standardized by Microsoft
			new P416ImageRenderer(),
			new AndroidYuv420ImageRenderer(),

			new Rgb565ImageRenderer(),
			new Bgr888ImageRenderer(),
			new Rgb888ImageRenderer(),
			new Bgrx8888ImageRenderer(),
			new Rgbx8888ImageRenderer(),
			new Xbgr8888ImageRenderer(),
			new Xrgb8888ImageRenderer(),
			new Bgr161616ImageRenderer(),
			new Rgb161616ImageRenderer(),

			new Abgr8888ImageRenderer(),
			new Argb8888ImageRenderer(),
			new Bgra8888ImageRenderer(),
			new Rgba8888ImageRenderer(),
			new Abgr2101010ImageRenderer(),
			new Argb2101010ImageRenderer(),
			new Bgra1010102ImageRenderer(),
			new Rgba1010102ImageRenderer(),
			new Abgr16161616ImageRenderer(),
			new Argb16161616ImageRenderer(),
			new Bgra16161616ImageRenderer(),
			new Rgba16161616ImageRenderer(),
			new AbgrF16ImageRenderer(),
			new ArgbF16ImageRenderer(),
			new BgraF16ImageRenderer(),
			new RgbaF16ImageRenderer(),

			new BayerPattern8ImageRenderer(),
			new BayerPattern10MipiImageRenderer(),
			new BayerPattern12MipiImageRenderer(),
			new BayerPattern14MipiImageRenderer(),
			new BayerPattern16ImageRenderer(),
		]);
		it.Add(Platform.IsMacOS 
			? new MacOSHeifImageRenderer() 
			: new HeifImageRenderer());
		it.AddRange([
			//new ArwImageRenderer(),
			//new Cr2ImageRenderer(),
			new JpegImageRenderer(),
			//new NefImageRenderer(),
			new PngImageRenderer(),
			new WebPImageRenderer(),
			new TiffImageRenderer(),
		]);
	});
	static volatile IAppSuiteApplication? app;
	static volatile ILogger? logger;


	/// <summary>
	/// Add the given <see cref="IImageRenderer"/> to <see cref="All"/>.
	/// </summary>
	/// <param name="renderer"><see cref="IImageRenderer"/> to add.</param>
	/// <remarks><see cref="All"/> is bound by UI directly, so this method must be called on UI thread.</remarks>
	internal static void Add(IImageRenderer renderer) =>
		all.Add(renderer);


	/// <summary>
	/// Get all available <see cref="IImageRenderer"/>s.
	/// </summary>
	public static IList<IImageRenderer> All { get; } = ListExtensions.AsReadOnly(all);


	/// <summary>
	/// Initialize.
	/// </summary>
	/// <param name="app">Application.</param>
	/// <returns>Task of initialization.</returns>
	/// <remarks>Built-in renderers are registered when <see cref="All"/> is first accessed, so this method only sets up the shared state that runtime-managed renderers rely on. It must be called once, before any runtime renderer is added.</remarks>
	public static Task InitializeAsync(IAppSuiteApplication app)
	{
		// check state
		if (ImageRenderers.app is not null)
			throw new InvalidOperationException("Image renderers have already been initialized.");

		// setup application and logger
		ImageRenderers.app = app;
		logger = app.LoggerFactory.CreateLogger(nameof(ImageRenderers));
		logger.LogDebug("Initialize");
		return Task.CompletedTask;
	}


	/// <summary>
	/// Remove the given <see cref="IImageRenderer"/> from <see cref="All"/>.
	/// </summary>
	/// <param name="renderer"><see cref="IImageRenderer"/> to remove.</param>
	/// <returns>True if the renderer has been removed.</returns>
	/// <remarks><see cref="All"/> is bound by UI directly, so this method must be called on UI thread.</remarks>
	internal static bool Remove(IImageRenderer renderer) =>
		all.Remove(renderer);


	/// <summary>
	/// Try finding specific <see cref="IImageRenderer"/> by the name of format supported by it.
	/// </summary>
	/// <param name="formatName">Name of format supported by <see cref="IImageRenderer"/>.</param>
	/// <param name="renderer">Found <see cref="IImageRenderer"/>, or null if no <see cref="IImageRenderer"/> supports given format.</param>
	/// <returns>True if <see cref="IImageRenderer"/> found.</returns>
	/// <remarks>The name of a user-defined format is its identifier, which is stable across renaming, so a name persisted before the user renamed the format still resolves to its renderer.</remarks>
	public static bool TryFindByFormatName(string formatName, out IImageRenderer? renderer)
	{
		foreach (var candidate in All)
		{
			if (candidate.Format.Name == formatName)
			{
				renderer = candidate;
				return true;
			}
		}
		renderer = null;
		return false;
	}
}
