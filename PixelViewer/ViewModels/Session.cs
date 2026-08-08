//#define SKIP_MAPPING_EMBEDDED_COLOR_SPACE_TO_BUILT_IN

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.Demosaicing;
using Carina.PixelViewer.Media.ImageEncoders;
using Carina.PixelViewer.Media.ImageFilters;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using CarinaStudio.Animation;
using CarinaStudio.AppSuite;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.IO;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using CarinaStudio.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Carina.PixelViewer.ViewModels;

/// <summary>
/// A session of rendering and displaying image.
/// </summary>
class Session : ViewModel<IAppSuiteApplication>
{
	// Activation token.
	class ActivationToken(Session session) : IDisposable
	{
		// Dispose.
		public void Dispose() => session.Deactivate(this);
	}

	// Frame of image.
	class ImageFrame : BaseDisposable
	{
		// Fields.
		readonly long dataSize;
		bool isTransferred;
		readonly IDisposable memoryUsageToken;
		readonly Session session;

		// Constructor
		ImageFrame(Session session, IDisposable memoryUsageToken, BitmapBuffer bitmapBuffer, long dataSize, long frameNumber)
		{
			this.BitmapBuffer = bitmapBuffer;
			this.dataSize = dataSize;
			this.FrameNumber = frameNumber;
			this.memoryUsageToken = memoryUsageToken;
			this.session = session;
		}
		ImageFrame(Session session, IDisposable memoryUsageToken, ImageFrame source)
        {
			this.BitmapBuffer = source.BitmapBuffer.Share();
			this.dataSize = source.dataSize;
			this.FrameNumber = source.FrameNumber;
			this.memoryUsageToken = memoryUsageToken;
			this.session = session;
		}

		public static ImageFrame Allocate(Session session, long frameNumber, BitmapFormat format, ColorSpace colorSpace, int width, int height)
		{
			var renderedImageDataSize = ((long)width * height * format.GetByteSize()); // no need to reserve for Avalonia bitmap
			var memoryUsageToken = session.RequestRenderedImageMemoryUsage(renderedImageDataSize);
			if (memoryUsageToken is null)
			{
				session.Logger.LogError("Unable to request memory usage for image frame");
				throw new OutOfMemoryException();
			}
			try
			{
				var bitmapBuffer = new BitmapBuffer(format, colorSpace, width, height);
				return new ImageFrame(session, memoryUsageToken, bitmapBuffer, renderedImageDataSize, frameNumber);
			}
			catch
			{
				memoryUsageToken.Dispose();
				throw;
			}
		}

		// Bitmap buffer.
		public readonly BitmapBuffer BitmapBuffer;

		// Dispose.
		protected override void Dispose(bool disposing)
		{
			this.BitmapBuffer.Dispose();
			if (this.session.CheckAccess())
				this.memoryUsageToken.Dispose();
			else
				this.session.SynchronizationContext.Post(this.memoryUsageToken.Dispose);
		}

		// Frame number.
		public readonly long FrameNumber;

		// Histograms.
		public BitmapHistograms? Histograms { get; set; }

		// Image renderer to render this frame.
		public IImageRenderer? ImageRenderer { get; set; }

		// Plane options.
		public IList<ImagePlaneOptions>? PlaneOptions { get; set; }

		// Rendering options.
		public ImageRenderingOptions RenderingOptions { get; set; }

		// Rendering result.
		public ImageRenderingResult RenderingResult { get; set; } = new();

		// Transfer resource ownership.
		// ReSharper disable once UnusedMember.Local
		public ImageFrame? Transfer(Session session)
		{
			// check state
			this.session.VerifyAccess();
			if (this.isTransferred)
				throw new InvalidOperationException();

			// update state
			this.isTransferred = true;

			// release memory usage
			this.memoryUsageToken.Dispose();

            // request memory usage
            var memoryUsageToken = session.RequestRenderedImageMemoryUsage(this.dataSize);
			if (memoryUsageToken is null)
			{
				this.session.Logger.LogError("Failed to transfer image frame to {session}", session);
				return null;
			}

			// transfer
			return new ImageFrame(session, memoryUsageToken, this);
        }
    }


	/// <summary>
	/// Data for image saving completed event.
	/// </summary>
	public class ImageSavingCompletedEventArgs : EventArgs
	{
		/// <summary>
		/// Initialize new <see cref="ImageSavingCompletedEventArgs"/> instance.
		/// </summary>
		/// <param name="fileName">File name to save image to.</param>
		/// <param name="succeeded">Whether image saving is succeeded or not.</param>
		public ImageSavingCompletedEventArgs(string fileName, bool succeeded)
		{
			this.FileName = fileName;
			this.IsSucceeded = succeeded;
		}
		
		/// <summary>
		/// File name to save image to.
		/// </summary>
		public string FileName { get; }
		
		/// <summary>
		/// Whether image saving is succeeded or not.
		/// </summary>
		public bool IsSucceeded { get; }
	}


	/// <summary>
	/// Parameters of saving image.
	/// </summary>
	public struct ImageSavingParams
    {
		/// <summary>
		/// Image encoder.
		/// </summary>
		public IImageEncoder? Encoder { get; set; }


		/// <summary>
		/// File name.
		/// </summary>
		public string? FileName { get; set; }


		/// <summary>
		/// Image encoding options.
		/// </summary>
		public ImageEncodingOptions Options { get; set; }
    }


	// Token of memory usage of rendered image.
	class RenderedImageMemoryUsageToken(Session session, long dataSize) : IDisposable
	{
		// Fields.
		public readonly long DataSize = dataSize;
		bool isDisposed;

		// Dispose.
		public void Dispose()
		{
			if (this.isDisposed)
				return;
			this.isDisposed = true;
			session.ReleaseRenderedImageMemoryUsage(this);
		}
	}
	
	
	// Constants for usage tracking events.
	static class UsageEvents
	{
		public const string AutoColorAdjustmentApplied = "Session.AutoColorAdjustmentApplied";
		public const string BrightnessAndContrastAdjustmentReset = "Session.BrightnessAndContrastAdjustmentReset";
		public const string ColorAdjustmentReset = "Session.ColorAdjustmentReset";
		public const string FilteringParamsApplied = "Session.FilteringParamsApplied";
		public const string RenderedImageSaved = "Session.RenderedImageSaved";
		public const string RenderingParamsApplied = "Session.RenderingParamsApplied";
	}


	// Constants for usage tracking metrics.
	static class UsageMetrics
	{
		public const string FrameNavigationCount = "Session.FrameNavigationCount";
		public const string LargestFilteredImageDimensionMP = "Session.LargestFilteredImageDimensionMP";
		public const string LargestRenderedImageDimensionMP = "Session.LargestRenderedImageDimensionMP";
		public const string LongestFilteringDuration = "Session.LongestFilteringDuration";
		public const string LongestRenderingDuration = "Session.LongestRenderingDuration";
	}


	// Constants for usage tracking properties.
	static class UsageProperties
	{
		public const string BlueColorAdjustment = "BlueColorAdjustment";
		public const string BrightnessAdjustment = "BrightnessAdjustment";
		public const string ColorSpace = "ColorSpace";
		public const string ContrastAdjustment = "ContrastAdjustment";
		public const string DimensionMP = "DimensionMP";
		public const string Duration = "Duration";
		public const string FilterCount = "FilterCount";
		public const string Filters = "Filters";
		public const string FrameCount = "FrameCount";
		public const string GreenColorAdjustment = "GreenColorAdjustment";
		public const string HighlightAdjustment = "HighlightAdjustment";
		public const string Id = "Id";
		public const string ImageEncoder = "ImageEncoder";
		public const string ImageRenderer = "ImageRenderer";
		public const string IsColorSpaceManagementEnabled = "IsColorSpaceManagementEnabled";
		public const string IsFileFormatProfile = "IsFileFormatProfile";
		public const string IsFilteredImage = "IsFilteredImage";
		public const string IsGrayscaleFilterEnabled = "IsGrayscaleFilterEnabled";
		public const string IsTransformationApplied = "IsTransformationApplied";
		public const string Profile = "Profile";
		public const string QualityLevel = "QualityLevel";
		public const string RedColorAdjustment = "RedColorAdjustment";
		public const string RenderCount = "RenderCount";
		public const string SaturationAdjustment = "SaturationAdjustment";
		public const string ShadowAdjustment = "ShadowAdjustment";
		public const string UseLinearColorSpace = "UseLinearColorSpace";
		public const string VibranceAdjustment = "VibranceAdjustment";
		public const string YuvToRgbaConversion = "YuvToRgbaConversion";
	}


	/// <summary>
	/// Maximum width of panel of histograms in pixels.
	/// </summary>
	public const double MaxHistogramsPanelSize = 400;
	/// <summary>
	/// Maximum scaling ratio of rendered image.
	/// </summary>
	public const double MaxRenderedImageScale = 20.0;
	/// <summary>
	/// Maximum size of panel of rendering parameters in pixels.
	/// </summary>
	public const double MaxRenderingParametersPanelSize = 400;
	/// <summary>
	/// Minimum width of panel of histograms in pixels.
	/// </summary>
	public const double MinHistogramsPanelSize = 150;
	/// <summary>
	/// Minimum scaling ratio of rendered image.
	/// </summary>
	public const double MinRenderedImageScale = 0.1;
	/// <summary>
	/// Minimum size of panel of rendering parameters in pixels.
	/// </summary>
	public const double MinRenderingParametersPanelSize = 200;


	/// <summary>
	/// Property of <see cref="AreAdjustableBlackWhiteLevels1"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> AreAdjustableBlackWhiteLevels1Property = ObservableProperty.Register<Session, bool>(nameof(AreAdjustableBlackWhiteLevels1));
	/// <summary>
	/// Property of <see cref="AreAdjustableBlackWhiteLevels2"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> AreAdjustableBlackWhiteLevels2Property = ObservableProperty.Register<Session, bool>(nameof(AreAdjustableBlackWhiteLevels2));
	/// <summary>
	/// Property of <see cref="AreAdjustableBlackWhiteLevels3"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> AreAdjustableBlackWhiteLevels3Property = ObservableProperty.Register<Session, bool>(nameof(AreAdjustableBlackWhiteLevels3));
	/// <summary>
	/// Property of <see cref="BayerPattern"/>.
	/// </summary>
	public static readonly ObservableProperty<BayerPattern> BayerPatternProperty = ObservableProperty.Register<Session, BayerPattern>(nameof(BayerPattern));
	/// <summary>
	/// Property of <see cref="BlueColorAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> BlueColorAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(BlueColorAdjustment), 0, validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="BlueColorGain"/>.
	/// </summary>
	public static readonly ObservableProperty<double> BlueColorGainProperty = ObservableProperty.Register<Session, double>(nameof(BlueColorGain), 1.0, coerce: (_, it) => ImageRenderingOptions.GetValidRgbGain(it));
	/// <summary>
	/// Property of <see cref="BrightnessAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> BrightnessAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(BrightnessAdjustment), 0, validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="ByteOrdering"/>.
	/// </summary>
	public static readonly ObservableProperty<ByteOrdering> ByteOrderingProperty = ObservableProperty.Register<Session, ByteOrdering>(nameof(ByteOrdering), ByteOrdering.BigEndian);
	/// <summary>
	/// Property of <see cref="ColorSpace"/>.
	/// </summary>
	public static readonly ObservableProperty<ColorSpace> ColorSpaceProperty = ObservableProperty.Register<Session, ColorSpace>(nameof(ColorSpace), ColorSpace.Default);
	/// <summary>
	/// Property of <see cref="ContrastAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> ContrastAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(ContrastAdjustment), 0, validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="CustomTitle"/>.
	/// </summary>
	public static readonly ObservableProperty<string?> CustomTitleProperty = ObservableProperty.Register<Session, string?>(nameof(CustomTitle));
	/// <summary>
	/// Property of <see cref="DataOffset"/>.
	/// </summary>
	public static readonly ObservableProperty<long> DataOffsetProperty = ObservableProperty.Register<Session, long>(nameof(DataOffset), 0L);
	/// <summary>
	/// Property of <see cref="DemosaicingAlgorithm"/>.
	/// </summary>
	public static readonly ObservableProperty<DemosaicingAlgorithm> DemosaicingAlgorithmProperty = ObservableProperty.Register<Session, DemosaicingAlgorithm>(nameof(DemosaicingAlgorithm), Media.Demosaicing.DemosaicingAlgorithms.Default, coerce: (session, it) => it.IsBayerPatternSupported(session.BayerPattern) ? it : SelectDefaultDemosaicingAlgorithm(session.BayerPattern));
	/// <summary>
	/// Property of <see cref="FitImageToViewport"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> FitImageToViewportProperty = ObservableProperty.Register<Session, bool>(nameof(FitImageToViewport), true);
	/// <summary>
	/// Property of <see cref="FrameCount"/>.
	/// </summary>
	public static readonly ObservableProperty<long> FrameCountProperty = ObservableProperty.Register<Session, long>(nameof(FrameCount), 0);
	/// <summary>
	/// Property of <see cref="FrameNumber"/>.
	/// </summary>
	public static readonly ObservableProperty<long> FrameNumberProperty = ObservableProperty.Register<Session, long>(nameof(FrameNumber), 0);
	/// <summary>
	/// Property of <see cref="FramePaddingSize"/>.
	/// </summary>
	public static readonly ObservableProperty<long> FramePaddingSizeProperty = ObservableProperty.Register<Session, long>(nameof(FramePaddingSize), 0L);
	/// <summary>
	/// Property of <see cref="FramePlaybackRate"/>.
	/// </summary>
	public static readonly ObservableProperty<int> FramePlaybackRateProperty = ObservableProperty.Register<Session, int>(nameof(FramePlaybackRate), 30, coerce: (_, it) => Math.Max(MinFramePlaybackRate, Math.Min(MaxFramePlaybackRate, it)));
	/// <summary>
	/// Property of <see cref="GreenColorAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> GreenColorAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(GreenColorAdjustment), 0, validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="GreenColorGain"/>.
	/// </summary>
	public static readonly ObservableProperty<double> GreenColorGainProperty = ObservableProperty.Register<Session, double>(nameof(GreenColorGain), 1.0, coerce: (_, it) => ImageRenderingOptions.GetValidRgbGain(it));
	/// <summary>
	/// Property of <see cref="HasBrightnessAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasBrightnessAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasBrightnessAdjustment));
	/// <summary>
	/// Property of <see cref="HasColorAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasColorAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasColorAdjustment));
	/// <summary>
	/// Property of <see cref="HasColorTables"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasColorTablesProperty = ObservableProperty.Register<Session, bool>(nameof(HasColorTables));
	/// <summary>
	/// Property of <see cref="HasContrastAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasContrastAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasContrastAdjustment));
	/// <summary>
	/// Property of <see cref="HasHighlightAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasHighlightAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasHighlightAdjustment));
	/// <summary>
	/// Property of <see cref="HasHistograms"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasHistogramsProperty = ObservableProperty.Register<Session, bool>(nameof(HasHistograms));
	/// <summary>
	/// Property of <see cref="HasImagePlane1"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasImagePlane1Property = ObservableProperty.Register<Session, bool>(nameof(HasImagePlane1), true);
	/// <summary>
	/// Property of <see cref="HasImagePlane2"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasImagePlane2Property = ObservableProperty.Register<Session, bool>(nameof(HasImagePlane2));
	/// <summary>
	/// Property of <see cref="HasImagePlane3"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasImagePlane3Property = ObservableProperty.Register<Session, bool>(nameof(HasImagePlane3));
	/// <summary>
	/// Property of <see cref="HasMultipleByteOrderings"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasMultipleByteOrderingsProperty = ObservableProperty.Register<Session, bool>(nameof(HasMultipleByteOrderings));
	/// <summary>
	/// Property of <see cref="HasMultipleFrames"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasMultipleFramesProperty = ObservableProperty.Register<Session, bool>(nameof(HasMultipleFrames));
	/// <summary>
	/// Property of <see cref="HasQuarterSizeRenderedImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasQuarterSizeRenderedImageProperty = ObservableProperty.Register<Session, bool>(nameof(HasQuarterSizeRenderedImage));
	/// <summary>
	/// Property of <see cref="HasRenderedImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasRenderedImageProperty = ObservableProperty.Register<Session, bool>(nameof(HasRenderedImage));
	/// <summary>
	/// Property of <see cref="HasRenderingError"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasRenderingErrorProperty = ObservableProperty.Register<Session, bool>(nameof(HasRenderingError));
	/// <summary>
	/// Property of <see cref="HasRgbGain"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasRgbGainProperty = ObservableProperty.Register<Session, bool>(nameof(HasRgbGain));
	/// <summary>
	/// Property of <see cref="HasSaturationAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasSaturationAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasSaturationAdjustment));
	/// <summary>
	/// Property of <see cref="HasSelectedRenderedImagePixel"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasSelectedRenderedImagePixelProperty = ObservableProperty.Register<Session, bool>(nameof(HasSelectedRenderedImagePixel));
	/// <summary>
	/// Property of <see cref="HasShadowAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasShadowAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasShadowAdjustment));
	/// <summary>
	/// Property of <see cref="HasSourceDataSize"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasSourceDataSizeProperty = ObservableProperty.Register<Session, bool>(nameof(HasSourceDataSize));
	/// <summary>
	/// Property of <see cref="HasVibranceAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> HasVibranceAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasVibranceAdjustment));
	/// <summary>
	/// Property of <see cref="HighlightAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> HighlightAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(HighlightAdjustment), 0, validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="HistogramsPanelSize"/>.
	/// </summary>
	public static readonly ObservableProperty<double> HistogramsPanelSizeProperty = ObservableProperty.Register<Session, double>(nameof(HistogramsPanelSize), 170,
		coerce: (_, it) =>
		{
			if (it >= MaxHistogramsPanelSize)
				return MaxHistogramsPanelSize;
			if (it <= MinHistogramsPanelSize)
				return MinHistogramsPanelSize;
			return it;
		},
		validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="Histograms"/>.
	/// </summary>
	public static readonly ObservableProperty<BitmapHistograms?> HistogramsProperty = ObservableProperty.Register<Session, BitmapHistograms?>(nameof(Histograms));
	/// <summary>
	/// Property of <see cref="ImageDisplayRotation"/>.
	/// </summary>
	public static readonly ObservableProperty<double> ImageDisplayRotationProperty = ObservableProperty.Register<Session, double>(nameof(ImageDisplayRotation));
	/// <summary>
	/// Property of <see cref="ImageDisplayScale"/>.
	/// </summary>
	public static readonly ObservableProperty<double> ImageDisplayScaleProperty = ObservableProperty.Register<Session, double>(nameof(ImageDisplayScale), double.NaN);
	/// <summary>
	/// Property of <see cref="ImageDisplaySize"/>.
	/// </summary>
	public static readonly ObservableProperty<Size> ImageDisplaySizeProperty = ObservableProperty.Register<Session, Size>(nameof(ImageDisplaySize));
	/// <summary>
	/// Property of <see cref="ImageHeight"/>.
	/// </summary>
	public static readonly ObservableProperty<int> ImageHeightProperty = ObservableProperty.Register<Session, int>(nameof(ImageHeight), 1, coerce: (_, it) => Math.Max(1, it));
	/// <summary>
	/// Property of <see cref="ImagePlaneCount"/>.
	/// </summary>
	public static readonly ObservableProperty<int> ImagePlaneCountProperty = ObservableProperty.Register<Session, int>(nameof(ImagePlaneCount), 1);
	/// <summary>
	/// Property of <see cref="ImageViewportSize"/>.
	/// </summary>
	public static readonly ObservableProperty<Size> ImageViewportSizeProperty = ObservableProperty.Register<Session, Size>(nameof(ImageViewportSize));
	/// <summary>
	/// Property of <see cref="ImageRenderer"/>.
	/// </summary>
	public static readonly ObservableProperty<IImageRenderer?> ImageRendererProperty = ObservableProperty.Register<Session, IImageRenderer?>(nameof(ImageRenderer));
	/// <summary>
	/// Property of <see cref="ImageWidth"/>.
	/// </summary>
	public static readonly ObservableProperty<int> ImageWidthProperty = ObservableProperty.Register<Session, int>(nameof(ImageWidth), 1, coerce: (_, it) => Math.Max(1, it));
	/// <summary>
	/// Property of <see cref="InsufficientMemoryForRenderedImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> InsufficientMemoryForRenderedImageProperty = ObservableProperty.Register<Session, bool>(nameof(InsufficientMemoryForRenderedImage));
	/// <summary>
	/// Property of <see cref="IsActivated"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsActivatedProperty = ObservableProperty.Register<Session, bool>(nameof(IsActivated));
	/// <summary>
	/// Property of <see cref="IsAdjustableEffectiveBits1"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsAdjustableEffectiveBits1Property = ObservableProperty.Register<Session, bool>(nameof(IsAdjustableEffectiveBits1));
	/// <summary>
	/// Property of <see cref="IsAdjustableEffectiveBits2"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsAdjustableEffectiveBits2Property = ObservableProperty.Register<Session, bool>(nameof(IsAdjustableEffectiveBits2));
	/// <summary>
	/// Property of <see cref="IsAdjustableEffectiveBits3"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsAdjustableEffectiveBits3Property = ObservableProperty.Register<Session, bool>(nameof(IsAdjustableEffectiveBits3));
	/// <summary>
	/// Property of <see cref="IsAdjustablePixelStride1"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsAdjustablePixelStride1Property = ObservableProperty.Register<Session, bool>(nameof(IsAdjustablePixelStride1));
	/// <summary>
	/// Property of <see cref="IsAdjustablePixelStride2"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsAdjustablePixelStride2Property = ObservableProperty.Register<Session, bool>(nameof(IsAdjustablePixelStride2));
	/// <summary>
	/// Property of <see cref="IsAdjustablePixelStride3"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsAdjustablePixelStride3Property = ObservableProperty.Register<Session, bool>(nameof(IsAdjustablePixelStride3));
	/// <summary>
	/// Property of <see cref="IsAlphaChannelAvailable"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsAlphaChannelAvailableProperty = ObservableProperty.Register<Session, bool>(nameof(IsAlphaChannelAvailable));
	/// <summary>
	/// Property of <see cref="IsBayerPatternSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsBayerPatternSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsBayerPatternSupported));
	/// <summary>
	/// Property of <see cref="IsBrightnessAdjustmentSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsBrightnessAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsBrightnessAdjustmentSupported));
	/// <summary>
	/// Property of <see cref="IsColorAdjustmentSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsColorAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsColorAdjustmentSupported));
	/// <summary>
	/// Property of <see cref="IsColorSpaceManagementEnabled"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsColorSpaceManagementEnabledProperty = ObservableProperty.Register<Session, bool>(nameof(IsColorSpaceManagementEnabled));
	/// <summary>
	/// Property of <see cref="IsCompressedImageFormat"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsCompressedImageFormatProperty = ObservableProperty.Register<Session, bool>(nameof(IsCompressedImageFormat));
	/// <summary>
	/// Property of <see cref="IsContrastAdjustmentSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsContrastAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsContrastAdjustmentSupported));
	/// <summary>
	/// Property of <see cref="IsConvertingColorSpace"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsConvertingColorSpaceProperty = ObservableProperty.Register<Session, bool>(nameof(IsConvertingColorSpace));
	/// <summary>
	/// Property of <see cref="IsDemosaicingSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsDemosaicingSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsDemosaicingSupported));
	/// <summary>
	/// Property of <see cref="IsFilteringRenderedImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsFilteringRenderedImageProperty = ObservableProperty.Register<Session, bool>(nameof(IsFilteringRenderedImage));
	/// <summary>
	/// Property of <see cref="IsFilteringRenderedImageNeeded"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsFilteringRenderedImageNeededProperty = ObservableProperty.Register<Session, bool>(nameof(IsFilteringRenderedImageNeeded));
	/// <summary>
	/// Property of <see cref="IsGrayscaleFilterEnabled"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsGrayscaleFilterEnabledProperty = ObservableProperty.Register<Session, bool>(nameof(IsGrayscaleFilterEnabled));
	/// <summary>
	/// Property of <see cref="IsGrayscaleFilterSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsGrayscaleFilterSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsGrayscaleFilterSupported));
	/// <summary>
	/// Property of <see cref="IsHibernated"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsHibernatedProperty = ObservableProperty.Register<Session, bool>(nameof(IsHibernated));
	/// <summary>
	/// Property of <see cref="IsHighlightAdjustmentSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsHighlightAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsHighlightAdjustmentSupported));
	/// <summary>
	/// Property of <see cref="IsHistogramMeanMarkerVisible"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsHistogramMeanMarkerVisibleProperty = ObservableProperty.Register<Session, bool>(nameof(IsHistogramMeanMarkerVisible), true);
	/// <summary>
	/// Property of <see cref="IsHistogramsVisible"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsHistogramsVisibleProperty = ObservableProperty.Register<Session, bool>(nameof(IsHistogramsVisible));
	/// <summary>
	/// Property of <see cref="IsImageFlippedX"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsImageFlippedXProperty = ObservableProperty.Register<Session, bool>(nameof(IsImageFlippedX));
	/// <summary>
	/// Property of <see cref="IsImageFlippedY"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsImageFlippedYProperty = ObservableProperty.Register<Session, bool>(nameof(IsImageFlippedY));
	/// <summary>
	/// Property of <see cref="IsOpeningSource"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsOpeningSourceProperty = ObservableProperty.Register<Session, bool>(nameof(IsOpeningSource));
	/// <summary>
	/// Property of <see cref="IsFramePlaybackLooping"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsFramePlaybackLoopingProperty = ObservableProperty.Register<Session, bool>(nameof(IsFramePlaybackLooping), true);
	/// <summary>
	/// Property of <see cref="IsFramePlaybackRateUnlimited"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsFramePlaybackRateUnlimitedProperty = ObservableProperty.Register<Session, bool>(nameof(IsFramePlaybackRateUnlimited));
	/// <summary>
	/// Property of <see cref="IsPlayingFrames"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsPlayingFramesProperty = ObservableProperty.Register<Session, bool>(nameof(IsPlayingFrames));
	/// <summary>
	/// Property of <see cref="IsProcessingImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsProcessingImageProperty = ObservableProperty.Register<Session, bool>(nameof(IsProcessingImage));
	/// <summary>
	/// Property of <see cref="IsProVersionActivated"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsProVersionActivatedProperty = ObservableProperty.Register<Session, bool>(nameof(IsProVersionActivated));
	/// <summary>
	/// Property of <see cref="IsRenderingImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsRenderingImageProperty = ObservableProperty.Register<Session, bool>(nameof(IsRenderingImage));
	/// <summary>
	/// Property of <see cref="IsRenderingParametersPanelVisible"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsRenderingParametersPanelVisibleProperty = ObservableProperty.Register<Session, bool>(nameof(IsRenderingParametersPanelVisible), true);
	/// <summary>
	/// Property of <see cref="IsRgbGainSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsRgbGainSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsRgbGainSupported));
	/// <summary>
	/// Property of <see cref="IsSaturationAdjustmentSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsSaturationAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsSaturationAdjustmentSupported));
	/// <summary>
	/// Property of <see cref="IsSavingFilteredImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsSavingFilteredImageProperty = ObservableProperty.Register<Session, bool>(nameof(IsSavingFilteredImage));
	/// <summary>
	/// Property of <see cref="IsSavingImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsSavingImageProperty = ObservableProperty.Register<Session, bool>(nameof(IsSavingImage));
	/// <summary>
	/// Property of <see cref="IsSavingRenderedImage"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsSavingRenderedImageProperty = ObservableProperty.Register<Session, bool>(nameof(IsSavingRenderedImage));
	/// <summary>
	/// Property of <see cref="IsShadowAdjustmentSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsShadowAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsShadowAdjustmentSupported));
	/// <summary>
	/// Property of <see cref="IsSourceOpened"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsSourceOpenedProperty = ObservableProperty.Register<Session, bool>(nameof(IsSourceOpened));
	/// <summary>
	/// Property of <see cref="IsVibranceAdjustmentSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsVibranceAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsVibranceAdjustmentSupported));
	/// <summary>
	/// Property of <see cref="IsYuvToBgraConverterSupported"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsYuvToBgraConverterSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsYuvToBgraConverterSupported));
	/// <summary>
	/// Property of <see cref="IsZooming"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> IsZoomingProperty = ObservableProperty.Register<Session, bool>(nameof(IsZooming));
	/// <summary>
	/// Property of <see cref="LuminanceHistogramGeometry"/>.
	/// </summary>
	public static readonly ObservableProperty<Geometry?> LuminanceHistogramGeometryProperty = ObservableProperty.Register<Session, Geometry?>(nameof(LuminanceHistogramGeometry));
	/// <summary>
	/// Property of <see cref="Profile"/>.
	/// </summary>
	public static readonly ObservableProperty<ImageRenderingProfile> ProfileProperty = ObservableProperty.Register<Session, ImageRenderingProfile>(nameof(Profile), ImageRenderingProfile.Default);
	/// <summary>
	/// Property of <see cref="QuarterSizeRenderedImage"/>.
	/// </summary>
	public static readonly ObservableProperty<Bitmap?> QuarterSizeRenderedImageProperty = ObservableProperty.Register<Session, Bitmap?>(nameof(QuarterSizeRenderedImage));
	/// <summary>
	/// Property of <see cref="RedColorAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> RedColorAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(RedColorAdjustment), 0, validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="RedColorGain"/>.
	/// </summary>
	public static readonly ObservableProperty<double> RedColorGainProperty = ObservableProperty.Register<Session, double>(nameof(RedColorGain), 1.0, coerce: (_, it) => ImageRenderingOptions.GetValidRgbGain(it));
	/// <summary>
	/// Property of <see cref="RenderedImage"/>.
	/// </summary>
	public static readonly ObservableProperty<Bitmap?> RenderedImageProperty = ObservableProperty.Register<Session, Bitmap?>(nameof(RenderedImage));
	/// <summary>
	/// Property of <see cref="RenderedImagesMemoryUsage"/>.
	/// </summary>
	public static readonly ObservableProperty<long> RenderedImagesMemoryUsageProperty = ObservableProperty.Register<Session, long>(nameof(RenderedImagesMemoryUsage));
	/// <summary>
	/// Property of <see cref="RenderingParametersPanelSize"/>.
	/// </summary>
	public static readonly ObservableProperty<double> RenderingParametersPanelSizeProperty = ObservableProperty.Register<Session, double>(nameof(RenderingParametersPanelSize), (MinRenderingParametersPanelSize + MaxRenderingParametersPanelSize) / 2, 
		coerce: (_, it) =>
		{
			if (it >= MaxRenderingParametersPanelSize)
				return MaxRenderingParametersPanelSize;
			if (it <= MinRenderingParametersPanelSize)
				return MinRenderingParametersPanelSize;
			return it;
		}, 
		validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="RequestedImageDisplayScale"/>.
	/// </summary>
	public static readonly ObservableProperty<double> RequestedImageDisplayScaleProperty = ObservableProperty.Register<Session, double>(nameof(RequestedImageDisplayScale), 1.0,
		coerce: (_, it) =>
		{
			if (it < MinRenderedImageScale)
				return MinRenderedImageScale;
			if (it > MaxRenderedImageScale)
				return MaxRenderedImageScale;
			return it;
		},
		validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="SaturationAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> SaturationAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(SaturationAdjustment), 0, 
		coerce: (_, it) => 
		{
			if (it < -1)
				return -1;
			if (it > 1)
				return 1;
			return it;
		},
		validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="ScreenPixelDensity"/>.
	/// </summary>
	public static readonly ObservableProperty<double> ScreenPixelDensityProperty = ObservableProperty.Register<Session, double>(nameof(ScreenPixelDensity), 1, 
		coerce: (_, it) => Math.Max(1, it),
		validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="SelectedRenderedImagePixelColor"/>.
	/// </summary>
	public static readonly ObservableProperty<Color64> SelectedRenderedImagePixelColorProperty = ObservableProperty.Register<Session, Color64>(nameof(SelectedRenderedImagePixelColor));
	/// <summary>
	/// Property of <see cref="SelectedRenderedImagePixelLabColor"/>.
	/// </summary>
	public static readonly ObservableProperty<Tuple<double, double, double>> SelectedRenderedImagePixelLabColorProperty = ObservableProperty.Register<Session, Tuple<double, double, double>>(nameof(SelectedRenderedImagePixelLabColor), new(0, 0, 0));
	/// <summary>
	/// Property of <see cref="SelectedRenderedImagePixelXyzColor"/>.
	/// </summary>
	public static readonly ObservableProperty<Tuple<double, double, double>> SelectedRenderedImagePixelXyzColorProperty = ObservableProperty.Register<Session, Tuple<double, double, double>>(nameof(SelectedRenderedImagePixelXyzColor), new(0, 0, 0));
	/// <summary>
	/// Property of <see cref="SelectedRenderedImagePixelPositionX"/>.
	/// </summary>
	public static readonly ObservableProperty<int> SelectedRenderedImagePixelPositionXProperty = ObservableProperty.Register<Session, int>(nameof(SelectedRenderedImagePixelPositionX), -1);
	/// <summary>
	/// Property of <see cref="SelectedRenderedImagePixelPositionY"/>.
	/// </summary>
	public static readonly ObservableProperty<int> SelectedRenderedImagePixelPositionYProperty = ObservableProperty.Register<Session, int>(nameof(SelectedRenderedImagePixelPositionY), -1);
	/// <summary>
	/// Property of <see cref="ShadowAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> ShadowAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(ShadowAdjustment), 0, validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="SourceDataSize"/>.
	/// </summary>
	public static readonly ObservableProperty<long> SourceDataSizeProperty = ObservableProperty.Register<Session, long>(nameof(SourceDataSize));
	/// <summary>
	/// Property of <see cref="SourceFileName"/>.
	/// </summary>
	public static readonly ObservableProperty<string?> SourceFileNameProperty = ObservableProperty.Register<Session, string?>(nameof(SourceFileName));
	/// <summary>
	/// Property of <see cref="SourceSizeString"/>.
	/// </summary>
	public static readonly ObservableProperty<string?> SourceSizeStringProperty = ObservableProperty.Register<Session, string?>(nameof(SourceSizeString));
	/// <summary>
	/// Property of <see cref="SourceImageEffectiveBits"/>.
	/// </summary>
	public static readonly ObservableProperty<int> SourceImageEffectiveBitsProperty = ObservableProperty.Register<Session, int>(nameof(SourceImageEffectiveBits), 8);
	/// <summary>
	/// Property of <see cref="TotalRenderedImagesMemoryUsage"/>.
	/// </summary>
	public static readonly ObservableProperty<long> TotalRenderedImagesMemoryUsageProperty = ObservableProperty.Register<Session, long>(nameof(TotalRenderedImagesMemoryUsage));
	/// <summary>
	/// Property of <see cref="UseLinearColorSpace"/>.
	/// </summary>
	public static readonly ObservableProperty<bool> UseLinearColorSpaceProperty = ObservableProperty.Register<Session, bool>(nameof(UseLinearColorSpace), false);
	/// <summary>
	/// Property of <see cref="VibranceAdjustment"/>.
	/// </summary>
	public static readonly ObservableProperty<double> VibranceAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(VibranceAdjustment), 0, 
		coerce: (_, it) => 
		{
			if (it < -1)
				return -1;
			if (it > 1)
				return 1;
			return it;
		},
		validate: double.IsFinite);
	/// <summary>
	/// Property of <see cref="YuvToBgraConverter"/>.
	/// </summary>
	public static readonly ObservableProperty<YuvToBgraConverter> YuvToBgraConverterProperty = ObservableProperty.Register<Session, YuvToBgraConverter>(nameof(YuvToBgraConverter), YuvToBgraConverter.Default);


	// Constants.
	/// <summary>
	/// Maximum frame rate (frames per second) for frame sequence playback.
	/// </summary>
	public const int MaxFramePlaybackRate = 60;
	/// <summary>
	/// Minimum frame rate (frames per second) for frame sequence playback.
	/// </summary>
	public const int MinFramePlaybackRate = 1;
	const int ReleaseCachedImagesDelay = 3000;
	const int RenderImageDelay = 500;
	const int TrackFilteringParamsAppliedEventDelay = 5000;
	const int TrackFilteringPerfDuration = 5000;
	const int TrackRenderingParamsAppliedEventDelay = 5000;
	const int TrackRenderingPerfDuration = 5000;


	// Static fields.
	static readonly SettingKey<bool> IsInitFramePlaybackLooping = new("Session.IsInitFramePlaybackLooping", true);
	static readonly SettingKey<bool> IsInitFramePlaybackRateUnlimited = new("Session.IsInitFramePlaybackRateUnlimited", false);
	static readonly SettingKey<bool> IsInitHistogramMeanMarkerVisible = new("Session.IsInitHistogramMeanMarkerVisible", true);
	static readonly SettingKey<bool> IsInitHistogramsPanelVisible = new("Session.IsInitHistogramsPanelVisible", false);
	static readonly SettingKey<int> LatestFramePlaybackRate = new("Session.LatestFramePlaybackRate", FramePlaybackRateProperty.DefaultValue);
	static readonly SettingKey<int> LatestHistogramsPanelSize = new("Session.LatestHistogramsPanelSize", (int)(HistogramsPanelSizeProperty.DefaultValue + 0.5));
	static readonly SettingKey<int> LatestRenderingParamsPanelSize = new("Session.LatestRenderingParamsPanelSize", (int)(RenderingParametersPanelSizeProperty.DefaultValue + 0.5));
	static readonly MutableObservableInt64 SharedRenderedImagesMemoryUsage = new();
	static readonly Func<double, double> ZoomingInterpolator = Interpolators.FastDeceleration;


	// Fields.
	readonly List<ActivationToken> activationTokens = new();
	ColorTable? alphaColorTable;
	IDisposable? avaQuarterSizeRenderedImageMemoryUsageToken;
	IDisposable? avaRenderedImageMemoryUsageToken;
	readonly uint[] blackLevels = new uint[ImageFormat.MaxPlaneCount];
	ColorTable? blueColorTable;
	WriteableBitmap? cachedAvaQuarterSizeRenderedImage;
	IDisposable? cachedAvaQuarterSizeRenderedImageMemoryUsageToken;
	WriteableBitmap? cachedAvaRenderedImage;
	IDisposable? cachedAvaRenderedImageMemoryUsageToken;
	readonly List<ImageFrame> cachedFilteredImageFrames = new(2);
	ImageFrame? cachedMosaicImageFrame;
	readonly MutableObservableBoolean canApplyProfile = new();
	readonly MutableObservableBoolean canMoveToNextFrame = new();
	readonly MutableObservableBoolean canMoveToPreviousFrame = new();
	readonly MutableObservableBoolean canOpenSource = new(true);
	readonly MutableObservableBoolean canPlayFrames = new();
	readonly MutableObservableBoolean canResetBrightnessAdjustment = new();
	readonly MutableObservableBoolean canResetColorAdjustment = new();
	readonly MutableObservableBoolean canResetContrastAdjustment = new();
	readonly MutableObservableBoolean canResetHighlightAdjustment = new();
	readonly MutableObservableBoolean canResetSaturationAdjustment = new();
	readonly MutableObservableBoolean canResetShadowAdjustment = new();
	readonly MutableObservableBoolean canResetVibranceAdjustment = new();
	readonly MutableObservableBoolean canSaveAsNewProfile = new();
	readonly MutableObservableBoolean canSaveOrDeleteProfile = new();
	readonly MutableObservableBoolean canSaveFilteredImage = new();
	readonly MutableObservableBoolean canSaveRenderedImage = new();
	readonly MutableObservableBoolean canSelectColorAdjustment = new();
	readonly MutableObservableBoolean canSelectRgbGain = new();
	readonly MutableObservableBoolean canZoomIn = new();
	readonly MutableObservableBoolean canZoomOut = new();
	readonly MutableObservableBoolean canZoomTo = new();
	ImageFrame? colorSpaceConvertedImageFrame;
	readonly SortedObservableList<ColorSpace> colorSpaces = new((lhs, rhs) =>
	{
		if (lhs.IsEmbedded)
			return rhs.IsEmbedded ? string.CompareOrdinal(lhs.Name, rhs.Name) : -1;
		if (rhs.IsEmbedded)
			return 1;
		if (lhs.IsBuiltIn)
			return rhs.IsBuiltIn ? string.CompareOrdinal(lhs.Name, rhs.Name) : -1;
		if (rhs.IsBuiltIn)
			return 1;
		if (lhs.IsSystemDefined)
			return rhs.IsSystemDefined ? string.CompareOrdinal(lhs.Name, rhs.Name) : -1;
		if (rhs.IsSystemDefined)
			return 1;
		return string.CompareOrdinal(lhs.Name, rhs.Name);
	});
	int colorTableBitDepth;
	readonly ObservableList<DemosaicingAlgorithm> demosaicingAlgorithms = new();
	readonly int[] effectiveBits = new int[ImageFormat.MaxPlaneCount];
	readonly Observer<ColorSpace> effectiveScreenColorSpaceObserver;
	IDisposable? effectiveScreenColorSpaceObserverToken;
	ImageRenderingProfile? fileFormatProfile;
	readonly ScheduledAction filterImageAction;
	ImageFrame? filteredImageFrame;
	long filteringPerfLargestDurationMs;
	string? filteringPerfLargestFilters;
	long filteringPerfLargestPixelCount;
	long filteringPerfLongestDurationMs;
	string? filteringPerfLongestFilters;
	long filteringPerfLongestPixelCount;
	int filteringPerfSampleCount;
	double fitRenderedImageToViewportScale = double.NaN;
	double fitRenderedImageToViewportScaleSwapped = double.NaN;
	int frameNavigationCount;
	bool hasPendingImageRendering;
	IImageDataSource? frameImageDataSource;
	long framePlaybackBaseFrameNumber;
	double framePlaybackBaseTime;
	long framePlaybackNextFrameNumber;
	readonly Stopwatch framePlaybackStopwatch = new();
	ColorTable? greenColorTable;
	IImageDataSource? imageDataSource;
	CancellationTokenSource? imageFilteringCancellationTokenSource;
	TaskCompletionSource? imageFilteringCompletionSource;
	int imageFilteringRequestId;
	CancellationTokenSource? imageRenderingCancellationTokenSource;
	TaskCompletionSource? imageRenderingCompletionSource;
	int imageRenderingRequestId;
	CancellationTokenSource? imageReportingCancellationTokenSource;
	DoubleAnimator? imageScalingAnimator;
	bool isImageDimensionsEvaluationNeeded = true;
	bool isImagePlaneOptionsResetNeeded = true;
	bool isImageRenderingForced;
	readonly int[] pixelStrides = new int[ImageFormat.MaxPlaneCount];
	readonly ScheduledAction playFrameAction;
	readonly SortedObservableList<ImageRenderingProfile> profiles = new(CompareProfiles);
	ColorTable? redColorTable;
	readonly ScheduledAction releasedCachedImagesAction;
	ImageFrame? renderedImageFrame;
	readonly ScheduledAction renderImageAction;
	long renderingPerfLargestDurationMs;
	long renderingPerfLargestPixelCount;
	string? renderingPerfLargestRendererName;
	long renderingPerfLongestDurationMs;
	long renderingPerfLongestPixelCount;
	string? renderingPerfLongestRendererName;
	int renderingPerfSampleCount;
	readonly int[] rowStrides = new int[ImageFormat.MaxPlaneCount];
	readonly IDisposable sharedRenderedImagesMemoryUsageObserverToken;
	readonly ScheduledAction trackFilteringParamsAppliedAction;
	readonly ScheduledAction trackFilteringPerfAction;
	readonly ScheduledAction trackRenderingParamsAppliedAction;
	readonly ScheduledAction trackRenderingPerfAction;
	readonly ScheduledAction updateFilterSupportingAction;
	readonly ScheduledAction updateImageDisplaySizeAction;
	readonly ScheduledAction updateIsFilteringImageNeededAction;
	readonly ScheduledAction updateIsProcessingImageAction;
	readonly uint[] whiteLevels = new uint[ImageFormat.MaxPlaneCount];


	/// <summary>
	/// Initialize new <see cref="Session"/> instance.
	/// </summary>
	public Session(IAppSuiteApplication app, JsonElement? savedState) : base(app)
	{
		// create commands
		var isSrcFileOpenedObservable = this.GetValueAsObservable(IsSourceOpenedProperty);
		this.AlignImageHeightCommand = new Command<int>(this.AlignImageHeight, isSrcFileOpenedObservable);
		this.AlignImageWidthCommand = new Command<int>(this.AlignImageWidth, isSrcFileOpenedObservable);
		this.AlignRowStride1Command = new Command<int>(this.AlignRowStride1, isSrcFileOpenedObservable);
		this.AlignRowStride2Command = new Command<int>(this.AlignRowStride2, isSrcFileOpenedObservable);
		this.AlignRowStride3Command = new Command<int>(this.AlignRowStride3, isSrcFileOpenedObservable);
		this.ApplyProfileCommand = new Command(this.ApplyProfile, this.canApplyProfile);
		this.ClearSourceCommand = new Command(this.ClearSource, isSrcFileOpenedObservable);
		this.DeleteProfileCommand = new Command(this.DeleteProfile, this.canSaveOrDeleteProfile);
		this.EvaluateImageDimensionsCommand = new Command<AspectRatio>(this.EvaluateImageDimensions, isSrcFileOpenedObservable);
		this.FlipXCommand = new Command(this.FlipX, isSrcFileOpenedObservable);
		this.FlipYCommand = new Command(this.FlipY, isSrcFileOpenedObservable);
		this.MoveToFirstFrameCommand = new Command(() =>
		{
			if (this.canMoveToPreviousFrame.Value)
				this.FrameNumber = 1;
		}, this.canMoveToPreviousFrame);
		this.MoveToLastFrameCommand = new Command(() =>
		{
			if (this.canMoveToNextFrame.Value)
				this.FrameNumber = this.FrameCount;
		}, this.canMoveToNextFrame);
		this.MoveToNextFrameCommand = new Command(() =>
		{
			if (this.canMoveToNextFrame.Value)
				++this.FrameNumber;
		}, this.canMoveToNextFrame);
		this.MoveToPreviousFrameCommand = new Command(() =>
		{
			if (this.canMoveToPreviousFrame.Value)
				--this.FrameNumber;
		}, this.canMoveToPreviousFrame);
		this.OpenSourceFileCommand = new Command<string>(filePath => _ = this.OpenSourceFile(filePath), this.canOpenSource);
		this.OpenSourceFilesCommand = new Command<IList<string>>(fileNames => _ = this.OpenSourceFiles(fileNames), this.canOpenSource);
		this.PlayFramesCommand = new Command(this.TogglePlayingFrames, this.canPlayFrames);
		this.RenderImageCommand = new Command(() => _ = this.ClearAndRenderImageAsync(), this.GetValueAsObservable(IsSourceOpenedProperty));
		this.ResetBrightnessAdjustmentCommand = new Command(this.ResetBrightnessAdjustment, this.canResetBrightnessAdjustment);
		this.ResetColorAdjustmentCommand = new Command(this.ResetColorAdjustment, this.canResetColorAdjustment);
		this.ResetContrastAdjustmentCommand = new Command(this.ResetContrastAdjustment, this.canResetContrastAdjustment);
		this.ResetHighlightAdjustmentCommand = new Command(this.ResetHighlightAdjustment, this.canResetHighlightAdjustment);
		this.ResetRgbGainCommand = new Command(this.ResetRgbGain, this.GetValueAsObservable(HasRgbGainProperty));
		this.ResetSaturationAdjustmentCommand = new Command(this.ResetSaturationAdjustment, this.canResetSaturationAdjustment);
		this.ResetShadowAdjustmentCommand = new Command(this.ResetShadowAdjustment, this.canResetShadowAdjustment);
		this.ResetVibranceAdjustmentCommand = new Command(this.ResetVibranceAdjustment, this.canResetVibranceAdjustment);
		this.RotateLeftCommand = new Command(this.RotateLeft, isSrcFileOpenedObservable);
		this.RotateRightCommand = new Command(this.RotateRight, isSrcFileOpenedObservable);
		this.SaveAsNewProfileCommand = new Command<string>(this.SaveAsNewProfile, this.canSaveAsNewProfile);
		this.SaveFilteredImageCommand = new Command<ImageSavingParams>(this.SaveFilteredImage, this.canSaveFilteredImage);
		this.SaveProfileCommand = new Command(this.SaveProfile, this.canSaveOrDeleteProfile);
		this.SaveRenderedImageCommand = new Command<ImageSavingParams>(this.SaveRenderedImage, this.canSaveRenderedImage);
		this.SelectColorAdjustmentCommand = new Command(this.SelectColorAdjustment, this.canSelectColorAdjustment);
		this.SelectRgbGainCommand = new Command(this.SelectRgbGain, this.canSelectRgbGain);
		this.ZoomInCommand = new Command(this.ZoomIn, this.canZoomIn);
		this.ZoomOutCommand = new Command(this.ZoomOut, this.canZoomOut);
		this.ZoomToCommand = new Command<double>(scale => 
		{
			scale = this.ZoomTo(scale);
			if (double.IsFinite(scale))
				this.SetValue(RequestedImageDisplayScaleProperty, scale);
		}, this.canZoomTo);

		// setup operations
		this.effectiveScreenColorSpaceObserver = new(_ => this.OnScreenColorSpaceChanged());
		this.filterImageAction = new ScheduledAction(() => _ = this.FilterImage());
		this.releasedCachedImagesAction = new ScheduledAction(() => this.ReleaseCachedImages());
		this.renderImageAction = new ScheduledAction(this.RenderImage, true);
		this.playFrameAction = new ScheduledAction(this.PlayNextFrame);
		this.trackFilteringParamsAppliedAction = new(() =>
		{
			if (!this.GetValue(IsSourceOpenedProperty))
				return;
			if (!this.GetValue(IsFilteringRenderedImageNeededProperty))
				return;
			var properties = this.PrepareFilteringParamsTrackingProperties();
			this.Application.UsageManager.TrackEvent(UsageEvents.FilteringParamsApplied, properties);
		});
		this.trackFilteringPerfAction = new(() =>
		{
			// defer until current filtering completes so the window covers a full filter pass
			if (this.IsFilteringRenderedImage)
			{
				this.trackFilteringPerfAction!.Reschedule(TrackFilteringPerfDuration);
				return;
			}

			// nothing to report
			if (this.filteringPerfSampleCount <= 0)
				return;

			// emit metrics
			var um = this.Application.UsageManager;
			var id = this.Id.ToString(CultureInfo.InvariantCulture);
			var filterCount = this.filteringPerfSampleCount.ToString(CultureInfo.InvariantCulture);
			var largestMP = (long)Math.Round(this.filteringPerfLargestPixelCount / 1_000_000.0);
			var longestMP = (long)Math.Round(this.filteringPerfLongestPixelCount / 1_000_000.0);
			um.TrackMetric(UsageMetrics.LargestFilteredImageDimensionMP, largestMP, new Dictionary<string, string>
			{
				[UsageProperties.Duration] = this.filteringPerfLargestDurationMs.ToString(CultureInfo.InvariantCulture),
				[UsageProperties.FilterCount] = filterCount,
				[UsageProperties.Filters] = this.filteringPerfLargestFilters ?? "",
				[UsageProperties.Id] = id,
			});
			um.TrackMetric(UsageMetrics.LongestFilteringDuration, this.filteringPerfLongestDurationMs, new Dictionary<string, string>
			{
				[UsageProperties.DimensionMP] = longestMP.ToString(CultureInfo.InvariantCulture),
				[UsageProperties.FilterCount] = filterCount,
				[UsageProperties.Filters] = this.filteringPerfLongestFilters ?? "",
				[UsageProperties.Id] = id,
			});

			// reset window
			this.ResetFilteringPerfWindow();
		});
		this.trackRenderingPerfAction = new(() =>
		{
			// defer until current rendering completes so the window covers a full render
			if (this.IsRenderingImage)
			{
				this.trackRenderingPerfAction!.Reschedule(TrackRenderingPerfDuration);
				return;
			}

			// nothing to report
			if (this.renderingPerfSampleCount <= 0)
				return;

			// emit metrics
			var um = this.Application.UsageManager;
			var id = this.Id.ToString(CultureInfo.InvariantCulture);
			var renderCount = this.renderingPerfSampleCount.ToString(CultureInfo.InvariantCulture);
			var largestMP = (long)Math.Round(this.renderingPerfLargestPixelCount / 1_000_000.0);
			var longestMP = (long)Math.Round(this.renderingPerfLongestPixelCount / 1_000_000.0);
			um.TrackMetric(UsageMetrics.LargestRenderedImageDimensionMP, largestMP, new Dictionary<string, string>
			{
				[UsageProperties.Duration] = this.renderingPerfLargestDurationMs.ToString(CultureInfo.InvariantCulture),
				[UsageProperties.Id] = id,
				[UsageProperties.ImageRenderer] = this.renderingPerfLargestRendererName ?? "Unknown",
				[UsageProperties.RenderCount] = renderCount,
			});
			um.TrackMetric(UsageMetrics.LongestRenderingDuration, this.renderingPerfLongestDurationMs, new Dictionary<string, string>
			{
				[UsageProperties.DimensionMP] = longestMP.ToString(CultureInfo.InvariantCulture),
				[UsageProperties.Id] = id,
				[UsageProperties.ImageRenderer] = this.renderingPerfLongestRendererName ?? "Unknown",
				[UsageProperties.RenderCount] = renderCount,
			});

			// reset window
			this.ResetRenderingPerfWindow();
		});
		this.trackRenderingParamsAppliedAction = new(() =>
		{
			if (!this.GetValue(IsSourceOpenedProperty))
				return;
			var imageRenderer = this.GetValue(ImageRendererProperty);
			if (imageRenderer is null || !ImageRenderers.All.Contains(imageRenderer))
				return;
			if (!this.GetValue(HasRenderedImageProperty))
			{
				this.trackRenderingParamsAppliedAction!.Reschedule(TrackRenderingParamsAppliedEventDelay);
				return;
			}
			var profile = this.GetValue(ProfileProperty);
			var properties = this.PrepareUsageTrackingProperties().Also(properties =>
			{
				properties[UsageProperties.ColorSpace] = this.GetValue(ColorSpaceProperty).Name;
				properties[UsageProperties.ImageRenderer] = imageRenderer.Format.Name;
				properties[UsageProperties.IsColorSpaceManagementEnabled] = this.GetValue(IsColorSpaceManagementEnabledProperty).ToString(CultureInfo.InvariantCulture);
				properties[UsageProperties.IsFileFormatProfile] = profile.IsFileFormat.ToString(CultureInfo.InvariantCulture);
				properties[UsageProperties.Profile] = profile.Type == ImageRenderingProfileType.UserDefined
					? "UserDefined"
					: profile.Name;
				properties[UsageProperties.UseLinearColorSpace] = this.GetValue(UseLinearColorSpaceProperty).ToString(CultureInfo.InvariantCulture);
				if (this.GetValue(IsYuvToBgraConverterSupportedProperty))
					properties[UsageProperties.YuvToRgbaConversion] = this.GetValue(YuvToBgraConverterProperty).Name;
			});
			this.Application.UsageManager.TrackEvent(UsageEvents.RenderingParamsApplied, properties);
		});
		this.updateFilterSupportingAction = new ScheduledAction(() =>
		{
			if (this.IsDisposed)
				return;
			if (!this.IsSourceOpened)
			{
				this.SetValue(IsBrightnessAdjustmentSupportedProperty, false);
				this.SetValue(IsColorAdjustmentSupportedProperty, false);
				this.SetValue(IsContrastAdjustmentSupportedProperty, false);
				this.SetValue(IsGrayscaleFilterSupportedProperty, false);
				this.SetValue(IsHighlightAdjustmentSupportedProperty, false);
				this.SetValue(IsSaturationAdjustmentSupportedProperty, false);
				this.SetValue(IsShadowAdjustmentSupportedProperty, false);
				this.SetValue(IsVibranceAdjustmentSupportedProperty, false);
			}
			else
			{
				var format = this.ImageRenderer.Format;
				this.SetValue(IsBrightnessAdjustmentSupportedProperty, true);
				this.SetValue(IsColorAdjustmentSupportedProperty, true);
				this.SetValue(IsContrastAdjustmentSupportedProperty, true);
				this.SetValue(IsGrayscaleFilterSupportedProperty, format.Category != ImageFormatCategory.Luminance);
				this.SetValue(IsHighlightAdjustmentSupportedProperty, true);
				this.SetValue(IsSaturationAdjustmentSupportedProperty, true);
				this.SetValue(IsShadowAdjustmentSupportedProperty, true);
				this.SetValue(IsVibranceAdjustmentSupportedProperty, true);
			}
		});
		this.updateImageDisplaySizeAction = new ScheduledAction(() =>
		{
			// check state
			if (this.IsDisposed)
				return;
			
			// get original image size
			var image = this.GetValue(RenderedImageProperty);
			if (image is null)
			{
				this.ResetValue(ImageDisplaySizeProperty);
				return;
			}
			var screenPixelDensity = this.GetValue(ScreenPixelDensityProperty);
			var imageWidth = image.Size.Width / screenPixelDensity;
			var imageHeight = image.Size.Height / screenPixelDensity;

			// calculate display size
			double scale;
			if (!this.GetValue(FitImageToViewportProperty))
			{
				scale = this.GetValue(ImageDisplayScaleProperty);
				if (!double.IsFinite(scale))
				{
					scale = this.GetValue(RequestedImageDisplayScaleProperty);
					this.SetValue(ImageDisplayScaleProperty, scale);
					this.CompleteZooming(true);
				}
			}
			else if (double.IsFinite(this.fitRenderedImageToViewportScale))
			{
				scale = this.GetValue(ImageDisplayScaleProperty);
				if (!double.IsFinite(scale))
				{
					scale = this.fitRenderedImageToViewportScale;
					this.SetValue(ImageDisplayScaleProperty, scale);
					this.CompleteZooming(true);
				}
			}
			else
			{
				// get size of viewport
				var viewport = this.GetValue(ImageViewportSizeProperty);
				var viewportWidth = viewport.Width;
				var viewportHeight = viewport.Height;
				if (viewportWidth <= 0 || viewportHeight <= 0)
				{
					this.ResetValue(ImageDisplaySizeProperty);
					return;
				}
				var useSwappedScale = (((int)(this.ImageDisplayRotation + 0.5) % 180) != 0);

				// calculate display size
				this.fitRenderedImageToViewportScale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
				this.fitRenderedImageToViewportScaleSwapped = Math.Min(viewportHeight / imageWidth, viewportWidth / imageHeight);
				this.CompleteZooming(true);
				scale = useSwappedScale ? this.fitRenderedImageToViewportScaleSwapped : this.fitRenderedImageToViewportScale;
				this.SetValue(ImageDisplayScaleProperty, scale);
				this.SetValue(ImageDisplaySizeProperty, new Size(imageWidth * scale, imageHeight * scale));
			}
			this.SetValue(ImageDisplaySizeProperty, new Size(imageWidth * scale, imageHeight * scale));
		});
		this.updateIsProcessingImageAction = new ScheduledAction(() =>
		{
			if (this.IsDisposed)
				return;
			this.SetValue(IsProcessingImageProperty, this.IsFilteringRenderedImage
				|| this.IsOpeningSource
				|| this.IsRenderingImage
				|| this.IsSavingImage);
		});
		this.updateIsFilteringImageNeededAction = new ScheduledAction(() =>
		{
			if (this.IsDisposed)
				return;
			this.SetValue(IsFilteringRenderedImageNeededProperty, this.canResetBrightnessAdjustment.Value
				|| this.canResetColorAdjustment.Value
				|| this.canResetContrastAdjustment.Value
				|| this.canResetHighlightAdjustment.Value
				|| this.canResetSaturationAdjustment.Value
				|| this.canResetShadowAdjustment.Value
				|| this.canResetVibranceAdjustment.Value
				|| (this.IsGrayscaleFilterEnabled && this.IsGrayscaleFilterSupported));
		});
		
		// attach to application
		app.PropertyChanged += this.OnApplicationPropertyChanged;
		this.SetValue(IsProVersionActivatedProperty, (app as App)?.IsProVersionActivated == true);

		// setup rendered images memory usage
		this.SetValue(TotalRenderedImagesMemoryUsageProperty, SharedRenderedImagesMemoryUsage.Value);
		this.sharedRenderedImagesMemoryUsageObserverToken = SharedRenderedImagesMemoryUsage.Subscribe(new Observer<long>(this.OnSharedRenderedImagesMemoryUsageChanged));

		// attach to profiles
		this.profiles.Add(ImageRenderingProfile.Default);
		foreach (var profile in ImageRenderingProfiles.UserDefinedProfiles)
		{
			profile.PropertyChanged += this.OnProfilePropertyChanged;
			this.profiles.Add(profile);
		}
		this.Profiles = ListExtensions.AsReadOnly(this.profiles);
		((INotifyCollectionChanged)ImageRenderingProfiles.UserDefinedProfiles).CollectionChanged += this.OnUserDefinedProfilesChanged;

		// select default image renderer
		this.SetValue(ImageRendererProperty, this.SelectDefaultImageRenderer());

		// select default byte ordering
		this.SetValue(ByteOrderingProperty, this.Settings.GetValueOrDefault(SettingKeys.DefaultByteOrdering));

		// attach to color spaces
		this.ColorSpaces = ListExtensions.AsReadOnly(this.colorSpaces);
		this.colorSpaces.AddAll(ColorSpace.AllColorSpaces);
		(ColorSpace.AllColorSpaces as INotifyCollectionChanged)?.Let(it =>
			it.CollectionChanged += this.OnAllColorSpacesChanged);

		// attach to demosaicing algorithms
		this.DemosaicingAlgorithms = ListExtensions.AsReadOnly(this.demosaicingAlgorithms);
		this.UpdateDemosaicingAlgorithms();
		(Media.Demosaicing.DemosaicingAlgorithms.All as INotifyCollectionChanged)?.Let(it =>
			it.CollectionChanged += this.OnAllDemosaicingAlgorithmsChanged);

		// select default YUV to RGB converter
		if (YuvToBgraConverter.TryGetByName(this.Settings.GetValueOrDefault(SettingKeys.DefaultYuvToBgraConversion), out var converter))
			this.SetValue(YuvToBgraConverterProperty, converter);

		// setup color space management
		this.SetValue(IsColorSpaceManagementEnabledProperty, this.Settings.GetValueOrDefault(SettingKeys.EnableColorSpaceManagement));
		if (ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName), out var colorSpace))
			this.SetValue(ColorSpaceProperty, colorSpace);

		// setup title
		this.UpdateTitle();

		// restore state
		if (savedState.HasValue)
			_ = this.RestoreState(savedState.Value);
		else
		{
			this.SetValue(FramePlaybackRateProperty, this.PersistentState.GetValueOrDefault(LatestFramePlaybackRate));
			this.SetValue(HistogramsPanelSizeProperty, this.PersistentState.GetValueOrDefault(LatestHistogramsPanelSize));
			this.SetValue(IsFramePlaybackLoopingProperty, this.PersistentState.GetValueOrDefault(IsInitFramePlaybackLooping));
			this.SetValue(IsFramePlaybackRateUnlimitedProperty, this.PersistentState.GetValueOrDefault(IsInitFramePlaybackRateUnlimited));
			this.SetValue(IsHistogramMeanMarkerVisibleProperty, this.PersistentState.GetValueOrDefault(IsInitHistogramMeanMarkerVisible));
			this.SetValue(IsHistogramsVisibleProperty, this.PersistentState.GetValueOrDefault(IsInitHistogramsPanelVisible));
			this.SetValue(RenderingParametersPanelSizeProperty, this.PersistentState.GetValueOrDefault(LatestRenderingParamsPanelSize));
		}

		// add event handlers
		ColorSpace.RemovingUserDefinedColorSpace += this.OnRemovingUserDefinedColorSpace;
	}


	/// <summary>
	/// Activate session.
	/// </summary>
	/// <returns>Token of activation.</returns>
	public IDisposable Activate()
	{
		// check state
		this.VerifyAccess();
		this.VerifyDisposed();

		// create token
		var token = new ActivationToken(this);
		this.activationTokens.Add(token);

		// activate
		if (this.activationTokens.Count == 1)
		{
			this.Logger.LogDebug("Activate");
			if (this.IsHibernated)
			{
				this.Logger.LogWarning("Leave hibernation");
				this.SetValue(IsHibernatedProperty, false);
			}
			if (!this.HasRenderedImage)
				this.renderImageAction.Reschedule();
			this.SetValue(IsActivatedProperty, true);
		}
		return token;
	}
	
	
	// Change height of image with given alignment.
	void AlignImageHeight(int bytes) =>
		this.SetValue(ImageHeightProperty, this.AlignToInteger(this.GetValue(ImageHeightProperty), bytes));
	
	
	/// <summary>
	/// Command to change height of image with given alignment.
	/// </summary>
	/// <remarks>The parameter is number to align, type is <see cref="int"/>.</remarks>
	public ICommand AlignImageHeightCommand { get; }
	
	
	// Change width of image with given alignment.
	void AlignImageWidth(int bytes) =>
		this.SetValue(ImageWidthProperty, this.AlignToInteger(this.GetValue(ImageWidthProperty), bytes));
	
	
	/// <summary>
	/// Command to change width of image with given alignment.
	/// </summary>
	/// <remarks>The parameter is number to align, type is <see cref="int"/>.</remarks>
	public ICommand AlignImageWidthCommand { get; }
	
	
	// Change row stride of 1st plane of image with given alignment.
	void AlignRowStride1(int bytes) =>
		this.ChangeRowStride(0, this.AlignToInteger(this.rowStrides[0], bytes));
	
	
	/// <summary>
	/// Command to change row stride of 1st plane of image with given alignment.
	/// </summary>
	/// <remarks>The parameter is number to align, type is <see cref="int"/>.</remarks>
	public ICommand AlignRowStride1Command { get; }
	
	
	// Change row stride of 2nd plane of image with given alignment.
	void AlignRowStride2(int bytes) =>
		this.ChangeRowStride(1, this.AlignToInteger(this.rowStrides[1], bytes));
	
	
	/// <summary>
	/// Command to change row stride of 2nd plane of image with given alignment.
	/// </summary>
	/// <remarks>The parameter is number to align, type is <see cref="int"/>.</remarks>
	public ICommand AlignRowStride2Command { get; }
	
	
	// Change row stride of 3rd plane of image with given alignment.
	void AlignRowStride3(int bytes) =>
		this.ChangeRowStride(2, this.AlignToInteger(this.rowStrides[2], bytes));
	
	
	/// <summary>
	/// Command to change row stride of 3rd plane of image with given alignment.
	/// </summary>
	/// <remarks>The parameter is number to align, type is <see cref="int"/>.</remarks>
	public ICommand AlignRowStride3Command { get; }


	// Align value to given integer.
	int AlignToInteger(int value, int n)
	{
		if (n <= 1)
			return value;
		var remaining = value % n;
		return remaining > 0 ? value + n - remaining : value;
	}


	// Try allocating image frame for filtered image.
	async Task<ImageFrame?> AllocateFilteredImageFrame(ImageFrame renderedImageFrame)
	{
		var extraWaitingPerformed = false;
		while (true)
		{
			try
			{
				if (!this.IsActivated)
				{
					this.Logger.LogWarning("No need to allocate filtered image frame because session has been deactivated");
					return null;
				}
				return ImageFrame.Allocate(this, renderedImageFrame.FrameNumber, renderedImageFrame.BitmapBuffer.Format, renderedImageFrame.BitmapBuffer.ColorSpace, renderedImageFrame.BitmapBuffer.Width, renderedImageFrame.BitmapBuffer.Height);
			}
			catch (Exception ex)
			{
				if (ex is OutOfMemoryException)
				{
					// the cached images are kept only to avoid reallocation, releasing them first keeps the image being displayed
					// until there is really nothing else to release, dropping it makes the image flicker while filtering continuously
					if (this.ReleaseCachedImages())
					{
						this.Logger.LogWarning("Unable to request memory usage for filtered image, release cached images");
						continue;
					}
					if (this.filteredImageFrame is not null)
					{
						this.Logger.LogWarning("Unable to request memory usage for filtered image, dispose current images");
						this.SetValue(HistogramsProperty, null);
						this.SetValue(QuarterSizeRenderedImageProperty, null);
						this.SetValue(RenderedImageProperty, null);
						this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
					}
					else if (!(await this.HibernateAnotherSessionAsync()))
					{
						if (extraWaitingPerformed)
						{
							this.Logger.LogWarning("Unable to release rendered image from another session");
							return null;
						}
						else
						{
							extraWaitingPerformed = true;
							await Task.Delay(1000);
						}
					}
				}
				else
				{
					this.Logger.LogError(ex, "Unable to allocate filtered image");
					return null;
				}
			}
		}
	}


	// Try allocating image frame for rendered image.
	async Task<ImageFrame?> AllocateRenderedImageFrame(long frameNumber, BitmapFormat format, ColorSpace colorSpace, int width, int height)
	{
		var extraWaitingPerformed = false;
		while (true)
		{
			try
			{
				if (!this.IsActivated)
				{
					this.Logger.LogWarning("No need to allocate rendered image frame because session has been deactivated");
					return null;
				}
				return ImageFrame.Allocate(this, frameNumber, format, colorSpace, width, height);
			}
			catch (Exception ex)
			{
				if (ex is OutOfMemoryException)
				{
					// the cached images are kept only to avoid reallocation, releasing them first keeps the image being displayed
					// until there is really nothing else to release, dropping it makes the image flicker while rendering continuously
					if (this.ReleaseCachedImages())
					{
						this.Logger.LogWarning("Unable to request memory usage for rendered image, release cached images");
						continue;
					}
					if (this.renderedImageFrame is not null)
					{
						this.Logger.LogWarning("Unable to request memory usage for rendered image, dispose current images");
						this.SetValue(HistogramsProperty, null);
						this.SetValue(QuarterSizeRenderedImageProperty, null);
						this.SetValue(RenderedImageProperty, null);
						this.canSelectColorAdjustment.Update(false);
						this.canSelectRgbGain.Update(false);
						this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
						this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
						this.colorSpaceConvertedImageFrame = this.colorSpaceConvertedImageFrame.DisposeAndReturnNull();
					}
					else if (!(await this.HibernateAnotherSessionAsync()))
					{
						if (extraWaitingPerformed)
						{
							this.Logger.LogWarning("Unable to release rendered image from another session");
							return null;
						}
						else
						{
							extraWaitingPerformed = true;
							await Task.Delay(1000);
						}
					}
				}
				else
				{
					this.Logger.LogError(ex, "Unable to allocate rendered image");
					return null;
				}
			}
		}
	}


	// Apply given filter.
	Task<bool> ApplyImageFilterAsync(IImageFilter<ImageFilterParams> filter, ImageFrame sourceFrame, ImageFrame resultFrame, CancellationToken cancellationToken) =>
		this.ApplyImageFilterAsync(filter, sourceFrame, resultFrame, ImageFilterParams.Empty, cancellationToken);
	async Task<bool> ApplyImageFilterAsync<TParam>(IImageFilter<TParam> filter, ImageFrame sourceFrame, ImageFrame resultFrame, TParam parameters, CancellationToken cancellationToken) where TParam : ImageFilterParams
	{
		try
		{
			await filter.ApplyFilterAsync(sourceFrame.BitmapBuffer, resultFrame.BitmapBuffer, parameters, cancellationToken);
			return true;
		}
		catch (Exception ex)
		{
			if (!cancellationToken.IsCancellationRequested)
				this.Logger.LogError(ex, "Error occurred while applying filter {filter}", filter);
			return false;
		}
	}


	// Apply values which are derived from format of given image renderer.
	void ApplyImageRendererFormat(IImageRenderer imageRenderer)
	{
		// evaluate dimensions if needed
		if (this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterChangingRenderer))
			this.isImageDimensionsEvaluationNeeded = true;

		// setup values according to format and renderer
		var imageFormatCategory = imageRenderer.Format.Category;
		var isBayerPatternFormat = imageFormatCategory == ImageFormatCategory.Bayer;
		this.SetValue(HasMultipleByteOrderingsProperty, imageRenderer.Format.HasMultipleByteOrderings);
		this.SetValue(IsBayerPatternSupportedProperty, isBayerPatternFormat);
		this.SetValue(IsCompressedImageFormatProperty, imageFormatCategory == ImageFormatCategory.Compressed);
		this.SetValue(IsDemosaicingSupportedProperty, isBayerPatternFormat);
		this.SetValue(IsRgbGainSupportedProperty, isBayerPatternFormat);
		this.SetValue(IsYuvToBgraConverterSupportedProperty, imageFormatCategory == ImageFormatCategory.YUV);
		this.UpdateHasColorTables();
		this.UpdateIsAlphaChannelAvailable();

		// reset plane options and restart rendering
		this.isImagePlaneOptionsResetNeeded = true;
		this.updateFilterSupportingAction.Reschedule();
		this.renderImageAction.Reschedule();
		this.trackRenderingParamsAppliedAction.Reschedule(TrackRenderingParamsAppliedEventDelay);
	}


	// Apply parameters defined in current profile.
	void ApplyProfile()
	{
		// get profile
		var profile = this.Profile;

		// update state
		this.UpdateCanSaveDeleteProfile();

		// apply profile
		if (profile.Type != ImageRenderingProfileType.Default)
		{
			// renderer
			this.SetValue(ImageRendererProperty, profile.Renderer);

			// data offset
			this.SetValue(DataOffsetProperty, profile.DataOffset);

			// frame padding size
			this.SetValue(FramePaddingSizeProperty, profile.FramePaddingSize);

			// byte ordering
			this.SetValue(ByteOrderingProperty, profile.ByteOrdering);

			// bayer pattern
			this.SetValue(BayerPatternProperty, profile.BayerPattern);

			// YUV to RGB converter
			this.SetValue(YuvToBgraConverterProperty, profile.YuvToBgraConverter);

			// color space
			this.colorSpaces.RemoveAll(it => it.IsEmbedded);
			ColorSpace colorSpace;
			if (profile.Type != ImageRenderingProfileType.UserDefined && this.IsYuvToBgraConverterSupported)
				colorSpace = this.YuvToBgraConverter.ColorSpace;
			else
			{
				colorSpace = profile.ColorSpace;
				if (colorSpace.IsEmbedded)
				{
#if SKIP_MAPPING_EMBEDDED_COLOR_SPACE_TO_BUILT_IN
					this.colorSpaces.Add(colorSpace);
#else
					if (ColorSpace.TryGetBuiltInColorSpace(colorSpace, out var builtInColorSpace))
						colorSpace = builtInColorSpace;
					else
						this.colorSpaces.Add(colorSpace);
#endif
				}
			}
			this.SetValue(ColorSpaceProperty, colorSpace);
			this.SetValue(UseLinearColorSpaceProperty, profile.UseLinearColorSpace);

			// demosaicing
			this.SetValue(DemosaicingAlgorithmProperty, profile.DemosaicingAlgorithm ?? Media.Demosaicing.DemosaicingAlgorithms.Bypass);

			// dimensions
			this.SetValue(ImageWidthProperty, profile.Width);
			this.SetValue(ImageHeightProperty, profile.Height);

			// color tables, they need to be applied before the plane options because they decide the effective bits of every plane
			this.ChangeColorTables(profile.RedColorTable, profile.GreenColorTable, profile.BlueColorTable, profile.AlphaColorTable);

			// plane options
			var imageFormat = this.ImageRenderer.Format;
			var defaultPlaneOptions = Global.Run(() =>
			{
				try
				{
					return this.ImageRenderer.CreateDefaultPlaneOptions(profile.Width, profile.Height);
				}
				catch (Exception ex)
				{
					this.Logger.LogError(ex, "Unable to get default plane options with dimensions {w}x{h}", profile.Width, profile.Height);
					return new ImagePlaneOptions[imageFormat.PlaneCount];
				}
			});
			for (var i = imageFormat.PlaneCount - 1; i >= 0; --i)
			{
				var planeDescriptor = imageFormat.PlaneDescriptors[i];
				this.ChangeEffectiveBits(i, profile.EffectiveBits[i].Let(it =>
				{
					// [Workaround] Override stale persisted value when effective bits is not user-adjustable.
					if (!planeDescriptor.IsAdjustableEffectiveBits && defaultPlaneOptions[i].EffectiveBits > 0)
						return defaultPlaneOptions[i].EffectiveBits;
					return it;
				}));
				this.ChangeBlackLevel(i, profile.BlackLevels[i]);
				this.ChangeWhiteLevel(i, profile.WhiteLevels[i].Let(it =>
				{
					// [Workaround] Handle case of white levels not saved to profile
					if (planeDescriptor.AreAdjustableBlackWhiteLevels && it == 0)
					{
						if (defaultPlaneOptions[i].WhiteLevel.HasValue)
							return defaultPlaneOptions[i].WhiteLevel.GetValueOrDefault();
						return (uint)(1 << planeDescriptor.MaxEffectiveBits) - 1;
					}
					return it;
				}));
				this.ChangePixelStride(i, profile.PixelStrides[i]);
				this.ChangeRowStride(i, profile.RowStrides[i]);
			}

			// RGB gain
			if (this.IsRgbGainSupported)
			{
				this.SetValue(RedColorGainProperty, profile.RedColorGain);
				this.SetValue(GreenColorGainProperty, profile.GreenColorGain);
				this.SetValue(BlueColorGainProperty, profile.BlueColorGain);
			}

			// rotation and flip
			if (profile.IsFileFormat)
			{
				var rotation = profile.Orientation;
				if (rotation < 0)
					rotation += 360;
				else if (rotation > 360)
					rotation -= 360;
				rotation = (int)(rotation / 90.0 + 0.5) * 90;
				this.SetValue(ImageDisplayRotationProperty, rotation);
				this.SetValue(IsImageFlippedXProperty, profile.FlipX);
				this.SetValue(IsImageFlippedYProperty, profile.FlipY);
				if (this.GetValue(FitImageToViewportProperty)
					&& double.IsFinite(this.fitRenderedImageToViewportScale))
				{
					var scale = (rotation % 180) == 0
						? this.fitRenderedImageToViewportScale
						: this.fitRenderedImageToViewportScaleSwapped;
					this.ZoomTo(scale, false);
				}
				else
					this.updateImageDisplaySizeAction.Schedule();
			}

			// update state
			if (this.renderImageAction.IsScheduled)
			{
				this.isImageDimensionsEvaluationNeeded = false;
				this.isImagePlaneOptionsResetNeeded = false;
			}
		}
		else
		{
			// the default profile carries no color table, the effective bits become adjustable by user again
			this.ChangeColorTables(null, null, null, null);
		}
	}


	/// <summary>
	/// Command to apply parameters defined by current <see cref="Profile"/>.
	/// </summary>
	public ICommand ApplyProfileCommand { get; }


	/// <summary>
	/// Check whether black/white levels for 1st image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool AreAdjustableBlackWhiteLevels1 => this.GetValue(AreAdjustableBlackWhiteLevels1Property);


	/// <summary>
	/// Check whether black/white levels for 2nd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool AreAdjustableBlackWhiteLevels2 => this.GetValue(AreAdjustableBlackWhiteLevels2Property);


	/// <summary>
	/// Check whether black/white levels for 3rd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool AreAdjustableBlackWhiteLevels3 => this.GetValue(AreAdjustableBlackWhiteLevels3Property);


	/// <summary>
	/// Get or set <see cref="BayerPattern"/> for rendered image.
	/// </summary>
	public BayerPattern BayerPattern
	{
		get => this.GetValue(BayerPatternProperty);
		set => this.SetValue(BayerPatternProperty, value);
	}


	/// <summary>
	/// Get or set black level of 1st image plane.
	/// </summary>
	public uint BlackLevel1
	{
		get => this.blackLevels[0];
		set => this.ChangeBlackLevel(0, value);
	}


	/// <summary>
	/// Get or set black level of 2nd image plane.
	/// </summary>
	public uint BlackLevel2
	{
		get => this.blackLevels[1];
		set => this.ChangeBlackLevel(1, value);
	}


	/// <summary>
	/// Get or set black level of 3rd image plane.
	/// </summary>
	public uint BlackLevel3
	{
		get => this.blackLevels[2];
		set => this.ChangeBlackLevel(2, value);
	}


	/// <summary>
	/// Get or set blue color adjustment.
	/// </summary>
	public double BlueColorAdjustment
	{
		get => this.GetValue(BlueColorAdjustmentProperty);
		set => this.SetValue(BlueColorAdjustmentProperty, value);
	}


	/// <summary>
	/// Get or set gain of blue color.
	/// </summary>
	public double BlueColorGain
	{
		get => this.GetValue(BlueColorGainProperty);
		set => this.SetValue(BlueColorGainProperty, value);
	}


	/// <summary>
	/// Get or set brightness adjustment for filter in EV.
	/// </summary>
	public double BrightnessAdjustment
	{
		get => this.GetValue(BrightnessAdjustmentProperty);
		set => this.SetValue(BrightnessAdjustmentProperty, value);
	}


	/// <summary>
	/// Get or set byte ordering.
	/// </summary>
	public ByteOrdering ByteOrdering
	{
		get => this.GetValue(ByteOrderingProperty);
		set => this.SetValue(ByteOrderingProperty, value);
	}


	// Cancel filtering image and wait for the completion of the filtering being cancelled.
	Task CancelFilteringImageAsync()
	{
		// cancel
		this.filterImageAction.Cancel();
		if (this.imageFilteringCancellationTokenSource is not null)
		{
			this.Logger.LogWarning("Cancel filtering image for source '{sourceFileName}'", this.SourceFileName);
			this.imageFilteringCancellationTokenSource.Cancel();
			if (this.imageFilteringCancellationTokenSource == this.imageReportingCancellationTokenSource)
			{
				this.Logger.LogWarning("Cancel reporting rendered image by cancelling filtering image");
				this.imageReportingCancellationTokenSource = null;
			}
			this.imageFilteringCancellationTokenSource = null;
			if (this.GetValue(IsConvertingColorSpaceProperty))
				this.Logger.LogWarning("Cancel color space conversion for filtering image"); // the state is reset by ConvertColorSpaceAsync() when the conversion unwinds
		}

		// wait for the completion of the filtering being cancelled, the filtering is still in progress until it reaches its next cancellation check
		return this.WaitForImageFilteringCompletionAsync();
	}
	
	
	// Cancel reporting rendered image.
	bool CancelReportingRenderedImage()
	{
		if (this.imageReportingCancellationTokenSource is null)
			return false;
		this.Logger.LogWarning("Cancel reporting rendered image for source '{sourceFileName}'", this.SourceFileName);
		this.imageReportingCancellationTokenSource.Cancel();
		this.imageReportingCancellationTokenSource = null;
		if (this.GetValue(IsConvertingColorSpaceProperty))
			this.Logger.LogWarning("Cancel color space conversion for reporting rendered image"); // the state is reset by ConvertColorSpaceAsync() when the conversion unwinds
		return true;
	}


	// Cancel rendering image and wait for the completion of the rendering being cancelled.
	Task CancelRenderingImageAsync(bool cancelPendingRendering = false)
	{
		// cancel
		this.renderImageAction.Cancel();
		if (cancelPendingRendering)
			this.hasPendingImageRendering = false;
		if (this.imageRenderingCancellationTokenSource is not null)
		{
			this.Logger.LogWarning("Cancel rendering image for source '{sourceFileName}'", this.SourceFileName);
			this.imageRenderingCancellationTokenSource.Cancel();
			if (this.imageRenderingCancellationTokenSource == this.imageReportingCancellationTokenSource)
			{
				this.Logger.LogWarning("Cancel reporting rendered image by cancelling rendering image");
				this.imageReportingCancellationTokenSource = null;
			}
			this.imageRenderingCancellationTokenSource = null;
			if (this.GetValue(IsConvertingColorSpaceProperty))
				this.Logger.LogWarning("Cancel color space conversion for rendering image"); // the state is reset by ConvertColorSpaceAsync() when the conversion unwinds
		}

		// wait for the completion of the rendering being cancelled, the rendering is still in progress until it reaches its next cancellation check
		return this.WaitForImageRenderingCompletionAsync();
	}


	// Change black level of given image plane.
	void ChangeBlackLevel(int index, uint blackLevel)
	{
		this.VerifyAccess();
		this.VerifyDisposed();
		if (this.blackLevels[index] == blackLevel)
			return;
		this.blackLevels[index] = blackLevel;
		this.OnBlackLevelChanged(index);
		this.renderImageAction.Reschedule(RenderImageDelay);
	}


	// Change the color tables applied to the rendering. The session shares the given tables so that they keep being alive after their provider is released.
	void ChangeColorTables(ColorTable? redColorTable, ColorTable? greenColorTable, ColorTable? blueColorTable, ColorTable? alphaColorTable)
	{
		// check state
		this.VerifyAccess();
		this.VerifyDisposed();
		if (this.alphaColorTable.IsSameAs(alphaColorTable)
			&& this.blueColorTable.IsSameAs(blueColorTable)
			&& this.greenColorTable.IsSameAs(greenColorTable)
			&& this.redColorTable.IsSameAs(redColorTable))
		{
			return;
		}

		// replace the tables, the previous tables are released after the new ones are shared in case they share the same colors
		var prevAlphaColorTable = this.alphaColorTable;
		var prevBlueColorTable = this.blueColorTable;
		var prevGreenColorTable = this.greenColorTable;
		var prevRedColorTable = this.redColorTable;
		this.alphaColorTable = alphaColorTable?.Share();
		this.blueColorTable = blueColorTable?.Share();
		this.greenColorTable = greenColorTable?.Share();
		this.redColorTable = redColorTable?.Share();
		prevAlphaColorTable?.Dispose();
		prevBlueColorTable?.Dispose();
		prevGreenColorTable?.Dispose();
		prevRedColorTable?.Dispose();

		// the colors of all tables are rendered into the same image, so the deepest table decides the color depth of the rendered image
		var colorTableBitDepth = Math.Max(this.alphaColorTable?.ColorBitDepth ?? 0, this.blueColorTable?.ColorBitDepth ?? 0);
		colorTableBitDepth = Math.Max(colorTableBitDepth, this.greenColorTable?.ColorBitDepth ?? 0);
		colorTableBitDepth = Math.Max(colorTableBitDepth, this.redColorTable?.ColorBitDepth ?? 0);
		this.colorTableBitDepth = colorTableBitDepth;

		// update state, the rendering also needs to be updated when only the tables are changed
		this.UpdateHasColorTables();
		this.renderImageAction.Reschedule(RenderImageDelay);
	}


	// Change effective bits of given image plane.
	void ChangeEffectiveBits(int index, int effectiveBits)
	{
		// check state
		this.VerifyAccess();
		this.VerifyDisposed();
		effectiveBits = this.CoerceEffectiveBitsToColorTables(effectiveBits);
		if (this.effectiveBits[index] == effectiveBits)
			return;

		// update effective bits
		this.effectiveBits[index] = effectiveBits;
		this.OnEffectiveBitsChanged(index);
		this.UpdateSourceImageEffectiveBits();
		this.renderImageAction.Reschedule(RenderImageDelay);

		// update black/white levels
		if (effectiveBits > 0)
		{
			var imageFormat = this.GetValue(ImageRendererProperty)?.Format;
			if (imageFormat is not null && imageFormat.PlaneDescriptors.Count > index)
			{
				var planeDescriptor = imageFormat.PlaneDescriptors[index];
				if (planeDescriptor.AreAdjustableBlackWhiteLevels)
				{
					var maxWhiteLevel = (uint)(1 << effectiveBits) - 1;
					this.ChangeWhiteLevel(index, maxWhiteLevel);
					if (this.blackLevels[index] >= maxWhiteLevel)
						this.ChangeBlackLevel(index, maxWhiteLevel - 1);
				}
			}
		}
	}


	// Change pixel stride of given image plane.
	void ChangePixelStride(int index, int pixelStride)
	{
		this.VerifyAccess();
		this.VerifyDisposed();
		if (this.pixelStrides[index] == pixelStride)
			return;
		this.pixelStrides[index] = pixelStride;
		this.OnPixelStrideChanged(index);
		this.renderImageAction.Reschedule(RenderImageDelay);
	}


	// Change row stride of given image plane.
	void ChangeRowStride(int index, int rowStride)
	{
		this.VerifyAccess();
		this.VerifyDisposed();
		if (this.rowStrides[index] == rowStride)
			return;
		this.rowStrides[index] = rowStride;
		this.OnRowStrideChanged(index);
		this.renderImageAction.Reschedule(RenderImageDelay);
	}


	// Change white level of given image plane.
	void ChangeWhiteLevel(int index, uint whiteLevel)
	{
		this.VerifyAccess();
		this.VerifyDisposed();
		if (this.whiteLevels[index] == whiteLevel)
			return;
		this.whiteLevels[index] = whiteLevel;
		this.OnWhiteLevelChanged(index);
		this.renderImageAction.Reschedule(RenderImageDelay);
	}
	
	
	// Clear the rendered image and render it again, which is what requesting rendering explicitly does.
	async Task ClearAndRenderImageAsync()
	{
		// clear the rendered image, the images are released only after the rendering being cancelled completes
		await this.ClearRenderedImageAsync();

		// render again
		if (!this.IsDisposed)
			this.renderImageAction.Reschedule();
	}


	// Clear filtered image.
	async Task ClearFilteredImageAsync()
	{
		// cancel filtering and wait for the completion of the filtering being cancelled
		await this.CancelFilteringImageAsync();

		// keep the images if a new filtering has taken over them while waiting
		if (this.IsDisposed || this.imageFilteringCompletionSource is not null)
			return;

		// release the images
		this.DisposeFilteredImage();
	}


	// Clear rendered image.
	async Task ClearRenderedImageAsync()
	{
		// clear filtered image, then cancel rendering and wait for the completion of the rendering being cancelled
		await this.ClearFilteredImageAsync();
		await this.CancelRenderingImageAsync(true);

		// keep the images if a new rendering has taken over them while waiting
		if (this.IsDisposed || this.imageRenderingCompletionSource is not null)
			return;

		// release the images
		this.DisposeRenderedImage();
	}


	// Close and clear the current source.
	void ClearSource()
	{
		// close source
		this.CloseSource(false);

		// update state, name of source file is cleared by CloseSource()
		this.SetValue(SourceSizeStringProperty, null);
		
		// reset scaling
		this.FitImageToViewport = true;
		this.RequestedImageDisplayScale = 1.0;

		// update title
		this.UpdateTitle();
	}
	
	
	/// <summary>
	/// Command for clearing the opened source.
	/// </summary>
	public ICommand ClearSourceCommand { get; }


	// Close current source.
	void CloseSource(bool disposing)
	{
		// stop frame playback
		this.StopPlayingFrames();

		// flush pending rendering-params and filtering-params tracking
		this.trackRenderingParamsAppliedAction.ExecuteIfScheduled();
		this.trackFilteringParamsAppliedAction.ExecuteIfScheduled();
		this.trackRenderingPerfAction.ExecuteIfScheduled();
		this.trackFilteringPerfAction.ExecuteIfScheduled();

		// emit frame navigation count
		if (this.frameNavigationCount > 0)
		{
			this.Application.UsageManager.TrackMetric(UsageMetrics.FrameNavigationCount, this.frameNavigationCount, new Dictionary<string, string>
			{
				[UsageProperties.FrameCount] = this.GetValue(FrameCountProperty).ToString(CultureInfo.InvariantCulture),
				[UsageProperties.Id] = this.Id.ToString(CultureInfo.InvariantCulture),
			});
			this.frameNavigationCount = 0;
		}

		// clear selected pixel
		this.SelectRenderedImagePixel(-1, -1);

		// complete zooming
		this.CompleteZooming(!disposing);

		// update state
		if (!disposing)
		{
			this.SetValue(DataOffsetProperty, 0L);
			this.SetValue(FrameCountProperty, 0);
			this.SetValue(FrameNumberProperty, 0);
			this.SetValue(FramePaddingSizeProperty, 0L);
			this.SetValue(HistogramsProperty, null);
			this.SetValue(QuarterSizeRenderedImageProperty, null);
			this.SetValue(RenderedImageProperty, null);
			this.SetValue(IsSourceOpenedProperty, false);
			this.SetValue(LuminanceHistogramGeometryProperty, null);
			this.canMoveToNextFrame.Update(false);
			this.canMoveToPreviousFrame.Update(false);
			this.canSaveRenderedImage.Update(false);
			this.canSelectColorAdjustment.Update(false);
			this.canSelectRgbGain.Update(false);
			this.SetValue(SourceDataSizeProperty, 0);
			this.UpdateCanSaveDeleteProfile();
		}
		this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
		this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
		this.colorSpaceConvertedImageFrame = this.colorSpaceConvertedImageFrame.DisposeAndReturnNull();
		this.isImageRenderingForced = false;
		if (!disposing)
		{
			this.ResetValue(ImageDisplayRotationProperty);
			this.ResetValue(HasRenderingErrorProperty);
			this.ResetValue(InsufficientMemoryForRenderedImageProperty);
			this.ResetValue(IsImageFlippedXProperty);
			this.ResetValue(IsImageFlippedYProperty);
		}

		// release cached images
		this.ReleaseCachedImages();

		// release memory usage tokens
		this.avaQuarterSizeRenderedImageMemoryUsageToken = this.avaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
		this.avaRenderedImageMemoryUsageToken = this.avaRenderedImageMemoryUsageToken.DisposeAndReturnNull();

		// update zooming state
		this.canZoomTo.Update(false);
		this.UpdateCanZoomInOut();

		// cancel rendering image, the frames have been released above so there is no need to wait for the cancellation to complete
		_ = this.CancelFilteringImageAsync();
		_ = this.CancelRenderingImageAsync(true);
		this.CancelReportingRenderedImage();

		// remove profile generated for file format
		if (this.fileFormatProfile is not null)
		{
			if (!disposing)
			{
				if (this.Profile == this.fileFormatProfile)
					this.SwitchToProfileWithoutApplying(ImageRenderingProfile.Default);
				this.profiles.Remove(this.fileFormatProfile);
			}
			this.fileFormatProfile.Dispose();
			this.fileFormatProfile = null;
		}

		// dispose image data source
		var imageDataSource = this.imageDataSource;
		var sourceFileName = this.SourceFileName;
		this.frameImageDataSource = this.frameImageDataSource.DisposeAndReturnNull();
		this.imageDataSource = null;
		if (!disposing)
		{
			// name of source file describes the source which has been closed, so it is cleared along with the source
			this.SetValue(SourceFileNameProperty, null);
			this.UpdateTitle(); // title is selected by source, so it is updated after the source has been detached
		}
		if (imageDataSource is not null)
		{
			_ = Task.Run(() =>
			{
				this.Logger.LogDebug("Dispose source for '{sourceFileName}'", sourceFileName);
				imageDataSource.Dispose();
			});
		}
	}
	
	
	// Replace the effective bits of an image plane by the color depth of the color tables when the tables are applied.
	// The tables define the colors to be rendered, so the value set by user has no effect on the rendering and reporting it would be misleading.
	int CoerceEffectiveBitsToColorTables(int effectiveBits) =>
		this.GetValue(HasColorTablesProperty) ? this.colorTableBitDepth : effectiveBits;


	// Clamp the frame number into [1, frameCount], applying the correction to the property and cancelling
	// the redundant re-render it would otherwise trigger. Returns the coerced frame number.
	long CoerceFrameNumberToRange(long frameNumber, long frameCount)
	{
		if (frameNumber < 1)
		{
			frameNumber = 1;
			this.SetValue(FrameNumberProperty, 1);
			this.renderImageAction.Cancel(); // prevent re-rendering caused by change of frame number
		}
		else if (frameNumber > frameCount)
		{
			frameNumber = frameCount;
			this.SetValue(FrameNumberProperty, frameCount);
			this.renderImageAction.Cancel(); // prevent re-rendering caused by change of frame number
		}
		return frameNumber;
	}


	/// <summary>
	/// Get or set color space of rendered image.
	/// </summary>
	public ColorSpace ColorSpace 
	{
		get => this.GetValue(ColorSpaceProperty);
		set => this.SetValue(ColorSpaceProperty, value);
	}


	/// <summary>
	/// Get available color spaces.
	/// </summary>
	public IList<ColorSpace> ColorSpaces { get; }


	// Compare profiles.
	static int CompareProfiles(ImageRenderingProfile? x, ImageRenderingProfile? y)
	{
		if (x is null)
			return y is null ? 0 : -1;
		if (y is null)
			return 1;
		var result = x.Type.CompareTo(y.Type);
		if (result != 0)
			return result;
		result = string.CompareOrdinal(x.Name, y.Name);
		return result != 0 ? result : x.GetHashCode() - y.GetHashCode();
	}


	// Complete opening image data source: update state, apply the given profile, then start rendering.
	void CompleteOpeningSource(IImageDataSource source, Action applyProfile)
	{
		// update state
		this.canZoomTo.Update(!this.GetValue(FitImageToViewportProperty));
		this.SetValue(DataOffsetProperty, 0L);
		this.SetValue(FrameNumberProperty, 1);
		this.SetValue(FramePaddingSizeProperty, 0L);
		this.SetValue(IsOpeningSourceProperty, false);
		this.SetValue(IsSourceOpenedProperty, true);
		this.canOpenSource.Update(true);
		this.SetValue(SourceSizeStringProperty, source.Size.ToFileSizeString());
		this.UpdateCanSaveDeleteProfile();

		// apply profile
		applyProfile();

		// render image
		if (this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterOpeningSourceFile) && this.Profile.Type == ImageRenderingProfileType.Default)
		{
			this.isImageDimensionsEvaluationNeeded = true;
			this.isImagePlaneOptionsResetNeeded = true;
		}
		if (this.IsActivated)
			this.renderImageAction.Reschedule();
		else
			this.renderImageAction.Cancel();

		// update zooming state
		this.UpdateCanZoomInOut();
	}


	// Complete current smooth zooming.
	void CompleteZooming(bool resetIsZooming)
	{
		if (this.imageScalingAnimator is null)
			return;
		this.imageScalingAnimator.Cancel();
		this.imageScalingAnimator = null;
		if (resetIsZooming)
			this.SetValue(IsZoomingProperty, false);
	}


	/// <summary>
	/// Get or set contrast adjustment.
	/// </summary>
	public double ContrastAdjustment
	{
		get => this.GetValue(ContrastAdjustmentProperty);
		set => this.SetValue(ContrastAdjustmentProperty, value);
	}


	// Convert color space asynchronously.
	async Task<ImageFrame?> ConvertColorSpaceAsync(ImageFrame src, ColorSpace srcColorSpace, ColorSpace destColorSpace, CancellationToken cancellationToken)
	{
		// check state
		if (this.GetValue(IsConvertingColorSpaceProperty))
		{
			this.Logger.LogError("Previous color space conversion is not completed yet");
			return null;
		}

		// update state, the state is kept until the conversion completes or unwinds so that it is not reported as completed while it is still being cancelled
		this.SetValue(IsConvertingColorSpaceProperty, true);
		try
		{
			// allocate frame
			var isReusingImageFrame = false;
			var colorSpaceConvertedImageFrame = this.colorSpaceConvertedImageFrame;
			if (colorSpaceConvertedImageFrame is not null
			    && colorSpaceConvertedImageFrame.BitmapBuffer.Width == src.BitmapBuffer.Width
			    && colorSpaceConvertedImageFrame.BitmapBuffer.Height == src.BitmapBuffer.Height
			    && colorSpaceConvertedImageFrame.BitmapBuffer.Format == src.BitmapBuffer.Format)
			{
				if (this.Application.IsDebugMode)
					this.Logger.LogDebug("Reuse color space converted image frame, size: {width}x{height}", src.BitmapBuffer.Width, src.BitmapBuffer.Height);
				isReusingImageFrame = true;
			}
			else
			{
				if (this.Application.IsDebugMode)
					this.Logger.LogWarning("Allocate color space converted image frame, size: {width}x{height}", src.BitmapBuffer.Width, src.BitmapBuffer.Height);
				colorSpaceConvertedImageFrame = await this.AllocateRenderedImageFrame(src.FrameNumber, src.BitmapBuffer.Format, destColorSpace, src.BitmapBuffer.Width, src.BitmapBuffer.Height);
				if (colorSpaceConvertedImageFrame is null)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						this.Logger.LogWarning("Color space conversion has been cancelled");
						cancellationToken.ThrowIfCancellationRequested();
					}
					this.Logger.LogError("Unable to allocate image frame for color space conversion");
					this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
					return null;
				}
			}

			// convert the frame, release it unless it is handed over to the caller
			var isImageFrameHandedOver = false;
			try
			{
				// give up if the conversion has been cancelled before it starts
				cancellationToken.ThrowIfCancellationRequested();

				// convert color space
				this.Logger.LogTrace("Convert color space from {s} to {d}", srcColorSpace, destColorSpace);
				await src.BitmapBuffer.ConvertToColorSpaceAsync(colorSpaceConvertedImageFrame.BitmapBuffer, this.UseLinearColorSpace, false, cancellationToken);
				colorSpaceConvertedImageFrame.RenderingResult = src.RenderingResult;

				// generate histogram
				colorSpaceConvertedImageFrame.Histograms = await BitmapHistograms.CreateAsync(colorSpaceConvertedImageFrame.BitmapBuffer, this.SourceImageEffectiveBits, cancellationToken);

				// complete
				this.Logger.LogTrace("Color space converted");
				isImageFrameHandedOver = true;
				return colorSpaceConvertedImageFrame;
			}
			catch (OperationCanceledException)
			{
				this.Logger.LogWarning("Color space conversion has been cancelled");
				throw;
			}
			finally
			{
				// the frame allocated by this conversion is owned by it until the caller takes it, the frame reused from the session is owned by the session
				if (!isImageFrameHandedOver && !isReusingImageFrame)
					colorSpaceConvertedImageFrame.Dispose();
			}
		}
		finally
		{
			if (!this.IsDisposed)
				this.ResetValue(IsConvertingColorSpaceProperty);
		}
	}


	/// <summary>
	/// Get or set custom title.
	/// </summary>
	public string? CustomTitle
    {
		get => this.GetValue(CustomTitleProperty);
		set => this.SetValue(CustomTitleProperty, value);
    }


	/// <summary>
	/// Get or set offset to first byte of data to render image.
	/// </summary>
	public long DataOffset
	{
		get => this.GetValue(DataOffsetProperty);
		set => this.SetValue(DataOffsetProperty, value);
	}


	// Deactivate.
	void Deactivate(ActivationToken token)
	{
		// check state
		this.VerifyAccess();
		if (this.IsDisposed)
			return;

		// remove token
		if (!this.activationTokens.Remove(token) || this.activationTokens.IsNotEmpty())
			return;

		// deactivate
		this.Logger.LogDebug("Deactivate");
		this.SetValue(IsActivatedProperty, false);

		// stop frame playback while the session is not active
		this.StopPlayingFrames();

		// hibernate directly, the hibernation completes after the rendering being cancelled completes so it is not waited for here
		if (!this.HasRenderedImage)
		{
			this.Logger.LogWarning("No rendered image before deactivation, hibernate the session");
			_ = this.HibernateAsync();
		}
	}


	// Delete current profile.
	void DeleteProfile()
	{
		// check state
		if (!this.canSaveOrDeleteProfile.Value)
			return;
		var profile = this.Profile;
		if (profile.Type != ImageRenderingProfileType.UserDefined)
		{
			this.Logger.LogError("Cannot delete non user defined profile");
			return;
		}

		// remove profile
		this.SwitchToProfileWithoutApplying(ImageRenderingProfile.Default);
		ImageRenderingProfiles.RemoveUserDefinedProfile(profile);
		profile.Dispose();
	}


	/// <summary>
	/// Command to delete current profile.
	/// </summary>
	public ICommand DeleteProfileCommand { get; }


	// Perform demosaicing on the image rendered with bayer filter pattern. The destination buffer is the same buffer as the source one unless the algorithm requires a dedicated buffer to receive the result.
	async Task DemosaicImageAsync(DemosaicingAlgorithm algorithm, IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		// check state
		if (srcBuffer.Format != destBuffer.Format || srcBuffer.Width != destBuffer.Width || srcBuffer.Height != destBuffer.Height)
			throw new ArgumentException("Format or dimensions of source and destination buffers of demosaicing are different.");
		if (srcBuffer.IsBufferSharedWith(destBuffer) && algorithm.CheckOutputBufferRequirement(renderingOptions.BayerPattern, srcBuffer.Width, srcBuffer.Height) == OutputBufferRequirement.Required)
			throw new ArgumentException($"In-place demosaicing is not supported by '{algorithm.Id}'.");

		// prepare
		var bayerPattern = renderingOptions.BayerPattern;
		var colorComponentSelector = bayerPattern.CreateColorComponentSelector();
		var stopwatch = Stopwatch.StartNew();

		// perform demosaicing
		using var sharedSrcBuffer = srcBuffer.Share();
		using var sharedDestBuffer = destBuffer.Share();
		await Task.Run(() => algorithm.Demosaic(sharedSrcBuffer, sharedDestBuffer, bayerPattern, colorComponentSelector, renderingOptions, cancellationToken), cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		// complete
		stopwatch.Stop();
		this.Logger.LogTrace("Demosaic: {algorithm} [ok], time: {duration} ms", algorithm.Id, stopwatch.ElapsedMilliseconds);
	}


	/// <summary>
	/// Get or set whether demosaicing is needed to be performed or not.
	/// </summary>
	/// <remarks>The property is the on/off view of <see cref="DemosaicingAlgorithm"/>, setting it to true selects <see cref="Media.Demosaicing.DemosaicingAlgorithms.Default"/>.</remarks>
	public bool Demosaicing
	{
		get => this.DemosaicingAlgorithm != Media.Demosaicing.DemosaicingAlgorithms.Bypass;
		set => this.SetValue(DemosaicingAlgorithmProperty, value ? Media.Demosaicing.DemosaicingAlgorithms.Default : Media.Demosaicing.DemosaicingAlgorithms.Bypass);
	}


	/// <summary>
	/// Get or set <see cref="DemosaicingAlgorithm"/> to perform demosaicing.
	/// </summary>
	/// <remarks><see cref="Media.Demosaicing.DemosaicingAlgorithms.Bypass"/> means that demosaicing will not be performed.</remarks>
	public DemosaicingAlgorithm DemosaicingAlgorithm
	{
		get => this.GetValue(DemosaicingAlgorithmProperty);
		set => this.SetValue(DemosaicingAlgorithmProperty, value);
	}


	/// <summary>
	/// Get list of <see cref="DemosaicingAlgorithm"/>s which support the current <see cref="BayerPattern"/>.
	/// </summary>
	/// <remarks>The list is the subset of <see cref="Media.Demosaicing.DemosaicingAlgorithms.All"/> to be selected by user, an algorithm which doesn't support the pattern is excluded instead of falling back to another behavior silently.</remarks>
	public IList<DemosaicingAlgorithm> DemosaicingAlgorithms { get; }


	// Dispose.
	protected override void Dispose(bool disposing)
	{
		// close source file
		if (disposing)
			this.CloseSource(true);
		
		// detach from application
		this.Application.PropertyChanged -= this.OnApplicationPropertyChanged;

		// detach from image renderer
		this.GetValue(ImageRendererProperty)?.Let(it => it.PropertyChanged -= this.OnImageRendererPropertyChanged);

		// detach from profiles
		((INotifyCollectionChanged)ImageRenderingProfiles.UserDefinedProfiles).CollectionChanged -= this.OnUserDefinedProfilesChanged;
		foreach (var profile in this.profiles)
			profile.PropertyChanged -= this.OnProfilePropertyChanged;
		
		// detach from color spaces
		(ColorSpace.AllColorSpaces as INotifyCollectionChanged)?.Let(it =>
			it.CollectionChanged -= this.OnAllColorSpacesChanged);

		// detach from demosaicing algorithms
		(Media.Demosaicing.DemosaicingAlgorithms.All as INotifyCollectionChanged)?.Let(it =>
			it.CollectionChanged -= this.OnAllDemosaicingAlgorithmsChanged);

		// release color tables
		this.alphaColorTable = this.alphaColorTable.DisposeAndReturnNull();
		this.blueColorTable = this.blueColorTable.DisposeAndReturnNull();
		this.greenColorTable = this.greenColorTable.DisposeAndReturnNull();
		this.redColorTable = this.redColorTable.DisposeAndReturnNull();

		// detach from shared rendered images memory usage
		this.sharedRenderedImagesMemoryUsageObserverToken.Dispose();

		// remove event handlers
		if (!disposing)
			ColorSpace.RemovingUserDefinedColorSpace -= this.OnRemovingUserDefinedColorSpace;

		// call super
		base.Dispose(disposing);
	}


	// Release the filtered image and the frames cached for filtering. The caller needs to make sure that no filtering is in progress.
	void DisposeFilteredImage()
	{
		// check state
		if (this.filteredImageFrame is null)
			return;

		// release the images
		this.SetValue(HistogramsProperty, null);
		this.SetValue(QuarterSizeRenderedImageProperty, null);
		this.SetValue(RenderedImageProperty, null);
		foreach (var cachedFrame in this.cachedFilteredImageFrames)
			cachedFrame.Dispose();
		this.cachedFilteredImageFrames.Clear();
		this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
	}


	// Release the rendered image and the frame converted from it. The caller needs to make sure that no rendering is in progress.
	void DisposeRenderedImage()
	{
		// release the frame cached for demosaicing, it is released even if no rendered image is kept because nothing else can reach it
		this.cachedMosaicImageFrame = this.cachedMosaicImageFrame.DisposeAndReturnNull();

		// check state
		if (this.renderedImageFrame is null)
			return;

		// release the images
		this.SetValue(HistogramsProperty, null);
		this.SetValue(QuarterSizeRenderedImageProperty, null);
		this.SetValue(RenderedImageProperty, null);
		this.canSelectColorAdjustment.Update(false);
		this.canSelectRgbGain.Update(false);
		this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
		this.colorSpaceConvertedImageFrame = this.colorSpaceConvertedImageFrame.DisposeAndReturnNull();
	}


	/// <summary>
	/// Get or set effective bits on 1st image plane.
	/// </summary>
	public int EffectiveBits1
	{
		get => this.effectiveBits[0];
		set => this.ChangeEffectiveBits(0, value);
	}


	/// <summary>
	/// Get or set effective bits on 2nd image plane.
	/// </summary>
	public int EffectiveBits2
	{
		get => this.effectiveBits[1];
		set => this.ChangeEffectiveBits(1, value);
	}


	/// <summary>
	/// Get or set effective bits on 3rd image plane.
	/// </summary>
	public int EffectiveBits3
	{
		get => this.effectiveBits[2];
		set => this.ChangeEffectiveBits(2, value);
	}


	// Evaluate image dimensions.
	void EvaluateImageDimensions(AspectRatio aspectRatio)
	{
		// check state, dimensions are evaluated by data of single frame
		var imageDataSource = this.frameImageDataSource ?? this.imageDataSource;
		if (imageDataSource is null or IMultiFrameImageDataSource)
			return;

		// evaluate
		this.ImageRenderer.EvaluateDimensions(imageDataSource, aspectRatio)?.Also((ref it) =>
		{
			if (this.ImageWidth != it.Width || this.ImageHeight != it.Height)
			{
				this.ImageWidth = it.Width;
				this.ImageHeight = it.Height;
				this.isImagePlaneOptionsResetNeeded = true;
				this.renderImageAction.ExecuteIfScheduled();
			}
		});
	}


	/// <summary>
	/// Command for image dimension evaluation.
	/// </summary>
	public ICommand EvaluateImageDimensionsCommand { get; }


	// Filter image.
	async Task FilterImage()
	{
		// check state
		if (!this.IsFilteringRenderedImageNeeded)
			return;

		// cancel current filtering and wait for the completion of the filtering being cancelled
		var requestId = ++this.imageFilteringRequestId;
		await this.CancelFilteringImageAsync();
		if (this.IsDisposed)
			return;
		if (requestId != this.imageFilteringRequestId)
		{
			this.Logger.LogWarning("Give up filtering image, a newer filtering has been requested while waiting for the cancellation");
			return;
		}

		// check state again, the state may have been changed while waiting for the cancellation
		if (!this.IsFilteringRenderedImageNeeded)
			return;
		var renderedImageFrame = this.SelectImageFrameToFilter();
		if (renderedImageFrame is null)
			return;

		// filter, then release whoever is waiting for the completion of this filtering
		var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		this.imageFilteringCompletionSource = completionSource;
		this.SetValue(IsFilteringRenderedImageProperty, true);
		try
		{
			await this.FilterImageCore(renderedImageFrame);
		}
		finally
		{
			this.imageFilteringCompletionSource = null;
			if (!this.IsDisposed)
				this.SetValue(IsFilteringRenderedImageProperty, false);
			completionSource.TrySetResult();
		}
	}


	// Filter image according to current state, after the state has been checked by FilterImage().
	async Task FilterImageCore(ImageFrame renderedImageFrame)
	{
		// log
		if (this.Application.IsDebugMode)
			this.Logger.LogTrace("Start filtering image");

		// prepare
		CancellationTokenSource cancellationTokenSource = new();
		this.imageFilteringCancellationTokenSource = cancellationTokenSource;
		this.canSaveFilteredImage.Update(false);

		// check filters needed
		var filterCount = 0;
		var isLuminanceLutFilterNeeded = false;
		var isColorLutFilterNeeded = false;
		var isSaturationFilterNeeded = false;
		var isGrayscaleFilterNeeded = false;
		if (this.canResetBrightnessAdjustment.Value 
			|| this.canResetContrastAdjustment.Value
			|| this.canResetHighlightAdjustment.Value
			|| this.canResetShadowAdjustment.Value)
		{
			isLuminanceLutFilterNeeded = true;
			++filterCount;
		}
		if (this.canResetSaturationAdjustment.Value 
			|| this.canResetVibranceAdjustment.Value)
		{
			isSaturationFilterNeeded = true;
			++filterCount;
		}
		if (this.canResetColorAdjustment.Value)
		{
			isColorLutFilterNeeded = true;
			++filterCount;
		}
		if (this.IsGrayscaleFilterEnabled && this.IsGrayscaleFilterSupported)
		{
			isGrayscaleFilterNeeded = true;
			++filterCount;
		}

		// release cached frames which is not suitable
		var width = renderedImageFrame.BitmapBuffer.Width;
		var height = renderedImageFrame.BitmapBuffer.Height;
		var format = renderedImageFrame.BitmapBuffer.Format;
		for (var i = this.cachedFilteredImageFrames.Count - 1; i >= 0; --i)
		{
			var cachedFrame = this.cachedFilteredImageFrames[i];
			if (cachedFrame.BitmapBuffer.Width != width 
				|| cachedFrame.BitmapBuffer.Height != height
				|| cachedFrame.BitmapBuffer.Format != format)
            {
				if (this.Application.IsDebugMode)
					this.Logger.LogTrace("Released cached filtered image frame, size: {width}x{height}", cachedFrame.BitmapBuffer.Width, cachedFrame.BitmapBuffer.Height);
				this.cachedFilteredImageFrames.RemoveAt(i);
				cachedFrame.Dispose();
            }
		}

		// allocate frames
		ImageFrame? filteredImageFrame1;
		var cachedFrameCount = this.cachedFilteredImageFrames.Count;
		if (cachedFrameCount > 0)
		{
			if (this.Application.IsDebugMode)
				this.Logger.LogTrace("Use cached filtered image frame 1");
			filteredImageFrame1 = this.cachedFilteredImageFrames[cachedFrameCount - 1];
			this.cachedFilteredImageFrames.RemoveAt(cachedFrameCount - 1);
		}
		else
        {
			if (this.Application.IsDebugMode)
				this.Logger.LogWarning("Allocate filtered image frame 1, size: {width}x{height}", width, height);
			filteredImageFrame1 = await this.AllocateFilteredImageFrame(renderedImageFrame);
		}
		if (filteredImageFrame1 is null)
		{
			if (!cancellationTokenSource.IsCancellationRequested)
			{
				this.imageFilteringCancellationTokenSource = null;
				this.SetValue(InsufficientMemoryForRenderedImageProperty, this.IsActivated);
				Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
				if (!this.IsActivated && !this.IsHibernated)
				{
					this.Logger.LogWarning("Unable to allocate filtered image frame after deactivation, hibernate the session");
					_ = this.HibernateAsync(); // the images are released after this filtering completes, waiting for it here would wait for this filtering itself
				}
			}
			else
				this.Logger.LogWarning("Filtering image has been cancelled");
			return;
		}
		var filteredImageFrame2 = (ImageFrame?)null;
		if (filterCount > 1)
		{
			cachedFrameCount = this.cachedFilteredImageFrames.Count;
			if (cachedFrameCount > 0)
			{
				if (this.Application.IsDebugMode)
					this.Logger.LogTrace("Use cached filtered image frame 2");
				filteredImageFrame2 = this.cachedFilteredImageFrames[cachedFrameCount - 1];
				this.cachedFilteredImageFrames.RemoveAt(cachedFrameCount - 1);
			}
			else
			{
				if (this.Application.IsDebugMode)
					this.Logger.LogWarning("Allocate filtered image frame 2, size: {width}x{height}", width, height);
				filteredImageFrame2 = await this.AllocateFilteredImageFrame(renderedImageFrame);
			}
			if (filteredImageFrame2 is null)
			{
				if (!cancellationTokenSource.IsCancellationRequested)
				{
					this.imageFilteringCancellationTokenSource = null;
					this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
					Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
				}
				else
					this.Logger.LogWarning("Filtering image has been cancelled");
				filteredImageFrame1.Dispose();
				return;
			}
		}
		var sourceImageFrame = renderedImageFrame;
		var resultImageFrame = filteredImageFrame1;
		var failedToApply = false;
		this.SetValue(InsufficientMemoryForRenderedImageProperty, false);

		// prepare for performance check
		var stopwatch = this.Application.IsDebugMode ? new Stopwatch() : null;
		var filteringStopwatch = Stopwatch.StartNew();

		// apply color LUT filter
		if (!failedToApply && isColorLutFilterNeeded)
		{
			// prepare LUT
			var rLut = ColorLut.ObtainIdentity(renderedImageFrame.BitmapBuffer.Format);
			var gLut = ColorLut.ObtainIdentity(renderedImageFrame.BitmapBuffer.Format);
			var bLut = ColorLut.ObtainIdentity(renderedImageFrame.BitmapBuffer.Format);
			try
			{
				stopwatch?.Restart();
				unsafe
				{
					var rFactor = this.RedColorAdjustment.Let(it => it > 0.001 ? it + 1 : -1 / (it - 1));
					var gFactor = this.GreenColorAdjustment.Let(it => it > 0.001 ? it + 1 : -1 / (it - 1));
					var bFactor = this.BlueColorAdjustment.Let(it => it > 0.001 ? it + 1 : -1 / (it - 1));
					var correction = 1 / ImageProcessing.SelectRgbToLuminanceConversion()(rFactor, gFactor, bFactor);
					rFactor *= correction;
					gFactor *= correction;
					bFactor *= correction;
					ColorLut.Multiply(rLut, rFactor);
					ColorLut.Multiply(gLut, gFactor);
					ColorLut.Multiply(bLut, bFactor);
				}
				if (stopwatch is not null)
					this.Logger.LogTrace("Take {ms} ms to prepare color LUT", stopwatch.ElapsedMilliseconds);
			}
			catch (Exception ex)
			{
				if (!cancellationTokenSource.IsCancellationRequested)
					this.Logger.LogError(ex, "Failed to prepare color LUT to filter image");
				else if (this.Application.IsDebugMode)
					this.Logger.LogWarning("Filtering cancelled when preparing color LUT");
			}

			// apply filter
			var parameters = new ColorLutImageFilter.Params()
			{
				RedLookupTable = rLut,
				GreenLookupTable = gLut,
				BlueLookupTable = bLut,
				AlphaLookupTable = ColorLut.ObtainReadOnlyIdentity(renderedImageFrame.BitmapBuffer.Format)
			};
			stopwatch?.Restart();
			if (await this.ApplyImageFilterAsync(new ColorLutImageFilter(), sourceImageFrame.AsNonNull(), resultImageFrame.AsNonNull(), parameters, cancellationTokenSource.Token))
			{
				if (stopwatch is not null)
					this.Logger.LogTrace("Take {ms} ms to apply color LUT filter", stopwatch.ElapsedMilliseconds);
				if (sourceImageFrame == renderedImageFrame)
				{
					sourceImageFrame = resultImageFrame;
					resultImageFrame = filteredImageFrame2;
				}
				else
					(sourceImageFrame, resultImageFrame) = (resultImageFrame, sourceImageFrame);
			}
			else
				failedToApply = true;
			ColorLut.Recycle(rLut);
			ColorLut.Recycle(gLut);
			ColorLut.Recycle(bLut);
		}

		// apply saturation filter
		if (!failedToApply && isSaturationFilterNeeded)
		{
			var parameters = new SaturationImageFilter.Params()
			{
				Saturation = this.SaturationAdjustment,
				Vibrance = this.VibranceAdjustment,
			};
			stopwatch?.Restart();
			if (await this.ApplyImageFilterAsync(new SaturationImageFilter(), sourceImageFrame.AsNonNull(), resultImageFrame.AsNonNull(), parameters, cancellationTokenSource.Token))
			{
				if (stopwatch is not null)
					this.Logger.LogTrace("Take {ms} ms to apply saturation filter", stopwatch.ElapsedMilliseconds);
				if (sourceImageFrame == renderedImageFrame)
				{
					sourceImageFrame = resultImageFrame;
					resultImageFrame = filteredImageFrame2;
				}
				else
					(sourceImageFrame, resultImageFrame) = (resultImageFrame, sourceImageFrame);
			}
			else
				failedToApply = true;
		}

		// apply luminance LUT filter
		if (!failedToApply && isLuminanceLutFilterNeeded)
		{
			// prepare LUT
			var lut = ColorLut.ObtainIdentity(renderedImageFrame.BitmapBuffer.Format);
			var histograms = renderedImageFrame.Histograms.AsNonNull();
			try
			{
				stopwatch?.Restart();
				if (this.canResetBrightnessAdjustment.Value)
					await ColorLut.BrightnessTransformAsync(histograms, lut, this.BrightnessAdjustment, this.Settings.GetValueOrDefault(SettingKeys.BrightnessTransformationFunction), cancellationTokenSource.Token);
				if (this.canResetContrastAdjustment.Value)
					await ColorLut.ContrastTransformAsync(lut, this.ContrastAdjustment, this.Settings.GetValueOrDefault(SettingKeys.ContrastTransformationFunction), cancellationTokenSource.Token);
				if (this.canResetHighlightAdjustment.Value)
					await ColorLut.HighlightTransformAsync(lut, this.HighlightAdjustment, cancellationTokenSource.Token);
				if (this.canResetShadowAdjustment.Value)
					await ColorLut.ShadowTransformAsync(lut, this.ShadowAdjustment, cancellationTokenSource.Token);
				if (stopwatch is not null)
					this.Logger.LogTrace("Take {ms} ms to prepare luminance LUT", stopwatch.ElapsedMilliseconds);
			}
			catch (Exception ex)
			{
				if (!cancellationTokenSource.IsCancellationRequested)
					this.Logger.LogError(ex, "Failed to prepare luminance LUT to filter image");
				else if (this.Application.IsDebugMode)
					this.Logger.LogWarning("Filtering cancelled when preparing luminance LUT");
			}

			// apply filter
			var parameters = new ColorLutImageFilter.Params()
			{
				RedLookupTable = lut,
				GreenLookupTable = lut,
				BlueLookupTable = lut,
				AlphaLookupTable = ColorLut.ObtainReadOnlyIdentity(renderedImageFrame.BitmapBuffer.Format)
			};
			stopwatch?.Restart();
			if (await this.ApplyImageFilterAsync(new ColorLutImageFilter(), sourceImageFrame.AsNonNull(), resultImageFrame.AsNonNull(), parameters, cancellationTokenSource.Token))
			{
				if (stopwatch is not null)
					this.Logger.LogTrace("Take {ms} ms to apply luminance LUT filter", stopwatch.ElapsedMilliseconds);
				if (sourceImageFrame == renderedImageFrame)
				{
					sourceImageFrame = resultImageFrame;
					resultImageFrame = filteredImageFrame2;
				}
				else
					(sourceImageFrame, resultImageFrame) = (resultImageFrame, sourceImageFrame);
			}
			else
				failedToApply = true;
			ColorLut.Recycle(lut);
		}

		// apply grayscale filter
		if (!failedToApply && isGrayscaleFilterNeeded)
		{
			stopwatch?.Restart();
			if (await this.ApplyImageFilterAsync(new LuminanceImageFilter(), sourceImageFrame.AsNonNull(), resultImageFrame.AsNonNull(), cancellationTokenSource.Token))
			{
				if (stopwatch is not null)
					this.Logger.LogTrace("Take {ms} ms to apply grayscale filter", stopwatch.ElapsedMilliseconds);
				if (sourceImageFrame == renderedImageFrame)
				{
					sourceImageFrame = resultImageFrame;
					// ReSharper disable once RedundantAssignment
					resultImageFrame = filteredImageFrame2;
				}
				else
					(sourceImageFrame, resultImageFrame) = (resultImageFrame, sourceImageFrame);
			}
			else
				failedToApply = true;
		}

		// stop measuring filter pipeline duration before post-filter steps
		filteringStopwatch.Stop();

		// check filtering result
		if (failedToApply)
		{
			this.cachedFilteredImageFrames.Add(filteredImageFrame1);
			if (filteredImageFrame2 is not null)
				this.cachedFilteredImageFrames.Add(filteredImageFrame2);
			if (!cancellationTokenSource.IsCancellationRequested)
			{
				this.imageFilteringCancellationTokenSource = null;
				this.SetValue(HasRenderingErrorProperty, true);
				Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
			}
			else if (this.Application.IsDebugMode)
				this.Logger.LogWarning("Filtering has been cancelled");
			return;
		}

		// generate histograms
		try
		{
			sourceImageFrame.AsNonNull().Histograms = await BitmapHistograms.CreateAsync(sourceImageFrame.AsNonNull().BitmapBuffer, this.SourceImageEffectiveBits, cancellationTokenSource.Token);
		}
		catch (Exception ex)
		{
			if (!cancellationTokenSource.IsCancellationRequested)
				this.Logger.LogError(ex, "Failed to generate histograms for filtered image");
			else if (this.Application.IsDebugMode)
				this.Logger.LogWarning("Filtering cancelled when generating histograms");
		}

		// cancellation check
		if (cancellationTokenSource.IsCancellationRequested)
		{
			this.cachedFilteredImageFrames.Add(filteredImageFrame1);
			if (filteredImageFrame2 is not null)
				this.cachedFilteredImageFrames.Add(filteredImageFrame2);
			if (this.Application.IsDebugMode)
				this.Logger.LogWarning("Filtering cancelled");
			return;
		}

		// log
		if (this.Application.IsDebugMode)
			this.Logger.LogTrace("Complete filtering image");

		// record filtering performance sample
		if (filterCount > 0)
		{
			var filterList = new List<string>(4);
			if (isColorLutFilterNeeded)
				filterList.Add("ColorLut");
			if (isSaturationFilterNeeded)
				filterList.Add("Saturation");
			if (isLuminanceLutFilterNeeded)
				filterList.Add("Luminance");
			if (isGrayscaleFilterNeeded)
				filterList.Add("Grayscale");
			this.RecordFilteringSample(width, height, filteringStopwatch.ElapsedMilliseconds, string.Join(",", filterList));
		}

		// complete
		this.imageFilteringCancellationTokenSource = null;
		if (this.filteredImageFrame is not null)
			this.cachedFilteredImageFrames.Add(this.filteredImageFrame);
		if (sourceImageFrame == filteredImageFrame1)
		{
			this.filteredImageFrame = filteredImageFrame1;
			if (filteredImageFrame2 is not null)
				this.cachedFilteredImageFrames.Add(filteredImageFrame2);
		}
		else
		{
			this.filteredImageFrame = filteredImageFrame2;
			if (filteredImageFrame1 is not null)
				this.cachedFilteredImageFrames.Add(filteredImageFrame1);
		}
		try
		{
			await this.ReportRenderedImageAsync(cancellationTokenSource);
		}
		catch (Exception ex)
		{
			if (ex is TaskCanceledException)
				return;
		}
		this.imageFilteringCancellationTokenSource = null;
	}


	/// <summary>
	/// Get or set whether rendered image should be fitted into viewport or not.
	/// </summary>
	public bool FitImageToViewport
	{
		get => this.GetValue(FitImageToViewportProperty);
		set => this.SetValue(FitImageToViewportProperty, value);
	}


	// Toggle horizontal flip of rendered image.
	void FlipX()
	{
		if (!this.IsSourceOpened)
			return;
		this.SetValue(IsImageFlippedXProperty, !this.GetValue(IsImageFlippedXProperty));
	}


	/// <summary>
	/// Command for flipping rendered image horizontally.
	/// </summary>
	public ICommand FlipXCommand { get; }


	// Toggle vertical flip of rendered image.
	void FlipY()
	{
		if (!this.IsSourceOpened)
			return;
		this.SetValue(IsImageFlippedYProperty, !this.GetValue(IsImageFlippedYProperty));
	}


	/// <summary>
	/// Command for flipping rendered image vertically.
	/// </summary>
	public ICommand FlipYCommand { get; }


	/// <summary>
	/// Get number of frames in source file.
	/// </summary>
	public long FrameCount => this.GetValue(FrameCountProperty);


	/// <summary>
	/// Get of set index of frame to render.
	/// </summary>
	public long FrameNumber
	{
		get => this.GetValue(FrameNumberProperty);
		set
		{
			this.StopPlayingFrames(); // moving to frame explicitly stops playback
			if (this.GetValue(IsSourceOpenedProperty) && value != this.GetValue(FrameNumberProperty))
				++this.frameNavigationCount;
			this.SetValue(FrameNumberProperty, value);
		}
	}


	/// <summary>
	/// Get of set padding size between frames in bytes.
	/// </summary>
	public long FramePaddingSize
	{
		get => this.GetValue(FramePaddingSizeProperty);
		set => this.SetValue(FramePaddingSizeProperty, value);
	}


	/// <summary>
	/// Get or set the target frame rate (frames per second) used when playing the frame sequence.
	/// </summary>
	public int FramePlaybackRate
	{
		get => this.GetValue(FramePlaybackRateProperty);
		set => this.SetValue(FramePlaybackRateProperty, value);
	}


	/// <summary>
	/// Get or set whether frame sequence playback loops back to the first frame after the last one.
	/// </summary>
	public bool IsFramePlaybackLooping
	{
		get => this.GetValue(IsFramePlaybackLoopingProperty);
		set => this.SetValue(IsFramePlaybackLoopingProperty, value);
	}


	/// <summary>
	/// Get or set whether frames are played as fast as possible, ignoring <see cref="FramePlaybackRate"/>.
	/// </summary>
	public bool IsFramePlaybackRateUnlimited
	{
		get => this.GetValue(IsFramePlaybackRateUnlimitedProperty);
		set => this.SetValue(IsFramePlaybackRateUnlimitedProperty, value);
	}


	/// <summary>
	/// Check whether the frame sequence is currently being played or not.
	/// </summary>
	public bool IsPlayingFrames => this.GetValue(IsPlayingFramesProperty);


	/// <summary>
	/// Generate proper name for new profile according to current parameters.
	/// </summary>
	/// <returns>Name for new profile.</returns>
	public string GenerateNameForNewProfile()
	{
		var name = $"{this.ImageWidth}x{this.ImageHeight} [{this.ImageRenderer.Format.DisplayName}]";
		if (ImageRenderingProfiles.ValidateNewUserDefinedProfileName(name))
			return name;
		for (var i = 1; i <= 1000; ++i)
		{
			var alternativeName = $"{name} ({i})";
			if (ImageRenderingProfiles.ValidateNewUserDefinedProfileName(alternativeName))
				return alternativeName;
		}
		return "";
	}


	/// <summary>
	/// Get or set green color adjustment.
	/// </summary>
	public double GreenColorAdjustment
	{
		get => this.GetValue(GreenColorAdjustmentProperty);
		set => this.SetValue(GreenColorAdjustmentProperty, value);
	}


	/// <summary>
	/// Get or set gain of green color.
	/// </summary>
	public double GreenColorGain
	{
		get => this.GetValue(GreenColorGainProperty);
		set => this.SetValue(GreenColorGainProperty, value);
	}


	/// <summary>
	/// Check whether <see cref="BrightnessAdjustment"/> is non-zero or not.
	/// </summary>
	public bool HasBrightnessAdjustment => this.GetValue(HasBrightnessAdjustmentProperty);


	/// <summary>
	/// Check whether at least one of <see cref="RedColorAdjustment"/>, <see cref="GreenColorAdjustment"/>, <see cref="BlueColorAdjustment"/> is non-zero or not.
	/// </summary>
	public bool HasColorAdjustment => this.GetValue(HasColorAdjustmentProperty);


	/// <summary>
	/// Check whether at least one color table is applied to the current rendering or not.
	/// </summary>
	/// <remarks>The color tables define the colors to be rendered, so the effective bits of image planes are decided by the tables instead of the value set by user.</remarks>
	public bool HasColorTables => this.GetValue(HasColorTablesProperty);


	/// <summary>
	/// Check whether <see cref="ContrastAdjustment"/> is non-zero or not.
	/// </summary>
	public bool HasContrastAdjustment => this.GetValue(HasContrastAdjustmentProperty);


	/// <summary>
	/// Check whether <see cref="HighlightAdjustment"/> is non-zero or not.
	/// </summary>
	public bool HasHighlightAdjustment => this.GetValue(HasHighlightAdjustmentProperty);


	/// <summary>
	/// Check whether <see cref="Histograms"/> is valid or not.
	/// </summary>
	public bool HasHistograms => this.GetValue(HasHistogramsProperty);


	/// <summary>
	/// Check whether 1st image plane exists or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool HasImagePlane1 => this.GetValue(HasImagePlane1Property);


	/// <summary>
	/// Check whether 2nd image plane exists or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool HasImagePlane2 => this.GetValue(HasImagePlane2Property);


	/// <summary>
	/// Check whether 3rd image plane exists or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool HasImagePlane3 => this.GetValue(HasImagePlane3Property);


	/// <summary>
	/// Check whether multiple byte orderings are supported by the format of current <see cref="ImageRenderer"/> or not.
	/// </summary>
	public bool HasMultipleByteOrderings => this.GetValue(HasMultipleByteOrderingsProperty);


	/// <summary>
	/// Check whether multiple frames are contained in source file or not.
	/// </summary>
	public bool HasMultipleFrames => this.GetValue(HasMultipleFramesProperty);


	/// <summary>
	/// Check whether <see cref="QuarterSizeRenderedImage"/> is non-null or not.
	/// </summary>
	public bool HasQuarterSizeRenderedImage => this.GetValue(HasQuarterSizeRenderedImageProperty);


	/// <summary>
	/// Check whether <see cref="RenderedImage"/> is non-null or not.
	/// </summary>
	public bool HasRenderedImage => this.GetValue(HasRenderedImageProperty);


	/// <summary>
	/// Check whether error was occurred when rendering or not.
	/// </summary>
	public bool HasRenderingError => this.GetValue(HasRenderingErrorProperty);


	/// <summary>
	/// Check whether RGB gain is not 1.0 or not.
	/// </summary>
	public bool HasRgbGain => this.GetValue(HasRgbGainProperty);


	/// <summary>
	/// Check whether <see cref="SaturationAdjustment"/> is non-zero or not.
	/// </summary>
	public bool HasSaturationAdjustment => this.GetValue(HasSaturationAdjustmentProperty);


	/// <summary>
	/// Check whether there is a pixel selected on rendered image or not.
	/// </summary>
	public bool HasSelectedRenderedImagePixel => this.GetValue(HasSelectedRenderedImagePixelProperty);


	/// <summary>
	/// Check whether <see cref="ShadowAdjustment"/> is non-zero or not.
	/// </summary>
	public bool HasShadowAdjustment => this.GetValue(HasShadowAdjustmentProperty);


	/// <summary>
	/// Check whether <see cref="SourceDataSize"/> is non-zero or not.
	/// </summary>
	public bool HasSourceDataSize => this.GetValue(HasSourceDataSizeProperty);


	/// <summary>
	/// Check whether <see cref="VibranceAdjustment"/> is non-zero or not.
	/// </summary>
	public bool HasVibranceAdjustment => this.GetValue(HasVibranceAdjustmentProperty);


	// Hibernate another session.
	async Task<bool> HibernateAnotherSessionAsync()
	{
		var maxMemoryUsage = 0L;
		var sessionToClearRenderedImage = (Session?)null;
		foreach (var candidateSession in ((Workspace)this.Owner.AsNonNull()).Sessions)
		{
			if (candidateSession == this || candidateSession.IsActivated || candidateSession.IsHibernated)
				continue;
			if (candidateSession.RenderedImagesMemoryUsage > maxMemoryUsage)
			{
				maxMemoryUsage = candidateSession.RenderedImagesMemoryUsage;
				sessionToClearRenderedImage = candidateSession;
			}
		}
		if (sessionToClearRenderedImage is not null)
		{
			this.Logger.LogWarning("Hibernate {sessionToClearRenderedImage}", sessionToClearRenderedImage);
			if (await sessionToClearRenderedImage.HibernateAsync()) // the images of the session are released before it completes, so no extra waiting is needed for the memory to be reclaimed
				return true;
			this.Logger.LogError("Failed to hibernate {sessionToClearRenderedImage}", sessionToClearRenderedImage);
			return false;
		}
		this.Logger.LogWarning("No deactivated session to hibernate");
		return false;
	}


	// Hibernate.
	async Task<bool> HibernateAsync()
    {
		// check state
		if (this.IsDisposed || this.IsActivated)
			return false;
		if (this.IsHibernated)
			return true;

		// cancel reporting, filtering and rendering, then wait for the completion of what is being cancelled
		this.CancelReportingRenderedImage();
		await this.CancelFilteringImageAsync();
		await this.CancelRenderingImageAsync(true);

		// give up if the session has been activated or disposed while waiting, the images belong to the activated session again
		if (this.IsDisposed)
			return false;
		if (this.IsActivated)
		{
			this.Logger.LogWarning("Give up hibernation, the session has been activated");
			return false;
		}
		if (this.imageFilteringCompletionSource is not null || this.imageRenderingCompletionSource is not null)
		{
			this.Logger.LogWarning("Give up hibernation, a new rendering has been started");
			return false;
		}
		if (this.IsHibernated)
			return true;

		this.Logger.LogWarning("Hibernate");

		// update state and release the images
		this.SetValue(IsHibernatedProperty, true);
		this.DisposeFilteredImage();
		this.DisposeRenderedImage();

		// complete
		return true;
    }


	/// <summary>
	/// Get or set highlight adjustment for filter.
	/// </summary>
	public double HighlightAdjustment
	{
		get => this.GetValue(HighlightAdjustmentProperty);
		set => this.SetValue(HighlightAdjustmentProperty, value);
	}


	/// <summary>
	/// Get histograms of <see cref="RenderedImage"/>.
	/// </summary>
	public BitmapHistograms? Histograms => this.GetValue(HistogramsProperty);


	/// <summary>
	/// Get or set width of panel of histograms in pixels.
	/// </summary>
	public double HistogramsPanelSize
	{
		get => this.GetValue(HistogramsPanelSizeProperty);
		set => this.SetValue(HistogramsPanelSizeProperty, value);
	}


	/// <summary>
	/// Get rotation for displaying rendered image.
	/// </summary>
	public double ImageDisplayRotation => this.GetValue(ImageDisplayRotationProperty);


	/// <summary>
	/// Get proper scale for displaying rendered image.
	/// </summary>
	public double ImageDisplayScale => this.GetValue(ImageDisplayScaleProperty);


	/// <summary>
	/// Get proper size for displaying rendered image.
	/// </summary>
	public Size ImageDisplaySize => this.GetValue(ImageDisplaySizeProperty);


	/// <summary>
	/// Get or set the requested height of <see cref="RenderedImage"/> in pixels.
	/// </summary>
	public int ImageHeight
	{
		get => this.GetValue(ImageHeightProperty);
		set => this.SetValue(ImageHeightProperty, value);
	}


	/// <summary>
	/// Get number of image planes according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public int ImagePlaneCount => this.GetValue(ImagePlaneCountProperty);


	/// <summary>
	/// Get or set <see cref="IImageRenderer"/> for rendering image from current source file.
	/// </summary>
	// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
	public IImageRenderer ImageRenderer
	{
		get => this.GetValue(ImageRendererProperty).AsNonNull();
		set => this.SetValue(ImageRendererProperty, value ?? this.SelectDefaultImageRenderer());
	}
	// ReSharper restore NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract


	/// <summary>
	/// Raised when image saving completed.
	/// </summary>
	public event EventHandler<ImageSavingCompletedEventArgs>? ImageSavingCompleted;


	/// <summary>
	/// Get or set size of viewport of showing rendered image.
	/// </summary>
	public Size ImageViewportSize
	{
		get => this.GetValue(ImageViewportSizeProperty);
		set => this.SetValue(ImageViewportSizeProperty, value);
	}


	/// <summary>
	/// Get or set the requested width of <see cref="RenderedImage"/> in pixels.
	/// </summary>
	public int ImageWidth
	{
		get => this.GetValue(ImageWidthProperty);
		set => this.SetValue(ImageWidthProperty, value);
	}


	/// <summary>
	/// Value to indicate whether there is insufficient memory for rendered image or not.
	/// </summary>
	public bool InsufficientMemoryForRenderedImage => this.GetValue(InsufficientMemoryForRenderedImageProperty);


	/// <summary>
	/// Check whether session is activated or not.
	/// </summary>
	public bool IsActivated => this.GetValue(IsActivatedProperty);


	/// <summary>
	/// Check whether effective bits for 1st image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool IsAdjustableEffectiveBits1 => this.GetValue(IsAdjustableEffectiveBits1Property);


	/// <summary>
	/// Check whether effective bits for 2nd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool IsAdjustableEffectiveBits2 => this.GetValue(IsAdjustableEffectiveBits2Property);


	/// <summary>
	/// Check whether effective bits for 3rd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool IsAdjustableEffectiveBits3 => this.GetValue(IsAdjustableEffectiveBits3Property);


	/// <summary>
	/// Check whether pixel stride for 1st image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool IsAdjustablePixelStride1 => this.GetValue(IsAdjustablePixelStride1Property);


	/// <summary>
	/// Check whether pixel stride for 2nd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool IsAdjustablePixelStride2 => this.GetValue(IsAdjustablePixelStride2Property);


	/// <summary>
	/// Check whether pixel stride for 3rd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
	/// </summary>
	public bool IsAdjustablePixelStride3 => this.GetValue(IsAdjustablePixelStride3Property);


	/// <summary>
	/// Check whether the source image carries a meaningful alpha channel. True for ARGB-category renderers and for Compressed-category renderers whose source <see cref="FileFormat"/> is PNG, WebP, or HEIF; false otherwise.
	/// </summary>
	public bool IsAlphaChannelAvailable => this.GetValue(IsAlphaChannelAvailableProperty);


	/// <summary>
	/// Check whether <see cref="BayerPattern"/> is supported by current <see cref="ImageRenderer"/> or not.
	/// </summary>
	public bool IsBayerPatternSupported => this.GetValue(IsBayerPatternSupportedProperty);


	/// <summary>
	/// Check whether brightness adjustment is supported or not.
	/// </summary>
	public bool IsBrightnessAdjustmentSupported => this.GetValue(IsBrightnessAdjustmentSupportedProperty);


	/// <summary>
	/// Check whether color adjustment is supported or not.
	/// </summary>
	public bool IsColorAdjustmentSupported => this.GetValue(IsColorAdjustmentSupportedProperty);


	/// <summary>
	/// Check whether color space management is enabled or not.
	/// </summary>
	public bool IsColorSpaceManagementEnabled => this.GetValue(IsColorSpaceManagementEnabledProperty);


	/// <summary>
	/// Check whether image format supported by current <see cref="ImageRenderer"/> is a compressed format or not.
	/// </summary>
	public bool IsCompressedImageFormat => this.GetValue(IsCompressedImageFormatProperty);


	/// <summary>
	/// Check whether contrast adjustment is supported or not.
	/// </summary>
	public bool IsContrastAdjustmentSupported => this.GetValue(IsContrastAdjustmentSupportedProperty);


	/// <summary>
	/// Check whether color space of rendered image is being converted or not.
	/// </summary>
	public bool IsConvertingColorSpace => this.GetValue(IsConvertingColorSpaceProperty);


	/// <summary>
	/// Check whether demosaicing is supported by current <see cref="ImageRenderer"/> or not.
	/// </summary>
	public bool IsDemosaicingSupported => this.GetValue(IsDemosaicingSupportedProperty);


	/// <summary>
	/// Check whether rendered image is being filtered or not.
	/// </summary>
	public bool IsFilteringRenderedImage => this.GetValue(IsFilteringRenderedImageProperty);


	/// <summary>
	/// Check whether rendered image is needed to be filtered or not.
	/// </summary>
	public bool IsFilteringRenderedImageNeeded => this.GetValue(IsFilteringRenderedImageNeededProperty);


	/// <summary>
	/// Enable or disable grayscale filter.
	/// </summary>
	public bool IsGrayscaleFilterEnabled
	{
		get => this.GetValue(IsGrayscaleFilterEnabledProperty);
		set => this.SetValue(IsGrayscaleFilterEnabledProperty, value);
	}


	/// <summary>
	/// Check whether grayscale filter is supported or not.
	/// </summary>
	public bool IsGrayscaleFilterSupported => this.GetValue(IsGrayscaleFilterSupportedProperty);


	/// <summary>
	/// Check whether instance is hibernated or not.
	/// </summary>
	public bool IsHibernated => this.GetValue(IsHibernatedProperty);


	/// <summary>
	/// Check whether highlight adjustment is supported or not.
	/// </summary>
	public bool IsHighlightAdjustmentSupported => this.GetValue(IsHighlightAdjustmentSupportedProperty);


	/// <summary>
	/// Get or set whether the mean marker of histograms is visible or not.
	/// </summary>
	public bool IsHistogramMeanMarkerVisible
	{
		get => this.GetValue(IsHistogramMeanMarkerVisibleProperty);
		set => this.SetValue(IsHistogramMeanMarkerVisibleProperty, value);
	}


	/// <summary>
	/// Get or set whether histograms of image is visible or not
	/// </summary>
	public bool IsHistogramsVisible
	{
		get => this.GetValue(IsHistogramsVisibleProperty);
		set => this.SetValue(IsHistogramsVisibleProperty, value);
	}


	/// <summary>
	/// Get or set whether displayed image is mirrored horizontally.
	/// </summary>
	public bool IsImageFlippedX
	{
		get => this.GetValue(IsImageFlippedXProperty);
		set => this.SetValue(IsImageFlippedXProperty, value);
	}


	/// <summary>
	/// Get or set whether displayed image is mirrored vertically.
	/// </summary>
	public bool IsImageFlippedY
	{
		get => this.GetValue(IsImageFlippedYProperty);
		set => this.SetValue(IsImageFlippedYProperty, value);
	}


	/// <summary>
	/// Check whether source file is being opened or not.
	/// </summary>
	public bool IsOpeningSource => this.GetValue(IsOpeningSourceProperty);


	/// <summary>
	/// Check whether image is being processed or not.
	/// </summary>
	public bool IsProcessingImage => this.GetValue(IsProcessingImageProperty);


	/// <summary>
	/// Check whether PixelViewer Pro is activated or not.
	/// </summary>
	public bool IsProVersionActivated => this.GetValue(IsProVersionActivatedProperty);


	/// <summary>
	/// Check whether image is being rendered or not.
	/// </summary>
	public bool IsRenderingImage => this.GetValue(IsRenderingImageProperty);


	/// <summary>
	/// Get or set whether panel of rendering parameters is visible or not.
	/// </summary>
	public bool IsRenderingParametersPanelVisible
    {
		get => this.GetValue(IsRenderingParametersPanelVisibleProperty);
		set => this.SetValue(IsRenderingParametersPanelVisibleProperty, value);
    }


	/// <summary>
	/// Check whether RGB gain is available for current <see cref="ImageRenderer"/> or not.
	/// </summary>
	public bool IsRgbGainSupported => this.GetValue(IsRgbGainSupportedProperty);


	/// <summary>
	/// Check whether saturation adjustment is supported or not.
	/// </summary>
	public bool IsSaturationAdjustmentSupported => this.GetValue(IsSaturationAdjustmentSupportedProperty);


	/// <summary>
	/// Check whether filtered image is being saved or not.
	/// </summary>
	public bool IsSavingFilteredImage => this.GetValue(IsSavingFilteredImageProperty);


	/// <summary>
	/// Check whether at least one image is being saved or not.
	/// </summary>
	public bool IsSavingImage => this.GetValue(IsSavingImageProperty);


	/// <summary>
	/// Check whether rendered image is being saved or not.
	/// </summary>
	public bool IsSavingRenderedImage => this.GetValue(IsSavingRenderedImageProperty);


	/// <summary>
	/// Check whether shadow adjustment is supported or not.
	/// </summary>
	public bool IsShadowAdjustmentSupported => this.GetValue(IsShadowAdjustmentSupportedProperty);


	/// <summary>
	/// Check whether source image file has been opened or not.
	/// </summary>
	public bool IsSourceOpened => this.GetValue(IsSourceOpenedProperty);


	/// <summary>
	/// Check whether vibrance adjustment is supported or not.
	/// </summary>
	public bool IsVibranceAdjustmentSupported => this.GetValue(IsVibranceAdjustmentSupportedProperty);


	/// <summary>
	/// Check whether <see cref="YuvToBgraConverter"/> is supported by current <see cref="ImageRenderer"/> or not.
	/// </summary>
	public bool IsYuvToBgraConverterSupported => this.GetValue(IsYuvToBgraConverterSupportedProperty);


	/// <summary>
	/// Check whether smooth zooming is on-going or not.
	/// </summary>
	public bool IsZooming => this.GetValue(IsZoomingProperty);


	/// <summary>
	/// Get <see cref="Geometry"/> of luminance histogram.
	/// </summary>
	public Geometry? LuminanceHistogramGeometry => this.GetValue(LuminanceHistogramGeometryProperty);


	/// <summary>
	/// Command to move to first frame and render.
	/// </summary>
	public ICommand MoveToFirstFrameCommand { get; }


	/// <summary>
	/// Command to move to last frame and render.
	/// </summary>
	public ICommand MoveToLastFrameCommand { get; }


	/// <summary>
	/// Command to move to next frame and render.
	/// </summary>
	public ICommand MoveToNextFrameCommand { get; }


	/// <summary>
	/// Command to move to previous frame and render.
	/// </summary>
	public ICommand MoveToPreviousFrameCommand { get; }


	// Called when list of all color spaces changed.
	void OnAllColorSpacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		switch (e.Action)
		{
			case NotifyCollectionChangedAction.Add:
				this.colorSpaces.AddAll(e.NewItems.AsNonNull().Cast<ColorSpace>());
				break;
			case NotifyCollectionChangedAction.Remove:
				this.colorSpaces.RemoveAll(e.OldItems.AsNonNull().Cast<ColorSpace>());
				break;
		}
	}


	// Called when list of all demosaicing algorithms changed.
	void OnAllDemosaicingAlgorithmsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
		this.UpdateDemosaicingAlgorithms();
	
	
	// Called when property of application changed.
	void OnApplicationPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(App.IsProVersionActivated))
			this.SetValue(IsProVersionActivatedProperty, (sender as App)?.IsProVersionActivated == true);
	}


	// Called when strings updated.
	protected override void OnApplicationStringsUpdated()
	{
		base.OnApplicationStringsUpdated();
		this.UpdateTitle();
	}


	// Raise PropertyChanged event for black level.
	void OnBlackLevelChanged(int index) => this.OnPropertyChanged(index switch
	{
		0 => nameof(this.BlackLevel1),
		1 => nameof(this.BlackLevel2),
		2 => nameof(this.BlackLevel3),
		_ => throw new ArgumentOutOfRangeException(nameof(index)),
	});


	// Raise PropertyChanged event for effective bits.
	void OnEffectiveBitsChanged(int index) => this.OnPropertyChanged(index switch
	{
		0 => nameof(this.EffectiveBits1),
		1 => nameof(this.EffectiveBits2),
		2 => nameof(this.EffectiveBits3),
		_ => throw new ArgumentOutOfRangeException(nameof(index)),
	});


	// Called when property of current image renderer changed.
	void OnImageRendererPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// ignore event from renderer which is no longer the current one
		if (sender is not IImageRenderer imageRenderer || !ReferenceEquals(imageRenderer, this.GetValue(ImageRendererProperty)))
			return;

		// apply swapped format
		switch (e.PropertyName)
		{
			case nameof(IImageRenderer.Format):
				this.isImageRenderingForced = true;
				this.ApplyImageRendererFormat(imageRenderer);
				break;
		}
	}


	// Called when owner changed.
	protected override void OnOwnerChanged(ViewModel? prevOwner, ViewModel? newOwner)
	{
		base.OnOwnerChanged(prevOwner, newOwner);
		this.effectiveScreenColorSpaceObserverToken = this.effectiveScreenColorSpaceObserverToken.DisposeAndReturnNull();
		this.effectiveScreenColorSpaceObserverToken = (newOwner as Workspace)?.GetValueAsObservable(Workspace.EffectiveScreenColorSpaceProperty).Subscribe(this.effectiveScreenColorSpaceObserver);
		this.OnScreenColorSpaceChanged();
	}


	// Raise PropertyChanged event for pixel stride.
	void OnPixelStrideChanged(int index) => this.OnPropertyChanged(index switch
	{
		0 => nameof(this.PixelStride1),
		1 => nameof(this.PixelStride2),
		2 => nameof(this.PixelStride3),
		_ => throw new ArgumentOutOfRangeException(nameof(index)),
	});


	// Called when property of profile changed.
	void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ImageRenderingProfile.Name))
			(sender as ImageRenderingProfile)?.Let(it => this.profiles.Sort(it));
	}


	// Property changed.
	protected override void OnPropertyChanged(ObservableProperty property, object? oldValue, object? newValue)
	{
		base.OnPropertyChanged(property, oldValue, newValue);
		if (property == BayerPatternProperty)
		{
			this.UpdateDemosaicingAlgorithms();
			if (this.IsBayerPatternSupported)
				this.renderImageAction.Reschedule();
		}
		else if (property == BlueColorAdjustmentProperty
			|| property == GreenColorAdjustmentProperty
			|| property == RedColorAdjustmentProperty)
		{
			this.SetValue(HasColorAdjustmentProperty, Math.Abs(this.BlueColorAdjustment) > 0.01
				|| Math.Abs(this.GreenColorAdjustment) > 0.01
				|| Math.Abs(this.RedColorAdjustment) > 0.01);
			this.canResetColorAdjustment.Update(this.HasColorAdjustment && this.IsColorAdjustmentSupported);
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Schedule(RenderImageDelay);
			if (this.IsSourceOpened)
				this.trackFilteringParamsAppliedAction.Reschedule(TrackFilteringParamsAppliedEventDelay);
		}
		else if (property == BlueColorGainProperty
			|| property == GreenColorGainProperty
			|| property == RedColorGainProperty)
		{
			if (this.IsRgbGainSupported)
				this.renderImageAction.Reschedule(RenderImageDelay);
			this.SetValue(HasRgbGainProperty, Math.Abs(this.BlueColorGain - 1) > 0.001
				|| Math.Abs(this.GreenColorGain - 1) > 0.001
				|| Math.Abs(this.RedColorGain - 1) > 0.001);
		}
		else if (property == BrightnessAdjustmentProperty)
		{
			this.SetValue(HasBrightnessAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
			this.canResetBrightnessAdjustment.Update(this.HasBrightnessAdjustment && this.IsBrightnessAdjustmentSupported);
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Schedule(RenderImageDelay);
			if (this.IsSourceOpened)
				this.trackFilteringParamsAppliedAction.Reschedule(TrackFilteringParamsAppliedEventDelay);
		}
		else if (property == ByteOrderingProperty)
		{
			if (this.HasMultipleByteOrderings)
				this.renderImageAction.Reschedule();
		}
		else if (property == ColorSpaceProperty
			|| property == UseLinearColorSpaceProperty)
		{
			if (this.IsColorSpaceManagementEnabled)
				this.renderImageAction.Reschedule();
			if (this.IsSourceOpened)
				this.trackRenderingParamsAppliedAction.Reschedule(TrackRenderingParamsAppliedEventDelay);
		}
		else if (property == ContrastAdjustmentProperty)
		{
			this.SetValue(HasContrastAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
			this.canResetContrastAdjustment.Update(this.HasContrastAdjustment && this.IsContrastAdjustmentSupported);
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Schedule(RenderImageDelay);
			if (this.IsSourceOpened)
				this.trackFilteringParamsAppliedAction.Reschedule(TrackFilteringParamsAppliedEventDelay);
		}
		else if (property == CustomTitleProperty)
			this.UpdateTitle();
		else if (property == DataOffsetProperty
			|| property == FrameNumberProperty
			|| property == FramePaddingSizeProperty
			|| property == ImageHeightProperty)
		{
			this.renderImageAction.Reschedule();
		}
		else if (property == DemosaicingAlgorithmProperty)
		{
			// notify the change of on/off state of demosaicing
			this.OnPropertyChanged(nameof(this.Demosaicing));

			// re-render the image
			if (this.IsDemosaicingSupported)
				this.renderImageAction.Reschedule();
		}
		else if (property == FitImageToViewportProperty)
		{
			var fitToViewport = (bool)newValue.AsNonNull();
			this.canZoomTo.Update(!fitToViewport && this.IsSourceOpened);
			this.UpdateCanZoomInOut();
			if (!fitToViewport)
				this.ZoomTo(this.GetValue(RequestedImageDisplayScaleProperty));
			else if (double.IsFinite(this.fitRenderedImageToViewportScale))
			{
				var scale = ((int)(this.GetValue(ImageDisplayRotationProperty) + 0.5) % 180) == 0
					? this.fitRenderedImageToViewportScale
					: this.fitRenderedImageToViewportScaleSwapped;
				this.ZoomTo(scale);
			}
			else
				this.updateImageDisplaySizeAction.Execute();
		}
		else if (property == FrameCountProperty)
		{
			this.SetValue(HasMultipleFramesProperty, (long)newValue.AsNonNull() > 1);
			this.UpdateCanPlayFrames();
		}
		else if (property == FramePlaybackRateProperty)
		{
			this.PersistentState.SetValue(LatestFramePlaybackRate, (int)newValue.AsNonNull());
			this.RestartFramePlaybackTimeline();
		}
		else if (property == IsFramePlaybackLoopingProperty)
			this.PersistentState.SetValue(IsInitFramePlaybackLooping, (bool)newValue.AsNonNull());
		else if (property == IsFramePlaybackRateUnlimitedProperty)
		{
			this.PersistentState.SetValue(IsInitFramePlaybackRateUnlimited, (bool)newValue.AsNonNull());
			this.RestartFramePlaybackTimeline();
		}
		else if (property == HasRenderingErrorProperty)
		{
			if ((bool)newValue!)
				this.trackRenderingParamsAppliedAction.Cancel();
			else if (this.GetValue(IsSourceOpenedProperty))
				this.trackRenderingParamsAppliedAction.Reschedule(TrackRenderingParamsAppliedEventDelay);
		}
		else if (property == HighlightAdjustmentProperty)
		{
			this.SetValue(HasHighlightAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
			this.canResetHighlightAdjustment.Update(this.HasHighlightAdjustment && this.IsHighlightAdjustmentSupported);
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Schedule(RenderImageDelay);
			if (this.IsSourceOpened)
				this.trackFilteringParamsAppliedAction.Reschedule(TrackFilteringParamsAppliedEventDelay);
		}
		else if (property == HistogramsPanelSizeProperty)
			this.PersistentState.SetValue(LatestHistogramsPanelSize, (int)(this.HistogramsPanelSize + 0.5));
		else if (property == HistogramsProperty)
			this.SetValue(HasHistogramsProperty, newValue is not null);
		else if (property == ImageRendererProperty)
		{
			// detach from previous renderer, its format may be swapped when user edits it
			(oldValue as IImageRenderer)?.Let(it => it.PropertyChanged -= this.OnImageRendererPropertyChanged);

			// attach to new renderer and apply its format
			if (ImageRenderers.All.Contains(newValue))
			{
				var imageRenderer = (IImageRenderer)newValue.AsNonNull();
				imageRenderer.PropertyChanged += this.OnImageRendererPropertyChanged;
				this.ApplyImageRendererFormat(imageRenderer);
			}
			else
			{
				this.Logger.LogError("{newValue} is not part of available image renderer list", newValue);
				this.trackRenderingParamsAppliedAction.Cancel();
			}
		}
		else if (property == ImageViewportSizeProperty
			|| property == ScreenPixelDensityProperty)
		{
			this.fitRenderedImageToViewportScale = double.NaN;
			this.updateImageDisplaySizeAction.Schedule();
		}
		else if (property == ImageWidthProperty)
		{
			if (this.Settings.GetValueOrDefault(SettingKeys.ResetImagePlaneOptionsAfterChangingImageDimensions))
				this.isImagePlaneOptionsResetNeeded = true;
			this.renderImageAction.Reschedule();
		}
		else if (property == IsBrightnessAdjustmentSupportedProperty)
		{
			this.canResetBrightnessAdjustment.Update(this.HasBrightnessAdjustment && (bool)newValue.AsNonNull());
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Reschedule();
		}
		else if (property == IsColorAdjustmentSupportedProperty)
		{
			this.canResetColorAdjustment.Update(this.HasColorAdjustment && (bool)newValue.AsNonNull());
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Reschedule();
		}
		else if (property == IsColorSpaceManagementEnabledProperty)
		{
			if (this.IsActivated)
				this.renderImageAction.Reschedule();
			else
				_ = this.ClearRenderedImageAsync();
			if (this.IsSourceOpened)
				this.trackRenderingParamsAppliedAction.Reschedule(TrackRenderingParamsAppliedEventDelay);
		}
		else if (property == IsContrastAdjustmentSupportedProperty)
		{
			this.canResetContrastAdjustment.Update(this.HasContrastAdjustment && (bool)newValue.AsNonNull());
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Reschedule();
		}
		else if (property == IsFilteringRenderedImageNeededProperty)
		{
			if ((bool)newValue.AsNonNull())
				this.filterImageAction.Schedule();
			else
			{
				_ = this.CancelFilteringImageAsync();
				this.SynchronizationContext.Post(async () =>
				{
					using var cancellationTokenSource = new CancellationTokenSource();
					await this.ReportRenderedImageAsync(cancellationTokenSource);
				});
				this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
			}
		}
		else if (property == IsFilteringRenderedImageProperty
			|| property == IsOpeningSourceProperty
			|| property == IsRenderingImageProperty)
		{
			this.updateIsProcessingImageAction.Schedule();
			if (property == IsRenderingImageProperty && !(bool)newValue.AsNonNull())
				this.ScheduleNextFrameForPlayback(); // rendering of current frame completed, playback moves to next frame
		}
		else if (property == IsGrayscaleFilterEnabledProperty
			|| property == IsGrayscaleFilterSupportedProperty)
		{
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Schedule();
			if (this.IsSourceOpened)
				this.trackFilteringParamsAppliedAction.Reschedule(TrackFilteringParamsAppliedEventDelay);
		}
		else if (property == IsHighlightAdjustmentSupportedProperty)
		{
			this.canResetHighlightAdjustment.Update(this.HasHighlightAdjustment && (bool)newValue.AsNonNull());
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Reschedule();
		}
		else if (property == IsHistogramMeanMarkerVisibleProperty)
			this.PersistentState.SetValue(IsInitHistogramMeanMarkerVisible, (bool)newValue.AsNonNull());
		else if (property == IsHistogramsVisibleProperty)
			this.PersistentState.SetValue(IsInitHistogramsPanelVisible, (bool)newValue.AsNonNull());
		else if (property == IsSaturationAdjustmentSupportedProperty)
		{
			this.canResetSaturationAdjustment.Update(this.HasSaturationAdjustment && (bool)newValue.AsNonNull());
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Reschedule();
		}
		else if (property == IsSavingFilteredImageProperty
			|| property == IsSavingRenderedImageProperty)
		{
			this.SetValue(IsSavingImageProperty, this.IsSavingFilteredImage || this.IsSavingRenderedImage);
			this.updateIsProcessingImageAction.Schedule();
		}
		else if (property == IsShadowAdjustmentSupportedProperty)
		{
			this.canResetShadowAdjustment.Update(this.HasShadowAdjustment && (bool)newValue.AsNonNull());
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Reschedule();
		}
		else if (property == IsSourceOpenedProperty)
		{
			if (this.IsSourceOpened)
				this.updateFilterSupportingAction.Schedule();
			else
			{
				this.trackFilteringParamsAppliedAction.Cancel();
				this.trackRenderingParamsAppliedAction.Cancel();
				this.updateFilterSupportingAction.Execute();
			}
			this.UpdateCanZoomInOut();
			this.UpdateCanPlayFrames();
		}
		else if (property == IsVibranceAdjustmentSupportedProperty)
		{
			this.canResetVibranceAdjustment.Update(this.HasVibranceAdjustment && (bool)newValue.AsNonNull());
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Reschedule();
		}
		else if (property == IsYuvToBgraConverterSupportedProperty)
		{
			if ((bool)newValue.AsNonNull())
			{
				this.SetValue(ColorSpaceProperty, this.YuvToBgraConverter.ColorSpace);
				if (this.IsColorSpaceManagementEnabled)
					this.renderImageAction.Reschedule();
			}
			if (this.IsSourceOpened)
				this.trackRenderingParamsAppliedAction.Reschedule(TrackRenderingParamsAppliedEventDelay);
		}
		else if (property == ProfileProperty)
		{
			if (this.IsSourceOpened)
				this.trackRenderingParamsAppliedAction.Reschedule(TrackRenderingParamsAppliedEventDelay);
			else
				this.trackRenderingParamsAppliedAction.Cancel();
			this.canApplyProfile.Update(((ImageRenderingProfile)newValue.AsNonNull()).Type != ImageRenderingProfileType.Default);
			this.ApplyProfile();
		}
		else if (property == QuarterSizeRenderedImageProperty)
		{
			if (newValue is null)
			{
				this.cachedAvaQuarterSizeRenderedImage = null;
				this.avaQuarterSizeRenderedImageMemoryUsageToken = this.avaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
				this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
			}
			this.SetValue(HasQuarterSizeRenderedImageProperty, newValue is not null);
		}
		else if (property == RenderedImageProperty)
		{
			if (newValue is null)
			{
				this.cachedAvaRenderedImage = null;
				this.avaRenderedImageMemoryUsageToken = this.avaRenderedImageMemoryUsageToken.DisposeAndReturnNull();
				this.cachedAvaRenderedImageMemoryUsageToken = this.cachedAvaRenderedImageMemoryUsageToken.DisposeAndReturnNull();
			}
			this.SetValue(HasRenderedImageProperty, newValue is not null);
			if (oldValue is null || newValue is null || ((IImage)oldValue).Size != ((IImage)newValue).Size)
				this.fitRenderedImageToViewportScale = double.NaN;
			this.updateImageDisplaySizeAction.Execute();
		}
		else if (property == RenderingParametersPanelSizeProperty)
			this.PersistentState.SetValue(LatestRenderingParamsPanelSize, (int)(this.RenderingParametersPanelSize + 0.5));
		else if (property == RequestedImageDisplayScaleProperty)
		{
			if (!this.GetValue(FitImageToViewportProperty))
			{
				var scale = (double)newValue.AsNonNull();
				this.UpdateCanZoomInOut();
				if (this.imageScalingAnimator is null 
					|| Math.Abs(this.imageScalingAnimator.EndValue - scale) > 0.0001)
				{
					this.ZoomTo(scale, false);
				}
			}
		}
		else if (property == SaturationAdjustmentProperty)
		{
			this.SetValue(HasSaturationAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
			this.canResetSaturationAdjustment.Update(this.HasSaturationAdjustment && this.IsSaturationAdjustmentSupported);
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Schedule(RenderImageDelay);
			if (this.IsSourceOpened)
				this.trackFilteringParamsAppliedAction.Reschedule(TrackFilteringParamsAppliedEventDelay);
		}
		else if (property == ShadowAdjustmentProperty)
		{
			this.SetValue(HasShadowAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
			this.canResetShadowAdjustment.Update(this.HasShadowAdjustment && this.IsShadowAdjustmentSupported);
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Schedule(RenderImageDelay);
			if (this.IsSourceOpened)
				this.trackFilteringParamsAppliedAction.Reschedule(TrackFilteringParamsAppliedEventDelay);
		}
		else if (property == SourceDataSizeProperty)
			this.SetValue(HasSourceDataSizeProperty, (long)newValue.AsNonNull() > 0);
		else if (property == SourceImageEffectiveBitsProperty)
			this.Logger.LogTrace("Source image effective bits: {bits}", newValue);
		else if (property == VibranceAdjustmentProperty)
		{
			this.SetValue(HasVibranceAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
			this.canResetVibranceAdjustment.Update(this.HasVibranceAdjustment && this.IsShadowAdjustmentSupported);
			this.updateIsFilteringImageNeededAction.Schedule();
			this.filterImageAction.Schedule(RenderImageDelay);
			if (this.IsSourceOpened)
				this.trackFilteringParamsAppliedAction.Reschedule(TrackFilteringParamsAppliedEventDelay);
		}
		else if (property == YuvToBgraConverterProperty)
		{
			if (this.IsYuvToBgraConverterSupported)
			{
				this.SetValue(ColorSpaceProperty, ((YuvToBgraConverter)newValue.AsNonNull()).ColorSpace);
				this.renderImageAction.Reschedule();
			}
			if (this.IsSourceOpened)
				this.trackRenderingParamsAppliedAction.Reschedule(TrackRenderingParamsAppliedEventDelay);
		}
    }


	// Called before removing user-defined color space.
	void OnRemovingUserDefinedColorSpace(object? sender, ColorSpaceEventArgs e)
	{
		if (e.ColorSpace.Equals(this.GetValue(ColorSpaceProperty)))
		{
			this.Logger.LogWarning("Color space '{colorSpace}' is being removed, switch back to default color space", e.ColorSpace);
			ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName), out var colorSpace);
			this.SetValue(ColorSpaceProperty, colorSpace);
		}
	}


	// Raise PropertyChanged event for row stride.
	void OnRowStrideChanged(int index) => this.OnPropertyChanged(index switch
	{
		0 => nameof(this.RowStride1),
		1 => nameof(this.RowStride2),
		2 => nameof(this.RowStride3),
		_ => throw new ArgumentOutOfRangeException(nameof(index)),
	});


	// Called when screen color space changed.
	void OnScreenColorSpaceChanged()
	{
		var prevScreenColorSpace = this.colorSpaces.FirstOrDefault(it => it.IsSystemDefined);
		if (prevScreenColorSpace is not null)
		{
			if (this.GetValue(ColorSpaceProperty).Equals(prevScreenColorSpace))
			{
				ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName), out var colorSpace);
				this.SetValue(ColorSpaceProperty, colorSpace);
			}
			this.colorSpaces.Remove(prevScreenColorSpace);
		}
		(this.Owner as Workspace)?.EffectiveScreenColorSpace.Let(screenColorSpace =>
		{
			if (screenColorSpace.IsSystemDefined)
				this.colorSpaces.Add(screenColorSpace);
		});
		if (this.IsColorSpaceManagementEnabled)
			this.renderImageAction.Reschedule();
	}


	// Setting changed.
    protected override void OnSettingChanged(SettingChangedEventArgs e)
    {
        base.OnSettingChanged(e);
		var key = e.Key;
		if (key == SettingKeys.BrightnessTransformationFunction
			|| key == SettingKeys.ContrastTransformationFunction)
		{
			if (this.IsActivated)
				this.filterImageAction.Reschedule();
			else
				_ = this.ClearFilteredImageAsync();
		}
		else if (key == SettingKeys.ColorSpaceConversionTiming 
		         || key == SettingKeys.Render32BitColorsOnly)
		{
			this.RenderImageCommand.TryExecute();
		}
		else if (key == SettingKeys.EnableColorSpaceManagement)
			this.SetValue(IsColorSpaceManagementEnabledProperty, (bool)e.Value.AsNonNull());
    }


    // Called when total memory usage of rendered images changed.
    void OnSharedRenderedImagesMemoryUsageChanged(long usage)
	{
		if (!this.IsDisposed)
			this.SetValue(TotalRenderedImagesMemoryUsageProperty, usage);
	}


	// Called when user defined profiles changed.
	void OnUserDefinedProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		switch (e.Action)
		{
			case NotifyCollectionChangedAction.Add:
				foreach (var profile in e.NewItems.AsNonNull().Cast<ImageRenderingProfile>())
				{
					profile.PropertyChanged += this.OnProfilePropertyChanged;
					this.profiles.Add(profile);
				}
				break;
			case NotifyCollectionChangedAction.Remove:
				foreach (var profile in e.OldItems.AsNonNull().Cast<ImageRenderingProfile>())
				{
					if (profile == this.Profile)
						this.SwitchToProfileWithoutApplying(ImageRenderingProfile.Default);
					profile.PropertyChanged -= this.OnProfilePropertyChanged;
					this.profiles.Remove(profile);
				}
				break;
		}
	}


	// Raise PropertyChanged event for white level.
	void OnWhiteLevelChanged(int index) => this.OnPropertyChanged(index switch
	{
		0 => nameof(this.WhiteLevel1),
		1 => nameof(this.WhiteLevel2),
		2 => nameof(this.WhiteLevel3),
		_ => throw new ArgumentOutOfRangeException(nameof(index)),
	});


    // Open given file as image data source.
    async Task OpenSourceFile(string? fileName)
	{
		if (fileName is null)
			return;
		await this.OpenSourceCore(fileName, () => Task.Run<IImageDataSource?>(() =>
		{
			try
			{
				this.Logger.LogDebug("Create source for '{fileName}'", fileName);
				return new FileImageDataSource(this.Application, fileName);
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Unable to create source for '{fileName}'", fileName);
				return null;
			}
		}));
	}


	/// <summary>
	/// Open multiple files as a single frame sequence which can be viewed frame-by-frame or played like a video.
	/// </summary>
	/// <param name="fileNames">Names of files, one per frame.</param>
	public async Task OpenSourceFiles(IList<string>? fileNames)
	{
		if (fileNames is null || fileNames.Count == 0)
			return;
		if (fileNames.Count == 1)
		{
			await this.OpenSourceFile(fileNames[0]);
			return;
		}
		var sortedFiles = FileSequenceImageDataSource.SortFiles(fileNames);
		await this.OpenSourceCore(sortedFiles[0], () => Task.Run<IImageDataSource?>(() =>
		{
			try
			{
				this.Logger.LogDebug("Create frame sequence source of {count} file(s)", sortedFiles.Length);
				return new FileSequenceImageDataSource(this.Application, sortedFiles);
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Unable to create frame sequence source");
				return null;
			}
		}));
	}


	// Open image data source produced by the given factory and complete the opening flow.
	async Task OpenSourceCore(string fileName, Func<Task<IImageDataSource?>> createDataSource)
	{
		// check state
		if (!this.canOpenSource.Value)
		{
			this.Logger.LogError("Cannot open '{fileName}' in current state", fileName);
			return;
		}

		// reset filter parameters
		if (this.Settings.GetValueOrDefault(SettingKeys.ResetFilterParamsAfterOpeningSourceFile))
		{
			this.ResetFilterParams();
			this.filterImageAction.Cancel();
		}

		// close current source file
		this.CloseSource(false);

		// update state
		this.canOpenSource.Update(false);
		this.SetValue(IsOpeningSourceProperty, true);

		// create image data source
		var imageDataSource = await createDataSource();
		if (this.IsDisposed)
		{
			this.Logger.LogWarning("Source for '{fileName}' created after disposing", fileName);
			if (imageDataSource is not null)
				_ = Task.Run(imageDataSource.Dispose);
			return;
		}
		if (imageDataSource is FileImageDataSource)
			this.SetValue(SourceFileNameProperty, fileName);
		else if (imageDataSource is null)
		{
			// reset state
			this.SetValue(SourceFileNameProperty, null);
			this.SetValue(IsSourceOpenedProperty, false);
			this.SetValue(IsOpeningSourceProperty, false);
			this.canOpenSource.Update(true);
			this.canZoomTo.Update(false);

			// update title
			this.UpdateTitle();

			// stop opening file
			return;
		}
		this.imageDataSource = imageDataSource;
		
		// update title
		this.UpdateTitle();

		// parse file format, format is parsed from data of the first frame
		var formatParsingSource = (IImageDataSource?)null;
		try
		{
			if (imageDataSource is IMultiFrameImageDataSource multiFrameImageDataSource)
				formatParsingSource = await multiFrameImageDataSource.GetFrameAsync(0, CancellationToken.None);
			this.fileFormatProfile = await Media.FileFormatParsers.FileFormatParsers.ParseImageRenderingProfileAsync(formatParsingSource ?? imageDataSource, CancellationToken.None);
			if (this.fileFormatProfile is not null)
				this.profiles.Add(this.fileFormatProfile);
		}
		catch
		{ /* best effort */ }
		finally
		{
			formatParsingSource?.Dispose();
		}

		// select image renderer by file name
		var evaluatedImageRenderer = (IImageRenderer?)null;
		if (this.fileFormatProfile is null 
			&& this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageRendererByFileName)
			&& ImageFormat.TryGetByFileName(fileName, out var imageFormat)
			&& imageFormat is not null)
		{
			foreach (var candidateRenderer in ImageRenderers.All)
			{
				if (candidateRenderer.Format.Equals(imageFormat))
				{
					evaluatedImageRenderer = candidateRenderer;
					break;
				}
			}
		}

		// complete opening
		this.CompleteOpeningSource(imageDataSource, () =>
		{
			// use profile of file format or reset to default renderer
			if (this.fileFormatProfile is not null)
				this.Profile = this.fileFormatProfile;
			else if (evaluatedImageRenderer is not null)
			{
				this.SetValue(ImageRendererProperty, evaluatedImageRenderer);
				if (this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterChangingRenderer))
					this.isImageDimensionsEvaluationNeeded = true;
				this.isImagePlaneOptionsResetNeeded = true;
			}
			else if (this.Settings.GetValueOrDefault(SettingKeys.UseDefaultImageRendererAfterOpeningSourceFile))
			{
				this.Logger.LogWarning("Use default image renderer after opening source '{fileName}'", fileName);
				var defaultImageRenderer = this.SelectDefaultImageRenderer();
				if (this.ImageRenderer != defaultImageRenderer)
				{
					this.SetValue(ImageRendererProperty, defaultImageRenderer);
					if (this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterChangingRenderer))
						this.isImageDimensionsEvaluationNeeded = true;
					this.isImagePlaneOptionsResetNeeded = true;
				}
			}
		});
	}


	/// <summary>
	/// Command for opening source file.
	/// </summary>
	public ICommand OpenSourceFileCommand { get; }


	/// <summary>
	/// Command for opening multiple files as a single frame sequence. The parameter is <see cref="IList{String}"/>.
	/// </summary>
	public ICommand OpenSourceFilesCommand { get; }


	/// <summary>
	/// Command to start or stop playing the frame sequence. Only available when the image has multiple frames.
	/// </summary>
	public ICommand PlayFramesCommand { get; }


	// Move to the frame which was selected for playback.
	void PlayNextFrame()
	{
		// check state
		if (!this.GetValue(IsPlayingFramesProperty))
			return;

		// move to the selected frame, render the current frame again if all frames were dropped in this round
		if (this.GetValue(FrameNumberProperty) == this.framePlaybackNextFrameNumber)
			this.renderImageAction.Reschedule();
		else
			this.SetValue(FrameNumberProperty, this.framePlaybackNextFrameNumber);
	}


	// Anchor the playback timeline to the frame being displayed so that the current frame rate takes effect immediately.
	void RestartFramePlaybackTimeline()
	{
		if (!this.GetValue(IsPlayingFramesProperty))
			return;
		this.framePlaybackBaseFrameNumber = this.GetValue(FrameNumberProperty);
		this.framePlaybackBaseTime = this.framePlaybackStopwatch.Elapsed.TotalMilliseconds;
		this.ScheduleNextFrameForPlayback();
	}


	// Select the frame to be rendered next and schedule moving to it.
	void ScheduleNextFrameForPlayback()
	{
		// check state
		if (!this.GetValue(IsPlayingFramesProperty) || this.hasPendingImageRendering)
			return;
		if (!this.canPlayFrames.Value)
		{
			this.StopPlayingFrames();
			return;
		}

		// select the frame to be rendered next
		var frameInterval = this.GetValue(IsFramePlaybackRateUnlimitedProperty)
			? 0.0
			: 1000.0 / this.GetValue(FramePlaybackRateProperty);
		var nextFrame = SelectNextFrameForPlayback(this.framePlaybackBaseFrameNumber, this.framePlaybackBaseTime, this.framePlaybackStopwatch.Elapsed.TotalMilliseconds, frameInterval, this.GetValue(FrameCountProperty), this.GetValue(IsFramePlaybackLoopingProperty));
		if (nextFrame is null)
		{
			this.StopPlayingFrames();
			return;
		}

		// anchor the timeline to the selected frame so that the next selection moves forward from it
		this.framePlaybackBaseFrameNumber = nextFrame.Value.FrameNumber;
		this.framePlaybackBaseTime = nextFrame.Value.PresentTime;

		// schedule moving to the frame at the time it should be presented
		this.framePlaybackNextFrameNumber = nextFrame.Value.FrameNumber;
		this.playFrameAction.Reschedule(nextFrame.Value.Delay);
	}


	/// <summary>
	/// Select the frame to be rendered next when playing frames.
	/// </summary>
	/// <param name="baseFrameNumber">1-based number of the frame which the playback timeline is anchored to.</param>
	/// <param name="baseTime">Time when the anchored frame was presented, in milliseconds.</param>
	/// <param name="currentTime">Current time in milliseconds.</param>
	/// <param name="frameInterval">Interval between frames in milliseconds, or 0 to play frames as fast as possible.</param>
	/// <param name="frameCount">Total number of frames.</param>
	/// <param name="looping">Whether playback loops back to the first frame after the last one.</param>
	/// <returns>Number of the frame to be rendered next, the delay before moving to it and the time it should be presented at, or null if playback should stop.</returns>
	/// <remarks>Frames are dropped when rendering is unable to catch up with the given frame interval, so that playback keeps the timeline instead of falling behind. The returned time should be used as the base time of the next selection, so that the timeline does not drift.</remarks>
	internal static (long FrameNumber, int Delay, double PresentTime)? SelectNextFrameForPlayback(long baseFrameNumber, double baseTime, double currentTime, double frameInterval, long frameCount, bool looping)
	{
		// check state
		if (frameCount <= 1)
			return null;
		if (baseFrameNumber < 1)
			baseFrameNumber = 1;

		// select the frame which should be presented next, frames are dropped if rendering took more than one interval
		var frameOffset = 1L;
		if (frameInterval > 0)
		{
			var elapsedFrameCount = (long)((currentTime - baseTime) / frameInterval);
			if (elapsedFrameCount > 0)
				frameOffset += elapsedFrameCount;
		}
		var frameNumber = baseFrameNumber + frameOffset;

		// stop or loop back after the last frame
		if (frameNumber > frameCount)
		{
			if (!looping)
				return null;
			frameNumber = ((frameNumber - 1) % frameCount) + 1;
		}

		// calculate the time the frame should be presented at and the delay before presenting it
		var presentTime = frameInterval > 0
			? baseTime + (frameOffset * frameInterval)
			: currentTime;
		return (frameNumber, (int)Math.Max(0, presentTime - currentTime), presentTime);
	}


	// Start playing the frame sequence.
	void StartPlayingFrames()
	{
		this.VerifyAccess();
		this.VerifyDisposed();
		if (!this.canPlayFrames.Value || this.GetValue(IsPlayingFramesProperty))
			return;
		this.SetValue(IsPlayingFramesProperty, true);
		this.framePlaybackBaseFrameNumber = this.GetValue(FrameNumberProperty);
		this.framePlaybackBaseTime = 0;
		this.framePlaybackStopwatch.Restart();
		this.ScheduleNextFrameForPlayback();
	}


	/// <summary>
	/// Stop playing the frame sequence.
	/// </summary>
	public void StopPlayingFrames()
	{
		if (!this.GetValue(IsPlayingFramesProperty))
			return;
		this.playFrameAction.Cancel();
		this.framePlaybackStopwatch.Stop();
		this.SetValue(IsPlayingFramesProperty, false);
	}


	// Toggle between playing and stopping the frame sequence.
	void TogglePlayingFrames()
	{
		if (this.GetValue(IsPlayingFramesProperty))
			this.StopPlayingFrames();
		else
			this.StartPlayingFrames();
	}


	// Update whether moving to previous/next frame is available or not.
	void UpdateCanMoveToFrames(long frameNumber, bool isFrameRendered)
	{
		// each frame of multi-frame source is rendered by its own source, keep moving to another frame available so that the frame which cannot be rendered can be skipped
		var canMove = isFrameRendered || this.imageDataSource is IMultiFrameImageDataSource;
		this.canMoveToNextFrame.Update(canMove && frameNumber < this.GetValue(FrameCountProperty));
		this.canMoveToPreviousFrame.Update(canMove && frameNumber > 1);
	}


	// Update whether the frame sequence can be played and stop playback when it cannot.
	void UpdateCanPlayFrames()
	{
		var canPlay = this.GetValue(IsSourceOpenedProperty) && this.GetValue(HasMultipleFramesProperty);
		this.canPlayFrames.Update(canPlay);
		if (!canPlay)
			this.StopPlayingFrames();
	}


	// Replace the profile generated for file format by the profile generated for file format of the given frame, so that each frame of frame sequence is rendered according to its own file format.
	async Task UpdateFileFormatProfileAsync(IImageDataSource frameImageDataSource, long frameNumber, CancellationToken cancellationToken)
	{
		// parse file format of frame
		var currentProfile = this.fileFormatProfile.AsNonNull();
		var frameProfile = (ImageRenderingProfile?)null;
		try
		{
			frameProfile = await Media.FileFormatParsers.FileFormatParsers.ParseImageRenderingProfileAsync(frameImageDataSource, cancellationToken);
		}
		catch (Exception ex)
		{
			if (ex is OperationCanceledException)
				throw;
			this.Logger.LogWarning(ex, "Unable to parse file format of frame {frameNumber}", frameNumber);
		}
		if (this.IsDisposed)
			return;

		// keep the current profile if file format of frame is unidentifiable, rendering the frame may fail if its format is actually different
		if (frameProfile is null)
		{
			this.Logger.LogWarning("Unable to identify file format of frame {frameNumber}, render it by renderer of {format}", frameNumber, currentProfile.Renderer.Format.Name);
			return;
		}

		// keep the current profile if frame is rendered in the same way
		if (currentProfile.HasSameRenderingParameters(frameProfile))
		{
			frameProfile.Dispose();
			return;
		}

		// replace the profile generated for file format and apply it to the rendering which is in progress
		this.Logger.LogDebug("Render frame {frameNumber} by profile of {format} instead of {currentFormat}", frameNumber, frameProfile.Renderer.Format.Name, currentProfile.Renderer.Format.Name);
		this.fileFormatProfile = frameProfile;
		this.profiles.Add(frameProfile);
		this.SetValue(ProfileProperty, frameProfile); // parameters of profile are applied by ApplyProfile()
		this.profiles.Remove(currentProfile);
		currentProfile.Dispose();
		this.renderImageAction.Cancel(); // prevent re-rendering caused by change of parameters
	}


	/// <summary>
	/// Get or set pixel stride of 1st image plane.
	/// </summary>
	public int PixelStride1
	{
		get => this.pixelStrides[0];
		set => this.ChangePixelStride(0, value);
	}


	/// <summary>
	/// Get or set pixel stride of 2nd image plane.
	/// </summary>
	public int PixelStride2
	{
		get => this.pixelStrides[1];
		set => this.ChangePixelStride(1, value);
	}


	/// <summary>
	/// Get or set pixel stride of 3rd image plane.
	/// </summary>
	public int PixelStride3
	{
		get => this.pixelStrides[2];
		set => this.ChangePixelStride(2, value);
	}
	
	
	// Prepare properties for tracking image filtering events.
	IDictionary<string, string> PrepareFilteringParamsTrackingProperties() => this.PrepareUsageTrackingProperties().Also(properties =>
	{
		properties[UsageProperties.BlueColorAdjustment] = this.GetValue(BlueColorAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.BrightnessAdjustment] = this.GetValue(BrightnessAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.ContrastAdjustment] = this.GetValue(ContrastAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.GreenColorAdjustment] = this.GetValue(GreenColorAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.HighlightAdjustment] = this.GetValue(HighlightAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.IsGrayscaleFilterEnabled] = this.GetValue(IsGrayscaleFilterEnabledProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.RedColorAdjustment] = this.GetValue(RedColorAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.SaturationAdjustment] = this.GetValue(SaturationAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.ShadowAdjustment] = this.GetValue(ShadowAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.VibranceAdjustment] = this.GetValue(VibranceAdjustmentProperty).ToString(CultureInfo.InvariantCulture);
	});


	// Prepare properties for tracking rendered image saved event.
	IDictionary<string, string> PrepareRenderedImageSavedTrackingProperties(ImageSavingParams savingParams, bool isFilteredImage, bool isTransformationApplied) => this.PrepareUsageTrackingProperties().Also(properties =>
	{
		properties[UsageProperties.ImageEncoder] = savingParams.Encoder?.Name ?? "Unknown";
		properties[UsageProperties.IsFilteredImage] = isFilteredImage.ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.IsTransformationApplied] = isTransformationApplied.ToString(CultureInfo.InvariantCulture);
		properties[UsageProperties.QualityLevel] = savingParams.Options.QualityLevel.ToString(CultureInfo.InvariantCulture);
	});
	
	
	// Prepare properties for usage tracking.
	IDictionary<string, string> PrepareUsageTrackingProperties() => new Dictionary<string, string>
	{
		[UsageProperties.Id] = this.Id.ToString(CultureInfo.InvariantCulture)
	};


	/// <summary>
	/// Get or set current profile.
	/// </summary>
	public ImageRenderingProfile Profile
	{
		get => this.GetValue(ProfileProperty);
		set => this.SetValue(ProfileProperty, value);
	}


	/// <summary>
	/// Get available profiles.
	/// </summary>
	public IList<ImageRenderingProfile> Profiles { get; }


	/// <summary>
	/// Get rendered image with quarter size.
	/// </summary>
	public Bitmap? QuarterSizeRenderedImage => this.GetValue(QuarterSizeRenderedImageProperty);


	/// <summary>
	/// Get or set red color adjustment.
	/// </summary>
	public double RedColorAdjustment
	{
		get => this.GetValue(RedColorAdjustmentProperty);
		set => this.SetValue(RedColorAdjustmentProperty, value);
	}


	/// <summary>
	/// Get or set gain of red color.
	/// </summary>
	public double RedColorGain
	{
		get => this.GetValue(RedColorGainProperty);
		set => this.SetValue(RedColorGainProperty, value);
	}


	// Record a single filtering sample into the current performance window.
	void RecordFilteringSample(int width, int height, long durationMs, string filters)
	{
		var pixelCount = (long)width * height;
		if (pixelCount > this.filteringPerfLargestPixelCount)
		{
			this.filteringPerfLargestPixelCount = pixelCount;
			this.filteringPerfLargestDurationMs = durationMs;
			this.filteringPerfLargestFilters = filters;
		}
		if (durationMs > this.filteringPerfLongestDurationMs)
		{
			this.filteringPerfLongestDurationMs = durationMs;
			this.filteringPerfLongestPixelCount = pixelCount;
			this.filteringPerfLongestFilters = filters;
		}

		// open a new window on the first sample; later samples do not extend it
		if (this.filteringPerfSampleCount == 0)
			this.trackFilteringPerfAction.Schedule(TrackFilteringPerfDuration);
		++this.filteringPerfSampleCount;
	}


	// Record a single rendering sample into the current performance window.
	void RecordRenderingSample(int width, int height, long durationMs, string rendererName)
	{
		var pixelCount = (long)width * height;
		if (pixelCount > this.renderingPerfLargestPixelCount)
		{
			this.renderingPerfLargestPixelCount = pixelCount;
			this.renderingPerfLargestDurationMs = durationMs;
			this.renderingPerfLargestRendererName = rendererName;
		}
		if (durationMs > this.renderingPerfLongestDurationMs)
		{
			this.renderingPerfLongestDurationMs = durationMs;
			this.renderingPerfLongestPixelCount = pixelCount;
			this.renderingPerfLongestRendererName = rendererName;
		}

		// open a new window on the first sample; later samples do not extend it
		if (this.renderingPerfSampleCount == 0)
			this.trackRenderingPerfAction.Schedule(TrackRenderingPerfDuration);
		++this.renderingPerfSampleCount;
	}


	// Release all cached images.
	bool ReleaseCachedImages()
	{
		var released = false;
		if (this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken is not null)
		{
			released = true;
			this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
			this.cachedAvaQuarterSizeRenderedImage = null;
		}
		if (this.cachedAvaRenderedImageMemoryUsageToken is not null)
		{
			released = true;
			this.cachedAvaRenderedImageMemoryUsageToken = this.cachedAvaRenderedImageMemoryUsageToken.DisposeAndReturnNull();
			this.cachedAvaRenderedImage = null;
		}
		if (this.cachedFilteredImageFrames.IsNotEmpty())
		{
			released = true;
			foreach (var frame in this.cachedFilteredImageFrames)
				frame.Dispose();
			this.cachedFilteredImageFrames.Clear();
		}
		if (this.cachedMosaicImageFrame is not null)
		{
			released = true;
			this.cachedMosaicImageFrame = this.cachedMosaicImageFrame.DisposeAndReturnNull();
		}
		this.releasedCachedImagesAction.Cancel();
		return released;
	}


	// Release token for rendered image memory usage.
	void ReleaseRenderedImageMemoryUsage(RenderedImageMemoryUsageToken token)
	{
		var maxUsage = this.Settings.GetValueOrDefault(SettingKeys.MaxRenderedImagesMemoryUsageMB) << 20;
		if (!this.IsDisposed)
			this.SetValue(RenderedImagesMemoryUsageProperty, this.RenderedImagesMemoryUsage - token.DataSize);
		SharedRenderedImagesMemoryUsage.Decrease(token.DataSize);
		this.Logger.LogDebug("Release {dataSize} for rendered image, total: {totalUsage}, max: {maxUsage}", token.DataSize.ToFileSizeString(), SharedRenderedImagesMemoryUsage.Value.ToFileSizeString(), maxUsage.ToFileSizeString());
	}


	/// <summary>
	/// Get rendered image.
	/// </summary>
	public Bitmap? RenderedImage => this.GetValue(RenderedImageProperty);


	/// <summary>
	/// Get memory usage of rendered images by this session in bytes.
	/// </summary>
	public long RenderedImagesMemoryUsage => this.GetValue(RenderedImagesMemoryUsageProperty);


	// Render image according to current state.
	async Task RenderImage()
	{
		// cancel current filtering and rendering, then wait for the completion of what is being cancelled
		var requestId = ++this.imageRenderingRequestId;
		this.hasPendingImageRendering = true;
		await this.CancelFilteringImageAsync();
		await this.CancelRenderingImageAsync();
		if (this.IsDisposed)
			return;
		if (requestId != this.imageRenderingRequestId)
		{
			this.Logger.LogWarning("Give up rendering image, a newer rendering has been requested while waiting for the cancellation");
			return;
		}

		// check state
		if (this.imageDataSource is null)
			return;
		if (!this.IsActivated && !this.HasRenderedImage)
		{
			if (!this.IsHibernated)
			{
				this.Logger.LogWarning("No image rendered before deactivation, hibernate the session");
				await this.HibernateAsync();
			}
			return;
		}
		this.hasPendingImageRendering = false;

		// render, then release whoever is waiting for the completion of this rendering
		var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		this.imageRenderingCompletionSource = completionSource;
		this.SetValue(IsRenderingImageProperty, true);
		try
		{
			await this.RenderImageCore();
		}
		finally
		{
			this.imageRenderingCompletionSource = null;
			if (!this.IsDisposed)
				this.SetValue(IsRenderingImageProperty, false);
			completionSource.TrySetResult();
		}
	}


	/// <summary>
	/// Command to request rendering image.
	/// </summary>
	public ICommand RenderImageCommand { get; }


	// Render image according to current state, after the state has been checked by RenderImage().
	async Task RenderImageCore()
	{
		// get state
		var imageDataSource = this.imageDataSource.AsNonNull();
		var sourceFileName = this.SourceFileName;

		// get source which provides data of frame to render
		var cancellationTokenSource = new CancellationTokenSource();
		this.imageRenderingCancellationTokenSource = cancellationTokenSource;
		var frameNumber = this.FrameNumber;
		var renderingImageDataSource = imageDataSource;
		if (imageDataSource is IMultiFrameImageDataSource multiFrameImageDataSource)
		{
			// frames are provided by the source itself, one source for each frame
			var frameCount = (long)multiFrameImageDataSource.FrameCount;
			frameNumber = this.CoerceFrameNumberToRange(frameNumber, frameCount);
			this.SetValue(FrameCountProperty, frameCount);
			this.frameImageDataSource = this.frameImageDataSource.DisposeAndReturnNull();
			try
			{
				this.frameImageDataSource = await multiFrameImageDataSource.GetFrameAsync((int)(frameNumber - 1), cancellationTokenSource.Token);
				if (this.IsDisposed)
					return;
				renderingImageDataSource = this.frameImageDataSource;

				// renderer and rendering parameters are selected by file format of each frame if they were selected by file format of source
				if (this.fileFormatProfile is not null && this.Profile == this.fileFormatProfile)
					await this.UpdateFileFormatProfileAsync(renderingImageDataSource, frameNumber, cancellationTokenSource.Token);
			}
			catch (Exception ex)
			{
				this.imageRenderingCancellationTokenSource = null;
				if (ex is OperationCanceledException)
					this.Logger.LogWarning("Preparing frame {frameNumber} of '{sourceFileName}' has been cancelled", frameNumber, sourceFileName);
				else
				{
					this.Logger.LogError(ex, "Unable to get source of frame {frameNumber} of '{sourceFileName}'", frameNumber, sourceFileName);
					this.SetValue(HasRenderingErrorProperty, true);
					this.DisposeFilteredImage();
					this.DisposeRenderedImage();
					this.UpdateCanMoveToFrames(frameNumber, false);
					this.ScheduleNextFrameForPlayback(); // playback moves to the next frame instead of stopping at the frame which cannot be rendered
				}
				return;
			}
			if (this.IsDisposed)
				return;

			// messages of rendering refer to the file of the frame being rendered instead of the first file of source
			if (renderingImageDataSource is FileImageDataSource frameFileImageDataSource)
				sourceFileName = frameFileImageDataSource.FileName;
		}
		var imageRenderer = this.ImageRenderer;

		// evaluate dimensions
		if (this.isImageDimensionsEvaluationNeeded)
		{
			this.Logger.LogDebug("Evaluate dimensions of image for '{sourceFileName}'", sourceFileName);
			this.isImageDimensionsEvaluationNeeded = false;
			imageRenderer.EvaluateDimensions(renderingImageDataSource, this.Settings.GetValueOrDefault(SettingKeys.DefaultImageDimensionsEvaluationAspectRatio))?.Also((ref it) =>
			{
				this.SetValue(ImageWidthProperty, it.Width);
				this.SetValue(ImageHeightProperty, it.Height);
				this.renderImageAction.Cancel(); // prevent re-rendering caused by change of dimensions
			});
		}

		// sync format information
		var planeDescriptors = imageRenderer.Format.PlaneDescriptors;
		if (imageRenderer.Format.Category != ImageFormatCategory.Compressed)
		{
			this.SetValue(HasImagePlane1Property, true);
			if (this.ImagePlaneCount != planeDescriptors.Count)
			{
				this.SetValue(ImagePlaneCountProperty, planeDescriptors.Count);
				this.SetValue(HasImagePlane2Property, planeDescriptors.Count >= 2);
				this.SetValue(HasImagePlane3Property, planeDescriptors.Count >= 3);
			}
		}
		else
		{
			this.SetValue(ImagePlaneCountProperty, 0);
			this.SetValue(HasImagePlane1Property, false);
			this.SetValue(HasImagePlane2Property, false);
			this.SetValue(HasImagePlane3Property, false);
		}
		for (var i = planeDescriptors.Count - 1; i >= 0; --i)
		{
			this.SetValue(i switch
			{
				0 => AreAdjustableBlackWhiteLevels1Property,
				1 => AreAdjustableBlackWhiteLevels2Property,
				2 => AreAdjustableBlackWhiteLevels3Property,
				_ => throw new ArgumentException(),
			}, planeDescriptors[i].AreAdjustableBlackWhiteLevels);
			this.SetValue(i switch
			{
				0 => IsAdjustableEffectiveBits1Property,
				1 => IsAdjustableEffectiveBits2Property,
				2 => IsAdjustableEffectiveBits3Property,
				_ => throw new ArgumentException(),
			}, planeDescriptors[i].IsAdjustableEffectiveBits);
			this.SetValue(i switch
			{
				0 => IsAdjustablePixelStride1Property,
				1 => IsAdjustablePixelStride2Property,
				2 => IsAdjustablePixelStride3Property,
				_ => throw new ArgumentException(),
			}, planeDescriptors[i].IsAdjustablePixelStride);
		}

		// prepare plane options
		var planeOptionsList = new List<ImagePlaneOptions>(imageRenderer.CreateDefaultPlaneOptions(this.ImageWidth, this.ImageHeight));
		if (this.isImagePlaneOptionsResetNeeded)
		{
			this.isImagePlaneOptionsResetNeeded = false;
			for (var i = planeOptionsList.Count - 1; i >= 0; --i)
			{
				var planeOptions = planeOptionsList[i];
				this.effectiveBits[i] = this.CoerceEffectiveBitsToColorTables(planeOptions.EffectiveBits);
				if (planeOptions.BlackLevel.HasValue && planeOptions.WhiteLevel.HasValue)
				{
					this.blackLevels[i] = planeOptions.BlackLevel.GetValueOrDefault();
					this.whiteLevels[i] = planeOptions.WhiteLevel.GetValueOrDefault();
				}
				else
				{
					this.blackLevels[i] = 0;
					this.whiteLevels[i] = (uint)(1 << this.effectiveBits[i]) - 1;
				}
				this.pixelStrides[i] = planeOptions.PixelStride;
				this.rowStrides[i] = planeOptions.RowStride;
				this.OnEffectiveBitsChanged(i);
				this.OnBlackLevelChanged(i);
				this.OnWhiteLevelChanged(i);
				this.OnPixelStrideChanged(i);
				this.OnRowStrideChanged(i);
			}
		}
		else
		{
			for (var i = planeOptionsList.Count - 1; i >= 0; --i)
			{
				planeOptionsList[i] = planeOptionsList[i].Let((it) =>
				{
					it.EffectiveBits = this.effectiveBits[i];
					if (planeDescriptors[i].AreAdjustableBlackWhiteLevels)
					{
						it.BlackLevel = this.blackLevels[i];
						it.WhiteLevel = this.whiteLevels[i];
					}
					it.PixelStride = this.pixelStrides[i];
					it.RowStride = this.rowStrides[i];
					return it;
				});
			}
		}
		
		// report effective bits
		this.UpdateSourceImageEffectiveBits();

		// prepare rendering options and calculate frame count of packed frames
		var isColorTableSupported = imageRenderer.IsColorTableSupported;
		var isRgbGainSupported = this.IsRgbGainSupported;
		var renderingOptions = new ImageRenderingOptions
		{
			AlphaColorTable = isColorTableSupported ? this.alphaColorTable : null,
			BayerPattern = this.BayerPattern,
			BlueColorTable = isColorTableSupported ? this.blueColorTable : null,
			BlueGain = isRgbGainSupported ?this.BlueColorGain : 1.0,
			ByteOrdering = this.ByteOrdering,
			DataOffset = this.DataOffset,
			Demosaicing = (this.IsDemosaicingSupported && this.DemosaicingAlgorithm != Media.Demosaicing.DemosaicingAlgorithms.Bypass) ? this.DemosaicingAlgorithm : null,
			GreenColorTable = isColorTableSupported ? this.greenColorTable : null,
			GreenGain = isRgbGainSupported ? this.GreenColorGain : 1.0,
			RedColorTable = isColorTableSupported ? this.redColorTable : null,
			RedGain = isRgbGainSupported ? this.RedColorGain : 1.0,
			YuvToBgraConverter = this.YuvToBgraConverter,
		};
		var frameDataSize = imageRenderer.EvaluateSourceDataSize(this.ImageWidth, this.ImageHeight, renderingOptions, planeOptionsList);
		if (imageDataSource is not IMultiFrameImageDataSource)
		{
			// frames are packed in data of source, locate the frame by its offset in data
			try
			{
				var totalDataSize = imageDataSource.Size - this.DataOffset;
				var frameCount = frameDataSize > 0
					? (totalDataSize <= frameDataSize)
						? 1
						: 1 + (totalDataSize - frameDataSize) / (frameDataSize + this.FramePaddingSize)
					: 1;
				frameNumber = this.CoerceFrameNumberToRange(frameNumber, frameCount);
				this.SetValue(FrameCountProperty, frameCount);
			}
			catch (Exception ex)
			{
				this.imageRenderingCancellationTokenSource = null;
				this.Logger.LogError(ex, "Unable to update frame count and index of '{sourceFileName}'", this.SourceFileName);
				this.SetValue(HasRenderingErrorProperty, true);
				this.DisposeFilteredImage();
				this.DisposeRenderedImage();
				return;
			}
			renderingOptions.DataOffset += ((frameDataSize + this.FramePaddingSize) * (frameNumber - 1));
		}

		// update state, image rendered before can still be saved while rendering the next one
		this.canSaveRenderedImage.Update(this.renderedImageFrame is not null && !this.IsSavingRenderedImage);

        // check color space
        var renderedColorSpace = this.IsColorSpaceManagementEnabled ? this.ColorSpace : ColorSpace.Default;

		// check whether rendering is needed or not, rendering by an edited renderer is forced because the parameters below cannot tell that the image it generates has changed
		var isRenderingNeeded = this.isImageRenderingForced || (this.renderedImageFrame?.Let(it =>
		{
			if (it.FrameNumber != frameNumber)
				return true;
			if (it.ImageRenderer != imageRenderer)
				return true;
			if (it.BitmapBuffer.Width != this.ImageWidth || it.BitmapBuffer.Height != this.ImageHeight)
				return true;
			if (it.RenderingOptions != renderingOptions)
				return true;
			var planeOptions = it.PlaneOptions;
			if (planeOptions is null || planeOptions.Count != planeOptionsList.Count)
				return true;
			for (var i = planeOptionsList.Count - 1; i >= 0; --i)
            {
				if (planeOptions[i] != planeOptionsList[i])
					return true;
            }
			return false;
		}) ?? true);
		
		// select rendered format
		BitmapFormat renderedFormat;
		try
		{
			renderedFormat = await imageRenderer.SelectRenderedFormatAsync(renderingImageDataSource, renderingOptions, planeOptionsList, cancellationTokenSource.Token);
			this.Logger.LogTrace("Select {format} as rendered format", renderedFormat);
		}
		catch (Exception ex)
		{
			if (ex is TaskCanceledException)
				this.Logger.LogWarning("Image rendering for '{sourceFileName}' has been cancelled", sourceFileName);
			else
			{
				// log error
				this.Logger.LogError(ex, "Error occurred while selecting rendered format for '{sourceFileName}'", sourceFileName);
				this.imageRenderingCancellationTokenSource = null;

				// drop cached frames so that the pending report does not resurrect the previous image and reset HasRenderingError
				this.DisposeFilteredImage();
				this.colorSpaceConvertedImageFrame = this.colorSpaceConvertedImageFrame.DisposeAndReturnNull();
				this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
				this.UpdateCanMoveToFrames(frameNumber, false);
				this.canSelectColorAdjustment.Update(false);
				this.canSelectRgbGain.Update(false);

				// request reporting and update state
				Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
				this.SetValue(HasRenderingErrorProperty, true);
			}
			return;
		}

		// release the cached mosaic image which cannot be used by this rendering. A dedicated buffer is allocated only for the algorithm which requires it, the preference of an algorithm which interpolates better with a dedicated buffer is going to be honored by a setting instead
		var demosaicingAlgorithm = renderingOptions.Demosaicing;
		var isMosaicImageNeeded = demosaicingAlgorithm is not null
			&& demosaicingAlgorithm.CheckOutputBufferRequirement(renderingOptions.BayerPattern, this.ImageWidth, this.ImageHeight) == OutputBufferRequirement.Required;
		if (this.cachedMosaicImageFrame is not null)
		{
			var cachedMosaicBitmapBuffer = this.cachedMosaicImageFrame.BitmapBuffer;
			if (!isMosaicImageNeeded
			    || cachedMosaicBitmapBuffer.Width != this.ImageWidth
			    || cachedMosaicBitmapBuffer.Height != this.ImageHeight
			    || cachedMosaicBitmapBuffer.Format != renderedFormat)
			{
				if (this.Application.IsDebugMode)
					this.Logger.LogTrace("Released cached mosaic image frame, size: {width}x{height}", cachedMosaicBitmapBuffer.Width, cachedMosaicBitmapBuffer.Height);
				this.cachedMosaicImageFrame = this.cachedMosaicImageFrame.DisposeAndReturnNull();
			}
		}

		// create rendered image
		if (this.Application.IsDebugMode && isRenderingNeeded)
			this.Logger.LogWarning("Allocate rendered image frame, size: {width}x{height}", this.ImageWidth, this.ImageHeight);
		var renderedImageFrame = isRenderingNeeded ? await this.AllocateRenderedImageFrame(frameNumber, renderedFormat, renderedColorSpace, this.ImageWidth, this.ImageHeight) : null;

		// create the image to keep the mosaic if the algorithm requires a dedicated buffer to receive the result, the cached frame is taken out of the cache so that releasing the cached images while rendering cannot dispose the frame being rendered into
		var mosaicImageFrame = (ImageFrame?)null;
		if (isRenderingNeeded && isMosaicImageNeeded && renderedImageFrame is not null)
		{
			mosaicImageFrame = this.cachedMosaicImageFrame;
			if (mosaicImageFrame is not null)
			{
				this.cachedMosaicImageFrame = null;
				mosaicImageFrame.BitmapBuffer.UpdateColorSpace(renderedColorSpace);
				if (this.Application.IsDebugMode)
					this.Logger.LogTrace("Use cached mosaic image frame");
			}
			else
			{
				if (this.Application.IsDebugMode)
					this.Logger.LogWarning("Allocate mosaic image frame for demosaicing, size: {width}x{height}", this.ImageWidth, this.ImageHeight);
				mosaicImageFrame = await this.AllocateRenderedImageFrame(frameNumber, renderedFormat, renderedColorSpace, this.ImageWidth, this.ImageHeight);
			}
		}
		if (isRenderingNeeded && (renderedImageFrame is null || (isMosaicImageNeeded && mosaicImageFrame is null)))
		{
			if (!cancellationTokenSource.IsCancellationRequested)
			{
				this.imageRenderingCancellationTokenSource = null;
				this.SetValue(InsufficientMemoryForRenderedImageProperty, this.IsActivated);
				Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
				if (!this.IsActivated && !this.IsHibernated)
				{
					this.Logger.LogWarning("Unable to allocate rendered image frame after deactivation, hibernate the session");
					_ = this.HibernateAsync(); // the images are released after this rendering completes, waiting for it here would wait for this rendering itself
				}
			}
			mosaicImageFrame?.Dispose();
			renderedImageFrame?.Dispose();
			return;
		}

		// update state
		this.SetValue(InsufficientMemoryForRenderedImageProperty, false);

		// render
		this.Logger.LogDebug("Render image for '{sourceFileName}', dimensions: {width}x{height}", sourceFileName, this.ImageWidth, this.ImageHeight);
		bool isColorSpaceConversionNeeded = false;
		var colorSpaceConvertedImageFrame = default(ImageFrame);
		var exception = (Exception?)null;
		var renderStopwatch = (Stopwatch?)null;
		try
		{
			// render
			if (isRenderingNeeded && renderedImageFrame is not null)
			{
				// render image, the mosaic image receives the rendered image if demosaicing needs another buffer to put its result into
				var renderingImageFrame = mosaicImageFrame ?? renderedImageFrame;
				renderStopwatch = Stopwatch.StartNew();
				renderedImageFrame.RenderingResult = await imageRenderer.RenderAsync(renderingImageDataSource, renderingImageFrame.BitmapBuffer, renderingOptions, planeOptionsList, cancellationTokenSource.Token);

				// perform demosaicing, its duration is counted as part of rendering because it was performed by the image renderer before
				if (demosaicingAlgorithm is not null)
					await this.DemosaicImageAsync(demosaicingAlgorithm, renderingImageFrame.BitmapBuffer, renderedImageFrame.BitmapBuffer, renderingOptions, cancellationTokenSource.Token);

				// update state of rendered image
				renderStopwatch.Stop();
				renderedImageFrame.ImageRenderer = imageRenderer;
				renderedImageFrame.RenderingOptions = renderingOptions;
				renderedImageFrame.PlaneOptions = planeOptionsList;
			}
			else
			{
				this.Logger.LogTrace("No need to render image again with same options");
				this.renderedImageFrame.AsNonNull().BitmapBuffer.UpdateColorSpace(renderedColorSpace);
			}
			
			// convert color space
			isColorSpaceConversionNeeded = this.Settings.GetValueOrDefault(SettingKeys.ColorSpaceConversionTiming) == ColorSpaceConversionTiming.BeforeApplyingFilters;
			if (isColorSpaceConversionNeeded && !cancellationTokenSource.IsCancellationRequested)
			{
				var screenColorSpace = this.ScreenColorSpace;
				if (this.IsColorSpaceManagementEnabled && !screenColorSpace.Equals(renderedColorSpace))
					colorSpaceConvertedImageFrame = await this.ConvertColorSpaceAsync(renderedImageFrame ?? this.renderedImageFrame.AsNonNull(), renderedColorSpace, screenColorSpace, cancellationTokenSource.Token);
			}
		}
		catch (Exception ex)
		{
			exception = ex;
		}

		// cache the mosaic image for the next rendering, its content is not needed anymore because the demosaiced image is kept by the rendered image
		if (mosaicImageFrame is not null)
		{
			if (this.IsHibernated || !this.IsActivated)
				mosaicImageFrame.Dispose();
			else
			{
				if (this.cachedMosaicImageFrame != mosaicImageFrame)
					this.cachedMosaicImageFrame?.Dispose();
				this.cachedMosaicImageFrame = mosaicImageFrame;
			}
		}

		// generate histograms
		if (exception is null && !cancellationTokenSource.IsCancellationRequested)
		{
			try
			{
				if (renderedImageFrame is not null)
					renderedImageFrame.Histograms = await BitmapHistograms.CreateAsync(renderedImageFrame.BitmapBuffer, this.SourceImageEffectiveBits, cancellationTokenSource.Token);
			}
			catch (Exception ex)
			{
				if (ex is not TaskCanceledException)
					this.Logger.LogError(ex, "Failed to generate histograms");
			}
		}

		// check whether rendering has been cancelled or not
		if (cancellationTokenSource.IsCancellationRequested)
		{
			this.Logger.LogWarning("Image rendering for '{sourceFileName}' has been cancelled", sourceFileName);
			this.SynchronizationContext.Post(() =>
            {
				colorSpaceConvertedImageFrame?.Dispose();
				renderedImageFrame?.Dispose();
            });
			return;
		}
		this.imageRenderingCancellationTokenSource = null;
		if (this.IsDisposed)
			return;

		// update state and continue filtering if needed
		if (exception is null)
		{
			this.Logger.LogDebug("Image for '{sourceFileName}' rendered", sourceFileName);

			// update state
			if (isColorSpaceConversionNeeded && this.colorSpaceConvertedImageFrame != colorSpaceConvertedImageFrame)
			{
				this.colorSpaceConvertedImageFrame?.Dispose();
				this.colorSpaceConvertedImageFrame = colorSpaceConvertedImageFrame;
			}
			if (renderedImageFrame is not null)
			{
				this.isImageRenderingForced = false;
				this.renderedImageFrame?.Dispose();
				this.renderedImageFrame = renderedImageFrame;
			}
			this.ResetValue(HasRenderingErrorProperty);
			this.SetValue(SourceDataSizeProperty, frameDataSize);
			this.UpdateCanMoveToFrames(frameNumber, true);
			this.canSelectColorAdjustment.Update((colorSpaceConvertedImageFrame ?? renderedImageFrame)?.Histograms is not null);
			this.canSelectRgbGain.Update((colorSpaceConvertedImageFrame ?? renderedImageFrame)?.RenderingResult.Let(it =>
				it.HasMeanOfRgb || it.HasWeightedMeanOfRgb) ?? false);

			// record rendering performance sample
			if (renderStopwatch is not null)
				this.RecordRenderingSample(this.ImageWidth, this.ImageHeight, renderStopwatch.ElapsedMilliseconds, imageRenderer.Format.Name);

			// filter image or report now
			if (this.IsFilteringRenderedImageNeeded && this.SelectImageFrameToFilter() is not null)
			{
				this.Logger.LogDebug("Continue filtering image after rendering");
				_ = this.FilterImage();
			}
			else
			{
				try
				{
					await this.ReportRenderedImageAsync(cancellationTokenSource);
				}
				catch (Exception ex)
				{
					if (ex is TaskCanceledException)
						return;
				}
			}
		}
		else
		{
			this.Logger.LogError(exception, "Error occurred while rendering image for '{sourceFileName}'", sourceFileName);

			// clear filtered image
			this.DisposeFilteredImage();

			// update state
			colorSpaceConvertedImageFrame?.Dispose();
			renderedImageFrame?.Dispose();
			this.colorSpaceConvertedImageFrame = this.colorSpaceConvertedImageFrame.DisposeAndReturnNull();
			this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
			this.SetValue(HasRenderingErrorProperty, true);
			this.UpdateCanMoveToFrames(frameNumber, false);
			this.canSelectColorAdjustment.Update(false);
			this.canSelectRgbGain.Update(false);
			Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
		}
	}


	/// <summary>
	/// Get or set width/height of panel of rendering parameters.
	/// </summary>
	public double RenderingParametersPanelSize
    {
		get => this.GetValue(RenderingParametersPanelSizeProperty);
		set => this.SetValue(RenderingParametersPanelSizeProperty, value);
    }


	/// <summary>
	/// Get or set requested scaling ratio of rendered image.
	/// </summary>
	public double RequestedImageDisplayScale
	{
		get => this.GetValue(RequestedImageDisplayScaleProperty);
		set => this.SetValue(RequestedImageDisplayScaleProperty, value);
	}


	// Request token for rendered image memory usage.
	IDisposable? RequestRenderedImageMemoryUsage(long dataSize)
	{
		var maxUsage = this.Settings.GetValueOrDefault(SettingKeys.MaxRenderedImagesMemoryUsageMB) << 20;
		var totalMemoryUsage = SharedRenderedImagesMemoryUsage.Value + dataSize;
		if (totalMemoryUsage <= maxUsage)
		{
			SharedRenderedImagesMemoryUsage.Update(totalMemoryUsage);
			this.SetValue(RenderedImagesMemoryUsageProperty, this.RenderedImagesMemoryUsage + dataSize);
			this.Logger.LogDebug("Request {dataSize} for rendered image, total: {totalMemoryUsage}, max: {maxUsage}", dataSize.ToFileSizeString(), totalMemoryUsage.ToFileSizeString(), maxUsage.ToFileSizeString());
			return new RenderedImageMemoryUsageToken(this, dataSize);
		}
		this.Logger.LogError("Unable to request {dataSize} for rendered image, total: {totalMemoryUsage}, max: {maxUsage}", dataSize.ToFileSizeString(), SharedRenderedImagesMemoryUsage.Value.ToFileSizeString(), maxUsage.ToFileSizeString());
		return null;
	}


	// Report rendered image according to current state.
	async Task ReportRenderedImageAsync(CancellationTokenSource cancellationTokenSource)
	{
		// cancel current reporting
		if (this.Application.IsDebugMode)
			this.Logger.LogTrace("Start reporting rendered image");
		this.CancelReportingRenderedImage();
		this.imageReportingCancellationTokenSource = cancellationTokenSource;
		
		// get image frame to be used
		var imageFrame = Global.Run(() =>
		{
			if (this.IsFilteringRenderedImageNeeded)
				return this.filteredImageFrame;
			if (this.Settings.GetValueOrDefault(SettingKeys.ColorSpaceConversionTiming) == ColorSpaceConversionTiming.BeforeApplyingFilters)
				return this.colorSpaceConvertedImageFrame;
			return this.renderedImageFrame;
		});
		
		// convert color space if needed
		var colorSpaceConvertedImageFrame = default(ImageFrame);
		if (imageFrame is not null && this.Settings.GetValueOrDefault(SettingKeys.ColorSpaceConversionTiming) == ColorSpaceConversionTiming.BeforeRenderingToDisplay)
		{
			var screenColorSpace = this.ScreenColorSpace;
			if (this.IsColorSpaceManagementEnabled && !screenColorSpace.Equals(this.ColorSpace))
			{
				try
				{
					if (this.Application.IsDebugMode)
						this.Logger.LogTrace("Convert color space before reporting rendered image");
					colorSpaceConvertedImageFrame = await this.ConvertColorSpaceAsync(imageFrame, this.ColorSpace, screenColorSpace, cancellationTokenSource.Token);
					if (colorSpaceConvertedImageFrame is null)
						this.Logger.LogError("Failed to convert color space before reporting rendered image");
					imageFrame = colorSpaceConvertedImageFrame;
					if (this.colorSpaceConvertedImageFrame != colorSpaceConvertedImageFrame)
					{
						this.colorSpaceConvertedImageFrame?.Dispose();
						this.colorSpaceConvertedImageFrame = colorSpaceConvertedImageFrame;
					}
				}
				catch (Exception ex)
				{
					// the conversion reports cancellation by OperationCanceledException, TaskCanceledException derives from it
					if (ex is OperationCanceledException)
					{
						this.Logger.LogWarning("Color space conversion has been cancelled before reporting rendered image");
						return;
					}
					this.Logger.LogError(ex, "Error occurred while converting color space before reporting rendered image");
				}
			}
		}
		
		// report
		if (imageFrame is not null)
		{
			// released cached image if it is not suitable
			var width = imageFrame.BitmapBuffer.Width;
			var height = imageFrame.BitmapBuffer.Height;
			var avaloniaPixelFormat = this.Settings.GetValueOrDefault(SettingKeys.Render32BitColorsOnly)
				? PixelFormats.Bgra8888
				: imageFrame.BitmapBuffer.Format switch
				{
					BitmapFormat.Bgra32 => PixelFormats.Bgra8888,
					BitmapFormat.Bgra64 => PixelFormats.Rgba64,
					_ => throw new NotSupportedException(),
				};
			if (this.cachedAvaRenderedImage is not null)
			{
				if (this.cachedAvaRenderedImage.PixelSize.Width != width
				    || this.cachedAvaRenderedImage.PixelSize.Height != height
				    || this.cachedAvaRenderedImage.Format != avaloniaPixelFormat)
                {
					if (this.Application.IsDebugMode)
						this.Logger.LogTrace("Release cached Avalonia bitmap, size: {w}x{h}", this.cachedAvaRenderedImage.PixelSize.Width, this.cachedAvaRenderedImage.PixelSize.Height);
					this.cachedAvaRenderedImage = null;
					this.cachedAvaRenderedImageMemoryUsageToken = this.cachedAvaRenderedImageMemoryUsageToken.DisposeAndReturnNull();
                }
            }

			// request memory usage
			IDisposable? memoryUsageToken;
			if (this.cachedAvaRenderedImage is not null)
			{
				if (this.Application.IsDebugMode)
					this.Logger.LogTrace("Use cached Avalonia bitmap, size: {w}x{h}", this.cachedAvaRenderedImage.PixelSize.Width, this.cachedAvaRenderedImage.PixelSize.Height);
				memoryUsageToken = this.cachedAvaRenderedImageMemoryUsageToken.AsNonNull();
				this.cachedAvaRenderedImageMemoryUsageToken = null;
			}
			else
			{
				var dataSize = width * height * (avaloniaPixelFormat.BitsPerPixel >> 3);
				memoryUsageToken = this.RequestRenderedImageMemoryUsage(dataSize);
				while (memoryUsageToken is null)
				{
					// the cached images are kept only to avoid reallocation, releasing them first keeps the image being displayed until
					// there is really nothing else to release, dropping it also drops the bitmap which the next reporting would reuse
					if (this.ReleaseCachedImages())
					{
						this.Logger.LogWarning("Unable to request memory usage for Avalonia Bitmap, release cached images");
						memoryUsageToken = this.RequestRenderedImageMemoryUsage(dataSize);
						continue;
					}
					if (this.RenderedImage is not null)
					{
						this.Logger.LogWarning("Unable to request memory usage for Avalonia Bitmap, drop the image being displayed");
						this.SetValue(QuarterSizeRenderedImageProperty, null);
						this.SetValue(RenderedImageProperty, null);
						memoryUsageToken = this.RequestRenderedImageMemoryUsage(dataSize);
						continue;
					}
					this.Logger.LogWarning("Unable to request memory usage for Avalonia Bitmap, try hibernating another session");
					if (await HibernateAnotherSessionAsync())
						memoryUsageToken = this.RequestRenderedImageMemoryUsage(dataSize);
					else
					{
						this.Logger.LogError("Unable to request memory usage for Avalonia Bitmap");
						if (colorSpaceConvertedImageFrame != this.colorSpaceConvertedImageFrame)
							colorSpaceConvertedImageFrame?.Dispose();
						this.canSelectColorAdjustment.Update(false);
						this.canSelectRgbGain.Update(false);
						this.ResetValue(HasRenderingErrorProperty);
						this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
						this.SetValue(HistogramsProperty, null);
						this.SetValue(QuarterSizeRenderedImageProperty, null);
						this.SetValue(RenderedImageProperty, null);
						return;
					}
				}
			}
			var quarterSizeMemoryUsageToken = (IDisposable?)null;

			// convert to Avalonia bitmap
			var bitmap = (WriteableBitmap?)null;
			var quarterSizeBitmap = (WriteableBitmap?)null;
			try
			{
				// create full-size Avalonia bitmap
				if (this.cachedAvaRenderedImage is not null)
				{
					bitmap = this.cachedAvaRenderedImage;
					this.cachedAvaRenderedImage = null;
				}
				else
				{
					if (this.Application.IsDebugMode)
						this.Logger.LogWarning("Allocate Avalonia bitmap, size: {width}x{height}", width, height);
					bitmap = await Task.Run(() => new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), avaloniaPixelFormat, AlphaFormat.Unpremul));
				}
				await imageFrame.BitmapBuffer.CopyToAvaloniaBitmapAsync(bitmap, cancellationTokenSource.Token);

				// create quarter-size Avalonia bitmap
				var halfWidth = width >> 1;
				var halfHeight = height >> 1;
				if (!cancellationTokenSource.IsCancellationRequested 
				    && (halfWidth > 1024 || halfHeight > 1024) 
				    && halfWidth > 0 
				    && halfHeight > 0)
				{
					// released cached image if it is not suitable
					if (this.cachedAvaQuarterSizeRenderedImage is not null)
					{
						if (this.cachedAvaQuarterSizeRenderedImage.PixelSize.Width != halfWidth
							|| this.cachedAvaQuarterSizeRenderedImage.PixelSize.Height != halfHeight
							|| this.cachedAvaQuarterSizeRenderedImage.Format != avaloniaPixelFormat)
						{
							if (this.Application.IsDebugMode)
								this.Logger.LogTrace("Release cached quarter-size Avalonia bitmap, size: {w}x{h}", this.cachedAvaQuarterSizeRenderedImage.PixelSize.Width, this.cachedAvaQuarterSizeRenderedImage.PixelSize.Height);
							this.cachedAvaQuarterSizeRenderedImage = null;
							this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
						}
					}

					// request memory usage
					if (this.cachedAvaQuarterSizeRenderedImage is not null)
                    {
						quarterSizeMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken;
						this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = null;
                    }
					else
						quarterSizeMemoryUsageToken = this.RequestRenderedImageMemoryUsage(halfWidth * halfHeight * (avaloniaPixelFormat.BitsPerPixel >> 3));

					// create bitmap
					if (quarterSizeMemoryUsageToken is not null)
					{
						if (this.cachedAvaQuarterSizeRenderedImage is not null)
						{
							quarterSizeBitmap = this.cachedAvaQuarterSizeRenderedImage;
							this.cachedAvaQuarterSizeRenderedImage = null;
						}
						else
						{
							if (this.Application.IsDebugMode)
								this.Logger.LogWarning("Allocate quarter-size Avalonia bitmap, size: {halfWidth}x{halfHeight}", halfWidth, halfHeight);
							quarterSizeBitmap = await Task.Run(() => new WriteableBitmap(new PixelSize(halfWidth, halfHeight), new Vector(96, 96), avaloniaPixelFormat, AlphaFormat.Unpremul));
						}
						await imageFrame.BitmapBuffer.CopyToQuarterSizeAvaloniaBitmapAsync(quarterSizeBitmap, cancellationTokenSource.Token);
					}
					else
						this.Logger.LogWarning("Unable to request memory usage for quarter-size Avalonia bitmap");
				}
			}
			catch (Exception ex)
			{
				this.cachedAvaQuarterSizeRenderedImage = null;
				this.cachedAvaRenderedImage = null;
				if (colorSpaceConvertedImageFrame != this.colorSpaceConvertedImageFrame)
					colorSpaceConvertedImageFrame?.Dispose();
				quarterSizeMemoryUsageToken?.Dispose();
				if (bitmap is null)
					memoryUsageToken.Dispose();
				if (ex is TaskCanceledException)
				{
					if (this.Application.IsDebugMode)
						this.Logger.LogWarning("Reporting rendered image has been cancelled");
					memoryUsageToken.Dispose();
					throw;
				}
				this.Logger.LogError(ex, "Failed to convert to Avalonia bitmap");
			}
			if (cancellationTokenSource.IsCancellationRequested)
			{
				if (this.Application.IsDebugMode)
					this.Logger.LogWarning("Reporting rendered image has been cancelled");
				throw new TaskCanceledException();
			}

			// update state
			this.cachedAvaRenderedImageMemoryUsageToken = this.cachedAvaRenderedImageMemoryUsageToken.DisposeAndReturnNull();
			this.cachedAvaRenderedImage = this.GetValue(RenderedImageProperty) as WriteableBitmap;
			this.cachedAvaRenderedImageMemoryUsageToken = this.avaRenderedImageMemoryUsageToken;
			this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
			this.cachedAvaQuarterSizeRenderedImage = this.GetValue(QuarterSizeRenderedImageProperty) as WriteableBitmap;
			this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = this.avaQuarterSizeRenderedImageMemoryUsageToken;
			this.avaQuarterSizeRenderedImageMemoryUsageToken = quarterSizeMemoryUsageToken;
			this.avaRenderedImageMemoryUsageToken = memoryUsageToken;
			this.canSaveFilteredImage.Update(!this.IsSavingFilteredImage && this.filteredImageFrame is not null);
			this.canSaveRenderedImage.Update(!this.IsSavingRenderedImage);
			this.canSelectColorAdjustment.Update(imageFrame.Histograms is not null);
			this.canSelectRgbGain.Update(imageFrame.RenderingResult.Let(it =>
				it.HasMeanOfRgb || it.HasWeightedMeanOfRgb));
			this.ResetValue(HasRenderingErrorProperty);
			this.SetValue(InsufficientMemoryForRenderedImageProperty, false);
			this.SetValue(HistogramsProperty, imageFrame.Histograms);
			this.SetValue(QuarterSizeRenderedImageProperty, quarterSizeBitmap);
			this.SetValue(RenderedImageProperty, bitmap);
		}
		else if (!this.IsFilteringRenderedImageNeeded || this.RenderedImage is null)
		{
			// there is nothing to report, the image reported before is kept when the frame is missing only because the filtering
			// which generates it has not completed yet, clearing it would make the image flicker while filtering continuously
			this.canSaveFilteredImage.Update(false);
			this.canSaveRenderedImage.Update(false);
			this.canSelectColorAdjustment.Update(false);
			this.canSelectRgbGain.Update(false);
			this.SetValue(HistogramsProperty, null);
			this.SetValue(QuarterSizeRenderedImageProperty, null);
			this.SetValue(RenderedImageProperty, null);
		}
		this.imageReportingCancellationTokenSource = null;
		this.releasedCachedImagesAction.Reschedule(ReleaseCachedImagesDelay);
		if (this.Application.IsDebugMode)
			this.Logger.LogTrace("Rendered image reported");
	}


	// Reset brightness adjustment.
	void ResetBrightnessAdjustment()
    {
		this.VerifyAccess();
		if (this.IsDisposed)
			return;
		this.SetValue(BrightnessAdjustmentProperty, 0);
    }


	/// <summary>
	/// Command to reset <see cref="BrightnessAdjustment"/>.
	/// </summary>
	public ICommand ResetBrightnessAdjustmentCommand { get; }


	// Reset color adjustment.
	void ResetColorAdjustment()
	{
		this.VerifyAccess();
		if (this.IsDisposed)
			return;
		this.SetValue(BlueColorAdjustmentProperty, 0);
		this.SetValue(GreenColorAdjustmentProperty, 0);
		this.SetValue(RedColorAdjustmentProperty, 0);
	}


	/// <summary>
	/// Command to reset <see cref="BlueColorAdjustment"/>, <see cref="GreenColorAdjustment"/> and <see cref="RedColorAdjustment"/>.
	/// </summary>
	public ICommand ResetColorAdjustmentCommand { get; }


	// Reset contrast adjustment.
	void ResetContrastAdjustment()
	{
		this.VerifyAccess();
		if (this.IsDisposed)
			return;
		this.SetValue(ContrastAdjustmentProperty, 0);
	}


	/// <summary>
	/// Command to reset <see cref="ContrastAdjustment"/>.
	/// </summary>
	public ICommand ResetContrastAdjustmentCommand { get; }


	// Reset all filter parameters.
	void ResetFilterParams()
	{
		this.ResetBrightnessAdjustment();
		this.ResetColorAdjustment();
		this.ResetContrastAdjustment();
		this.ResetHighlightAdjustment();
		this.ResetSaturationAdjustment();
		this.ResetShadowAdjustment();
		this.ResetVibranceAdjustment();
		this.SetValue(IsGrayscaleFilterEnabledProperty, false);
	}


	// Reset state of current filtering performance window.
	void ResetFilteringPerfWindow()
	{
		this.filteringPerfLargestDurationMs = 0;
		this.filteringPerfLargestFilters = null;
		this.filteringPerfLargestPixelCount = 0;
		this.filteringPerfLongestDurationMs = 0;
		this.filteringPerfLongestFilters = null;
		this.filteringPerfLongestPixelCount = 0;
		this.filteringPerfSampleCount = 0;
	}


	// Reset highlight adjustment.
	void ResetHighlightAdjustment()
	{
		this.VerifyAccess();
		if (this.IsDisposed)
			return;
		this.SetValue(HighlightAdjustmentProperty, 0);
	}


	/// <summary>
	/// Command to reset <see cref="HighlightAdjustment"/>.
	/// </summary>
	public ICommand ResetHighlightAdjustmentCommand { get; }


	// Reset state of current rendering performance window.
	void ResetRenderingPerfWindow()
	{
		this.renderingPerfLargestDurationMs = 0;
		this.renderingPerfLargestPixelCount = 0;
		this.renderingPerfLargestRendererName = null;
		this.renderingPerfLongestDurationMs = 0;
		this.renderingPerfLongestPixelCount = 0;
		this.renderingPerfLongestRendererName = null;
		this.renderingPerfSampleCount = 0;
	}


	// Reset RGB gain.
	void ResetRgbGain()
	{
		this.VerifyAccess();
		this.VerifyDisposed();
		this.SetValue(BlueColorGainProperty, 1.0);
		this.SetValue(GreenColorGainProperty, 1.0);
		this.SetValue(RedColorGainProperty, 1.0);
		this.renderImageAction.Reschedule();
	}


	/// <summary>
	/// Command to reset RGB gain.
	/// </summary>
	public ICommand ResetRgbGainCommand { get; }


	// Reset saturation adjustment.
	void ResetSaturationAdjustment()
	{
		this.VerifyAccess();
		if (this.IsDisposed)
			return;
		this.SetValue(SaturationAdjustmentProperty, 0);
	}


	/// <summary>
	/// Command to reset <see cref="SaturationAdjustment"/>.
	/// </summary>
	public ICommand ResetSaturationAdjustmentCommand { get; }


	// Reset shadow adjustment.
	void ResetShadowAdjustment()
	{
		this.VerifyAccess();
		if (this.IsDisposed)
			return;
		this.SetValue(ShadowAdjustmentProperty, 0);
	}


	/// <summary>
	/// Command to reset <see cref="ShadowAdjustment"/>.
	/// </summary>
	public ICommand ResetShadowAdjustmentCommand { get; }


	// Reset vibrance adjustment.
	void ResetVibranceAdjustment()
	{
		this.VerifyAccess();
		if (this.IsDisposed)
			return;
		this.SetValue(VibranceAdjustmentProperty, 0);
	}


	/// <summary>
	/// Command to reset <see cref="VibranceAdjustment"/>.
	/// </summary>
	public ICommand ResetVibranceAdjustmentCommand { get; }


	/// <summary>
	/// Restore state.
	/// </summary>
	/// <param name="savedState">Root JSON element represents saved state.</param>
	public async Task RestoreState(JsonElement savedState)
    {
		// check parameter
		if (savedState.ValueKind != JsonValueKind.Object)
			return;

		this.Logger.LogWarning("Start restoring state");

		// load rendering parameters
		var fileName = (string?)null;
		if (savedState.TryGetProperty(nameof(SourceFileName), out var jsonProperty) && jsonProperty.ValueKind == JsonValueKind.String)
			fileName = jsonProperty.GetString().AsNonNull();
		else
			this.Logger.LogDebug("Restoring state without source file");
		var profile = Global.Run(() =>
		{
			if (savedState.TryGetProperty(nameof(Profile), out var jsonProperty))
			{
				if (jsonProperty.ValueKind == JsonValueKind.Null)
					return ImageRenderingProfile.Default;
				if (jsonProperty.ValueKind == JsonValueKind.String)
				{
					var name = jsonProperty.GetString();
					return ImageRenderingProfiles.UserDefinedProfiles.FirstOrDefault(it => it.Name == name);
				}
			}
			return null;
		});
		var renderer = Global.Run(() =>
		{
			if (savedState.TryGetProperty(nameof(ImageRenderer), out var jsonProperty)
				&& jsonProperty.ValueKind == JsonValueKind.String)
			{
				if (ImageRenderers.TryFindByFormatName(jsonProperty.GetString().AsNonNull(), out var renderer))
					return renderer;
				this.Logger.LogWarning("Cannot find image renderer of '{s}' to restore", jsonProperty.GetString());
			}
			return null;
		});
		var dataOffset = 0L;
		var framePaddingSize = 0L;
		var byteOrdering = ByteOrdering.BigEndian;
		var yuvToBgraConverter = this.YuvToBgraConverter;
		var colorSpace = ColorSpace.Default;
		var useLinearColorSpace = false;
		var demosaicingAlgorithm = Media.Demosaicing.DemosaicingAlgorithms.Default;
		var width = 1;
		var height = 1;
		var effectiveBits = new int[this.effectiveBits.Length];
		var blackLevels = new uint[this.blackLevels.Length];
		var whiteLevels = new uint[this.whiteLevels.Length];
		var pixelStrides = new int[this.pixelStrides.Length];
		var rowStrides = new int[this.rowStrides.Length];
		var rGain = 1.0;
		var gGain = 1.0;
		var bGain = 1.0;
		if (savedState.TryGetProperty(nameof(DataOffset), out jsonProperty))
			jsonProperty.TryGetInt64(out dataOffset);
		if (savedState.TryGetProperty(nameof(FramePaddingSize), out jsonProperty))
			jsonProperty.TryGetInt64(out framePaddingSize);
		if (savedState.TryGetProperty(nameof(ByteOrdering), out jsonProperty)
			&& Enum.TryParse(jsonProperty.GetString(), out byteOrdering))
		{ }
		if (savedState.TryGetProperty(nameof(YuvToBgraConverter), out jsonProperty))
			YuvToBgraConverter.TryGetByName(jsonProperty.GetString(), out yuvToBgraConverter);
		if (savedState.TryGetProperty(nameof(ColorSpace), out jsonProperty))
			ColorSpace.TryGetColorSpace(jsonProperty.GetString().AsNonNull(), out colorSpace);
		if (savedState.TryGetProperty(nameof(UseLinearColorSpace), out jsonProperty))
			useLinearColorSpace = jsonProperty.ValueKind == JsonValueKind.True;
		if (savedState.TryGetProperty(nameof(DemosaicingAlgorithm), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.String)
		{
			var demosaicingAlgorithmId = jsonProperty.GetString();
			if (!Media.Demosaicing.DemosaicingAlgorithms.TryGetById(demosaicingAlgorithmId, out demosaicingAlgorithm))
				this.Logger.LogWarning("Unknown demosaicing algorithm to restore, id: {id}", demosaicingAlgorithmId);
		}
		else if (savedState.TryGetProperty("Demosaicing", out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.False)
			demosaicingAlgorithm = Media.Demosaicing.DemosaicingAlgorithms.Bypass;
		if (savedState.TryGetProperty(nameof(ImageWidth), out jsonProperty))
			jsonProperty.TryGetInt32(out width);
		if (savedState.TryGetProperty(nameof(ImageHeight), out jsonProperty))
			jsonProperty.TryGetInt32(out height);
		if (savedState.TryGetProperty("EffectiveBits", out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
		{
			var index = 0;
			foreach (var jsonValue in jsonProperty.EnumerateArray())
			{
				if (jsonValue.TryGetInt32(out var intValue))
					effectiveBits[index] = intValue;
				++index;
				if (index >= this.effectiveBits.Length)
					break;
			}
		}
		if (savedState.TryGetProperty("BlackLevels", out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
		{
			var index = 0;
			foreach (var jsonValue in jsonProperty.EnumerateArray())
			{
				if (jsonValue.TryGetUInt32(out var uintValue))
					blackLevels[index] = uintValue;
				++index;
				if (index >= this.blackLevels.Length)
					break;
			}
		}
		if (savedState.TryGetProperty("WhiteLevels", out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
		{
			var index = 0;
			foreach (var jsonValue in jsonProperty.EnumerateArray())
			{
				if (jsonValue.TryGetUInt32(out var uintValue))
					whiteLevels[index] = uintValue;
				++index;
				if (index >= this.blackLevels.Length)
					break;
			}
		}
		if (savedState.TryGetProperty("PixelStrides", out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
		{
			var index = 0;
			foreach (var jsonValue in jsonProperty.EnumerateArray())
			{
				if (jsonValue.TryGetInt32(out var intValue))
					pixelStrides[index] = intValue;
				++index;
				if (index >= this.pixelStrides.Length)
					break;
			}
		}
		if (savedState.TryGetProperty("RowStrides", out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
		{
			var index = 0;
			foreach (var jsonValue in jsonProperty.EnumerateArray())
			{
				if (jsonValue.TryGetInt32(out var intValue))
					rowStrides[index] = intValue;
				++index;
				if (index >= this.rowStrides.Length)
					break;
			}
		}
		if (savedState.TryGetProperty(nameof(RedColorGain), out jsonProperty) && jsonProperty.TryGetDouble(out rGain))
			rGain = ImageRenderingOptions.GetValidRgbGain(rGain);
		if (savedState.TryGetProperty(nameof(GreenColorGain), out jsonProperty) && jsonProperty.TryGetDouble(out gGain))
			gGain = ImageRenderingOptions.GetValidRgbGain(gGain);
		if (savedState.TryGetProperty(nameof(BlueColorGain), out jsonProperty) && jsonProperty.TryGetDouble(out bGain))
			bGain = ImageRenderingOptions.GetValidRgbGain(bGain);

		// load filtering parameters
		var blueColorAdjustment = 0.0;
		var brightnessAdjustment = 0.0;
		var contrastAdjustment = 0.0;
		var greenColorAdjustment = 0.0;
		var highlightAdjustment = 0.0;
		var isGrayscaleFilterEnabled = false;
		var redColorAdjustment = 0.0;
		var shadowAdjustment = 0.0;
		var vibranceAdjustment = 0.0;
		if (savedState.TryGetProperty(nameof(BlueColorAdjustment), out jsonProperty))
			jsonProperty.TryGetDouble(out blueColorAdjustment);
		if (savedState.TryGetProperty(nameof(BrightnessAdjustment), out jsonProperty))
			jsonProperty.TryGetDouble(out brightnessAdjustment);
		if (savedState.TryGetProperty(nameof(ContrastAdjustment), out jsonProperty))
			jsonProperty.TryGetDouble(out contrastAdjustment);
		if (savedState.TryGetProperty(nameof(GreenColorAdjustment), out jsonProperty))
			jsonProperty.TryGetDouble(out greenColorAdjustment);
		if (savedState.TryGetProperty(nameof(HighlightAdjustment), out jsonProperty))
			jsonProperty.TryGetDouble(out highlightAdjustment);
		if (savedState.TryGetProperty(nameof(IsGrayscaleFilterEnabled), out jsonProperty))
			isGrayscaleFilterEnabled = jsonProperty.ValueKind != JsonValueKind.False;
		if (savedState.TryGetProperty(nameof(RedColorAdjustment), out jsonProperty))
			jsonProperty.TryGetDouble(out redColorAdjustment);
		if (savedState.TryGetProperty(nameof(ShadowAdjustment), out jsonProperty))
			jsonProperty.TryGetDouble(out shadowAdjustment);
		if (savedState.TryGetProperty(nameof(VibranceAdjustment), out jsonProperty))
			jsonProperty.TryGetDouble(out vibranceAdjustment);

		// load displaying parameters
		var fitToViewport = true;
		var frameNumber = 1L;
		var framePlaybackRate = this.PersistentState.GetValueOrDefault(LatestFramePlaybackRate);
		var histogramsPanelSize = HistogramsPanelSizeProperty.DefaultValue;
		var isFramePlaybackLooping = this.PersistentState.GetValueOrDefault(IsInitFramePlaybackLooping);
		var isFramePlaybackRateUnlimited = this.PersistentState.GetValueOrDefault(IsInitFramePlaybackRateUnlimited);
		var isHistogramMeanMarkerVisible = this.PersistentState.GetValueOrDefault(IsInitHistogramMeanMarkerVisible);
		var isHistogramsVisible = this.PersistentState.GetValueOrDefault(IsInitHistogramsPanelVisible);
		var isImageFlippedX = false;
		var isImageFlippedY = false;
		var isRenderingParamsPanelVisible = true;
		var renderingParamsPanelSize = RenderingParametersPanelSizeProperty.DefaultValue;
		var rotation = 0;
		var scale = 1.0;
		if (savedState.TryGetProperty(nameof(FitImageToViewport), out jsonProperty))
			fitToViewport = jsonProperty.ValueKind != JsonValueKind.False;
		if (savedState.TryGetProperty(nameof(FrameNumber), out jsonProperty) && jsonProperty.TryGetInt64(out frameNumber))
			frameNumber = Math.Max(1, frameNumber);
		if (savedState.TryGetProperty(nameof(ImageDisplayRotation), out jsonProperty))
			jsonProperty.TryGetInt32(out rotation);
		if (savedState.TryGetProperty(nameof(FramePlaybackRate), out jsonProperty))
			jsonProperty.TryGetInt32(out framePlaybackRate);
		if (savedState.TryGetProperty(nameof(IsFramePlaybackLooping), out jsonProperty))
			isFramePlaybackLooping = jsonProperty.ValueKind != JsonValueKind.False;
		if (savedState.TryGetProperty(nameof(IsFramePlaybackRateUnlimited), out jsonProperty))
			isFramePlaybackRateUnlimited = jsonProperty.ValueKind == JsonValueKind.True;
		if (savedState.TryGetProperty(nameof(IsHistogramMeanMarkerVisible), out jsonProperty))
			isHistogramMeanMarkerVisible = jsonProperty.ValueKind != JsonValueKind.False;
		if (savedState.TryGetProperty(nameof(IsHistogramsVisible), out jsonProperty))
			isHistogramsVisible = jsonProperty.ValueKind != JsonValueKind.False;
		if (savedState.TryGetProperty(nameof(IsImageFlippedX), out jsonProperty))
			isImageFlippedX = jsonProperty.ValueKind != JsonValueKind.False;
		if (savedState.TryGetProperty(nameof(IsImageFlippedY), out jsonProperty))
			isImageFlippedY = jsonProperty.ValueKind != JsonValueKind.False;
		if (savedState.TryGetProperty(nameof(IsRenderingParametersPanelVisible), out jsonProperty))
			isRenderingParamsPanelVisible = jsonProperty.ValueKind != JsonValueKind.False;
		if (savedState.TryGetProperty(nameof(RequestedImageDisplayScale), out jsonProperty))
			jsonProperty.TryGetDouble(out scale);
		if (savedState.TryGetProperty(nameof(HistogramsPanelSize), out jsonProperty)
			&& jsonProperty.TryGetDouble(out histogramsPanelSize))
		{
			histogramsPanelSize = this.CoerceValue(HistogramsPanelSizeProperty, histogramsPanelSize);
			if (!HistogramsPanelSizeProperty.ValidationFunction(histogramsPanelSize))
				histogramsPanelSize = HistogramsPanelSizeProperty.DefaultValue;
		}
		if (savedState.TryGetProperty(nameof(RenderingParametersPanelSize), out jsonProperty)
			&& jsonProperty.TryGetDouble(out renderingParamsPanelSize))
		{
			renderingParamsPanelSize = this.CoerceValue(RenderingParametersPanelSizeProperty, renderingParamsPanelSize);
			if (!RenderingParametersPanelSizeProperty.ValidationFunction(renderingParamsPanelSize))
				renderingParamsPanelSize = RenderingParametersPanelSizeProperty.DefaultValue;
		}

		// load other state
		if (savedState.TryGetProperty(nameof(CustomTitle), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.String)
			this.SetValue(CustomTitleProperty, jsonProperty.GetString());
		
		// restore size and visibility of histograms before opening the source (opening the source may block for a while)
		this.SetValue(HistogramsPanelSizeProperty, histogramsPanelSize);
		this.SetValue(IsHistogramMeanMarkerVisibleProperty, isHistogramMeanMarkerVisible);
		this.SetValue(IsHistogramsVisibleProperty, isHistogramsVisible);

		// restore parameters of frame playback
		this.SetValue(FramePlaybackRateProperty, framePlaybackRate);
		this.SetValue(IsFramePlaybackLoopingProperty, isFramePlaybackLooping);
		this.SetValue(IsFramePlaybackRateUnlimitedProperty, isFramePlaybackRateUnlimited);

		// open source file
		if (fileName is not null)
		{
			await this.OpenSourceFile(fileName);
			if (!this.IsSourceOpened)
				this.Logger.LogError("Unable to restore source file '{fileName}'", fileName);
		}

		// apply profile
		if (profile is not null)
			this.SetValue(ProfileProperty, profile);

		// apply rendering parameters
		if (renderer is not null)
			this.SetValue(ImageRendererProperty, renderer);
		this.SetValue(DataOffsetProperty, dataOffset);
		this.SetValue(FramePaddingSizeProperty, framePaddingSize);
		this.SetValue(ByteOrderingProperty, byteOrdering);
		this.SetValue(YuvToBgraConverterProperty, yuvToBgraConverter);
		this.SetValue(ColorSpaceProperty, colorSpace);
		this.SetValue(UseLinearColorSpaceProperty, useLinearColorSpace);
		this.SetValue(DemosaicingAlgorithmProperty, demosaicingAlgorithm);
		this.SetValue(ImageWidthProperty, width);
		this.SetValue(ImageHeightProperty, height);
		for (var i = effectiveBits.Length - 1; i >= 0; --i)
			this.ChangeEffectiveBits(i, effectiveBits[i]);
		for (var i = blackLevels.Length - 1; i >= 0; --i)
			this.ChangeBlackLevel(i, blackLevels[i]);
		for (var i = whiteLevels.Length - 1; i >= 0; --i)
			this.ChangeWhiteLevel(i, whiteLevels[i]);
		for (var i = pixelStrides.Length - 1; i >= 0; --i)
			this.ChangePixelStride(i, pixelStrides[i]);
		for (var i = rowStrides.Length - 1; i >= 0; --i)
			this.ChangeRowStride(i, rowStrides[i]);
		this.SetValue(RedColorGainProperty, rGain);
		this.SetValue(GreenColorGainProperty, gGain);
		this.SetValue(BlueColorGainProperty, bGain);

		// apply filtering parameters
		this.SetValue(BlueColorAdjustmentProperty, blueColorAdjustment);
		this.SetValue(BrightnessAdjustmentProperty, brightnessAdjustment);
		this.SetValue(ContrastAdjustmentProperty, contrastAdjustment);
		this.SetValue(GreenColorAdjustmentProperty, greenColorAdjustment);
		this.SetValue(HighlightAdjustmentProperty, highlightAdjustment);
		this.SetValue(IsGrayscaleFilterEnabledProperty, isGrayscaleFilterEnabled);
		this.SetValue(RedColorAdjustmentProperty, redColorAdjustment);
		this.SetValue(ShadowAdjustmentProperty, shadowAdjustment);
		this.SetValue(VibranceAdjustmentProperty, vibranceAdjustment);

		// apply displaying parameters
		this.SetValue(FitImageToViewportProperty, fitToViewport);
		this.SetValue(FrameNumberProperty, frameNumber);
		this.SetValue(ImageDisplayRotationProperty, rotation);
		this.SetValue(IsImageFlippedXProperty, isImageFlippedX);
		this.SetValue(IsImageFlippedYProperty, isImageFlippedY);
		this.SetValue(RenderingParametersPanelSizeProperty, renderingParamsPanelSize);
		this.SetValue(IsRenderingParametersPanelVisibleProperty, isRenderingParamsPanelVisible);
		this.SetValue(RequestedImageDisplayScaleProperty, scale);

		this.Logger.LogWarning("State restored");

		// start rendering
		this.isImageDimensionsEvaluationNeeded = false;
		this.isImagePlaneOptionsResetNeeded = false;
		if (this.IsActivated)
			this.renderImageAction.Reschedule();
		else
			this.renderImageAction.Cancel();
    }


	// Rotate rendered image counter-clockwise.
	void RotateLeft()
	{
		if (!this.IsSourceOpened)
			return;
		var rotation = (int)(this.GetValue(ImageDisplayRotationProperty) + 0.5) switch
		{
			0 => 270,
			180 => 90,
			270 => 180,
			_ => 0,
		};
		this.SetValue(ImageDisplayRotationProperty, rotation);
		if (this.GetValue(FitImageToViewportProperty) 
			&& double.IsFinite(this.fitRenderedImageToViewportScale))
		{
			var scale = (rotation % 180) == 0
				? this.fitRenderedImageToViewportScale
				: this.fitRenderedImageToViewportScaleSwapped;
			this.ZoomTo(scale, false);
		}
		else
			this.updateImageDisplaySizeAction.Schedule();
	}


	/// <summary>
	/// Command for rotating rendered image counter-clockwise.
	/// </summary>
	public ICommand RotateLeftCommand { get; }


	// Rotate rendered image clockwise.
	void RotateRight()
	{
		if (!this.IsSourceOpened)
			return;
		var rotation = (int)(this.GetValue(ImageDisplayRotationProperty) + 0.5) switch
		{
			0 => 90,
			90 => 180,
			180 => 270,
			_ => 0,
		};
		this.SetValue(ImageDisplayRotationProperty, rotation);
		if (this.GetValue(FitImageToViewportProperty) 
			&& double.IsFinite(this.fitRenderedImageToViewportScale))
		{
			var scale = (rotation % 180) == 0
				? this.fitRenderedImageToViewportScale
				: this.fitRenderedImageToViewportScaleSwapped;
			this.ZoomTo(scale, false);
		}
		else
			this.updateImageDisplaySizeAction.Schedule();
	}


	/// <summary>
	/// Command for rotating rendered image clockwise.
	/// </summary>
	public ICommand RotateRightCommand { get; }


	/// <summary>
	/// Get or set row stride of 1st image plane.
	/// </summary>
	public int RowStride1
	{
		get => this.rowStrides[0];
		set => this.ChangeRowStride(0, value);
	}


	/// <summary>
	/// Get or set row stride of 2nd image plane.
	/// </summary>
	public int RowStride2
	{
		get => this.rowStrides[1];
		set => this.ChangeRowStride(1, value);
	}


	/// <summary>
	/// Get or set row stride of 3rd image plane.
	/// </summary>
	public int RowStride3
	{
		get => this.rowStrides[2];
		set => this.ChangeRowStride(2, value);
	}


	/// <summary>
	/// Get or set saturation adjustment. Range is [-1.0, 1.0].
	/// </summary>
	public double SaturationAdjustment
	{
		get => this.GetValue(SaturationAdjustmentProperty);
		set => this.SetValue(SaturationAdjustmentProperty, value);
	}


	// Save as new profile.
	void SaveAsNewProfile(string name)
	{
		// check state
		if (!this.canSaveAsNewProfile.Value)
			return;

		// check name
		if (name.Length == 0)
		{
			this.Logger.LogError("Cannot create profile with empty name");
			return;
		}

		// create profile
		var profile = new ImageRenderingProfile(name, this.ImageRenderer).Also(this.WriteParametersToProfile);
		if (!ImageRenderingProfiles.AddUserDefinedProfile(profile))
		{
			this.Logger.LogError("Unable to add profile '{name}'", name);
			return;
		}

		// switch to profile
		this.SwitchToProfileWithoutApplying(profile);
	}


	/// <summary>
	/// Command for saving current parameters as new profile.
	/// </summary>
	public ICommand SaveAsNewProfileCommand { get; }


	// Save filtered image.
	async Task<bool> SaveFilteredImage(ImageSavingParams parameters)
	{
		// check state
		if (string.IsNullOrWhiteSpace(parameters.FileName))
			return false;
		if (!this.canSaveFilteredImage.Value)
			return false;
		
		// select color space
		var options = parameters.Options;
		if (!this.Settings.GetValueOrDefault(SettingKeys.EnableColorSpaceManagement))
			options.ColorSpace = null;
		else if (options.ColorSpace is null)
		{
			options.ColorSpace = this.Settings.GetValueOrDefault(SettingKeys.ColorSpaceConversionTiming) == ColorSpaceConversionTiming.BeforeRenderingToDisplay 
				? this.ColorSpace 
				: this.ScreenColorSpace;
		}

		// save image
		var encoder = parameters.Encoder;
		if (encoder is null && !ImageEncoders.TryGetEncoderByFormat(FileFormats.Png, out encoder))
			return false;
		var applyTransformation = this.Settings.GetValueOrDefault(SettingKeys.SaveRenderedImageWithTransformation);
		var flipX = applyTransformation && this.GetValue(IsImageFlippedXProperty);
		var flipY = applyTransformation && this.GetValue(IsImageFlippedYProperty);
		if (applyTransformation)
			options.Orientation = (int)(this.GetValue(ImageDisplayRotationProperty) + 0.5);
		this.canSaveFilteredImage.Update(false);
		this.SetValue(IsSavingFilteredImageProperty, true);
		var properties = this.PrepareRenderedImageSavedTrackingProperties(parameters, true, applyTransformation);
		try
		{
			using IBitmapBuffer bufferToEncode = this.filteredImageFrame.AsNonNull().BitmapBuffer.Let(it => flipX || flipY
				? it.Flip(flipX, flipY)
				: it.Share());
			var encodingStopwatch = Stopwatch.StartNew();
			await encoder.AsNonNull().EncodeAsync(bufferToEncode, new FileStreamProvider(parameters.FileName), options, new CancellationToken());
			encodingStopwatch.Stop();
			properties[UsageProperties.DimensionMP] = ((long)Math.Round((long)bufferToEncode.Width * bufferToEncode.Height / 1_000_000.0)).ToString(CultureInfo.InvariantCulture);
			properties[UsageProperties.Duration] = encodingStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
			this.Application.UsageManager.TrackEvent(UsageEvents.RenderedImageSaved, properties);
			this.ImageSavingCompleted?.Invoke(this, new(parameters.FileName, true));
			return true;
		}
		catch (Exception ex)
		{
			this.Logger.LogError(ex, "Unable to save filtered image");
			this.Application.UsageManager.TrackException(ex, properties: properties);
			this.ImageSavingCompleted?.Invoke(this, new(parameters.FileName, false));
			return false;
		}
		finally
		{
			this.canSaveFilteredImage.Update(!this.IsFilteringRenderedImage);
			this.SetValue(IsSavingFilteredImageProperty, false);
		}
	}


	/// <summary>
	/// Command for saving filtered image to file or stream.
	/// </summary>
	/// <remarks>Type of parameter is <see cref="ImageSavingParams"/>.</remarks>
	public ICommand SaveFilteredImageCommand { get; }


	// Save current parameters to profile.
	async Task SaveProfile()
	{
		// check state
		if (!this.canSaveOrDeleteProfile.Value)
			return;
		var profile = this.Profile;
		if (profile.Type != ImageRenderingProfileType.UserDefined)
		{
			this.Logger.LogError("Cannot save non user defined profile");
			return;
		}

		// update parameters
		this.WriteParametersToProfile(profile);

		// save
		try
		{
			await profile.SaveAsync();
		}
		catch (Exception ex)
		{
			this.Logger.LogError(ex, "Failed to save profile '{profileName}'", profile.Name);
		}
	}


	/// <summary>
	/// Command to save parameters to current profile.
	/// </summary>
	public ICommand SaveProfileCommand { get; }


	// Save rendered image.
	async Task<bool> SaveRenderedImage(ImageSavingParams parameters)
	{
		// check state
		if (string.IsNullOrWhiteSpace(parameters.FileName))
			return false;
		if (!this.canSaveRenderedImage.Value)
			return false;
		var renderedImageFrame = this.renderedImageFrame;
		if (renderedImageFrame is null)
		{
			this.Logger.LogError("No rendered image to save");
			return false;
		}

		// select color space
		var options = parameters.Options;
		if (!this.Settings.GetValueOrDefault(SettingKeys.EnableColorSpaceManagement))
			options.ColorSpace = null;
		else if (options.ColorSpace is null)
			options.ColorSpace = this.ColorSpace;

		// save image
		var encoder = parameters.Encoder;
		if (encoder is null && !ImageEncoders.TryGetEncoderByFormat(FileFormats.Png, out encoder))
			return false;
		var applyTransformation = this.Settings.GetValueOrDefault(SettingKeys.SaveRenderedImageWithTransformation);
		var flipX = applyTransformation && this.GetValue(IsImageFlippedXProperty);
		var flipY = applyTransformation && this.GetValue(IsImageFlippedYProperty);
		if (applyTransformation)
			options.Orientation = (int)(this.GetValue(ImageDisplayRotationProperty) + 0.5);
		this.canSaveRenderedImage.Update(false);
		this.SetValue(IsSavingRenderedImageProperty, true);
		var properties = this.PrepareRenderedImageSavedTrackingProperties(parameters, false, applyTransformation);
		try
		{
			using IBitmapBuffer bufferToEncode = renderedImageFrame.BitmapBuffer.Let(it => flipX || flipY
				? it.Flip(flipX, flipY)
				: it.Share());
			var encodingStopwatch = Stopwatch.StartNew();
			await encoder.AsNonNull().EncodeAsync(bufferToEncode, new FileStreamProvider(parameters.FileName), options, new CancellationToken());
			encodingStopwatch.Stop();
			properties[UsageProperties.DimensionMP] = ((long)Math.Round((long)bufferToEncode.Width * bufferToEncode.Height / 1_000_000.0)).ToString(CultureInfo.InvariantCulture);
			properties[UsageProperties.Duration] = encodingStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
			this.Application.UsageManager.TrackEvent(UsageEvents.RenderedImageSaved, properties);
			this.ImageSavingCompleted?.Invoke(this, new(parameters.FileName, true));
			return true;
		}
		catch (Exception ex)
		{
			this.Logger.LogError(ex, "Unable to save rendered image");
			this.Application.UsageManager.TrackException(ex, properties: properties);
			this.ImageSavingCompleted?.Invoke(this, new(parameters.FileName, false));
			return false;
		}
		finally
		{
			this.SetValue(IsSavingRenderedImageProperty, false);
			this.canSaveRenderedImage.Update(this.renderedImageFrame is not null);
		}
	}


	/// <summary>
	/// Command for saving rendered image to file or stream.
	/// </summary>
	/// <remarks>Type of parameter is <see cref="ImageSavingParams"/>.</remarks>
	public ICommand SaveRenderedImageCommand { get; }


	/// <summary>
	/// Save instance state in JSON format.
	/// </summary>
	public void SaveState(Utf8JsonWriter writer)
	{
		// start
		writer.WriteStartObject();
	
		// file and profile
		var fileName = this.SourceFileName;
		if (!string.IsNullOrEmpty(fileName))
			writer.WriteString(nameof(SourceFileName), fileName.AsNonNull());
		else
			this.Logger.LogDebug("Saving state without source file");

		// rendering parameters
		switch (this.Profile.Type)
		{
			case ImageRenderingProfileType.Default:
				writer.WriteNull(nameof(Profile));
				break;
			case ImageRenderingProfileType.UserDefined:
				writer.WriteString(nameof(Profile), this.Profile.Name);
				break;
		}
		writer.WriteString(nameof(ImageRenderer), this.ImageRenderer.Format.Name);
		writer.WriteNumber(nameof(DataOffset), this.DataOffset);
		writer.WriteNumber(nameof(FramePaddingSize), this.FramePaddingSize);
		writer.WriteString(nameof(ByteOrdering), this.ByteOrdering.ToString());
		writer.WriteString(nameof(YuvToBgraConverter), this.YuvToBgraConverter.Name);
		writer.WriteString(nameof(ColorSpace), this.ColorSpace.Name);
		if (this.UseLinearColorSpace)
			writer.WriteBoolean(nameof(UseLinearColorSpace), true);
		writer.WriteString(nameof(DemosaicingAlgorithm), this.DemosaicingAlgorithm.Id);
		writer.WriteNumber(nameof(ImageWidth), this.ImageWidth);
		writer.WriteNumber(nameof(ImageHeight), this.ImageHeight);
		writer.WritePropertyName("EffectiveBits");
		writer.WriteStartArray();
		foreach (var n in this.effectiveBits)
			writer.WriteNumberValue(n);
		writer.WriteEndArray();
		writer.WritePropertyName("BlackLevels");
		writer.WriteStartArray();
		foreach (var n in this.blackLevels)
			writer.WriteNumberValue(n);
		writer.WriteEndArray();
		writer.WritePropertyName("WhiteLevels");
		writer.WriteStartArray();
		foreach (var n in this.whiteLevels)
			writer.WriteNumberValue(n);
		writer.WriteEndArray();
		writer.WritePropertyName("PixelStrides");
		writer.WriteStartArray();
		foreach (var n in this.pixelStrides)
			writer.WriteNumberValue(n);
		writer.WriteEndArray();
		writer.WritePropertyName("RowStrides");
		writer.WriteStartArray();
		foreach (var n in this.rowStrides)
			writer.WriteNumberValue(n);
		writer.WriteEndArray();
		if (this.IsRgbGainSupported)
		{
			writer.WriteNumber(nameof(RedColorGain), this.RedColorGain);
			writer.WriteNumber(nameof(GreenColorGain), this.GreenColorGain);
			writer.WriteNumber(nameof(BlueColorGain), this.BlueColorGain);
		}

		// filtering parameters
		if (this.HasBrightnessAdjustment)
			writer.WriteNumber(nameof(BrightnessAdjustment), this.BrightnessAdjustment);
		if (this.HasColorAdjustment)
		{
			writer.WriteNumber(nameof(BlueColorAdjustment), this.BlueColorAdjustment);
			writer.WriteNumber(nameof(GreenColorAdjustment), this.GreenColorAdjustment);
			writer.WriteNumber(nameof(RedColorAdjustment), this.RedColorAdjustment);
		}
		if (this.HasContrastAdjustment)
			writer.WriteNumber(nameof(ContrastAdjustment), this.ContrastAdjustment);
		if (this.HasHighlightAdjustment)
			writer.WriteNumber(nameof(HighlightAdjustment), this.HighlightAdjustment);
		if (this.HasShadowAdjustment)
			writer.WriteNumber(nameof(ShadowAdjustment), this.ShadowAdjustment);
		if (this.HasVibranceAdjustment)
			writer.WriteNumber(nameof(VibranceAdjustment), this.VibranceAdjustment);
		writer.WriteBoolean(nameof(IsGrayscaleFilterEnabled), this.IsGrayscaleFilterEnabled);

		// displaying parameters
		writer.WriteBoolean(nameof(FitImageToViewport), this.GetValue(FitImageToViewportProperty));
		writer.WriteNumber(nameof(FrameNumber), this.FrameNumber);
		writer.WriteNumber(nameof(FramePlaybackRate), this.FramePlaybackRate);
		writer.WriteNumber(nameof(HistogramsPanelSize), this.HistogramsPanelSize);
		writer.WriteNumber(nameof(ImageDisplayRotation), (int)(this.GetValue(ImageDisplayRotationProperty) + 0.5));
		writer.WriteBoolean(nameof(IsFramePlaybackLooping), this.IsFramePlaybackLooping);
		writer.WriteBoolean(nameof(IsFramePlaybackRateUnlimited), this.IsFramePlaybackRateUnlimited);
		writer.WriteBoolean(nameof(IsHistogramMeanMarkerVisible), this.IsHistogramMeanMarkerVisible);
		writer.WriteBoolean(nameof(IsHistogramsVisible), this.IsHistogramsVisible);
		writer.WriteBoolean(nameof(IsImageFlippedX), this.IsImageFlippedX);
		writer.WriteBoolean(nameof(IsImageFlippedY), this.IsImageFlippedY);
		writer.WriteBoolean(nameof(IsRenderingParametersPanelVisible), this.IsRenderingParametersPanelVisible);
		writer.WriteNumber(nameof(RequestedImageDisplayScale), this.GetValue(RequestedImageDisplayScaleProperty));
		writer.WriteNumber(nameof(RenderingParametersPanelSize), this.RenderingParametersPanelSize);

		// other state
		if (this.CustomTitle is not null)
			writer.WriteString(nameof(CustomTitle), this.CustomTitle ?? "");
		
		// complete
		writer.WriteEndObject();
	}
	
	
	// Effective color space of screen.
	ColorSpace ScreenColorSpace => (this.Owner as Workspace)?.EffectiveScreenColorSpace ?? Global.Run(() =>
	{
		ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.ScreenColorSpaceName), out var colorSpace);
		return colorSpace;
	});


	/// <summary>
	/// Get or set pixel density of current screen.
	/// </summary>
	public double ScreenPixelDensity
	{
		get => this.GetValue(ScreenPixelDensityProperty);
		set => this.SetValue(ScreenPixelDensityProperty, value);
	}


	// Perform auto color adjustment.
	void SelectColorAdjustment()
	{
		// check state
		this.VerifyAccess();
		this.VerifyDisposed();
		
		// get histogram
		var imageFrame = Global.Run(() =>
		{
			if (this.Settings.GetValueOrDefault(SettingKeys.ColorSpaceConversionTiming) == ColorSpaceConversionTiming.BeforeApplyingFilters)
				return this.colorSpaceConvertedImageFrame ?? this.renderedImageFrame;
			return this.renderedImageFrame;
		});
		var histograms = imageFrame?.Histograms;
		if (histograms is null)
			return;
		
		// calculate ratio of RGB
		double rRatio;
		double gRatio;
		double bRatio;
		var refR = histograms.MeanOfRed;
		var refG = histograms.MeanOfGreen;
		var refB = histograms.MeanOfBlue;
		if (refR > refG)
		{
			if (refR > refB)
			{
				if (refG > refB) // R > G > B
				{
					rRatio = refG / refR;
					gRatio = 1.0;
					bRatio = refG / refB;
				}
				else // R > B >= G
				{
					rRatio = refB / refR;
					gRatio = refB / refG;
					bRatio = 1.0;
				}
			}
			else // B >= R > G
			{
				rRatio = 1.0;
				gRatio = refR / refG;
				bRatio = refR / refB;
			}
		}
		else if (refG > refB)
		{
			if (refR > refB) // G > R > B
			{
				rRatio = 1.0;
				gRatio = refR / refG;
				bRatio = refR / refB;
			}
			else // G > B >= R
			{
				rRatio = refB / refR;
				gRatio = refB / refG;
				bRatio = 1.0;
			}
		}
		else // B >= G >= R
		{
			rRatio = refG / refR;
			gRatio = 1.0;
			bRatio = refG / refB;
		}
		if (!double.IsFinite(rRatio) || !double.IsFinite(gRatio) || !double.IsFinite(bRatio))
			return;
		if (rRatio == 0 || gRatio == 0 || bRatio == 0)
			return;

		// track auto color adjustment
		this.trackFilteringParamsAppliedAction.ExecuteIfScheduled();
		if (this.GetValue(IsSourceOpenedProperty))
		{
			var properties = this.PrepareUsageTrackingProperties();
			this.Application.UsageManager.TrackEvent(UsageEvents.AutoColorAdjustmentApplied, properties);
		}

		// apply color adjustment
		static double Quantize(double value) => (int)(value * 100 + 0.5) / 100.0;
		this.SetValue(RedColorAdjustmentProperty, rRatio < 0.5
			? -1
			: rRatio > 2
				? 1
				: rRatio >= 1
					? Quantize(rRatio - 1)
					: Quantize(1 - 1 / rRatio));
		this.SetValue(GreenColorAdjustmentProperty, gRatio < 0.5
			? -1
			: gRatio > 2
				? 1
				: gRatio >= 1
					? Quantize(gRatio - 1)
					: Quantize(1 - 1 / gRatio));
		this.SetValue(BlueColorAdjustmentProperty, bRatio < 0.5
			? -1
			: bRatio > 2
				? 1
				: bRatio >= 1
					? Quantize(bRatio - 1)
					: Quantize(1 - 1 / bRatio));
		if (this.filterImageAction.IsScheduled)
			this.filterImageAction.Reschedule();
	}


	/// <summary>
	/// Command to apply auto color adjustment.
	/// </summary>
	public ICommand SelectColorAdjustmentCommand { get; }


	// Select the default algorithm to replace the one which doesn't support the given bayer pattern. Bypass is the last resort because it supports every pattern by definition.
	static DemosaicingAlgorithm SelectDefaultDemosaicingAlgorithm(BayerPattern bayerPattern)
	{
		var algorithm = Media.Demosaicing.DemosaicingAlgorithms.Default;
		if (algorithm.IsBayerPatternSupported(bayerPattern))
			return algorithm;
		return Media.Demosaicing.DemosaicingAlgorithms.Bypass;
	}


	// Select the image frame which the filters should be applied on, according to the timing of color space conversion.
	ImageFrame? SelectImageFrameToFilter()
	{
		if (this.colorSpaceConvertedImageFrame is not null
		    && this.Settings.GetValueOrDefault(SettingKeys.ColorSpaceConversionTiming) == ColorSpaceConversionTiming.BeforeApplyingFilters)
		{
			return this.colorSpaceConvertedImageFrame;
		}
		return this.renderedImageFrame;
	}


	// Perform auto RGB gain selection.
	void SelectRgbGain()
	{
		// check state
		this.VerifyAccess();
		this.VerifyDisposed();
		
		// get rendering result
		var renderingResult = (this.renderedImageFrame?.RenderingResult).GetValueOrDefault();
		if (double.IsNaN(renderingResult.MeanOfBlue)
			|| double.IsNaN(renderingResult.MeanOfGreen)
			|| double.IsNaN(renderingResult.MeanOfRed))
		{
			return;
		}
		
		// calculate ratio of RGB
		double rRatio;
		double gRatio;
		double bRatio;
		//double mean;
		var refR = renderingResult.WeightedMeanOfRed;
		var refG = renderingResult.WeightedMeanOfGreen;
		var refB = renderingResult.WeightedMeanOfBlue;
		if (double.IsNaN(refR) || double.IsNaN(refG) || double.IsNaN(refB))
		{
			refR = renderingResult.MeanOfRed;
			refG = renderingResult.MeanOfGreen;
			refB = renderingResult.MeanOfBlue;
		}
		if (refR > refG)
		{
			if (refR > refB)
			{
				if (refG > refB) // R > G > B
				{
					rRatio = refG / refR;
					gRatio = 1.0;
					bRatio = refG / refB;
					//mean = refG;
				}
				else // R > B >= G
				{
					rRatio = refB / refR;
					gRatio = refB / refG;
					bRatio = 1.0;
					//mean = refB;
				}
			}
			else // B >= R > G
			{
				rRatio = 1.0;
				gRatio = refR / refG;
				bRatio = refR / refB;
				//mean = refR;
			}
		}
		else if (refG > refB)
		{
			if (refR > refB) // G > R > B
			{
				rRatio = 1.0;
				gRatio = refR / refG;
				bRatio = refR / refB;
				//mean = refR;
			}
			else // G > B >= R
			{
				rRatio = refB / refR;
				gRatio = refB / refG;
				bRatio = 1.0;
				//mean = refB;
			}
		}
		else // B >= G >= R
		{
			rRatio = refG / refR;
			gRatio = 1.0;
			bRatio = refG / refB;
			//mean = refG;
		}
		if (!double.IsFinite(rRatio) || !double.IsFinite(gRatio) || !double.IsFinite(bRatio))
			return;
		if (rRatio == 0 || gRatio == 0 || bRatio == 0)
			return;
		
		// apply RGB gain
		static double Quantize(double value) => (int)(value * 100 + 0.5) / 100.0;
		this.SetValue(RedColorGainProperty, Quantize(rRatio));
		this.SetValue(GreenColorGainProperty, Quantize(gRatio));
		this.SetValue(BlueColorGainProperty, Quantize(bRatio));
		if (this.renderImageAction.IsScheduled)
			this.renderImageAction.Reschedule();
	}


	/// <summary>
	/// Command to apply auto RGB gain selection.
	/// </summary>
	public ICommand SelectRgbGainCommand { get; }


	// Select default image renderer according to settings.
	IImageRenderer SelectDefaultImageRenderer()
	{
		if (ImageRenderers.TryFindByFormatName(this.Settings.GetValueOrDefault(SettingKeys.DefaultImageRendererFormatName), out var imageRenderer))
			return imageRenderer.AsNonNull();
		return ImageRenderers.All.SingleOrDefault((candidate) => candidate is L8ImageRenderer) ?? ImageRenderers.All[0];
	}


	/// <summary>
	/// Get color of selected pixel on rendered image.
	/// </summary>
	public Color64 SelectedRenderedImagePixelColor => this.GetValue(SelectedRenderedImagePixelColorProperty);


	/// <summary>
	/// Get L*a*b* color of selected pixel on rendered image.
	/// </summary>
	public Tuple<double, double, double> SelectedRenderedImagePixelLabColor => this.GetValue(SelectedRenderedImagePixelLabColorProperty);


	/// <summary>
	/// Get XYZ color of selected pixel on rendered image.
	/// </summary>
	public Tuple<double, double, double> SelectedRenderedImagePixelXyzColor => this.GetValue(SelectedRenderedImagePixelXyzColorProperty);


	/// <summary>
	/// Get horizontal position of selected pixel on rendered image. Return -1 if no pixel selected.
	/// </summary>
	public int SelectedRenderedImagePixelPositionX => this.GetValue(SelectedRenderedImagePixelPositionXProperty);


	/// <summary>
	/// Get vertical position of selected pixel on rendered image. Return -1 if no pixel selected.
	/// </summary>
	public int SelectedRenderedImagePixelPositionY => this.GetValue(SelectedRenderedImagePixelPositionYProperty);


	/// <summary>
	/// Select pixel on rendered image.
	/// </summary>
	/// <param name="x">Horizontal position of selected pixel.</param>
	/// <param name="y">Vertical position of selected pixel.</param>
	public unsafe void SelectRenderedImagePixel(int x, int y)
	{
		if (this.IsDisposed)
			return;
		var imageFrame = Global.Run(() =>
		{
			if (this.IsFilteringRenderedImageNeeded)
				return this.filteredImageFrame;
			if (this.Settings.GetValueOrDefault(SettingKeys.ColorSpaceConversionTiming) == ColorSpaceConversionTiming.BeforeApplyingFilters)
				return this.colorSpaceConvertedImageFrame ?? this.renderedImageFrame;
			return this.renderedImageFrame;
		});
		var renderedImageBuffer = imageFrame?.BitmapBuffer;
		if (renderedImageBuffer is null 
			|| x < 0 || x >= renderedImageBuffer.Width
			|| y < 0 || y >= renderedImageBuffer.Height)
		{
			if (this.HasSelectedRenderedImagePixel)
			{
				this.SetValue(HasSelectedRenderedImagePixelProperty, false);
				this.SetValue(SelectedRenderedImagePixelColorProperty, SelectedRenderedImagePixelColorProperty.DefaultValue);
				this.SetValue(SelectedRenderedImagePixelLabColorProperty, SelectedRenderedImagePixelLabColorProperty.DefaultValue);
				this.SetValue(SelectedRenderedImagePixelXyzColorProperty, SelectedRenderedImagePixelXyzColorProperty.DefaultValue);
				this.SetValue(SelectedRenderedImagePixelPositionXProperty, -1);
				this.SetValue(SelectedRenderedImagePixelPositionYProperty, -1);
			}
		}
		else
		{
			// get color of pixel
			var argbR = 0.0;
			var argbG = 0.0;
			var argbB = 0.0;
			var color = renderedImageBuffer.Memory.Pin((baseAddress) =>
			{
				var pixelPtr = (byte*)baseAddress + renderedImageBuffer.GetPixelOffset(x, y);
				return renderedImageBuffer.Format switch
				{
					BitmapFormat.Bgra32 => new Color64(pixelPtr[3], pixelPtr[2], pixelPtr[1], pixelPtr[0]).Also((ref it) =>
					{
						argbR = pixelPtr[2] / 255.0;
						argbG = pixelPtr[1] / 255.0;
						argbB = pixelPtr[0] / 255.0;
					}),
					BitmapFormat.Bgra64 => Global.Run(()=>
                    {
						var blue = (ushort)0;
						var green = (ushort)0;
						var red = (ushort)0;
						var alpha = (ushort)0;
						var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
						unpackFunc(*(ulong*)pixelPtr, &blue, &green, &red, &alpha);
						argbR = red / 65535.0;
						argbG = green / 65535.0;
						argbB = blue / 65535.0;
						return new Color64(alpha, red, green, blue);
					}),
					_ => default,
				};
			});

			// convert to Lab color
			var colorSpace = this.ColorSpace;
			var (labL, labA, labB) = colorSpace.RgbToLab(argbR, argbG, argbB);
			labL *= 100;
			labA *= 128;
			labB *= 128;

			// convert to XYZ color
			var (xyzX, xyzY, xyzZ) = colorSpace.RgbToXyz(argbR, argbG, argbB);
			xyzX *= 100;
			xyzY *= 100;
			xyzZ *= 100;

			// update state
			this.SetValue(SelectedRenderedImagePixelColorProperty, color);
			this.SetValue(SelectedRenderedImagePixelLabColorProperty, new Tuple<double, double, double>(labL, labA, labB));
			this.SetValue(SelectedRenderedImagePixelXyzColorProperty, new Tuple<double, double, double>(xyzX, xyzY, xyzZ));
			this.SetValue(SelectedRenderedImagePixelPositionXProperty, x);
			this.SetValue(SelectedRenderedImagePixelPositionYProperty, y);
			this.SetValue(HasSelectedRenderedImagePixelProperty, true);
		}
	}


	/// <summary>
	/// Get or set shadow adjustment for filter.
	/// </summary>
	public double ShadowAdjustment
	{
		get => this.GetValue(ShadowAdjustmentProperty);
		set => this.SetValue(ShadowAdjustmentProperty, value);
	}


	/// <summary>
	/// Get size of source image data in bytes.
	/// </summary>
	public long SourceDataSize => this.GetValue(SourceDataSizeProperty);


	/// <summary>
	/// Get name of source image file.
	/// </summary>
	public string? SourceFileName => this.GetValue(SourceFileNameProperty);


	/// <summary>
	/// Get description of size of source image file.
	/// </summary>
	public string? SourceSizeString => this.GetValue(SourceSizeStringProperty);


	/// <summary>
	/// Get the highest effective bits-per-channel among the source image's planes. Returns 8 when no image renderer or planes are available.
	/// </summary>
	public int SourceImageEffectiveBits => this.GetValue(SourceImageEffectiveBitsProperty);


	// Switch profile without applying parameters.
	void SwitchToProfileWithoutApplying(ImageRenderingProfile profile)
	{
		this.SetValue(ProfileProperty, profile);
		this.UpdateCanSaveDeleteProfile();
	}


	/// <summary>
	/// Get title of session.
	/// </summary>
	public string? Title { get; private set; }


	/// <summary>
	/// Get total memory usage for rendered images in bytes.
	/// </summary>
	public long TotalRenderedImagesMemoryUsage => this.GetValue(TotalRenderedImagesMemoryUsageProperty);


	/// <summary>
	/// Track event of resetting brightness, contrast, highlight and shadow adjustments.
	/// </summary>
	public void TrackBrightnessAndContrastAdjustmentResetEvent()
	{
		this.trackFilteringParamsAppliedAction.ExecuteIfScheduled();
		if (this.GetValue(IsSourceOpenedProperty))
		{
			var properties = this.PrepareUsageTrackingProperties();
			this.Application.UsageManager.TrackEvent(UsageEvents.BrightnessAndContrastAdjustmentReset, properties);
		}
	}


	/// <summary>
	/// Track event of resetting color, saturation and vibrance adjustments.
	/// </summary>
	public void TrackColorAdjustmentResetEvent()
	{
		this.trackFilteringParamsAppliedAction.ExecuteIfScheduled();
		if (this.GetValue(IsSourceOpenedProperty))
		{
			var properties = this.PrepareUsageTrackingProperties();
			this.Application.UsageManager.TrackEvent(UsageEvents.ColorAdjustmentReset, properties);
		}
	}


	// Update CanSaveOrDeleteProfile and CanSaveAsNewProfile according to current state.
	void UpdateCanSaveDeleteProfile()
	{
		if (this.IsDisposed)
			return;
		if (!this.IsSourceOpened)
		{
			this.canSaveAsNewProfile.Update(false);
			this.canSaveOrDeleteProfile.Update(false);
		}
		else
		{
			this.canSaveAsNewProfile.Update(true);
			this.canSaveOrDeleteProfile.Update(this.Profile.Type == ImageRenderingProfileType.UserDefined);
		}
	}


	// Update CanZoomIn and CanZoomOut according to current state.
	void UpdateCanZoomInOut()
	{
		if (this.IsDisposed)
			return;
		if (this.GetValue(FitImageToViewportProperty) || !this.IsSourceOpened)
		{
			this.canZoomIn.Update(false);
			this.canZoomOut.Update(false);
		}
		else
		{
			var scale = this.GetValue(RequestedImageDisplayScaleProperty);
			this.canZoomIn.Update(scale < (MaxRenderedImageScale - 0.001));
			this.canZoomOut.Update(scale > (MinRenderedImageScale + 0.001));
		}
	}


	// Update the list of demosaicing algorithms which support the current bayer pattern.
	void UpdateDemosaicingAlgorithms()
	{
		// select another algorithm before the selected one is removed from the list, otherwise the selection of the combo box is reset to null
		var bayerPattern = this.BayerPattern;
		if (!this.DemosaicingAlgorithm.IsBayerPatternSupported(bayerPattern))
			this.SetValue(DemosaicingAlgorithmProperty, SelectDefaultDemosaicingAlgorithm(bayerPattern));

		// remove the algorithms which are unsupported or unregistered
		var allAlgorithms = Media.Demosaicing.DemosaicingAlgorithms.All;
		for (var i = this.demosaicingAlgorithms.Count - 1; i >= 0; --i)
		{
			var algorithm = this.demosaicingAlgorithms[i];
			if (!algorithm.IsBayerPatternSupported(bayerPattern) || !allAlgorithms.Contains(algorithm))
				this.demosaicingAlgorithms.RemoveAt(i);
		}

		// insert the algorithms which are supported, the order of the registry is kept so that Bypass is still the first algorithm
		var index = 0;
		foreach (var algorithm in allAlgorithms)
		{
			if (!algorithm.IsBayerPatternSupported(bayerPattern))
				continue;
			if (index >= this.demosaicingAlgorithms.Count)
				this.demosaicingAlgorithms.Add(algorithm);
			else if (this.demosaicingAlgorithms[index] != algorithm)
				this.demosaicingAlgorithms.Insert(index, algorithm);
			++index;
		}
	}


	// Update HasColorTables according to the tables held by the session and whether the current renderer applies them or not.
	void UpdateHasColorTables()
	{
		if (this.IsDisposed)
			return;
		var hasColorTables = this.GetValue(ImageRendererProperty)?.IsColorTableSupported is true
			&& (this.alphaColorTable is not null
				|| this.blueColorTable is not null
				|| this.greenColorTable is not null
				|| this.redColorTable is not null);
		this.SetValue(HasColorTablesProperty, hasColorTables);
	}


	// Update IsAlphaChannelAvailable based on the current renderer's category and (for Compressed) the profile's file format.
	void UpdateIsAlphaChannelAvailable()
	{
		if (this.IsDisposed)
			return;
		var renderer = this.GetValue(ImageRendererProperty);
		var available = renderer?.Format.Category switch
		{
			ImageFormatCategory.ARGB => true,
			ImageFormatCategory.Compressed => Global.Run(() =>
			{
				var fileFormat = this.Profile.FileFormat;
				return fileFormat == FileFormats.Png
					|| fileFormat == FileFormats.WebP
					|| fileFormat == FileFormats.Heif
					|| fileFormat == FileFormats.Tiff;
			}),
			_ => false,
		};
		this.SetValue(IsAlphaChannelAvailableProperty, available);
	}


	// Update SourceImageEffectiveBits to the maximum effective bits across the current image's planes.
	void UpdateSourceImageEffectiveBits()
	{
		if (this.IsDisposed)
			return;
		var imageRenderer = this.GetValue(ImageRendererProperty);
		var format = imageRenderer?.Format;
		var maxBits = 0;
		if (format is null || format.Category == ImageFormatCategory.Compressed)
			maxBits = 8;
		else
		{
			var planeCount = format.PlaneDescriptors.Count;
			for (var i = 0; i < planeCount; ++i)
			{
				var bits = this.effectiveBits[i];
				if (bits > maxBits)
					maxBits = bits;
			}
			if (maxBits <= 0)
				maxBits = 8;
		}
		this.SetValue(SourceImageEffectiveBitsProperty, maxBits);
	}


	// Update title.
	void UpdateTitle()
	{
		// check state
		if (this.IsDisposed)
			return;

		// generate title
		var title = this.CustomTitle
		            ?? (this.SourceFileName is not null
			            ? Path.GetFileName(this.SourceFileName)
			            : this.imageDataSource switch
			            {
				            IFileSequenceImageDataSource fileSequenceImageDataSource => this.Application.GetFormattedString("Session.MultipleFiles", fileSequenceImageDataSource.FrameCount),
				            IMultiFrameImageDataSource multiFrameImageDataSource => this.Application.GetFormattedString("Session.FrameSequence", multiFrameImageDataSource.FrameCount),
				            _ => this.Application.GetString("Session.EmptyTitle")
			            });

		// update property
		if (this.Title != title)
		{
			this.Title = title;
			this.OnPropertyChanged(nameof(this.Title));
		}
	}


	/// <summary>
	/// Get or set whether <see cref="ColorSpace"/> should be treat as linear color space or not.
	/// </summary>
	/// <value></value>
	public bool UseLinearColorSpace
	{
		get => this.GetValue(UseLinearColorSpaceProperty);
		set => this.SetValue(UseLinearColorSpaceProperty, value);
	}


	/// <summary>
	/// Get or set vibrance adjustment. Range is [-1.0, 1.0].
	/// </summary>
	public double VibranceAdjustment
	{
		get => this.GetValue(VibranceAdjustmentProperty);
		set => this.SetValue(VibranceAdjustmentProperty, value);
	}
	
	
	// Wait for the completion of the image filtering which is in progress.
	Task WaitForImageFilteringCompletionAsync() =>
		this.imageFilteringCompletionSource?.Task ?? Task.CompletedTask;


	// Wait for the completion of the image rendering which is in progress.
	Task WaitForImageRenderingCompletionAsync() =>
		this.imageRenderingCompletionSource?.Task ?? Task.CompletedTask;


	/// <summary>
	/// Get or set white level of 1st image plane.
	/// </summary>
	public uint WhiteLevel1
	{
		get => this.whiteLevels[0];
		set => this.ChangeWhiteLevel(0, value);
	}


	/// <summary>
	/// Get or set white level of 2nd image plane.
	/// </summary>
	public uint WhiteLevel2
	{
		get => this.whiteLevels[1];
		set => this.ChangeWhiteLevel(1, value);
	}


	/// <summary>
	/// Get or set white level of 3rd image plane.
	/// </summary>
	public uint WhiteLevel3
	{
		get => this.whiteLevels[2];
		set => this.ChangeWhiteLevel(2, value);
	}


	// Write current parameters to given profile.
	void WriteParametersToProfile(ImageRenderingProfile profile)
	{
		profile.Renderer = this.ImageRenderer;
		profile.DataOffset = this.DataOffset;
		profile.FramePaddingSize = this.FramePaddingSize;
		profile.ByteOrdering = this.ByteOrdering;
		profile.BayerPattern = this.BayerPattern;
		profile.YuvToBgraConverter = this.YuvToBgraConverter;
		if (this.IsColorSpaceManagementEnabled)
		{
			profile.ColorSpace = this.ColorSpace;
			profile.UseLinearColorSpace = this.UseLinearColorSpace;
		}
		profile.DemosaicingAlgorithm = this.DemosaicingAlgorithm;
		profile.Width = this.ImageWidth;
		profile.Height = this.ImageHeight;
		profile.EffectiveBits = this.effectiveBits;
		profile.BlackLevels = this.blackLevels;
		profile.WhiteLevels = this.whiteLevels;
		profile.PixelStrides = this.pixelStrides;
		profile.RowStrides = this.rowStrides;
		if (this.IsRgbGainSupported)
		{
			profile.RedColorGain = this.RedColorGain;
			profile.GreenColorGain = this.GreenColorGain;
			profile.BlueColorGain = this.BlueColorGain;
		}
		profile.RedColorTable = this.redColorTable;
		profile.GreenColorTable = this.greenColorTable;
		profile.BlueColorTable = this.blueColorTable;
		profile.AlphaColorTable = this.alphaColorTable;
	}


	/// <summary>
	/// Get or set YUV to RGB converter.
	/// </summary>
	public YuvToBgraConverter YuvToBgraConverter
    {
		get => this.GetValue(YuvToBgraConverterProperty);
		set => this.SetValue(YuvToBgraConverterProperty, value);
    }


	// Zoom-in rendered image.
	void ZoomIn()
	{
		this.VerifyAccess();
		this.VerifyDisposed();
		if (!this.canZoomIn.Value)
			return;
		var scale = this.GetValue(RequestedImageDisplayScaleProperty).Let((it) =>
		{
			if (it <= 0.999)
				return (Math.Floor(it * 20) + 1) / 20;
			return (int)it + 1;
		});
		scale = this.ZoomTo(scale);
		if (double.IsFinite(scale))
			this.SetValue(RequestedImageDisplayScaleProperty, scale);
	}


	/// <summary>
	/// Command of zooming-in rendered image.
	/// </summary>
	public ICommand ZoomInCommand { get; }


	// Zoom-out rendered image.
	void ZoomOut()
	{
		this.VerifyAccess();
		this.VerifyDisposed();
		if (!this.canZoomOut.Value)
			return;
		var scale = this.GetValue(RequestedImageDisplayScaleProperty).Let((it) =>
		{
			if (it <= 1.001)
				return (Math.Ceiling(it * 20) - 1) / 20;
			return Math.Ceiling(it) - 1;
		});
		scale = this.ZoomTo(scale);
		if (double.IsFinite(scale))
			this.SetValue(RequestedImageDisplayScaleProperty, scale);
	}


	/// <summary>
	/// Command of zooming-out rendered image.
	/// </summary>
	public ICommand ZoomOutCommand { get; }


	/// <summary>
	/// Zoom rendered image to given scale.
	/// </summary>
	/// <param name="scale">Target scale. Clamped to the allowed range unless fitting to viewport.</param>
	/// <param name="animate">Whether to animate the zoom; pass <see langword="false"/> for direct manipulation (e.g. pinch).</param>
	/// <returns>The actually applied scale, or <see cref="double.NaN"/> if the request was rejected.</returns>
	public double ZoomTo(double scale, bool animate = true)
    {
		// check state
		this.VerifyAccess();
		this.VerifyDisposed();
		if (!this.GetValue(FitImageToViewportProperty) && !this.canZoomTo.Value)
			return double.NaN;
		if (!double.IsFinite(scale))
			return double.NaN;

		// check zoom
		if (!this.GetValue(FitImageToViewportProperty))
		{
			if (scale < MinRenderedImageScale)
				scale = MinRenderedImageScale;
			else if (scale > MaxRenderedImageScale)
				scale = MaxRenderedImageScale;
		}
		var initScale = this.GetValue(ImageDisplayScaleProperty);
		if (!double.IsFinite(initScale))
			animate = false;

		// cancel current zooming
		this.CompleteZooming(false);

		// start zooming
		if (animate)
		{
			this.imageScalingAnimator = new DoubleAnimator(initScale, scale).Also(it =>
			{
				it.Completed += (_, _) => 
				{
					this.SetValue(ImageDisplayScaleProperty, it.EndValue);
					this.updateImageDisplaySizeAction.Execute();
					this.CompleteZooming(true);
				};
				it.Duration = TimeSpan.FromMilliseconds(this.Application.Configuration.GetValueOrDefault(ConfigurationKeys.ZoomAnimationDuration));
				it.Interpolator = ZoomingInterpolator;
				it.ProgressChanged += (_, _) =>
				{
					this.SetValue(ImageDisplayScaleProperty, it.Value);
					this.updateImageDisplaySizeAction.Execute();
				};
				it.Start();
			});
			this.SetValue(IsZoomingProperty, true);
		}
		else
		{
			this.SetValue(ImageDisplayScaleProperty, scale);
			this.SetValue(IsZoomingProperty, false);
			this.updateImageDisplaySizeAction.Execute();
		}
		return scale;
    }


	/// <summary>
	/// Command to start smooth zooming to given scale.
	/// </summary>
	/// <remarks>Type of parameter is <see cref="double"/>.</remarks>
	public ICommand ZoomToCommand { get; }
}
