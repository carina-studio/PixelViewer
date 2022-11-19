using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageEncoders;
using Carina.PixelViewer.Media.ImageFilters;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.Platform;
using Carina.PixelViewer.Threading;
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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// A session of rendering and displaying image.
	/// </summary>
	class Session : ViewModel<IAppSuiteApplication>
	{
		// Activation token.
		class ActivationToken : IDisposable
		{
			// Fields.
			readonly Session session;

			// Constructor.
			public ActivationToken(Session session) => this.session = session;

			// Dispose.
			public void Dispose() => this.session.Deactivate(this);
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
				if (memoryUsageToken == null)
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
			public ImageRenderingResult RenderingResult { get; set; } = new ImageRenderingResult();

			// Transfer resource ownership.
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
				if (memoryUsageToken == null)
				{
					this.session.Logger.LogError("Failed to transfer image frame to {session}", session);
					return null;
				}

				// transfer
				return new ImageFrame(session, memoryUsageToken, this);
            }
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
		class RenderedImageMemoryUsageToken : IDisposable
		{
			// Fields.
			public readonly long DataSize;
			bool isDisposed;
			public readonly Session Session;

			// Constructor.
			public RenderedImageMemoryUsageToken(Session session, long dataSize)
			{
				this.DataSize = dataSize;
				this.Session = session;
			}

			// Dispose.
			public void Dispose()
			{
				if (this.isDisposed)
					return;
				this.isDisposed = true;
				this.Session.ReleaseRenderedImageMemoryUsage(this);
			}
		}


		/// <summary>
		/// Maximum scaling ratio of rendered image.
		/// </summary>
		public const double MaxRenderedImageScale = 20.0;
		/// <summary>
		/// Maximum size of panel of rendering parameters in pixels.
		/// </summary>
		public const double MaxRenderingParametersPanelSize = 400;
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
		public static readonly ObservableProperty<double> BlueColorAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(BlueColorAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="BlueColorGain"/>.
		/// </summary>
		public static readonly ObservableProperty<double> BlueColorGainProperty = ObservableProperty.Register<Session, double>(nameof(BlueColorGain), 1.0, coerce: (s, it) => ImageRenderingOptions.GetValidRgbGain(it));
		/// <summary>
		/// Property of <see cref="BrightnessAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<double> BrightnessAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(BrightnessAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="ByteOrdering"/>.
		/// </summary>
		public static readonly ObservableProperty<ByteOrdering> ByteOrderingProperty = ObservableProperty.Register<Session, ByteOrdering>(nameof(ByteOrdering), ByteOrdering.BigEndian);
		/// <summary>
		/// Property of <see cref="ColorSpace"/>.
		/// </summary>
		public static readonly ObservableProperty<ColorSpace> ColorSpaceProperty = ObservableProperty.Register<Session, ColorSpace>(nameof(ColorSpace), Media.ColorSpace.Default);
		/// <summary>
		/// Property of <see cref="ContrastAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<double> ContrastAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(ContrastAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="CustomTitle"/>.
		/// </summary>
		public static readonly ObservableProperty<string?> CustomTitleProperty = ObservableProperty.Register<Session, string?>(nameof(CustomTitle));
		/// <summary>
		/// Property of <see cref="DataOffset"/>.
		/// </summary>
		public static readonly ObservableProperty<long> DataOffsetProperty = ObservableProperty.Register<Session, long>(nameof(DataOffset), 0L);
		/// <summary>
		/// Property of <see cref="Demosaicing"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> DemosaicingProperty = ObservableProperty.Register<Session, bool>(nameof(Demosaicing), true);
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
		/// Property of <see cref="GreenColorAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<double> GreenColorAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(GreenColorAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="GreenColorGain"/>.
		/// </summary>
		public static readonly ObservableProperty<double> GreenColorGainProperty = ObservableProperty.Register<Session, double>(nameof(GreenColorGain), 1.0, coerce: (s, it) => ImageRenderingOptions.GetValidRgbGain(it));
		/// <summary>
		/// Property of <see cref="HasBrightnessAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> HasBrightnessAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasBrightnessAdjustment));
		/// <summary>
		/// Property of <see cref="HasColorAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> HasColorAdjustmentProperty = ObservableProperty.Register<Session, bool>(nameof(HasColorAdjustment));
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
		public static readonly ObservableProperty<double> HighlightAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(HighlightAdjustment), 0, validate: it => double.IsFinite(it));
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
		public static readonly ObservableProperty<int> ImageHeightProperty = ObservableProperty.Register<Session, int>(nameof(ImageHeight), 1, coerce: (S, it) => Math.Max(1, it));
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
		public static readonly ObservableProperty<int> ImageWidthProperty = ObservableProperty.Register<Session, int>(nameof(ImageWidth), 1, coerce: (s, it) => Math.Max(1, it));
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
		/// Property of <see cref="IsHistogramsVisible"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsHistogramsVisibleProperty = ObservableProperty.Register<Session, bool>(nameof(IsHistogramsVisible));
		/// <summary>
		/// Property of <see cref="IsOpeningSourceFile"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsOpeningSourceFileProperty = ObservableProperty.Register<Session, bool>(nameof(IsOpeningSourceFile));
		/// <summary>
		/// Property of <see cref="IsProcessingImage"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsProcessingImageProperty = ObservableProperty.Register<Session, bool>(nameof(IsProcessingImage));
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
		/// Property of <see cref="IsSourceFileOpened"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsSourceFileOpenedProperty = ObservableProperty.Register<Session, bool>(nameof(IsSourceFileOpened));
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
		public static readonly ObservableProperty<IBitmap?> QuarterSizeRenderedImageProperty = ObservableProperty.Register<Session, IBitmap?>(nameof(QuarterSizeRenderedImage));
		/// <summary>
		/// Property of <see cref="RedColorAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<double> RedColorAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(RedColorAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="RedColorGain"/>.
		/// </summary>
		public static readonly ObservableProperty<double> RedColorGainProperty = ObservableProperty.Register<Session, double>(nameof(RedColorGain), 1.0, coerce: (s, it) => ImageRenderingOptions.GetValidRgbGain(it));
		/// <summary>
		/// Property of <see cref="RenderedImage"/>.
		/// </summary>
		public static readonly ObservableProperty<IBitmap?> RenderedImageProperty = ObservableProperty.Register<Session, IBitmap?>(nameof(RenderedImage));
		/// <summary>
		/// Property of <see cref="RenderedImagesMemoryUsage"/>.
		/// </summary>
		public static readonly ObservableProperty<long> RenderedImagesMemoryUsageProperty = ObservableProperty.Register<Session, long>(nameof(RenderedImagesMemoryUsage));
		/// <summary>
		/// Property of <see cref="RenderingParametersPanelSize"/>.
		/// </summary>
		public static readonly ObservableProperty<double> RenderingParametersPanelSizeProperty = ObservableProperty.Register<Session, double>(nameof(RenderingParametersPanelSize), (MinRenderingParametersPanelSize + MaxRenderingParametersPanelSize) / 2, 
			coerce: (s, it) =>
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
			coerce: (s, it) =>
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
			coerce: (s, it) => 
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
			coerce: (s, it) => Math.Max(1, it),
			validate: double.IsFinite);
		/// <summary>
		/// Property of <see cref="SelectedRenderedImagePixelColor"/>.
		/// </summary>
		public static readonly ObservableProperty<Color> SelectedRenderedImagePixelColorProperty = ObservableProperty.Register<Session, Color>(nameof(SelectedRenderedImagePixelColor));
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
		public static readonly ObservableProperty<double> ShadowAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(ShadowAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="SourceDataSize"/>.
		/// </summary>
		public static readonly ObservableProperty<long> SourceDataSizeProperty = ObservableProperty.Register<Session, long>(nameof(SourceDataSize));
		/// <summary>
		/// Property of <see cref="SourceFileName"/>.
		/// </summary>
		public static readonly ObservableProperty<string?> SourceFileNameProperty = ObservableProperty.Register<Session, string?>(nameof(SourceFileName));
		/// <summary>
		/// Property of <see cref="SourceFileSizeString"/>.
		/// </summary>
		public static readonly ObservableProperty<string?> SourceFileSizeStringProperty = ObservableProperty.Register<Session, string?>(nameof(SourceFileSizeString));
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
			coerce: (s, it) => 
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
		const int ReleaseCachedImagesDelay = 3000;
		const int RenderImageDelay = 500;


		// Static fields.
		static readonly SettingKey<bool> IsInitHistogramsPanelVisible = new("Session.IsInitHistogramsPanelVisible", false);
		static readonly SettingKey<int> LatestRenderingParamsPanelSize = new("Session.LatestRenderingParamsPanelSize", (int)(RenderingParametersPanelSizeProperty.DefaultValue + 0.5));
		static readonly MutableObservableInt64 SharedRenderedImagesMemoryUsage = new();
		static readonly TimeSpan ZoomAnimationDuration = TimeSpan.FromMilliseconds(500);


		// Fields.
		readonly List<ActivationToken> activationTokens = new();
		IDisposable? avaQuarterSizeRenderedImageMemoryUsageToken;
		IDisposable? avaRenderedImageMemoryUsageToken;
		readonly uint[] blackLevels = new uint[ImageFormat.MaxPlaneCount];
		WriteableBitmap? cachedAvaQuarterSizeRenderedImage;
		IDisposable? cachedAvaQuarterSizeRenderedImageMemoryUsageToken;
		WriteableBitmap? cachedAvaRenderedImage;
		IDisposable? cachedAvaRenderedImageMemoryUsageToken;
		readonly List<ImageFrame> cachedFilteredImageFrames = new(2);
		readonly MutableObservableBoolean canApplyProfile = new();
		readonly MutableObservableBoolean canMoveToNextFrame = new();
		readonly MutableObservableBoolean canMoveToPreviousFrame = new();
		readonly MutableObservableBoolean canOpenSourceFile = new(true);
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
			if (lhs == null)
				return rhs == null ? 0 : -1;
			if (rhs == null)
				return 1;
			if (lhs.IsEmbedded)
				return rhs.IsEmbedded ? string.Compare(lhs.Name, rhs.Name) : -1;
			if (rhs.IsEmbedded)
				return 1;
			if (lhs.IsBuiltIn)
				return rhs.IsBuiltIn ? string.Compare(lhs.Name, rhs.Name) : -1;
			if (rhs.IsBuiltIn)
				return 1;
			if (lhs.IsSystemDefined)
				return rhs.IsSystemDefined ? string.Compare(lhs.Name, rhs.Name) : -1;
			if (rhs.IsSystemDefined)
				return 1;
			return string.Compare(lhs.Name, rhs.Name);
		});
		readonly int[] effectiveBits = new int[ImageFormat.MaxPlaneCount];
		readonly Observer<Media.ColorSpace> effectiveScreenColorSpaceObserver;
		IDisposable? effectiveScreenColorSpaceObserverToken;
		ImageRenderingProfile? fileFormatProfile;
		readonly ScheduledAction filterImageAction;
		ImageFrame? filteredImageFrame;
		double fitRenderedImageToViewportScale = double.NaN;
		double fitRenderedImageToViewportScaleSwapped = double.NaN;
		bool hasPendingImageFiltering;
		bool hasPendingImageRendering;
		IImageDataSource? imageDataSource;
		CancellationTokenSource? imageFilteringCancellationTokenSource;
		CancellationTokenSource? imageRenderingCancellationTokenSource;
		DoubleAnimator? imageScalingAnimator;
		bool isFirstImageRenderingForSource = true;
		bool isImageDimensionsEvaluationNeeded = true;
		bool isImagePlaneOptionsResetNeeded = true;
		readonly int[] pixelStrides = new int[ImageFormat.MaxPlaneCount];
		readonly SortedObservableList<ImageRenderingProfile> profiles = new(CompareProfiles);
		readonly ScheduledAction releasedCachedImagesAction;
		ImageFrame? renderedImageFrame;
		readonly ScheduledAction renderImageAction;
		readonly int[] rowStrides = new int[ImageFormat.MaxPlaneCount];
		readonly IDisposable sharedRenderedImagesMemoryUsageObserverToken;
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
			var isSrcFileOpenedObservable = this.GetValueAsObservable(IsSourceFileOpenedProperty);
			this.ApplyProfileCommand = new Command(this.ApplyProfile, this.canApplyProfile);
			this.CloseSourceFileCommand = new Command(() => this.CloseSourceFile(false), isSrcFileOpenedObservable);
			this.DeleteProfileCommand = new Command(this.DeleteProfile, this.canSaveOrDeleteProfile);
			this.EvaluateImageDimensionsCommand = new Command<AspectRatio>(this.EvaluateImageDimensions, isSrcFileOpenedObservable);
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
			this.OpenSourceFileCommand = new Command<string>(filePath => _ = this.OpenSourceFile(filePath), this.canOpenSourceFile);
			this.RenderImageCommand = new Command(() => 
			{
				this.ClearRenderedImage();
				this.renderImageAction?.Reschedule();
			}, this.GetValueAsObservable(IsSourceFileOpenedProperty));
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
			this.SaveAsNewProfileCommand = new Command<string>(name => this.SaveAsNewProfile(name), this.canSaveAsNewProfile);
			this.SaveFilteredImageCommand = new Command<ImageSavingParams>(parameters => _ = this.SaveFilteredImage(parameters), this.canSaveFilteredImage);
			this.SaveProfileCommand = new Command(() => this.SaveProfile(), this.canSaveOrDeleteProfile);
			this.SaveRenderedImageCommand = new Command<ImageSavingParams>(parameters => _ = this.SaveRenderedImage(parameters), this.canSaveRenderedImage);
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
			this.filterImageAction = new ScheduledAction(() =>
			{
				if (this.colorSpaceConvertedImageFrame != null)
					this.FilterImage(this.colorSpaceConvertedImageFrame);
				else if (this.renderedImageFrame != null)
					this.FilterImage(this.renderedImageFrame);
			});
			this.releasedCachedImagesAction = new ScheduledAction(() => this.ReleaseCachedImages());
			this.renderImageAction = new ScheduledAction(this.RenderImage);
			this.updateFilterSupportingAction = new ScheduledAction(() =>
			{
				if (this.IsDisposed)
					return;
				if (!this.IsSourceFileOpened)
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
				if (image == null)
				{
					this.ResetValue(ImageDisplaySizeProperty);
					return;
				}
				var screenPixelDensity = this.GetValue(ScreenPixelDensityProperty);
				var imageWidth = image.Size.Width / screenPixelDensity;
				var imageHeight = image.Size.Height / screenPixelDensity;

				// calculate display size
				var scale = 1.0;
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
					|| this.IsOpeningSourceFile
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
			this.colorSpaces.AddAll(Media.ColorSpace.AllColorSpaces);
			(Media.ColorSpace.AllColorSpaces as INotifyCollectionChanged)?.Let(it =>
				it.CollectionChanged += this.OnAllColorSpacesChanged);

			// select default YUV to RGB converter
			if (YuvToBgraConverter.TryGetByName(this.Settings.GetValueOrDefault(SettingKeys.DefaultYuvToBgraConversion), out var converter))
				this.SetValue(YuvToBgraConverterProperty, converter);

			// setup color space management
			this.SetValue(IsColorSpaceManagementEnabledProperty, this.Settings.GetValueOrDefault(SettingKeys.EnableColorSpaceManagement));
			if (Media.ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName), out var colorSpace))
				this.SetValue(ColorSpaceProperty, colorSpace);

			// setup title
			this.UpdateTitle();

			// restore state
			if (savedState.HasValue)
				this.RestoreState(savedState.Value);
			else
			{
				this.SetValue(IsHistogramsVisibleProperty, this.PersistentState.GetValueOrDefault(IsInitHistogramsPanelVisible));
				this.SetValue(RenderingParametersPanelSizeProperty, this.PersistentState.GetValueOrDefault(LatestRenderingParamsPanelSize));
			}

			// add event handlers
			Media.ColorSpace.RemovingUserDefinedColorSpace += this.OnRemovingUserDefinedColorSpace;
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
						if (this.filteredImageFrame != null)
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
						if (this.renderedImageFrame != null)
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
				this.SetValue(ImageRendererProperty, profile.Renderer ?? this.SelectDefaultImageRenderer());

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
				var colorSpace = Media.ColorSpace.Default;
				if (profile.Type != ImageRenderingProfileType.UserDefined && this.IsYuvToBgraConverterSupported)
					colorSpace = this.YuvToBgraConverter.ColorSpace;
				else
				{
					colorSpace = profile.ColorSpace;
					if (colorSpace.IsEmbedded)
					{
						if (Media.ColorSpace.TryGetBuiltInColorSpace(colorSpace, out var builtInColorSpace))
							colorSpace = builtInColorSpace;
						else
							this.colorSpaces.Add(colorSpace);
					}
				}
				this.SetValue(ColorSpaceProperty, colorSpace);
				this.SetValue(UseLinearColorSpaceProperty, profile.UseLinearColorSpace);

				// demosaicing
				this.SetValue(DemosaicingProperty, profile.Demosaicing);

				// dimensions
				this.SetValue(ImageWidthProperty, profile.Width);
				this.SetValue(ImageHeightProperty, profile.Height);

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
					this.ChangeEffectiveBits(i, profile.EffectiveBits[i]);
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

				// rotation
				if (profile.IsFileFormat)
				{
					var rotation = profile.Orientation;
					if (rotation < 0)
						rotation += 360;
					else if (rotation > 360)
						rotation -= 360;
					rotation = (int)(rotation / 90.0 + 0.5) * 90;
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

				// update state
				if (this.renderImageAction.IsScheduled)
				{
					this.isImageDimensionsEvaluationNeeded = false;
					this.isImagePlaneOptionsResetNeeded = false;
				}
			}
		}


		/// <summary>
		/// Command to apply parameters defined by current <see cref="Profile"/>.
		/// </summary>
		public ICommand ApplyProfileCommand { get; }


		/// <summary>
		/// Check whether black/white levels for 1st image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool AreAdjustableBlackWhiteLevels1 { get => this.GetValue(AreAdjustableBlackWhiteLevels1Property); }


		/// <summary>
		/// Check whether black/white levels for 2nd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool AreAdjustableBlackWhiteLevels2 { get => this.GetValue(AreAdjustableBlackWhiteLevels2Property); }


		/// <summary>
		/// Check whether black/white levels for 3rd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool AreAdjustableBlackWhiteLevels3 { get => this.GetValue(AreAdjustableBlackWhiteLevels3Property); }


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


		// Cancel filtering image.
		bool CancelFilteringImage(bool cancelPendingRendering = false)
		{
			// cancel
			this.filterImageAction.Cancel();
			if (this.imageFilteringCancellationTokenSource == null)
				return false;
			this.Logger.LogWarning("Cancel filtering image for source '{sourceFileName}'", this.SourceFileName);
			this.imageFilteringCancellationTokenSource.Cancel();
			this.imageFilteringCancellationTokenSource = null;

			// update state
			if (!this.IsDisposed)
				this.SetValue(IsFilteringRenderedImageProperty, false);
			if (cancelPendingRendering)
				this.hasPendingImageFiltering = false;

			// complete
			return true;
		}


		// Cancel rendering image.
		bool CancelRenderingImage(bool cancelPendingRendering = false)
		{
			// cancel
			this.renderImageAction.Cancel();
			if (this.imageRenderingCancellationTokenSource == null)
				return false;
			this.Logger.LogWarning("Cancel rendering image for source '{sourceFileName}'", this.SourceFileName);
			this.imageRenderingCancellationTokenSource.Cancel();
			this.imageRenderingCancellationTokenSource = null;

			// update state
			if (!this.IsDisposed)
				this.SetValue(IsRenderingImageProperty, false);
			if (cancelPendingRendering)
				this.hasPendingImageRendering = false;

			// complete
			return true;
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


		// Change effective bits of given image plane.
		void ChangeEffectiveBits(int index, int effectiveBits)
		{
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();
			if (this.effectiveBits[index] == effectiveBits)
				return;
			
			// update effective bits
			this.effectiveBits[index] = effectiveBits;
			this.OnEffectiveBitsChanged(index);
			this.renderImageAction.Reschedule(RenderImageDelay);

			// update black/white levels
			if (effectiveBits > 0)
			{
				var imageFormat = this.GetValue(ImageRendererProperty)?.Format;
				if (imageFormat != null)
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


		// Clear filtered image.
		bool ClearFilteredImage()
		{
			// cancel filtering
			this.CancelFilteringImage(true);

			// clear images
			if (!this.IsFilteringRenderedImage && this.filteredImageFrame != null)
			{
				this.SetValue(HistogramsProperty, null);
				this.SetValue(QuarterSizeRenderedImageProperty, null);
				this.SetValue(RenderedImageProperty, null);
				foreach (var cachedFrame in this.cachedFilteredImageFrames)
					cachedFrame.Dispose();
				this.cachedFilteredImageFrames.Clear();
				this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
			}
			return true;
		}


		// Clear rendered image.
		bool ClearRenderedImage()
		{
			// clear filtered image
			this.ClearFilteredImage();

			// cancel rendering
			this.CancelRenderingImage(true);

			// clear images
			if (!this.IsRenderingImage && this.renderedImageFrame != null)
			{
				this.SetValue(HistogramsProperty, null);
				this.SetValue(QuarterSizeRenderedImageProperty, null);
				this.SetValue(RenderedImageProperty, null);
				this.canSelectColorAdjustment.Update(false);
				this.canSelectRgbGain.Update(false);
				this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
				this.colorSpaceConvertedImageFrame = this.colorSpaceConvertedImageFrame.DisposeAndReturnNull();
			}
			return true;
		}


		// Close and clear current source file.
		void ClearSourceFile()
		{
			// close source file
			this.CloseSourceFile(false);

			// update state
			this.SetValue(SourceFileNameProperty, null);
			this.SetValue(SourceFileSizeStringProperty, null);

			// update title
			this.UpdateTitle();
		}


		// Close current source file.
		void CloseSourceFile(bool disposing)
		{
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
				this.SetValue(IsSourceFileOpenedProperty, false);
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
			if (!disposing)
			{
				this.ResetValue(ImageDisplayRotationProperty);
				this.ResetValue(HasRenderingErrorProperty);
				this.ResetValue(InsufficientMemoryForRenderedImageProperty);
			}

			// release cached images
			this.ReleaseCachedImages();

			// release memory usage tokens
			this.avaQuarterSizeRenderedImageMemoryUsageToken = this.avaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
			this.avaRenderedImageMemoryUsageToken = this.avaRenderedImageMemoryUsageToken.DisposeAndReturnNull();

			// update zooming state
			this.canZoomTo.Update(false);
			this.UpdateCanZoomInOut();

			// cancel rendering image
			this.CancelFilteringImage(true);
			this.CancelRenderingImage(true);

			// remove profile generated for file format
			if (this.fileFormatProfile != null)
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
			this.imageDataSource = null;
			if (imageDataSource != null)
			{
				_ = Task.Run(() =>
				{
					this.Logger.LogDebug("Dispose source for '{sourceFileName}'", sourceFileName);
					imageDataSource?.Dispose();
				});
			}
		}


		/// <summary>
		/// Command for closing opened source file.
		/// </summary>
		public ICommand CloseSourceFileCommand { get; }


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
			if (x == null)
				return y == null ? 0 : -1;
			if (y == null)
				return 1;
			var result = x.Type.CompareTo(y.Type);
			if (result != 0)
				return result;
			result = x.Name.CompareTo(y.Name);
			return result != 0 ? result : x.GetHashCode() - y.GetHashCode();
		}


		// Complete current smooth zooming.
		void CompleteZooming(bool resetIsZooming)
		{
			if (this.imageScalingAnimator == null)
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

			// hibernate directly
			if (!this.HasRenderedImage)
			{
				this.Logger.LogWarning("No rendered image before deactivation, hibernate the session");
				this.Hibernate();
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


		/// <summary>
		/// Get or set whether demosaicing is needed to be performed or not.
		/// </summary>
		public bool Demosaicing
		{
			get => this.GetValue(DemosaicingProperty);
			set => this.SetValue(DemosaicingProperty, value);
		}


		// Dispose.
		protected override void Dispose(bool disposing)
		{
			// close source file
			if (disposing)
				this.CloseSourceFile(true);

			// detach from profiles
			((INotifyCollectionChanged)ImageRenderingProfiles.UserDefinedProfiles).CollectionChanged -= this.OnUserDefinedProfilesChanged;
			foreach (var profile in this.profiles)
				profile.PropertyChanged -= this.OnProfilePropertyChanged;
			
			// detach from color spaces
			(Media.ColorSpace.AllColorSpaces as INotifyCollectionChanged)?.Let(it =>
				it.CollectionChanged -= this.OnAllColorSpacesChanged);

			// detach from shared rendered images memory usage
			this.sharedRenderedImagesMemoryUsageObserverToken.Dispose();

			// remove event handlers
			if (!disposing)
				Media.ColorSpace.RemovingUserDefinedColorSpace -= this.OnRemovingUserDefinedColorSpace;

			// call super
			base.Dispose(disposing);
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
			// check state
			if (this.imageDataSource == null)
				return;

			// evaluate
			this.ImageRenderer.EvaluateDimensions(this.imageDataSource, aspectRatio)?.Also((ref PixelSize it) =>
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
		async void FilterImage(ImageFrame renderedImageFrame)
		{
			// check state
			if (!this.IsFilteringRenderedImageNeeded)
				return;

			// cancel current filtering
			if (this.CancelFilteringImage(true))
			{
				this.Logger.LogWarning("Continue filtering when current filtering completed");
				hasPendingImageFiltering = true;
				return;
			}
			this.hasPendingImageFiltering = false;

			// log
			if (this.Application.IsDebugMode)
				this.Logger.LogTrace("Start filtering image");

			// prepare
			CancellationTokenSource cancellationTokenSource = new();
			this.imageFilteringCancellationTokenSource = cancellationTokenSource;
			this.canSaveFilteredImage.Update(false);
			this.SetValue(IsFilteringRenderedImageProperty, true);

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
			var filteredImageFrame1 = (ImageFrame?)null;
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
			if (filteredImageFrame1 == null)
			{
				if (!cancellationTokenSource.IsCancellationRequested)
				{
					this.imageFilteringCancellationTokenSource = null;
					this.SetValue(InsufficientMemoryForRenderedImageProperty, this.IsActivated);
					Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
					this.SetValue(IsFilteringRenderedImageProperty, false);
					if (!this.IsActivated && !this.IsHibernated)
					{
						this.Logger.LogWarning("Ubable to allocate filtered image frame after deactivation, hibernate the session");
						this.Hibernate();
					}
				}
				else if (this.hasPendingImageFiltering)
				{
					this.Logger.LogWarning("Filtering image has been cancelled, start next filtering");
					this.filterImageAction.Schedule();
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
				if (filteredImageFrame2 == null)
				{
					if (!cancellationTokenSource.IsCancellationRequested)
					{
						this.imageFilteringCancellationTokenSource = null;
						this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
						Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
						this.SetValue(IsFilteringRenderedImageProperty, false);
					}
					else if (this.hasPendingImageFiltering)
					{
						this.Logger.LogWarning("Filtering image has been cancelled, start next filtering");
						this.filterImageAction.Schedule();
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
					if (stopwatch != null)
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
					if (stopwatch != null)
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
					if (stopwatch != null)
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
					if (stopwatch != null)
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
					if (stopwatch != null)
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
					if (stopwatch != null)
						this.Logger.LogTrace("Take {ms} ms to apply grayscale filter", stopwatch.ElapsedMilliseconds);
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

			// check filtering result
			if (failedToApply)
			{
				this.cachedFilteredImageFrames.Add(filteredImageFrame1);
				if (filteredImageFrame2 != null)
					this.cachedFilteredImageFrames.Add(filteredImageFrame2);
				if (!cancellationTokenSource.IsCancellationRequested)
				{
					this.imageFilteringCancellationTokenSource = null;
					this.SetValue(HasRenderingErrorProperty, true);
					Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
					this.SetValue(IsFilteringRenderedImageProperty, false);
				}
				else if (this.hasPendingImageFiltering)
				{
					this.Logger.LogDebug("Filtering has been cancelled, start next filtering");
					this.filterImageAction.Schedule();
				}
				else if (this.Application.IsDebugMode)
					this.Logger.LogWarning("Filtering has been cancelled");
				return;
			}

			// generate histograms
			try
			{
				sourceImageFrame.AsNonNull().Histograms = await BitmapHistograms.CreateAsync(sourceImageFrame.AsNonNull().BitmapBuffer, cancellationTokenSource.Token);
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
				if (filteredImageFrame2 != null)
					this.cachedFilteredImageFrames.Add(filteredImageFrame2);
				if (this.hasPendingImageFiltering)
				{
					this.Logger.LogDebug("Start next filtering");
					this.filterImageAction.Schedule();
				}
				else if (this.Application.IsDebugMode)
					this.Logger.LogWarning("Filtering cancelled");
				return;
			}

			// log
			if (this.Application.IsDebugMode)
				this.Logger.LogTrace("Complete filtering image");

			// complete
			this.imageFilteringCancellationTokenSource = null;
			if (this.filteredImageFrame != null)
				this.cachedFilteredImageFrames.Add(this.filteredImageFrame);
			if (sourceImageFrame == filteredImageFrame1)
			{
				this.filteredImageFrame = filteredImageFrame1;
				if (filteredImageFrame2 != null)
					this.cachedFilteredImageFrames.Add(filteredImageFrame2);
			}
			else
			{
				this.filteredImageFrame = filteredImageFrame2;
				if (filteredImageFrame1 != null)
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
			this.SetValue(IsFilteringRenderedImageProperty, false);
		}


		/// <summary>
		/// Get or set whether rendered image should be fitted into viewport or not.
		/// </summary>
		public bool FitImageToViewport
		{
			get => this.GetValue(FitImageToViewportProperty);
			set => this.SetValue(FitImageToViewportProperty, value);
		}


		/// <summary>
		/// Get number of frames in source file.
		/// </summary>
		public long FrameCount { get => this.GetValue(FrameCountProperty); }


		/// <summary>
		/// Get of set index of frame to render.
		/// </summary>
		public long FrameNumber
		{
			get => this.GetValue(FrameNumberProperty);
			set => this.SetValue(FrameNumberProperty, value);
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
		/// Generate proper name for new profile according to current parameters.
		/// </summary>
		/// <returns>Name for new profile.</returns>
		public string GenerateNameForNewProfile()
		{
			var name = $"{this.ImageWidth}x{this.ImageHeight} [{this.ImageRenderer.Format.Name}]";
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
		public bool HasBrightnessAdjustment { get => this.GetValue(HasBrightnessAdjustmentProperty); }


		/// <summary>
		/// Check whether at least one of <see cref="RedColorAdjustment"/>, <see cref="GreenColorAdjustment"/>, <see cref="BlueColorAdjustment"/> is non-zero or not.
		/// </summary>
		public bool HasColorAdjustment { get => this.GetValue(HasColorAdjustmentProperty); }


		/// <summary>
		/// Check whether <see cref="ContrastAdjustment"/> is non-zero or not.
		/// </summary>
		public bool HasContrastAdjustment { get => this.GetValue(HasContrastAdjustmentProperty); }


		/// <summary>
		/// Check whether <see cref="HighlightAdjustment"/> is non-zero or not.
		/// </summary>
		public bool HasHighlightAdjustment { get => this.GetValue(HasHighlightAdjustmentProperty); }


		/// <summary>
		/// Check whether <see cref="Histograms"/> is valid or not.
		/// </summary>
		public bool HasHistograms { get => this.GetValue(HasHistogramsProperty); }


		/// <summary>
		/// Check whether 1st image plane exists or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool HasImagePlane1 { get => this.GetValue(HasImagePlane1Property); }


		/// <summary>
		/// Check whether 2nd image plane exists or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool HasImagePlane2 { get => this.GetValue(HasImagePlane2Property); }


		/// <summary>
		/// Check whether 3rd image plane exists or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool HasImagePlane3 { get => this.GetValue(HasImagePlane3Property); }


		/// <summary>
		/// Check whether multiple byte orderings are supported by the format of current <see cref="ImageRenderer"/> or not.
		/// </summary>
		public bool HasMultipleByteOrderings { get => this.GetValue(HasMultipleByteOrderingsProperty); }


		/// <summary>
		/// Check whether multiple frames are contained in source file or not.
		/// </summary>
		public bool HasMultipleFrames { get => this.GetValue(HasMultipleFramesProperty); }


		/// <summary>
		/// Check whether <see cref="QuarterSizeRenderedImage"/> is non-null or not.
		/// </summary>
		public bool HasQuarterSizeRenderedImage { get => this.GetValue(HasQuarterSizeRenderedImageProperty); }


		/// <summary>
		/// Check whether <see cref="RenderedImage"/> is non-null or not.
		/// </summary>
		public bool HasRenderedImage { get => this.GetValue(HasRenderedImageProperty); }


		/// <summary>
		/// Check whether error was occurred when rendering or not.
		/// </summary>
		public bool HasRenderingError { get => this.GetValue(HasRenderingErrorProperty); }


		/// <summary>
		/// Check whether RGB gain is not 1.0 or not.
		/// </summary>
		public bool HasRgbGain { get => this.GetValue(HasRgbGainProperty); }


		/// <summary>
		/// Check whether <see cref="SaturationAdjustment"/> is non-zero or not.
		/// </summary>
		public bool HasSaturationAdjustment { get => this.GetValue(HasSaturationAdjustmentProperty); }


		/// <summary>
		/// Check whether there is a pixel selected on rendered image or not.
		/// </summary>
		public bool HasSelectedRenderedImagePixel { get => this.GetValue(HasSelectedRenderedImagePixelProperty); }


		/// <summary>
		/// Check whether <see cref="ShadowAdjustment"/> is non-zero or not.
		/// </summary>
		public bool HasShadowAdjustment { get => this.GetValue(HasShadowAdjustmentProperty); }


		/// <summary>
		/// Check whether <see cref="SourceDataSize"/> is non-zero or not.
		/// </summary>
		public bool HasSourceDataSize { get => this.GetValue(HasSourceDataSizeProperty); }


		/// <summary>
		/// Check whether <see cref="VibranceAdjustment"/> is non-zero or not.
		/// </summary>
		public bool HasVibranceAdjustment { get => this.GetValue(HasVibranceAdjustmentProperty); }


		// Hibernate.
		bool Hibernate()
        {
			// check state
			if (this.IsDisposed || this.IsActivated)
				return false;
			if (this.IsHibernated)
				return true;

			this.Logger.LogWarning("Hibernate");

			// update state
			this.SetValue(IsHibernatedProperty, true);

			// clear images
			this.ClearRenderedImage();

			// complete
			return true;
        }


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
			if (sessionToClearRenderedImage != null)
			{
				this.Logger.LogWarning("Hibernate {sessionToClearRenderedImage}", sessionToClearRenderedImage);
				if (sessionToClearRenderedImage.Hibernate())
				{
					await Task.Delay(1000);
					return true;
				}
				this.Logger.LogError("Failed to hibernate {sessionToClearRenderedImage}", sessionToClearRenderedImage);
				return false;
			}
			this.Logger.LogWarning("No deactivated session to hibernate");
			return false;
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
		public BitmapHistograms? Histograms { get => this.GetValue(HistogramsProperty); }


		/// <summary>
		/// Get rotation for displaying rendered image.
		/// </summary>
		public double ImageDisplayRotation { get => this.GetValue(ImageDisplayRotationProperty); }


		/// <summary>
		/// Get proper scale for displaying rendered image.
		/// </summary>
		public double ImageDisplayScale { get => this.GetValue(ImageDisplayScaleProperty); }


		/// <summary>
		/// Get proper size for displaying rendered image.
		/// </summary>
		public Size ImageDisplaySize { get => this.GetValue(ImageDisplaySizeProperty); }


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
		public int ImagePlaneCount { get => this.GetValue(ImagePlaneCountProperty); }


		/// <summary>
		/// Get or set <see cref="IImageRenderer"/> for rendering image from current source file.
		/// </summary>
		public IImageRenderer ImageRenderer
		{
			get => this.GetValue(ImageRendererProperty).AsNonNull();
			set => this.SetValue(ImageRendererProperty, value.AsNonNull());
		}


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
		public bool InsufficientMemoryForRenderedImage { get => this.GetValue(InsufficientMemoryForRenderedImageProperty); }


		/// <summary>
		/// Check whether session is activated or not.
		/// </summary>
		public bool IsActivated { get => this.GetValue(IsActivatedProperty); }


		/// <summary>
		/// Check whether effective bits for 1st image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustableEffectiveBits1 { get => this.GetValue(IsAdjustableEffectiveBits1Property); }


		/// <summary>
		/// Check whether effective bits for 2nd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustableEffectiveBits2 { get => this.GetValue(IsAdjustableEffectiveBits2Property); }


		/// <summary>
		/// Check whether effective bits for 3rd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustableEffectiveBits3 { get => this.GetValue(IsAdjustableEffectiveBits3Property); }


		/// <summary>
		/// Check whether pixel stride for 1st image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustablePixelStride1 { get => this.GetValue(IsAdjustablePixelStride1Property); }


		/// <summary>
		/// Check whether pixel stride for 2nd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustablePixelStride2 { get => this.GetValue(IsAdjustablePixelStride2Property); }


		/// <summary>
		/// Check whether pixel stride for 3rd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustablePixelStride3 { get => this.GetValue(IsAdjustablePixelStride3Property); }


		/// <summary>
		/// Check whether <see cref="BayerPattern"/> is supported by current <see cref="ImageRenderer"/> or not.
		/// </summary>
		public bool IsBayerPatternSupported { get => this.GetValue(IsBayerPatternSupportedProperty); }


		/// <summary>
		/// Check whether brightness adjustment is supported or not.
		/// </summary>
		public bool IsBrightnessAdjustmentSupported { get => this.GetValue(IsBrightnessAdjustmentSupportedProperty); }


		/// <summary>
		/// Check whether color adjustment is supported or not.
		/// </summary>
		public bool IsColorAdjustmentSupported { get => this.GetValue(IsColorAdjustmentSupportedProperty); }


		/// <summary>
		/// Check whether color space management is enabled or not.
		/// </summary>
		public bool IsColorSpaceManagementEnabled { get => this.GetValue(IsColorSpaceManagementEnabledProperty); }


		/// <summary>
		/// Check whether image format supported by current <see cref="ImageRenderer"/> is a compressed format or not.
		/// </summary>
		public bool IsCompressedImageFormat { get => this.GetValue(IsCompressedImageFormatProperty); }


		/// <summary>
		/// Check whether contrast adjustment is supported or not.
		/// </summary>
		public bool IsContrastAdjustmentSupported { get => this.GetValue(IsContrastAdjustmentSupportedProperty); }


		/// <summary>
		/// Check whether color space of rendered image is being converted or not.
		/// </summary>
		public bool IsConvertingColorSpace { get => this.GetValue(IsConvertingColorSpaceProperty); }


		/// <summary>
		/// Check whether demosaicing is supported by current <see cref="ImageRenderer"/> or not.
		/// </summary>
		public bool IsDemosaicingSupported { get => this.GetValue(IsDemosaicingSupportedProperty); }


		/// <summary>
		/// Check whether rendered image is being filtered or not.
		/// </summary>
		public bool IsFilteringRenderedImage { get => this.GetValue(IsFilteringRenderedImageProperty); }


		/// <summary>
		/// Check whether rendered image is needed to be filtered or not.
		/// </summary>
		public bool IsFilteringRenderedImageNeeded { get => this.GetValue(IsFilteringRenderedImageNeededProperty); }


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
		public bool IsGrayscaleFilterSupported { get => this.GetValue(IsGrayscaleFilterSupportedProperty); }


		/// <summary>
		/// Check whether instance is hibernated or not.
		/// </summary>
		public bool IsHibernated { get => this.GetValue(IsHibernatedProperty); }


		/// <summary>
		/// Check whether highlight adjustment is supported or not.
		/// </summary>
		public bool IsHighlightAdjustmentSupported { get => this.GetValue(IsHighlightAdjustmentSupportedProperty); }


		/// <summary>
		/// Get or set whether histograms of image is visible or not
		/// </summary>
		public bool IsHistogramsVisible
		{
			get => this.GetValue(IsHistogramsVisibleProperty);
			set => this.SetValue(IsHistogramsVisibleProperty, value);
		}


		/// <summary>
		/// Check whether source file is being opened or not.
		/// </summary>
		public bool IsOpeningSourceFile { get => this.GetValue(IsOpeningSourceFileProperty); }


		/// <summary>
		/// Check whether image is being processed or not.
		/// </summary>
		public bool IsProcessingImage { get => this.GetValue(IsProcessingImageProperty); }


		/// <summary>
		/// Check whether image is being rendered or not.
		/// </summary>
		public bool IsRenderingImage { get => this.GetValue(IsRenderingImageProperty); }


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
		public bool IsRgbGainSupported { get => this.GetValue(IsRgbGainSupportedProperty); }


		/// <summary>
		/// Check whether saturation adjustment is supported or not.
		/// </summary>
		public bool IsSaturationAdjustmentSupported { get => this.GetValue(IsSaturationAdjustmentSupportedProperty); }


		/// <summary>
		/// Check whether filtered image is being saved or not.
		/// </summary>
		public bool IsSavingFilteredImage { get => this.GetValue(IsSavingFilteredImageProperty); }


		/// <summary>
		/// Check whether at least one image is being saved or not.
		/// </summary>
		public bool IsSavingImage { get => this.GetValue(IsSavingImageProperty); }


		/// <summary>
		/// Check whether rendered image is being saved or not.
		/// </summary>
		public bool IsSavingRenderedImage { get => this.GetValue(IsSavingRenderedImageProperty); }


		/// <summary>
		/// Check whether shadow adjustment is supported or not.
		/// </summary>
		public bool IsShadowAdjustmentSupported { get => this.GetValue(IsShadowAdjustmentSupportedProperty); }


		/// <summary>
		/// Check whether source image file has been opened or not.
		/// </summary>
		public bool IsSourceFileOpened { get => this.GetValue(IsSourceFileOpenedProperty); }


		/// <summary>
		/// Check whether vibrance adjustment is supported or not.
		/// </summary>
		public bool IsVibranceAdjustmentSupported { get => this.GetValue(IsVibranceAdjustmentSupportedProperty); }


		/// <summary>
		/// Check whether <see cref="YuvToBgraConverter"/> is supported by current <see cref="ImageRenderer"/> or not.
		/// </summary>
		public bool IsYuvToBgraConverterSupported { get => this.GetValue(IsYuvToBgraConverterSupportedProperty); }


		/// <summary>
		/// Check whether smooth zooming is on-going or not.
		/// </summary>
		public bool IsZooming { get => this.GetValue(IsZoomingProperty); }


		/// <summary>
		/// Get <see cref="Geometry"/> of luminance histogram.
		/// </summary>
		public Geometry? LuminanceHistogramGeometry { get => this.GetValue(LuminanceHistogramGeometryProperty); }


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


		// Raise PropertyChanged event for effectie bits.
		void OnEffectiveBitsChanged(int index) => this.OnPropertyChanged(index switch
		{
			0 => nameof(this.EffectiveBits1),
			1 => nameof(this.EffectiveBits2),
			2 => nameof(this.EffectiveBits3),
			_ => throw new ArgumentOutOfRangeException(nameof(index)),
		});


		// Called when state of loading profiles has been changed.
		void OnLoadingProfilesStateChanged()
		{
			this.UpdateCanSaveDeleteProfile();
		}


		// Called when owner changed.
		protected override void OnOwnerChanged(ViewModel? prevOwner, ViewModel? newOwner)
		{
			base.OnOwnerChanged(prevOwner, newOwner);
			this.effectiveScreenColorSpaceObserverToken = this.effectiveScreenColorSpaceObserverToken.DisposeAndReturnNull();
			this.effectiveScreenColorSpaceObserverToken = (newOwner as Workspace)?.GetValueAsObservable(Workspace.EffectiveScreenColorSpaceProperty)?.Subscribe(this.effectiveScreenColorSpaceObserver);
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
			}
			else if (property == ContrastAdjustmentProperty)
			{
				this.SetValue(HasContrastAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
				this.canResetContrastAdjustment.Update(this.HasContrastAdjustment && this.IsContrastAdjustmentSupported);
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Schedule(RenderImageDelay);
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
			else if (property == DemosaicingProperty)
			{
				if (this.IsDemosaicingSupported)
					this.renderImageAction.Reschedule();
			}
			else if (property == FitImageToViewportProperty)
			{
				var fitToViewport = (bool)newValue.AsNonNull();
				this.canZoomTo.Update(!fitToViewport && this.IsSourceFileOpened);
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
				this.SetValue(HasMultipleFramesProperty, (long)newValue.AsNonNull() > 1);
			else if (property == HighlightAdjustmentProperty)
			{
				this.SetValue(HasHighlightAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
				this.canResetHighlightAdjustment.Update(this.HasHighlightAdjustment && this.IsHighlightAdjustmentSupported);
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Schedule(RenderImageDelay);
			}
			else if (property == HistogramsProperty)
				this.SetValue(HasHistogramsProperty, newValue != null);
			else if (property == ImageRendererProperty)
			{
				if (ImageRenderers.All.Contains(newValue))
				{
					if (this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterChangingRenderer))
						this.isImageDimensionsEvaluationNeeded = true;
					var imageRenderer = (IImageRenderer)newValue.AsNonNull();
					var imageFormatCategory = imageRenderer.Format.Category;
					var isBayerPatternFormat = imageFormatCategory == ImageFormatCategory.Bayer;
					this.SetValue(HasMultipleByteOrderingsProperty, imageRenderer.Format.HasMultipleByteOrderings);
					this.SetValue(IsBayerPatternSupportedProperty, isBayerPatternFormat);
					this.SetValue(IsCompressedImageFormatProperty, imageFormatCategory == ImageFormatCategory.Compressed);
					this.SetValue(IsDemosaicingSupportedProperty, isBayerPatternFormat);
					this.SetValue(IsRgbGainSupportedProperty, isBayerPatternFormat);
					this.SetValue(IsYuvToBgraConverterSupportedProperty, imageFormatCategory == ImageFormatCategory.YUV);
					this.isImagePlaneOptionsResetNeeded = true;
					this.updateFilterSupportingAction.Reschedule();
					this.renderImageAction.Reschedule();
				}
				else
					this.Logger.LogError("{newValue} is not part of available image renderer list", newValue);
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
					this.ClearRenderedImage();
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
					using var cancellationTokenSource = new CancellationTokenSource();
					this.CancelFilteringImage();
					_ = this.ReportRenderedImageAsync(cancellationTokenSource);
					this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
				}
			}
			else if (property == IsFilteringRenderedImageProperty
				|| property == IsOpeningSourceFileProperty
				|| property == IsRenderingImageProperty)
			{
				this.updateIsProcessingImageAction.Schedule();
			}
			else if (property == IsGrayscaleFilterEnabledProperty
				|| property == IsGrayscaleFilterSupportedProperty)
			{
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Schedule();
			}
			else if (property == IsHighlightAdjustmentSupportedProperty)
			{
				this.canResetHighlightAdjustment.Update(this.HasHighlightAdjustment && (bool)newValue.AsNonNull());
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Reschedule();
			}
			else if (property == IsHistogramsVisibleProperty)
				this.PersistentState.SetValue<bool>(IsInitHistogramsPanelVisible, (bool)newValue.AsNonNull());
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
			else if (property == IsSourceFileOpenedProperty)
			{
				if (this.IsSourceFileOpened)
					this.updateFilterSupportingAction.Schedule();
				else
					this.updateFilterSupportingAction.Execute();
				this.UpdateCanZoomInOut();
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
			}
			else if (property == ProfileProperty)
			{
				this.canApplyProfile.Update(((ImageRenderingProfile)newValue.AsNonNull()).Type != ImageRenderingProfileType.Default);
				this.ApplyProfile();
			}
			else if (property == QuarterSizeRenderedImageProperty)
			{
				if (newValue == null)
				{
					this.cachedAvaQuarterSizeRenderedImage = null;
					this.avaQuarterSizeRenderedImageMemoryUsageToken = this.avaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
					this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
				}
				this.SetValue(HasQuarterSizeRenderedImageProperty, newValue != null);
			}
			else if (property == RenderedImageProperty)
			{
				if (newValue == null)
				{
					this.cachedAvaRenderedImage = null;
					this.avaRenderedImageMemoryUsageToken = this.avaRenderedImageMemoryUsageToken.DisposeAndReturnNull();
					this.cachedAvaRenderedImageMemoryUsageToken = this.cachedAvaRenderedImageMemoryUsageToken.DisposeAndReturnNull();
				}
				this.SetValue(HasRenderedImageProperty, newValue != null);
				if (oldValue == null || newValue == null || ((IImage)oldValue).Size != ((IImage)newValue).Size)
					this.fitRenderedImageToViewportScale = double.NaN;
				this.updateImageDisplaySizeAction.Execute();
			}
			else if (property == RenderingParametersPanelSizeProperty)
				this.PersistentState.SetValue<int>(LatestRenderingParamsPanelSize, (int)(this.RenderingParametersPanelSize + 0.5));
			else if (property == RequestedImageDisplayScaleProperty)
			{
				if (!this.GetValue(FitImageToViewportProperty))
				{
					var scale = (double)newValue.AsNonNull();
					this.UpdateCanZoomInOut();
					if (this.imageScalingAnimator == null 
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
			}
			else if (property == ShadowAdjustmentProperty)
			{
				this.SetValue(HasShadowAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
				this.canResetShadowAdjustment.Update(this.HasShadowAdjustment && this.IsShadowAdjustmentSupported);
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Schedule(RenderImageDelay);
			}
			else if (property == SourceDataSizeProperty)
				this.SetValue(HasSourceDataSizeProperty, (long)newValue.AsNonNull() > 0);
			else if (property == VibranceAdjustmentProperty)
			{
				this.SetValue(HasVibranceAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
				this.canResetVibranceAdjustment.Update(this.HasVibranceAdjustment && this.IsShadowAdjustmentSupported);
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Schedule(RenderImageDelay);
			}
			else if (property == YuvToBgraConverterProperty)
			{
				if (this.IsYuvToBgraConverterSupported)
				{
					this.SetValue(ColorSpaceProperty, ((YuvToBgraConverter)newValue.AsNonNull()).ColorSpace);
					this.renderImageAction.Reschedule();
				}
			}
        }


		// Called before removing user-defined color space.
		void OnRemovingUserDefinedColorSpace(object? sender, Media.ColorSpaceEventArgs e)
		{
			if (e.ColorSpace == this.GetValue(ColorSpaceProperty))
			{
				this.Logger.LogWarning("Color space '{colorSpace}' is being removed, switch back to default color space", e.ColorSpace);
				Media.ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName), out var colorSpace);
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
			if (prevScreenColorSpace != null)
			{
				if (this.GetValue(ColorSpaceProperty) == prevScreenColorSpace)
				{
					Media.ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName), out var colorSpace);
					this.SetValue(ColorSpaceProperty, colorSpace);
				}
				this.colorSpaces.Remove(prevScreenColorSpace);
			}
			(this.Owner as Workspace)?.EffectiveScreenColorSpace?.Let(screenColorSpace =>
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
					this.ClearFilteredImage();
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
			// check state
			if (fileName == null)
				return;
			if (!this.canOpenSourceFile.Value)
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
			this.CloseSourceFile(false);

			// update state
			this.canOpenSourceFile.Update(false);
			this.SetValue(IsOpeningSourceFileProperty, true);
			this.SetValue(SourceFileNameProperty, fileName);

			// update title
			this.UpdateTitle();

			// create image data source
			var imageDataSource = await Task.Run<IImageDataSource?>(() =>
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
			});
			if (this.IsDisposed)
			{
				this.Logger.LogWarning("Source for '{fileName}' created after disposing.", fileName);
				if (imageDataSource != null)
					_ = Task.Run(imageDataSource.Dispose);
				return;
			}
			if (imageDataSource == null)
			{
				// reset state
				this.SetValue(SourceFileNameProperty, null);
				this.SetValue(IsSourceFileOpenedProperty, false);
				this.SetValue(IsOpeningSourceFileProperty, false);
				this.canOpenSourceFile.Update(true);
				this.canZoomTo.Update(false);

				// update title
				this.UpdateTitle();

				// stop opening file
				return;
			}
			this.imageDataSource = imageDataSource;

			// parse file format
			try
			{
				this.fileFormatProfile = await Media.FileFormatParsers.FileFormatParsers.ParseImageRenderingProfileAsync(imageDataSource, new CancellationToken());
				if (this.fileFormatProfile != null)
					this.profiles.Add(this.fileFormatProfile);
			}
			catch
			{ }

			// select image renderer by file name
			var evaluatedImageRenderer = (IImageRenderer?)null;
			if (this.fileFormatProfile == null 
				&& this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageRendererByFileName)
				&& ImageFormat.TryGetByFileName(fileName, out var imageFormat)
				&& imageFormat != null)
			{
				foreach (var candidateRenderer in ImageRenderers.All)
				{
					if (candidateRenderer.Format == imageFormat)
					{
						evaluatedImageRenderer = candidateRenderer;
						break;
					}
				}
			}

			// update state
			this.SetValue(DataOffsetProperty, 0L);
			this.SetValue(FrameNumberProperty, 1);
			this.SetValue(FramePaddingSizeProperty, 0L);
			this.SetValue(IsOpeningSourceFileProperty, false);
			this.SetValue(IsSourceFileOpenedProperty, true);
			this.canOpenSourceFile.Update(true);
			this.SetValue(SourceFileSizeStringProperty, imageDataSource.Size.ToFileSizeString());
			this.UpdateCanSaveDeleteProfile();

			// use profile of file format or reset to default renderer
			if (this.fileFormatProfile != null)
				this.Profile = this.fileFormatProfile;
			else if (evaluatedImageRenderer != null)
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

			// render image
			if (this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterOpeningSourceFile) && this.Profile.Type == ImageRenderingProfileType.Default)
			{
				this.isImageDimensionsEvaluationNeeded = true;
				this.isImagePlaneOptionsResetNeeded = true;
			}
			this.isFirstImageRenderingForSource = true;
			if (this.IsActivated)
				this.renderImageAction.Reschedule();
			else
				this.renderImageAction.Cancel();

			// update zooming state
			this.UpdateCanZoomInOut();
		}


		/// <summary>
		/// Command for opening source file.
		/// </summary>
		public ICommand OpenSourceFileCommand { get; }


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
		public IBitmap? QuarterSizeRenderedImage { get => this.GetValue(QuarterSizeRenderedImageProperty); }


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


		// Release all cached images.
		bool ReleaseCachedImages()
		{
			var released = false;
			if (this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken != null)
			{
				released = true;
				this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
				this.cachedAvaQuarterSizeRenderedImage = null;
			}
			if (this.cachedAvaRenderedImageMemoryUsageToken != null)
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
		public IBitmap? RenderedImage { get => this.GetValue(RenderedImageProperty); }


		/// <summary>
		/// Get memory usage of rendered images by this session in bytes.
		/// </summary>
		public long RenderedImagesMemoryUsage { get => this.GetValue(RenderedImagesMemoryUsageProperty); }


		// Render image according to current state.
		async void RenderImage()
		{
			// cancel filtering
			this.CancelFilteringImage(true);

			// cancel rendering
			var cancelled = this.CancelRenderingImage();

			// clear selected pixel
			this.SelectRenderedImagePixel(-1, -1);

			// get state
			var imageDataSource = this.imageDataSource;
			if (imageDataSource == null)
				return;
			var imageRenderer = this.ImageRenderer;
			var sourceFileName = this.SourceFileName;

			// render later
			if (!this.isFirstImageRenderingForSource && cancelled)
			{
				this.Logger.LogWarning("Rendering image, start next rendering after cancellation completed");
				this.hasPendingImageRendering = true;
				return;
			}
			if (!this.IsActivated && !this.HasRenderedImage)
			{
				if (!this.IsHibernated)
				{
					this.Logger.LogWarning("No image rendered before deactivation, hibernate the session");
					this.Hibernate();
				}
				return;
			}
			this.isFirstImageRenderingForSource = false;
			this.hasPendingImageRendering = false;

			// evaluate dimensions
			if (this.isImageDimensionsEvaluationNeeded)
			{
				this.Logger.LogDebug("Evaluate dimensions of image for '{sourceFileName}'", sourceFileName);
				this.isImageDimensionsEvaluationNeeded = false;
				imageRenderer.EvaluateDimensions(imageDataSource, this.Settings.GetValueOrDefault(SettingKeys.DefaultImageDimensionsEvaluationAspectRatio))?.Also((ref PixelSize it) =>
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
					this.effectiveBits[i] = planeOptions.EffectiveBits;
					if (planeOptions.BlackLevel.HasValue && planeOptions.WhiteLevel.HasValue)
					{
						this.blackLevels[i] = planeOptions.BlackLevel.GetValueOrDefault();
						this.whiteLevels[i] = planeOptions.WhiteLevel.GetValueOrDefault();
					}
					else
					{
						this.blackLevels[i] = 0;
						this.whiteLevels[i] = (uint)(1 << planeOptions.EffectiveBits) - 1;
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

			// calculate frame count and index
			var isRgbGainSupported = this.IsRgbGainSupported;
			var renderingOptions = new ImageRenderingOptions()
			{
				BayerPattern = this.BayerPattern,
				BlueGain = isRgbGainSupported ?this.BlueColorGain : 1.0,
				ByteOrdering = this.ByteOrdering,
				DataOffset = this.DataOffset,
				Demosaicing = (this.IsDemosaicingSupported && this.Demosaicing),
				GreenGain = isRgbGainSupported ? this.GreenColorGain : 1.0,
				RedGain = isRgbGainSupported ? this.RedColorGain : 1.0,
				YuvToBgraConverter = this.YuvToBgraConverter,
			};
			var frameNumber = this.FrameNumber;
			var frameDataSize = imageRenderer.EvaluateSourceDataSize(this.ImageWidth, this.ImageHeight, renderingOptions, planeOptionsList);
			try
			{
				var totalDataSize = imageDataSource.Size - this.DataOffset;
				var frameCount = frameDataSize > 0
					? (totalDataSize <= frameDataSize)
						? 1
						: 1 + (totalDataSize - frameDataSize) / (frameDataSize + this.FramePaddingSize)
					: 1;
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
				this.SetValue(FrameCountProperty, frameCount);
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Unable to update frame count and index of '{sourceFileName}'", this.SourceFileName);
				this.SetValue(HasRenderingErrorProperty, true);
				this.ClearRenderedImage();
				return;
			}
			renderingOptions.DataOffset += ((frameDataSize + this.FramePaddingSize) * (frameNumber - 1));

			// update state
			this.canSaveRenderedImage.Update(false);
			this.SetValue(IsRenderingImageProperty, true);

            // check color space
            var renderedColorSpace = this.IsColorSpaceManagementEnabled ? this.ColorSpace : ColorSpace.Default;
			var screenColorSpace = (this.Owner as Workspace)?.EffectiveScreenColorSpace ?? Global.Run(() =>
			{
				Media.ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.ScreenColorSpaceName), out var colorSpace);
				return colorSpace;
			});
			var isColorSpaceConversionNeeded = this.IsColorSpaceManagementEnabled && !screenColorSpace.Equals(renderedColorSpace);

			// check whether rendering is needed or not
			var isRenderingNeeded = this.renderedImageFrame?.Let(it =>
			{
				if (it.ImageRenderer != imageRenderer)
					return true;
				if (it.BitmapBuffer.Width != this.ImageWidth || it.BitmapBuffer.Height != this.ImageHeight)
					return true;
				if (it.RenderingOptions != renderingOptions)
					return true;
				var planeOptions = it.PlaneOptions;
				if (planeOptions == null || planeOptions.Count != planeOptionsList.Count)
					return true;
				for (var i = planeOptionsList.Count - 1; i >= 0; --i)
                {
					if (planeOptions[i] != planeOptionsList[i])
						return true;
                }
				return false;
			}) ?? true;

			// create rendered image
			var cancellationTokenSource = new CancellationTokenSource();
			this.imageRenderingCancellationTokenSource = cancellationTokenSource;
			var renderedFormat = imageRenderer.RenderedFormat;
			var renderedImageFrame = isRenderingNeeded ? await this.AllocateRenderedImageFrame(frameNumber, renderedFormat, renderedColorSpace, this.ImageWidth, this.ImageHeight) : null;
			var colorSpaceConvertedImageFrame = isColorSpaceConversionNeeded
				? await this.AllocateRenderedImageFrame(frameNumber, renderedFormat, screenColorSpace, this.ImageWidth, this.ImageHeight)
				: null;
			if ((isRenderingNeeded && renderedImageFrame == null)
				|| (isColorSpaceConversionNeeded && colorSpaceConvertedImageFrame == null))
			{
				if (!cancellationTokenSource.IsCancellationRequested)
				{
					this.imageRenderingCancellationTokenSource = null;
					this.SetValue(InsufficientMemoryForRenderedImageProperty, this.IsActivated);
					Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
					this.SetValue(IsRenderingImageProperty, false);
					if (!this.IsActivated && !this.IsHibernated)
					{
						this.Logger.LogWarning("Ubable to allocate rendered image frame after deactivation, hibernate the session");
						this.Hibernate();
					}
				}
				else if (this.hasPendingImageRendering)
				{
					if (this.IsActivated)
					{
						this.Logger.LogWarning("Start next rendering");
						this.renderImageAction.Schedule();
					}
					else
						this.hasPendingImageRendering = false;
				}
				renderedImageFrame?.Dispose();
				colorSpaceConvertedImageFrame?.Dispose();
				return;
			}

			// update state
			this.SetValue(InsufficientMemoryForRenderedImageProperty, false);

			// render
			this.Logger.LogDebug("Render image for '{sourceFileName}', dimensions: {width}x{height}", sourceFileName, this.ImageWidth, this.ImageHeight);
			var exception = (Exception?)null;
			try
			{
				// render
				if (isRenderingNeeded && renderedImageFrame != null)
				{
					renderedImageFrame.RenderingResult = await imageRenderer.Render(imageDataSource, renderedImageFrame.BitmapBuffer, renderingOptions, planeOptionsList, cancellationTokenSource.Token);
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
				if (colorSpaceConvertedImageFrame != null && !cancellationTokenSource.IsCancellationRequested)
				{
					this.SetValue(IsConvertingColorSpaceProperty, true);
					var sourceImageFrame = renderedImageFrame ?? this.renderedImageFrame.AsNonNull();
					await sourceImageFrame.BitmapBuffer.ConvertToColorSpaceAsync(colorSpaceConvertedImageFrame.AsNonNull().BitmapBuffer, this.UseLinearColorSpace, false, cancellationTokenSource.Token);
					colorSpaceConvertedImageFrame.RenderingResult = sourceImageFrame.RenderingResult;
				}
			}
			catch (Exception ex)
			{
				exception = ex;
			}

			// generate histograms
			if (exception == null && !cancellationTokenSource.IsCancellationRequested)
			{
				try
				{
					if (colorSpaceConvertedImageFrame != null)
						colorSpaceConvertedImageFrame.Histograms = await BitmapHistograms.CreateAsync(colorSpaceConvertedImageFrame.BitmapBuffer, cancellationTokenSource.Token);
					if (renderedImageFrame != null)
						renderedImageFrame.Histograms = await BitmapHistograms.CreateAsync(renderedImageFrame.BitmapBuffer, cancellationTokenSource.Token);
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
				if (this.hasPendingImageRendering)
				{
					if (this.IsActivated)
					{
						this.Logger.LogWarning("Start next rendering");
						this.renderImageAction.Schedule();
					}
					else
						this.hasPendingImageRendering = false;
				}
				return;
			}
			this.imageRenderingCancellationTokenSource = null;
			if (this.IsDisposed)
				return;

			// update state and continue filtering if needed
			if (exception == null)
			{
				this.Logger.LogDebug("Image for '{sourceFileName}' rendered", sourceFileName);

				// update state
				this.colorSpaceConvertedImageFrame?.Dispose();
				this.colorSpaceConvertedImageFrame = colorSpaceConvertedImageFrame;
				if (renderedImageFrame != null)
				{
					this.renderedImageFrame?.Dispose();
					this.renderedImageFrame = renderedImageFrame;
				}
				this.SetValue(HasRenderingErrorProperty, false);
				this.SetValue(SourceDataSizeProperty, frameDataSize);
				this.canMoveToNextFrame.Update(frameNumber < this.FrameCount);
				this.canMoveToPreviousFrame.Update(frameNumber > 1);
				this.canSelectColorAdjustment.Update((colorSpaceConvertedImageFrame ?? renderedImageFrame)?.Histograms != null);
				this.canSelectRgbGain.Update((colorSpaceConvertedImageFrame ?? renderedImageFrame)?.RenderingResult.Let(it =>
					it.HasMeanOfRgb || it.HasWeightedMeanOfRgb) ?? false);

				// filter image or report now
				if (this.IsFilteringRenderedImageNeeded)
				{
					this.Logger.LogDebug("Continue filtering image after rendering");
					this.FilterImage(colorSpaceConvertedImageFrame ?? this.renderedImageFrame.AsNonNull());
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
				this.ClearFilteredImage();

				// update state
				colorSpaceConvertedImageFrame?.Dispose();
				renderedImageFrame?.Dispose();
				this.colorSpaceConvertedImageFrame = this.colorSpaceConvertedImageFrame.DisposeAndReturnNull();
				this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
				this.SetValue(HasRenderingErrorProperty, true);
				this.canMoveToNextFrame.Update(false);
				this.canMoveToPreviousFrame.Update(false);
				this.canSelectColorAdjustment.Update(false);
				this.canSelectRgbGain.Update(false);
				Global.RunWithoutError(() => _ = this.ReportRenderedImageAsync(cancellationTokenSource));
			}
			this.SetValue(IsConvertingColorSpaceProperty, false);
			this.SetValue(IsRenderingImageProperty, false);
		}


		/// <summary>
		/// Command to request rendering image.
		/// </summary>
		public ICommand RenderImageCommand { get; }


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
			var imageFrame = this.IsFilteringRenderedImageNeeded 
				? this.filteredImageFrame 
				: (this.colorSpaceConvertedImageFrame ?? this.renderedImageFrame);
			if (imageFrame != null)
			{
				// released cached image if it is not suitable
				var width = imageFrame.BitmapBuffer.Width;
				var height = imageFrame.BitmapBuffer.Height;
				if (this.cachedAvaRenderedImage != null)
                {
					if (this.cachedAvaRenderedImage.PixelSize.Width != width
						|| this.cachedAvaRenderedImage.PixelSize.Height != height)
                    {
						if (this.Application.IsDebugMode)
							this.Logger.LogTrace("Release cached Avalonia bitmap, size: {w}x{h}", this.cachedAvaRenderedImage.PixelSize.Width, this.cachedAvaRenderedImage.PixelSize.Height);
						this.cachedAvaRenderedImage = null;
						this.cachedAvaRenderedImageMemoryUsageToken = this.cachedAvaRenderedImageMemoryUsageToken.DisposeAndReturnNull();
                    }
                }

				// request memory usage
				var memoryUsageToken = (IDisposable?)null;
				if (this.cachedAvaRenderedImage != null)
				{
					if (this.Application.IsDebugMode)
						this.Logger.LogTrace("Use cached Avalonia bitmap, size: {w}x{h}", this.cachedAvaRenderedImage.PixelSize.Width, this.cachedAvaRenderedImage.PixelSize.Height);
					memoryUsageToken = this.cachedAvaRenderedImageMemoryUsageToken.AsNonNull();
					this.cachedAvaRenderedImageMemoryUsageToken = null;
				}
				else
				{
					var dataSize = (width * height * 4);
					memoryUsageToken = this.RequestRenderedImageMemoryUsage(dataSize);
					while (memoryUsageToken == null)
					{
						if (this.RenderedImage != null)
						{
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
							this.SetValue(HasRenderingErrorProperty, false);
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
					if (this.cachedAvaRenderedImage != null)
					{
						bitmap = this.cachedAvaRenderedImage;
						this.cachedAvaRenderedImage = null;
					}
					else
					{
						if (this.Application.IsDebugMode)
							this.Logger.LogWarning("Allocate Avalonia bitmap, size: {width}x{height}", width, height);
						bitmap = await Task.Run(() => new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul));
					}
					await imageFrame.BitmapBuffer.CopyToAvaloniaBitmapAsync(bitmap);

					// create quarter-size Avalonia bitmap
					var halfWidth = width >> 1;
					var halfHeight = height >> 1;
					if (!cancellationTokenSource.IsCancellationRequested && (halfWidth > 1024 || halfHeight > 1024))
					{
						// released cached image if it is not suitable
						if (this.cachedAvaQuarterSizeRenderedImage != null)
						{
							if (this.cachedAvaQuarterSizeRenderedImage.PixelSize.Width != halfWidth
								|| this.cachedAvaQuarterSizeRenderedImage.PixelSize.Height != halfHeight)
							{
								if (this.Application.IsDebugMode)
									this.Logger.LogTrace("Release cached quarter-size Avalonia bitmap, size: {w}x{h}", this.cachedAvaQuarterSizeRenderedImage.PixelSize.Width, this.cachedAvaQuarterSizeRenderedImage.PixelSize.Height);
								this.cachedAvaQuarterSizeRenderedImage = null;
								this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken.DisposeAndReturnNull();
							}
						}

						// request memory usage
						if (this.cachedAvaQuarterSizeRenderedImage != null)
                        {
							quarterSizeMemoryUsageToken = this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken;
							this.cachedAvaQuarterSizeRenderedImageMemoryUsageToken = null;
                        }
						else
							quarterSizeMemoryUsageToken = this.RequestRenderedImageMemoryUsage(halfWidth * halfHeight * 4);

						// create bitmap
						if (quarterSizeMemoryUsageToken != null)
						{
							if (this.cachedAvaQuarterSizeRenderedImage != null)
							{
								quarterSizeBitmap = this.cachedAvaQuarterSizeRenderedImage;
								this.cachedAvaQuarterSizeRenderedImage = null;
							}
							else
							{
								if (this.Application.IsDebugMode)
									this.Logger.LogWarning("Allocate quarter-size Avalonia bitmap, size: {halfWidth}x{halfHeight}", halfWidth, halfHeight);
								quarterSizeBitmap = await Task.Run(() => new WriteableBitmap(new PixelSize(halfWidth, halfHeight), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul));
							}
							await imageFrame.BitmapBuffer.CopyToQuarterSizeAvaloniaBitmapAsync(quarterSizeBitmap);
						}
						else
							this.Logger.LogWarning("Unable to request memory usage for quarter-size Avalonia bitmap");
					}
				}
				catch (Exception ex)
				{
					this.cachedAvaQuarterSizeRenderedImage = null;
					this.cachedAvaRenderedImage = null;
					quarterSizeMemoryUsageToken?.Dispose();
					if (bitmap == null)
						memoryUsageToken?.Dispose();
					if (ex is TaskCanceledException)
					{
						if (this.Application.IsDebugMode)
							this.Logger.LogWarning("Reporting rendered image has been cancelled");
						memoryUsageToken?.Dispose();
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
				this.canSaveFilteredImage.Update(!this.IsSavingFilteredImage && this.filteredImageFrame != null);
				this.canSaveRenderedImage.Update(!this.IsSavingRenderedImage);
				this.SetValue(HasRenderingErrorProperty, false);
				this.SetValue(InsufficientMemoryForRenderedImageProperty, false);
				this.SetValue(HistogramsProperty, imageFrame.Histograms);
				this.SetValue(QuarterSizeRenderedImageProperty, quarterSizeBitmap);
				this.SetValue(RenderedImageProperty, bitmap);
			}
			else
			{
				this.canSaveFilteredImage.Update(false);
				this.canSaveRenderedImage.Update(false);
				this.SetValue(HistogramsProperty, null);
				this.SetValue(QuarterSizeRenderedImageProperty, null);
				this.SetValue(RenderedImageProperty, null);
			}
			this.releasedCachedImagesAction.Reschedule(ReleaseCachedImagesDelay);
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
		public async void RestoreState(JsonElement savedState)
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
			var demosaicing = true;
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
				Media.ColorSpace.TryGetColorSpace(jsonProperty.GetString().AsNonNull(), out colorSpace);
			if (savedState.TryGetProperty(nameof(UseLinearColorSpace), out jsonProperty))
				useLinearColorSpace = jsonProperty.ValueKind == JsonValueKind.True;
			if (savedState.TryGetProperty(nameof(Demosaicing), out jsonProperty))
				demosaicing = jsonProperty.ValueKind != JsonValueKind.False;
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
			var isHistogramsVisible = this.PersistentState.GetValueOrDefault(IsInitHistogramsPanelVisible);
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
			if (savedState.TryGetProperty(nameof(IsHistogramsVisible), out jsonProperty))
				isHistogramsVisible = jsonProperty.ValueKind != JsonValueKind.False;
			if (savedState.TryGetProperty(nameof(IsRenderingParametersPanelVisible), out jsonProperty))
				isRenderingParamsPanelVisible = jsonProperty.ValueKind != JsonValueKind.False;
			if (savedState.TryGetProperty(nameof(RequestedImageDisplayScale), out jsonProperty))
				jsonProperty.TryGetDouble(out scale);
			if (savedState.TryGetProperty(nameof(RenderingParametersPanelSize), out jsonProperty)
				&& jsonProperty.TryGetDouble(out renderingParamsPanelSize))
			{
				renderingParamsPanelSize = this.CoerceValue(RenderingParametersPanelSizeProperty, renderingParamsPanelSize);
				if (RenderingParametersPanelSizeProperty.ValidationFunction?.Invoke(renderingParamsPanelSize) == false)
					renderingParamsPanelSize = RenderingParametersPanelSizeProperty.DefaultValue;
			}

			// load other state
			if (savedState.TryGetProperty(nameof(CustomTitle), out jsonProperty) && jsonProperty.ValueKind == JsonValueKind.String)
				this.SetValue(CustomTitleProperty, jsonProperty.GetString());

			// open source file
			if (fileName != null)
			{
				await this.OpenSourceFile(fileName);
				if (!this.IsSourceFileOpened)
					this.Logger.LogError("Unable to restore source file '{fileName}'", fileName);
			}

			// apply profile
			if (profile != null)
				this.SetValue(ProfileProperty, profile);

			// apply rendering parameters
			if (renderer != null)
				this.SetValue(ImageRendererProperty, renderer);
			this.SetValue(DataOffsetProperty, dataOffset);
			this.SetValue(FramePaddingSizeProperty, framePaddingSize);
			this.SetValue(ByteOrderingProperty, byteOrdering);
			this.SetValue(YuvToBgraConverterProperty, yuvToBgraConverter);
			this.SetValue(ColorSpaceProperty, colorSpace);
			this.SetValue(UseLinearColorSpaceProperty, useLinearColorSpace);
			this.SetValue(DemosaicingProperty, demosaicing);
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
			this.SetValue(IsHistogramsVisibleProperty, isHistogramsVisible);
			this.SetValue(IsRenderingParametersPanelVisibleProperty, isRenderingParamsPanelVisible);
			this.SetValue(RequestedImageDisplayScaleProperty, scale);
			this.SetValue(RenderingParametersPanelSizeProperty, renderingParamsPanelSize);

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
			if (!this.IsSourceFileOpened)
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
			if (!this.IsSourceFileOpened)
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
			var profile = new ImageRenderingProfile(name, this.ImageRenderer).Also((it) => this.WriteParametersToProfile(it));
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
			if (parameters.FileName == null)
				return false;
			if (!this.canSaveFilteredImage.Value)
				return false;

			// save image
			var encoder = parameters.Encoder;
			if (encoder == null && !ImageEncoders.TryGetEncoderByFormat(FileFormats.Png, out encoder))
				return false;
			var options = parameters.Options;
			if (this.Settings.GetValueOrDefault(SettingKeys.SaveRenderedImageWithOrientation))
				options.Orientation = (int)(this.GetValue(ImageDisplayRotationProperty) + 0.5);
			this.canSaveFilteredImage.Update(false);
			this.SetValue(IsSavingFilteredImageProperty, true);
			try
			{
				await encoder.AsNonNull().EncodeAsync(this.filteredImageFrame.AsNonNull().BitmapBuffer, new FileStreamProvider(parameters.FileName), options, new CancellationToken());
				return true;
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Unable to save filtered image");
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
		async void SaveProfile()
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
			if (parameters.FileName == null)
				return false;
			if (!this.canSaveRenderedImage.Value)
				return false;

			// save image
			var encoder = parameters.Encoder;
			if (encoder == null && !ImageEncoders.TryGetEncoderByFormat(FileFormats.Png, out encoder))
				return false;
			var options = parameters.Options;
			if (this.Settings.GetValueOrDefault(SettingKeys.SaveRenderedImageWithOrientation))
				options.Orientation = (int)(this.GetValue(ImageDisplayRotationProperty) + 0.5);
			this.canSaveRenderedImage.Update(false);
			this.SetValue(IsSavingRenderedImageProperty, true);
			try
			{
				var imageFrame = this.colorSpaceConvertedImageFrame ?? this.renderedImageFrame.AsNonNull();
				await encoder.AsNonNull().EncodeAsync(imageFrame.BitmapBuffer, new FileStreamProvider(parameters.FileName), options, new CancellationToken());
				return true;
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Unable to save rendered image");
				return false;
			}
			finally
			{
				this.canSaveRenderedImage.Update(!this.IsRenderingImage);
				this.SetValue(IsSavingRenderedImageProperty, false);
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
			writer.WriteBoolean(nameof(Demosaicing), this.Demosaicing);
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
			writer.WriteNumber(nameof(ImageDisplayRotation), (int)(this.GetValue(ImageDisplayRotationProperty) + 0.5));
			writer.WriteBoolean(nameof(IsHistogramsVisible), this.IsHistogramsVisible);
			writer.WriteBoolean(nameof(IsRenderingParametersPanelVisible), this.IsRenderingParametersPanelVisible);
			writer.WriteNumber(nameof(RequestedImageDisplayScale), this.GetValue(RequestedImageDisplayScaleProperty));
			writer.WriteNumber(nameof(RenderingParametersPanelSize), this.RenderingParametersPanelSize);

			// other state
			if (this.CustomTitle != null)
				writer.WriteString(nameof(CustomTitle), this.CustomTitle ?? "");
			
			// complete
			writer.WriteEndObject();
		}


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
			var histograms = (this.colorSpaceConvertedImageFrame ?? this.renderedImageFrame)?.Histograms;
			if (histograms == null)
				return;
			
			// calculate ratio of RGB
			var rRatio = 0.0;
			var gRatio = 0.0;
			var bRatio = 0.0;
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
		public Color SelectedRenderedImagePixelColor { get => this.GetValue(SelectedRenderedImagePixelColorProperty); }


		/// <summary>
		/// Get L*a*b* color of selected pixel on rendered image.
		/// </summary>
		public Tuple<double, double, double> SelectedRenderedImagePixelLabColor { get => this.GetValue(SelectedRenderedImagePixelLabColorProperty); }


		/// <summary>
		/// Get XYZ color of selected pixel on rendered image.
		/// </summary>
		public Tuple<double, double, double> SelectedRenderedImagePixelXyzColor { get => this.GetValue(SelectedRenderedImagePixelXyzColorProperty); }


		/// <summary>
		/// Get horizontal position of selected pixel on rendered image. Return -1 if no pixel selected.
		/// </summary>
		public int SelectedRenderedImagePixelPositionX { get => this.GetValue(SelectedRenderedImagePixelPositionXProperty); }


		/// <summary>
		/// Get vertical position of selected pixel on rendered image. Return -1 if no pixel selected.
		/// </summary>
		public int SelectedRenderedImagePixelPositionY { get => this.GetValue(SelectedRenderedImagePixelPositionYProperty); }


		/// <summary>
		/// Select pixel on rendered image.
		/// </summary>
		/// <param name="x">Horizontal position of selected pixel.</param>
		/// <param name="y">Vertical position of selected pixel.</param>
		public unsafe void SelectRenderedImagePixel(int x, int y)
		{
			if (this.IsDisposed)
				return;
			var renderedImageBuffer = (this.filteredImageFrame ?? this.colorSpaceConvertedImageFrame ?? this.renderedImageFrame)?.BitmapBuffer;
			if (renderedImageBuffer == null 
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
				var argbA = 0.0;
				var argbR = 0.0;
				var argbG = 0.0;
				var argbB = 0.0;
				var color = renderedImageBuffer.Memory.Pin((baseAddress) =>
				{
					var pixelPtr = (byte*)baseAddress + renderedImageBuffer.GetPixelOffset(x, y);
					return renderedImageBuffer.Format switch
					{
						BitmapFormat.Bgra32 => new Color(pixelPtr[3], pixelPtr[2], pixelPtr[1], pixelPtr[0]).Also((ref Color it) =>
						{
							argbA = it.A / 255.0;
							argbR = it.R / 255.0;
							argbG = it.G / 255.0;
							argbB = it.B / 255.0;
						}),
						BitmapFormat.Bgra64 => Global.Run(()=>
                        {
							var blue = (ushort)0;
							var green = (ushort)0;
							var red = (ushort)0;
							var alpha = (ushort)0;
							var unpackFunc = ImageProcessing.SelectBgra64Unpacking();
							unpackFunc(*(ulong*)pixelPtr, &blue, &green, &red, &alpha);
							argbA = alpha / 65535.0;
							argbR = red / 65535.0;
							argbG = green / 65535.0;
							argbB = blue / 65535.0;
							return new Color((byte)(alpha >> 8), (byte)(red >> 8), (byte)(green >> 8), (byte)(blue >> 8));
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
		public long SourceDataSize { get => this.GetValue(SourceDataSizeProperty); }


		/// <summary>
		/// Get name of source image file.
		/// </summary>
		public string? SourceFileName { get => this.GetValue(SourceFileNameProperty); }


		/// <summary>
		/// Get description of size of source image file.
		/// </summary>
		public string? SourceFileSizeString { get => this.GetValue(SourceFileSizeStringProperty); }


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
		public long TotalRenderedImagesMemoryUsage { get => this.GetValue(TotalRenderedImagesMemoryUsageProperty); }


		// Update CanSaveOrDeleteProfile and CanSaveAsNewProfile according to current state.
		void UpdateCanSaveDeleteProfile()
		{
			if (this.IsDisposed)
				return;
			if (!this.IsSourceFileOpened)
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
			if (this.GetValue(FitImageToViewportProperty) || !this.IsSourceFileOpened)
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


		// Update title.
		void UpdateTitle()
		{
			// check state
			if (this.IsDisposed)
				return;

			// generate title
			var title = this.CustomTitle;
			if (title == null)
			{
				if (this.SourceFileName != null)
					title = Path.GetFileName(this.SourceFileName);
				else
					title = this.Application.GetString("Session.EmptyTitle");
			}

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
				profile.ColorSpace = this.ColorSpace;
			profile.Demosaicing = this.Demosaicing;
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
				return (Math.Floor(it * 2) + 1) / 2;
			});
			scale = this.ZoomTo(scale);
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
				return (Math.Ceiling(it * 2) - 1) / 2;
			});
			scale = this.ZoomTo(scale);
			this.SetValue(RequestedImageDisplayScaleProperty, scale);
		}


		/// <summary>
		/// Command of zooming-out rendered image.
		/// </summary>
		public ICommand ZoomOutCommand { get; }


		// Start zooming to given scale.
		double ZoomTo(double scale, bool animate = true)
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
					it.Completed += (_, e) => 
					{
						this.SetValue(ImageDisplayScaleProperty, it.EndValue);
						this.updateImageDisplaySizeAction.Execute();
						this.CompleteZooming(true);
					};
					it.Duration = ZoomAnimationDuration;
					it.Interpolator = Interpolators.Deceleration;
					it.ProgressChanged += (_, e) =>
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
}
