using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageFilters;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.Platform;
using Carina.PixelViewer.Threading;
using CarinaStudio;
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
	class Session : ViewModel
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
			readonly IDisposable memoryUsageToken;
			readonly Session session;

			// Constructor
			ImageFrame(Session session, IDisposable memoryUsageToken, IBitmapBuffer bitmapBuffer, long frameNumber)
			{
				this.BitmapBuffer = bitmapBuffer;
				this.FrameNumber = frameNumber;
				this.memoryUsageToken = memoryUsageToken;
				this.session = session;
			}

			public static ImageFrame Allocate(Session session, long frameNumber, BitmapFormat format, int width, int height)
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
					var bitmapBuffer = new BitmapBuffer(format, width, height);
					return new ImageFrame(session, memoryUsageToken, bitmapBuffer, frameNumber);
				}
				catch
				{
					memoryUsageToken.Dispose();
					throw;
				}
			}

			// Bitmap buffer.
			public readonly IBitmapBuffer BitmapBuffer;

			// Dispose.
			protected override void Dispose(bool disposing)
			{
				this.BitmapBuffer.Dispose();
				if (this.session.CheckAccess())
					this.memoryUsageToken.Dispose();
				else
					this.session.SynchronizationContext.Post(this.memoryUsageToken.Dispose);
			}

			// Histograms.
			public BitmapHistograms? Histograms { get; set; }

			// Frame number.
			public readonly long FrameNumber;
        }


		// Token of memory usage of rendered image.
		class RenderedImageMemoryUsageToken : IDisposable
		{
			// Fields.
			public readonly long DataSize;
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
				this.Session.ReleaseRenderedImageMemoryUsage(this);
			}
		}


		/// <summary>
		/// Property of <see cref="BlueColorAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<double> BlueColorAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(BlueColorAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="BrightnessAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<double> BrightnessAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(BrightnessAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="ByteOrdering"/>.
		/// </summary>
		public static readonly ObservableProperty<ByteOrdering> ByteOrderingProperty = ObservableProperty.Register<Session, ByteOrdering>(nameof(ByteOrdering), ByteOrdering.BigEndian);
		/// <summary>
		/// Property of <see cref="ContrastAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<double> ContrastAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(ContrastAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="DataOffset"/>.
		/// </summary>
		public static readonly ObservableProperty<long> DataOffsetProperty = ObservableProperty.Register<Session, long>(nameof(DataOffset), 0L);
		/// <summary>
		/// Property of <see cref="Demosaicing"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> DemosaicingProperty = ObservableProperty.Register<Session, bool>(nameof(Demosaicing), true);
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
		/// Property of <see cref="HasRenderedImage"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> HasRenderedImageProperty = ObservableProperty.Register<Session, bool>(nameof(HasRenderedImage));
		/// <summary>
		/// Property of <see cref="HasRenderingError"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> HasRenderingErrorProperty = ObservableProperty.Register<Session, bool>(nameof(HasRenderingError));
		/// <summary>
		/// Property of <see cref="HasSelectedRenderedImagePixel"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> HasSelectedRenderedImagePixelProperty = ObservableProperty.Register<Session, bool>(nameof(HasSelectedRenderedImagePixel));
		/// <summary>
		/// Property of <see cref="Histograms"/>.
		/// </summary>
		public static readonly ObservableProperty<BitmapHistograms?> HistogramsProperty = ObservableProperty.Register<Session, BitmapHistograms?>(nameof(Histograms));
		/// <summary>
		/// Property of <see cref="ImageHeight"/>.
		/// </summary>
		public static readonly ObservableProperty<int> ImageHeightProperty = ObservableProperty.Register<Session, int>(nameof(ImageHeight), 1, coerce: it => Math.Max(1, it));
		/// <summary>
		/// Property of <see cref="ImagePlaneCount"/>.
		/// </summary>
		public static readonly ObservableProperty<int> ImagePlaneCountProperty = ObservableProperty.Register<Session, int>(nameof(ImagePlaneCount), 1);
		/// <summary>
		/// Property of <see cref="ImageRenderer"/>.
		/// </summary>
		public static readonly ObservableProperty<IImageRenderer?> ImageRendererProperty = ObservableProperty.Register<Session, IImageRenderer?>(nameof(ImageRenderer));
		/// <summary>
		/// Property of <see cref="ImageWidth"/>.
		/// </summary>
		public static readonly ObservableProperty<int> ImageWidthProperty = ObservableProperty.Register<Session, int>(nameof(ImageWidth), 1, coerce: it => Math.Max(1, it));
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
		/// Property of <see cref="IsBrightnessAdjustmentSupported"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsBrightnessAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsBrightnessAdjustmentSupported));
		/// <summary>
		/// Property of <see cref="IsColorAdjustmentSupported"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsColorAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsColorAdjustmentSupported));
		/// <summary>
		/// Property of <see cref="IsContrastAdjustmentSupported"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsContrastAdjustmentSupportedProperty = ObservableProperty.Register<Session, bool>(nameof(IsContrastAdjustmentSupported));
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
		/// Property of <see cref="IsSourceFileOpened"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsSourceFileOpenedProperty = ObservableProperty.Register<Session, bool>(nameof(IsSourceFileOpened));
		/// <summary>
		/// Property of <see cref="LuminanceHistogramGeometry"/>.
		/// </summary>
		public static readonly ObservableProperty<Geometry?> LuminanceHistogramGeometryProperty = ObservableProperty.Register<Session, Geometry?>(nameof(LuminanceHistogramGeometry));
		/// <summary>
		/// Property of <see cref="Profile"/>.
		/// </summary>
		public static readonly ObservableProperty<ImageRenderingProfile> ProfileProperty = ObservableProperty.Register<Session, ImageRenderingProfile>(nameof(Profile), ImageRenderingProfile.Default);
		/// <summary>
		/// Property of <see cref="RedColorAdjustment"/>.
		/// </summary>
		public static readonly ObservableProperty<double> RedColorAdjustmentProperty = ObservableProperty.Register<Session, double>(nameof(RedColorAdjustment), 0, validate: it => double.IsFinite(it));
		/// <summary>
		/// Property of <see cref="RenderedImage"/>.
		/// </summary>
		public static readonly ObservableProperty<IBitmap?> RenderedImageProperty = ObservableProperty.Register<Session, IBitmap?>(nameof(RenderedImage));
		/// <summary>
		/// Property of <see cref="RenderedImagesMemoryUsage"/>.
		/// </summary>
		public static readonly ObservableProperty<long> RenderedImagesMemoryUsageProperty = ObservableProperty.Register<Session, long>(nameof(RenderedImagesMemoryUsage));
		/// <summary>
		/// Property of <see cref="SelectedRenderedImagePixelColor"/>.
		/// </summary>
		public static readonly ObservableProperty<Color> SelectedRenderedImagePixelColorProperty = ObservableProperty.Register<Session, Color>(nameof(SelectedRenderedImagePixelColor));
		/// <summary>
		/// Property of <see cref="SelectedRenderedImagePixelPositionX"/>.
		/// </summary>
		public static readonly ObservableProperty<int> SelectedRenderedImagePixelPositionXProperty = ObservableProperty.Register<Session, int>(nameof(SelectedRenderedImagePixelPositionX), -1);
		/// <summary>
		/// Property of <see cref="SelectedRenderedImagePixelPositionY"/>.
		/// </summary>
		public static readonly ObservableProperty<int> SelectedRenderedImagePixelPositionYProperty = ObservableProperty.Register<Session, int>(nameof(SelectedRenderedImagePixelPositionY), -1);
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


		// Constants.
		const int RenderImageDelay = 500;


		// Static fields.
		static readonly SettingKey<bool> IsInitHistogramsPanelVisible = new SettingKey<bool>("Session.IsInitHistogramsPanelVisible", false);
		static readonly MutableObservableInt64 SharedRenderedImagesMemoryUsage = new MutableObservableInt64();


		// Fields.
		readonly List<ActivationToken> activationTokens = new List<ActivationToken>();
		readonly MutableObservableBoolean canApplyProfile = new MutableObservableBoolean();
		readonly MutableObservableBoolean canMoveToNextFrame = new MutableObservableBoolean();
		readonly MutableObservableBoolean canMoveToPreviousFrame = new MutableObservableBoolean();
		readonly MutableObservableBoolean canOpenSourceFile = new MutableObservableBoolean(true);
		readonly MutableObservableBoolean canResetBrightnessAdjustment = new MutableObservableBoolean();
		readonly MutableObservableBoolean canResetColorAdjustment = new MutableObservableBoolean();
		readonly MutableObservableBoolean canResetContrastAdjustment = new MutableObservableBoolean();
		readonly MutableObservableBoolean canSaveAsNewProfile = new MutableObservableBoolean();
		readonly MutableObservableBoolean canSaveOrDeleteProfile = new MutableObservableBoolean();
		readonly MutableObservableBoolean canSaveFilteredImage = new MutableObservableBoolean();
		readonly MutableObservableBoolean canSaveRenderedImage = new MutableObservableBoolean();
		readonly MutableObservableBoolean canZoomIn = new MutableObservableBoolean();
		readonly MutableObservableBoolean canZoomOut = new MutableObservableBoolean();
		readonly int[] effectiveBits = new int[ImageFormat.MaxPlaneCount];
		ImageRenderingProfile? fileFormatProfile;
		readonly ScheduledAction filterImageAction;
		ImageFrame? filteredImageFrame;
		bool fitRenderedImageToViewport = true;
		bool hasPendingImageFiltering;
		bool hasPendingImageRendering;
		IImageDataSource? imageDataSource;
		CancellationTokenSource? imageFilteringCancellationTokenSource;
		CancellationTokenSource? imageRenderingCancellationTokenSource;
		bool isFirstImageRenderingForSource = true;
		bool isImageDimensionsEvaluationNeeded = true;
		bool isImagePlaneOptionsResetNeeded = true;
		readonly int[] pixelStrides = new int[ImageFormat.MaxPlaneCount];
		readonly SortedObservableList<ImageRenderingProfile> profiles = new SortedObservableList<ImageRenderingProfile>(CompareProfiles);
		ImageFrame? renderedImageFrame;
		double renderedImageScale = 1.0;
		readonly ScheduledAction renderImageAction;
		readonly int[] rowStrides = new int[ImageFormat.MaxPlaneCount];
		readonly IDisposable sharedRenderedImagesMemoryUsageObserverToken;
		readonly ScheduledAction updateFilterSupportingAction;
		readonly ScheduledAction updateIsFilteringImageNeededAction;
		readonly ScheduledAction updateIsProcessingImageAction;


		/// <summary>
		/// Initialize new <see cref="Session"/> instance.
		/// </summary>
		public Session(Workspace workspace, JsonElement? savedState) : base(workspace)
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
			this.ResetBrightnessAdjustmentCommand = new Command(this.ResetBrightnessAdjustment, this.canResetBrightnessAdjustment);
			this.ResetColorAdjustmentCommand = new Command(this.ResetColorAdjustment, this.canResetColorAdjustment);
			this.ResetContrastAdjustmentCommand = new Command(this.ResetContrastAdjustment, this.canResetContrastAdjustment);
			this.RotateLeftCommand = new Command(this.RotateLeft, isSrcFileOpenedObservable);
			this.RotateRightCommand = new Command(this.RotateRight, isSrcFileOpenedObservable);
			this.SaveAsNewProfileCommand = new Command<string>(name => this.SaveAsNewProfile(name), this.canSaveAsNewProfile);
			this.SaveFilteredImageCommand = new Command<string>(fileName => _ = this.SaveFilteredImage(fileName), this.canSaveFilteredImage);
			this.SaveProfileCommand = new Command(() => this.SaveProfile(), this.canSaveOrDeleteProfile);
			this.SaveRenderedImageCommand = new Command<string>(fileName => _ = this.SaveRenderedImage(fileName), this.canSaveRenderedImage);
			this.ZoomInCommand = new Command(this.ZoomIn, this.canZoomIn);
			this.ZoomOutCommand = new Command(this.ZoomOut, this.canZoomOut);

			// setup operations
			this.filterImageAction = new ScheduledAction(() =>
			{
				if (this.renderedImageFrame != null)
					this.FilterImage(this.renderedImageFrame);
			});
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
				}
				else
				{
					var format = this.ImageRenderer.Format;
					this.SetValue(IsBrightnessAdjustmentSupportedProperty, true);
					this.SetValue(IsColorAdjustmentSupportedProperty, true);
					this.SetValue(IsContrastAdjustmentSupportedProperty, true);
					this.SetValue(IsGrayscaleFilterSupportedProperty, format.Category != ImageFormatCategory.Luminance);
				}
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
			this.Profiles = this.profiles.AsReadOnly();
			((INotifyCollectionChanged)ImageRenderingProfiles.UserDefinedProfiles).CollectionChanged += this.OnUserDefinedProfilesChanged;

			// select default image renderer
			this.SetValue(ImageRendererProperty, this.SelectDefaultImageRenderer());

			// setup title
			this.UpdateTitle();

			// restore state
			if (savedState.HasValue)
				this.RestoreState(savedState.Value);
			else
				this.SetValue(IsHistogramsVisibleProperty, this.PersistentState.GetValueOrDefault(IsInitHistogramsPanelVisible));
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
				if (!this.HasRenderedImage)
					this.renderImageAction.Reschedule();
				this.SetValue(IsActivatedProperty, true);
			}
			return token;
        }


		// Try allocating image frame for filtered image.
		async Task<ImageFrame?> AllocateFilteredImageFrame(ImageFrame renderedImageFrame)
        {
			while (true)
			{
				try
				{
					return ImageFrame.Allocate(this, renderedImageFrame.FrameNumber, renderedImageFrame.BitmapBuffer.Format, renderedImageFrame.BitmapBuffer.Width, renderedImageFrame.BitmapBuffer.Height);
				}
				catch (Exception ex)
				{
					if (ex is OutOfMemoryException)
					{
						if (this.filteredImageFrame != null)
						{
							this.Logger.LogWarning("Unable to request memory usage for filtered image, dispose current images");
							this.SetValue(HistogramsProperty, null);
							this.SetValue(RenderedImageProperty, null);
							this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
						}
						else if (!(await this.ReleaseRenderedImageMemoryFromAnotherSession()))
						{
							this.Logger.LogWarning("Unable to release rendered image from another session");
							return null;
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
		async Task<ImageFrame?> AllocateRenderedImageFrame(long frameNumber, BitmapFormat format, int width, int height)
		{
			while (true)
			{
				try
				{
					return ImageFrame.Allocate(this, frameNumber, format, width, height);
				}
				catch (Exception ex)
				{
					if (ex is OutOfMemoryException)
					{
						if (this.renderedImageFrame != null)
						{
							this.Logger.LogWarning("Unable to request memory usage for rendered image, dispose current images");
							this.SetValue(HistogramsProperty, null);
							this.SetValue(RenderedImageProperty, null);
							this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
							this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
						}
						else if (!(await this.ReleaseRenderedImageMemoryFromAnotherSession()))
						{
							this.Logger.LogWarning("Unable to release rendered image from another session");
							return null;
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
					this.Logger.LogError(ex, $"Error occurred while applying filter {filter}");
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

				// demosaicing
				this.SetValue(DemosaicingProperty, profile.Demosaicing);

				// dimensions
				this.SetValue(ImageWidthProperty, profile.Width);
				this.SetValue(ImageHeightProperty, profile.Height);

				// plane options
				for (var i = this.ImageRenderer.Format.PlaneCount - 1; i >= 0; --i)
				{
					this.ChangeEffectiveBits(i, profile.EffectiveBits[i]);
					this.ChangePixelStride(i, profile.PixelStrides[i]);
					this.ChangeRowStride(i, profile.RowStrides[i]);
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
		/// Get or set blue color adjustment.
		/// </summary>
		public double BlueColorAdjustment
		{
			get => this.GetValue(BlueColorAdjustmentProperty);
			set => this.SetValue(BlueColorAdjustmentProperty, value);
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
			this.Logger.LogWarning($"Cancel filtering image for source '{this.SourceFileName}'");
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
			this.Logger.LogWarning($"Cancel rendering image for source '{this.SourceFileName}'");
			this.imageRenderingCancellationTokenSource.Cancel();
			this.imageRenderingCancellationTokenSource = null;

			// update state
			if(!this.IsDisposed)
			this.SetValue(IsRenderingImageProperty, false);
			if (cancelPendingRendering)
				this.hasPendingImageRendering = false;

			// complete
			return true;
		}


		// Change effective bits of given image plane.
		void ChangeEffectiveBits(int index, int effectiveBits)
		{
			this.VerifyAccess();
			this.VerifyDisposed();
			if (this.effectiveBits[index] == effectiveBits)
				return;
			this.effectiveBits[index] = effectiveBits;
			this.OnEffectiveBitsChanged(index);
			this.renderImageAction.Reschedule(RenderImageDelay);
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


		// Clear filtered image.
		bool ClearFilteredImage()
        {
			// cancel filtering
			this.CancelFilteringImage(true);

			// clear images
			if (!this.IsFilteringRenderedImage && this.filteredImageFrame != null)
			{
				this.SetValue(HistogramsProperty, null);
				this.SetValue(RenderedImageProperty, null);
				this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
			}
			return true;
		}


		// Clear rendered image.
		bool ClearRenderedImage(bool checkActivation)
        {
			// check state
			if (this.IsActivated && checkActivation)
				return false;

			// clear filtered image
			this.ClearFilteredImage();

			// cancel rendering
			this.CancelRenderingImage(true);

			// clear images
			if (!this.IsRenderingImage && this.renderedImageFrame != null)
			{
				this.SetValue(HistogramsProperty, null);
				this.SetValue(RenderedImageProperty, null);
				this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
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

			// update state
			if (!disposing)
			{
				this.SetValue(DataOffsetProperty, 0L);
				this.SetValue(FrameCountProperty, 0);
				this.SetValue(FrameNumberProperty, 0);
				this.SetValue(FramePaddingSizeProperty, 0L);
				this.SetValue(HistogramsProperty, null);
				this.SetValue(RenderedImageProperty, null);
				this.SetValue(IsSourceFileOpenedProperty, false);
				this.SetValue(LuminanceHistogramGeometryProperty, null);
				this.canMoveToNextFrame.Update(false);
				this.canMoveToPreviousFrame.Update(false);
				this.canSaveRenderedImage.Update(false);
				this.SetValue(SourceDataSizeProperty, 0);
				this.UpdateCanSaveDeleteProfile();
			}
			this.filteredImageFrame = this.filteredImageFrame.DisposeAndReturnNull();
			this.renderedImageFrame = this.renderedImageFrame.DisposeAndReturnNull();
			if (Math.Abs(this.EffectiveRenderedImageRotation) > 0.1)
			{
				this.EffectiveRenderedImageRotation = 0.0;
				this.OnPropertyChanged(nameof(this.EffectiveRenderedImageRotation));
			}
			if (!disposing)
			{
				this.SetValue(HasRenderingErrorProperty, false);
				this.SetValue(InsufficientMemoryForRenderedImageProperty, false);
			}

			// update zooming state
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
					this.Logger.LogDebug($"Dispose source for '{sourceFileName}'");
					imageDataSource?.Dispose();
				});
			}
		}


		/// <summary>
		/// Command for closing opened source file.
		/// </summary>
		public ICommand CloseSourceFileCommand { get; }


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


		/// <summary>
		/// Get or set contrast adjustment.
		/// </summary>
		public double ContrastAdjustment
		{
			get => this.GetValue(ContrastAdjustmentProperty);
			set => this.SetValue(ContrastAdjustmentProperty, value);
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

			// detach from shared rendered images memory usage
			this.sharedRenderedImagesMemoryUsageObserverToken.Dispose();

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


		/// <summary>
		/// Get effective rotation of rendered image in degrees.
		/// </summary>
		public double EffectiveRenderedImageRotation { get; private set; } = 0.0;


		/// <summary>
		/// Get effective scaling ratio of rendered image.
		/// </summary>
		public double EffectiveRenderedImageScale { get; private set; } = 1.0;


		// Evaluate image dimensions.
		void EvaluateImageDimensions(AspectRatio aspectRatio)
		{
			// check state
			if (this.imageDataSource == null)
				return;

			// evaluate
			this.ImageRenderer.EvaluateDimensions(this.imageDataSource, aspectRatio)?.Also((it) =>
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

			// prepare
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			this.imageFilteringCancellationTokenSource = cancellationTokenSource;
			this.canSaveFilteredImage.Update(false);
			this.SetValue(IsFilteringRenderedImageProperty, true);

			// setup swap function
			void SwapImageFrames(ref ImageFrame? x, ref ImageFrame? y)
			{
				var t = x;
				x = y;
				y = t;
			}

			// check filters needed
			var filterCount = 0;
			var isColorLutFilterNeeded = false;
			if (this.canResetBrightnessAdjustment.Value || this.canResetColorAdjustment.Value || this.canResetContrastAdjustment.Value)
			{
				isColorLutFilterNeeded = true;
				++filterCount;
			}
			var isGrayscaleFilterNeeded = false;
			if (this.IsGrayscaleFilterEnabled && this.IsGrayscaleFilterSupported)
			{
				isGrayscaleFilterNeeded = true;
				++filterCount;
			}

			// allocate frames
			var filteredImageFrame1 = await this.AllocateFilteredImageFrame(renderedImageFrame);
			if (filteredImageFrame1 == null)
			{
				if (!cancellationTokenSource.IsCancellationRequested)
				{
					this.imageFilteringCancellationTokenSource = null;
					this.SetValue(IsFilteringRenderedImageProperty, false);
					this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
					this.ReportRenderedImage();
				}
				else if (this.hasPendingImageFiltering)
				{
					this.Logger.LogWarning("Start next filtering");
					this.filterImageAction.Schedule();
				}
				return;
			}
			var filteredImageFrame2 = (ImageFrame?)null;
			if (filterCount > 1)
			{
				filteredImageFrame2 = await this.AllocateFilteredImageFrame(renderedImageFrame);
				if (filteredImageFrame2 == null)
				{
					if (!cancellationTokenSource.IsCancellationRequested)
					{
						this.imageFilteringCancellationTokenSource = null;
						this.SetValue(IsFilteringRenderedImageProperty, false);
						this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
						this.ReportRenderedImage();
					}
					else if (this.hasPendingImageFiltering)
					{
						this.Logger.LogWarning("Start next filtering");
						this.filterImageAction.Schedule();
					}
					filteredImageFrame1.Dispose();
					return;
				}
			}
			var sourceImageFrame = renderedImageFrame;
			var resultImageFrame = filteredImageFrame1;
			var failedToApply = false;
			this.SetValue(InsufficientMemoryForRenderedImageProperty, false);

			// apply color LUT filter
			if (isColorLutFilterNeeded)
			{
				// prepare color LUT
				var rLut = ColorLut.BuildIdentity(renderedImageFrame.BitmapBuffer.Format);
				var gLut = rLut;
				var bLut = rLut;
				if (this.canResetBrightnessAdjustment.Value)
					ColorLut.Multiply(rLut, Math.Pow(2, this.BrightnessAdjustment));
				if (this.canResetContrastAdjustment.Value)
                {
					var middleColor = (rLut.Count - 1) / 2.0;
					var factor = this.ContrastAdjustment.Let(it => it > 0.1 ? it + 1 : -1 / (it - 1));
					ColorLut.Multiply(rLut, factor);
					ColorLut.Translate(rLut, (1 - factor) * middleColor);
                }
				if (this.canResetColorAdjustment.Value)
				{
					var rFactor = this.RedColorAdjustment.Let(it => it > 0.1 ? it + 1 : -1 / (it - 1));
					var gFactor = this.GreenColorAdjustment.Let(it => it > 0.1 ? it + 1 : -1 / (it - 1));
					var bFactor = this.BlueColorAdjustment.Let(it => it > 0.1 ? it + 1 : -1 / (it - 1));
					var correction = 3 / (rFactor + gFactor + bFactor);
					rFactor *= correction;
					gFactor *= correction;
					bFactor *= correction;
					gLut = rLut.ToArray();
					bLut = rLut.ToArray();
					ColorLut.Multiply(rLut, rFactor);
					ColorLut.Multiply(gLut, gFactor);
					ColorLut.Multiply(bLut, bFactor);
				}

				// apply filter
				var parameters = new ColorLutImageFilter.Params()
				{
					RedLookupTable = rLut,
					GreenLookupTable = gLut,
					BlueLookupTable = bLut,
					AlphaLookupTable = ColorLut.BuildIdentity(renderedImageFrame.BitmapBuffer.Format)
			};
				if (await this.ApplyImageFilterAsync(new ColorLutImageFilter(), sourceImageFrame.AsNonNull(), resultImageFrame.AsNonNull(), parameters, cancellationTokenSource.Token))
				{
					if (sourceImageFrame == renderedImageFrame)
					{
						sourceImageFrame = resultImageFrame;
						resultImageFrame = filteredImageFrame2;
					}
					else
						SwapImageFrames(ref sourceImageFrame, ref resultImageFrame);
				}
				else
					failedToApply = true;
			}

			// apply luminance filter
			if (!failedToApply && isGrayscaleFilterNeeded)
			{
				if (await this.ApplyImageFilterAsync(new LuminanceImageFilter(), sourceImageFrame.AsNonNull(), resultImageFrame.AsNonNull(), cancellationTokenSource.Token))
				{
					if (sourceImageFrame == renderedImageFrame)
					{
						sourceImageFrame = resultImageFrame;
						resultImageFrame = filteredImageFrame2;
					}
					else
						SwapImageFrames(ref sourceImageFrame, ref resultImageFrame);
				}
				else
					failedToApply = true;
			}

			// check filtering result
			if (failedToApply)
			{
				filteredImageFrame1.Dispose();
				filteredImageFrame2?.Dispose();
				if (!cancellationTokenSource.IsCancellationRequested)
				{
					this.imageFilteringCancellationTokenSource = null;
					this.SetValue(HasRenderingErrorProperty, true);
					this.SetValue(IsFilteringRenderedImageProperty, false);
					this.ReportRenderedImage();
				}
				else if (this.hasPendingImageFiltering)
				{
					this.Logger.LogDebug("Start next filtering");
					this.filterImageAction.Schedule();
				}
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
			}

			// cancellation check
			if (cancellationTokenSource.IsCancellationRequested)
			{
				filteredImageFrame1.Dispose();
				filteredImageFrame2?.Dispose();
				if (this.hasPendingImageFiltering)
				{
					this.Logger.LogDebug("Start next filtering");
					this.filterImageAction.Schedule();
				}
				return;
			}

			// complete
			this.imageFilteringCancellationTokenSource = null;
			this.filteredImageFrame?.Dispose();
			if (sourceImageFrame == filteredImageFrame1)
            {
				this.filteredImageFrame = filteredImageFrame1;
				filteredImageFrame2?.Dispose();
            }
			else
            {
				this.filteredImageFrame = filteredImageFrame2;
				filteredImageFrame1.Dispose();
			}
			this.SetValue(IsFilteringRenderedImageProperty, false);
			this.ReportRenderedImage();
		}


		/// <summary>
		/// Get or set whether rendered image should be fitted into viewport or not.
		/// </summary>
		public bool FitRenderedImageToViewport
		{
			get => this.fitRenderedImageToViewport;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (this.fitRenderedImageToViewport == value)
					return;
				this.fitRenderedImageToViewport = value;
				this.OnPropertyChanged(nameof(this.FitRenderedImageToViewport));
				if (value)
					this.EffectiveRenderedImageScale = 1.0;
				else
					this.EffectiveRenderedImageScale = this.renderedImageScale;
				this.OnPropertyChanged(nameof(this.EffectiveRenderedImageScale));
				this.UpdateCanZoomInOut();
			}
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
		/// Check whether <see cref="RenderedImage"/> is non-null or not.
		/// </summary>
		public bool HasRenderedImage { get => this.GetValue(HasRenderedImageProperty); }


		/// <summary>
		/// Check whether error was occurred when rendering or not.
		/// </summary>
		public bool HasRenderingError { get => this.GetValue(HasRenderingErrorProperty); }


		/// <summary>
		/// Check whether there is a pixel selected on rendered image or not.
		/// </summary>
		public bool HasSelectedRenderedImagePixel { get => this.GetValue(HasSelectedRenderedImagePixelProperty); }


		/// <summary>
		/// Get histograms of <see cref="RenderedImage"/>.
		/// </summary>
		public BitmapHistograms? Histograms { get => this.GetValue(HistogramsProperty); }


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
		/// Check whether brightness adjustment is supported or not.
		/// </summary>
		public bool IsBrightnessAdjustmentSupported { get => this.GetValue(IsBrightnessAdjustmentSupportedProperty); }


		/// <summary>
		/// Check whether color adjustment is supported or not.
		/// </summary>
		public bool IsColorAdjustmentSupported { get => this.GetValue(IsColorAdjustmentSupportedProperty); }


		/// <summary>
		/// Check whether contrast adjustment is supported or not.
		/// </summary>
		public bool IsContrastAdjustmentSupported { get => this.GetValue(IsContrastAdjustmentSupportedProperty); }


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
		/// Check whether source image file has been opened or not.
		/// </summary>
		public bool IsSourceFileOpened { get => this.GetValue(IsSourceFileOpenedProperty); }


		/// <summary>
		/// Get <see cref="Geometry"/> of luminance histogram.
		/// </summary>
		public Geometry? LuminanceHistogramGeometry { get => this.GetValue(LuminanceHistogramGeometryProperty); }


		/// <summary>
		/// Get maximum scaling ratio of rendered image.
		/// </summary>
		public double MaxRenderedImageScale { get; } = 10.0;


		/// <summary>
		/// Get minimum scaling ratio of rendered image.
		/// </summary>
		public double MinRenderedImageScale { get; } = 0.1;


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


		// Called when strings updated.
		protected override void OnApplicationStringsUpdated()
		{
			base.OnApplicationStringsUpdated();
			this.UpdateTitle();
		}


		// Raise PropertyChanged event for effectie bits.
		void OnEffectiveBitsChanged(int index) => this.OnPropertyChanged(index switch
		{
			0 => nameof(this.EffectiveBits1),
			1 => nameof(this.EffectiveBits2),
			2 => nameof(this.EffectiveBits3),
			_ => throw new ArgumentOutOfRangeException(),
		});


		// Called when state of loading profiles has been changed.
		void OnLoadingProfilesStateChanged(bool isLoading)
		{
			this.UpdateCanSaveDeleteProfile();
		}


		// Raise PropertyChanged event for pixel stride.
		void OnPixelStrideChanged(int index) => this.OnPropertyChanged(index switch
		{
			0 => nameof(this.PixelStride1),
			1 => nameof(this.PixelStride2),
			2 => nameof(this.PixelStride3),
			_ => throw new ArgumentOutOfRangeException(),
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
			if (property == BlueColorAdjustmentProperty
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
			else if (property == ContrastAdjustmentProperty)
			{
				this.SetValue(HasContrastAdjustmentProperty, Math.Abs((double)newValue.AsNonNull()) > 0.01);
				this.canResetContrastAdjustment.Update(this.HasContrastAdjustment && this.IsContrastAdjustmentSupported);
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Schedule(RenderImageDelay);
			}
			else if (property == DataOffsetProperty
				|| property == FramePaddingSizeProperty
				|| property == ImageHeightProperty)
			{
				this.renderImageAction.Reschedule(RenderImageDelay);
			}
			else if (property == DemosaicingProperty)
			{
				if (this.IsDemosaicingSupported)
					this.renderImageAction.Reschedule();
			}
			else if (property == FrameCountProperty)
				this.SetValue(HasMultipleFramesProperty, (long)newValue.AsNonNull() > 1);
			else if (property == FrameNumberProperty)
				this.renderImageAction.Reschedule();
			else if (property == HistogramsProperty)
				this.SetValue(HasHistogramsProperty, newValue != null);
			else if (property == ImageRendererProperty)
			{
				if (ImageRenderers.All.Contains(newValue))
				{
					if (this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterChangingRenderer))
						this.isImageDimensionsEvaluationNeeded = true;
					var imageRenderer = (IImageRenderer)newValue.AsNonNull();
					this.SetValue(HasMultipleByteOrderingsProperty, imageRenderer.Format.HasMultipleByteOrderings);
					this.SetValue(IsDemosaicingSupportedProperty, imageRenderer.Format.Category == ImageFormatCategory.Bayer);
					this.isImagePlaneOptionsResetNeeded = true;
					this.updateFilterSupportingAction.Reschedule();
					this.renderImageAction.Reschedule();
				}
				else
					this.Logger.LogError($"{newValue} is not part of available image renderer list");
			}
			else if (property == ImageWidthProperty)
			{
				if (this.Settings.GetValueOrDefault(SettingKeys.ResetImagePlaneOptionsAfterChangingImageDimensions))
					this.isImagePlaneOptionsResetNeeded = true;
				this.renderImageAction.Reschedule(RenderImageDelay);
			}
			else if (property == IsBrightnessAdjustmentSupportedProperty)
			{
				this.canResetBrightnessAdjustment.Update(this.HasBrightnessAdjustment && this.IsBrightnessAdjustmentSupported);
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Reschedule();
			}
			else if (property == IsColorAdjustmentSupportedProperty)
			{
				this.canResetColorAdjustment.Update(this.HasColorAdjustment && this.IsColorAdjustmentSupported);
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Reschedule();
			}
			else if (property == IsContrastAdjustmentSupportedProperty)
			{
				this.canResetContrastAdjustment.Update(this.HasContrastAdjustment && this.IsContrastAdjustmentSupported);
				this.updateIsFilteringImageNeededAction.Schedule();
				this.filterImageAction.Reschedule();
			}
			else if (property == IsFilteringRenderedImageNeededProperty)
			{
				if ((bool)newValue.AsNonNull())
					this.filterImageAction.Schedule();
				else
				{
					this.CancelFilteringImage();
					this.ReportRenderedImage();
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
			else if (property == IsHistogramsVisibleProperty)
				this.PersistentState.SetValue<bool>(IsInitHistogramsPanelVisible, (bool)newValue.AsNonNull());
			else if (property == IsSavingFilteredImageProperty
				|| property == IsSavingRenderedImageProperty)
			{
				this.SetValue(IsSavingImageProperty, this.IsSavingFilteredImage || this.IsSavingRenderedImage);
				this.updateIsProcessingImageAction.Schedule();
			}
			else if (property == IsSourceFileOpenedProperty)
			{
				if (this.IsSourceFileOpened)
					this.updateFilterSupportingAction.Schedule();
				else
					this.updateFilterSupportingAction.Execute();
			}
			else if (property == ProfileProperty)
			{
				this.canApplyProfile.Update(((ImageRenderingProfile)newValue.AsNonNull()).Type != ImageRenderingProfileType.Default);
				this.ApplyProfile();
			}
			else if (property == RenderedImageProperty)
			{
				if (oldValue != null)
					this.SynchronizationContext.Post(() => (oldValue as IDisposable)?.Dispose());
				this.SetValue(HasRenderedImageProperty, newValue != null);
			}
        }


		// Raise PropertyChanged event for row stride.
		void OnRowStrideChanged(int index) => this.OnPropertyChanged(index switch
		{
			0 => nameof(this.RowStride1),
			1 => nameof(this.RowStride2),
			2 => nameof(this.RowStride3),
			_ => throw new ArgumentOutOfRangeException(),
		});


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


        // Open given file as image data source.
        async Task OpenSourceFile(string? fileName)
		{
			// check state
			if (fileName == null)
				return;
			if (!this.canOpenSourceFile.Value)
			{
				this.Logger.LogError($"Cannot open '{fileName}' in current state");
				return;
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
					this.Logger.LogDebug($"Create source for '{fileName}'");
					return new FileImageDataSource(this.Application, fileName);
				}
				catch (Exception ex)
				{
					this.Logger.LogError(ex, $"Unable to create source for '{fileName}'");
					return null;
				}
			});
			if (this.IsDisposed)
			{
				this.Logger.LogWarning($"Source for '{fileName}' created after disposing.");
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
			else if (this.Settings.GetValueOrDefault(SettingKeys.UseDefaultImageRendererAfterOpeningSourceFile))
			{
				this.Logger.LogWarning($"Use default image renderer after opening source '{fileName}'");
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
		/// Get or set red color adjustment.
		/// </summary>
		public double RedColorAdjustment
		{
			get => this.GetValue(RedColorAdjustmentProperty);
			set => this.SetValue(RedColorAdjustmentProperty, value);
		}


		// Release memory of rendered images from another session.
		async Task<bool> ReleaseRenderedImageMemoryFromAnotherSession()
        {
			var maxMemoryUsage = 0L;
			var sessionToClearRenderedImage = (Session?)null;
			foreach (var candidateSession in ((Workspace)this.Owner.AsNonNull()).Sessions)
			{
				if (candidateSession == this || candidateSession.IsActivated)
					continue;
				if (candidateSession.RenderedImagesMemoryUsage > maxMemoryUsage)
				{
					maxMemoryUsage = candidateSession.RenderedImagesMemoryUsage;
					sessionToClearRenderedImage = candidateSession;
				}
			}
			if (sessionToClearRenderedImage != null)
			{
				this.Logger.LogWarning($"Release rendered image of {sessionToClearRenderedImage}");
				if (sessionToClearRenderedImage.ClearRenderedImage(true))
				{
					await Task.Delay(1000);
					return true;
				}
				this.Logger.LogError($"Failed to release rendered image of {sessionToClearRenderedImage}");
				return false;
			}
			this.Logger.LogWarning("No deactivated session to release rendered image");
			return false;
		}


		// Release token for rendered image memory usage.
		void ReleaseRenderedImageMemoryUsage(RenderedImageMemoryUsageToken token)
		{
			var maxUsage = this.Settings.GetValueOrDefault(SettingKeys.MaxRenderedImagesMemoryUsageMB) << 20;
			if (!this.IsDisposed)
				this.SetValue(RenderedImagesMemoryUsageProperty, this.RenderedImagesMemoryUsage - token.DataSize);
			SharedRenderedImagesMemoryUsage.Decrease(token.DataSize);
			this.Logger.LogDebug($"Release {token.DataSize.ToFileSizeString()} for rendered image, total: {SharedRenderedImagesMemoryUsage.Value.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
		}


		/// <summary>
		/// Get rendered image.
		/// </summary>
		public IBitmap? RenderedImage { get => this.GetValue(RenderedImageProperty); }


		/// <summary>
		/// Get or set scaling ratio of rendered image.
		/// </summary>
		public double RenderedImageScale
		{
			get => this.renderedImageScale;
			set
			{
				this.VerifyAccess();
				this.VerifyDisposed();
				if (!double.IsFinite(value))
					throw new ArgumentOutOfRangeException();
				if (value < this.MinRenderedImageScale)
					value = this.MinRenderedImageScale;
				else if (value > this.MaxRenderedImageScale)
					value = this.MaxRenderedImageScale;
				if (Math.Abs(this.renderedImageScale - value) <= 0.001)
					return;
				this.renderedImageScale = value;
				this.OnPropertyChanged(nameof(this.RenderedImageScale));
				if (!this.fitRenderedImageToViewport)
				{
					this.EffectiveRenderedImageScale = value;
					this.OnPropertyChanged(nameof(this.EffectiveRenderedImageScale));
				}
			}
		}


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
			this.isFirstImageRenderingForSource = false;
			this.hasPendingImageRendering = false;

			// evaluate dimensions
			if (this.isImageDimensionsEvaluationNeeded)
			{
				this.Logger.LogDebug($"Evaluate dimensions of image for '{sourceFileName}'");
				this.isImageDimensionsEvaluationNeeded = false;
				imageRenderer.EvaluateDimensions(imageDataSource, this.Settings.GetValueOrDefault(SettingKeys.DefaultImageDimensionsEvaluationAspectRatio))?.Also((it) =>
				{
					this.SetValue(ImageWidthProperty, it.Width);
					this.SetValue(ImageHeightProperty, it.Height);
					this.renderImageAction.Cancel(); // prevent re-rendering caused by change of dimensions
				});
			}

			// sync format information
			var planeDescriptors = imageRenderer.Format.PlaneDescriptors;
			if (this.ImagePlaneCount != planeDescriptors.Count)
			{
				this.SetValue(ImagePlaneCountProperty, planeDescriptors.Count);
				this.SetValue(HasImagePlane2Property, planeDescriptors.Count >= 2);
				this.SetValue(HasImagePlane3Property, planeDescriptors.Count >= 3);
			}
			for (var i = planeDescriptors.Count - 1; i >= 0; --i)
			{
				this.SetValue(i switch
				{
					0 => IsAdjustableEffectiveBits1Property,
					1 => IsAdjustableEffectiveBits2Property,
					2 => IsAdjustableEffectiveBits3Property,
					_ => throw new ArgumentException(),
				}, planeDescriptors[i].IsAdjustableEffectiveBits);
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
					this.pixelStrides[i] = planeOptions.PixelStride;
					this.rowStrides[i] = planeOptions.RowStride;
					this.OnEffectiveBitsChanged(i);
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
						it.PixelStride = this.pixelStrides[i];
						it.RowStride = this.rowStrides[i];
						return it;
					});
				}
			}

			// calculate frame count and index
			var renderingOptions = new ImageRenderingOptions()
			{
				ByteOrdering = this.ByteOrdering,
				DataOffset = this.DataOffset,
				Demosaicing = (this.IsDemosaicingSupported && this.Demosaicing),
			};
			var frameNumber = this.FrameNumber;
			var frameDataSize = imageRenderer.EvaluateSourceDataSize(this.ImageWidth, this.ImageHeight, renderingOptions, planeOptionsList);
			try
			{
				var totalDataSize = imageDataSource.Size - this.DataOffset;
				var frameCount = Math.Max(1, totalDataSize / frameDataSize);
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
				this.Logger.LogError(ex, $"Unable to update frame count and index of '{this.SourceFileName}'");
				this.SetValue(HasRenderingErrorProperty, true);
				return;
			}

			// update state
			this.canSaveRenderedImage.Update(false);
			this.SetValue(IsRenderingImageProperty, true);

			// create rendered image
			var cancellationTokenSource = new CancellationTokenSource();
			this.imageRenderingCancellationTokenSource = cancellationTokenSource;
			var renderedImageFrame = await this.AllocateRenderedImageFrame(frameNumber, imageRenderer.RenderedFormat, this.ImageWidth, this.ImageHeight);
			if (renderedImageFrame == null)
			{
				if (!cancellationTokenSource.IsCancellationRequested)
				{
					this.imageRenderingCancellationTokenSource = null;
					if (this.IsActivated)
						this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
					this.SetValue(IsRenderingImageProperty, false);
					this.ReportRenderedImage();
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
				return;
			}

			// update state
			this.SetValue(InsufficientMemoryForRenderedImageProperty, false);

			// render
			this.Logger.LogDebug($"Render image for '{sourceFileName}', dimensions: {this.ImageWidth}x{this.ImageHeight}");
			var exception = (Exception?)null;
			try
			{
				renderingOptions.DataOffset += ((frameDataSize + this.FramePaddingSize) * (frameNumber - 1));
				await imageRenderer.Render(imageDataSource, renderedImageFrame.BitmapBuffer, renderingOptions, planeOptionsList, cancellationTokenSource.Token);
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
				this.Logger.LogWarning($"Image rendering for '{sourceFileName}' has been cancelled");
				this.SynchronizationContext.Post(renderedImageFrame.Dispose);
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
				this.Logger.LogDebug($"Image for '{sourceFileName}' rendered");
			else
			{
				this.Logger.LogError(exception, $"Error occurred while rendering image for '{sourceFileName}'");
				renderedImageFrame.Dispose();
			}
			this.renderedImageFrame?.Dispose();
			this.renderedImageFrame = renderedImageFrame;
			if (exception == null)
			{
				// update state
				this.SetValue(HasRenderingErrorProperty, false);
				this.SetValue(SourceDataSizeProperty, frameDataSize);
				this.canMoveToNextFrame.Update(frameNumber < this.FrameCount);
				this.canMoveToPreviousFrame.Update(frameNumber > 1);

				// filter image or report now
				if (this.IsFilteringRenderedImageNeeded)
				{
					this.Logger.LogDebug("Continue filtering image after rendering");
					this.FilterImage(renderedImageFrame);
				}
				else
					this.ReportRenderedImage();
			}
			else
			{
				this.SetValue(HasRenderingErrorProperty, true);
				this.canMoveToNextFrame.Update(false);
				this.canMoveToPreviousFrame.Update(false);
				this.ReportRenderedImage();
			}
			this.SetValue(IsRenderingImageProperty, false);
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
				this.Logger.LogDebug($"Request {dataSize.ToFileSizeString()} for rendered image, total: {totalMemoryUsage.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
				return new RenderedImageMemoryUsageToken(this, dataSize);
			}
			this.Logger.LogError($"Unable to request {dataSize.ToFileSizeString()} for rendered image, total: {SharedRenderedImagesMemoryUsage.Value.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
			return null;
		}


		// Report rendered image according to current state.
		async void ReportRenderedImage()
		{
			var imageFrame = this.IsFilteringRenderedImageNeeded ? this.filteredImageFrame : this.renderedImageFrame;
			if (imageFrame != null)
			{
				// convert to Avalonia bitmap
				var bitmap = await imageFrame.BitmapBuffer.CreateAvaloniaBitmapAsync();
				var currentImageFrame = this.IsFilteringRenderedImageNeeded ? this.filteredImageFrame : this.renderedImageFrame;
				if (currentImageFrame != imageFrame)
					return;

				// update state
				this.canSaveFilteredImage.Update(!this.IsFilteringRenderedImage && this.filteredImageFrame != null);
				this.canSaveRenderedImage.Update(!this.IsSavingRenderedImage);
				this.SetValue(HasRenderingErrorProperty, false);
				this.SetValue(InsufficientMemoryForRenderedImageProperty, false);
				this.SetValue(HistogramsProperty, imageFrame.Histograms);
				this.SetValue(RenderedImageProperty, bitmap);
			}
			else
			{
				this.canSaveFilteredImage.Update(false);
				this.canSaveRenderedImage.Update(false);
				this.SetValue(HistogramsProperty, null);
				this.SetValue(RenderedImageProperty, null);
			}
		}


		// Reset brightness adjustment.
		void ResetBrightnessAdjustment()
        {
			this.VerifyAccess();
			if (this.IsDisposed || !this.canResetBrightnessAdjustment.Value)
				return;
			this.BrightnessAdjustment = 0;
        }


		/// <summary>
		/// Command to reset <see cref="BrightnessAdjustment"/>.
		/// </summary>
		public ICommand ResetBrightnessAdjustmentCommand { get; }


		// Reset color adjustment.
		void ResetColorAdjustment()
		{
			this.VerifyAccess();
			if (this.IsDisposed || !this.canResetColorAdjustment.Value)
				return;
			this.BlueColorAdjustment = 0;
			this.GreenColorAdjustment = 0;
			this.RedColorAdjustment = 0;
		}


		/// <summary>
		/// Command to reset <see cref="BlueColorAdjustment"/>, <see cref="GreenColorAdjustment"/> and <see cref="RedColorAdjustment"/>.
		/// </summary>
		public ICommand ResetColorAdjustmentCommand { get; }


		// Reset contrast adjustment.
		void ResetContrastAdjustment()
		{
			this.VerifyAccess();
			if (this.IsDisposed || !this.canResetContrastAdjustment.Value)
				return;
			this.ContrastAdjustment = 0;
		}


		/// <summary>
		/// Command to reset <see cref="ContrastAdjustment"/>.
		/// </summary>
		public ICommand ResetContrastAdjustmentCommand { get; }


		// Restore state.
		async void RestoreState(JsonElement savedState)
        {
			// check parameter
			if (savedState.ValueKind != JsonValueKind.Object)
				return;

			this.Logger.LogWarning("Start restoring state");

			// load rendering parameters
			if (!savedState.TryGetProperty(nameof(SourceFileName), out var jsonProperty) || jsonProperty.ValueKind != JsonValueKind.String)
			{
				this.Logger.LogDebug("No source file to restore");
				return;
			}
			var fileName = jsonProperty.GetString().AsNonNull();
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
					this.Logger.LogWarning($"Cannot find image renderer of '{jsonProperty.GetString()}' to restore");
				}
				return null;
			});
			var dataOffset = 0L;
			var framePaddingSize = 0L;
			var byteOrdering = ByteOrdering.BigEndian;
			var demosaicing = true;
			var width = 1;
			var height = 1;
			var effectiveBits = new int[this.effectiveBits.Length];
			var pixelStrides = new int[this.pixelStrides.Length];
			var rowStrides = new int[this.rowStrides.Length];
			if (savedState.TryGetProperty(nameof(DataOffset), out jsonProperty))
				jsonProperty.TryGetInt64(out dataOffset);
			if (savedState.TryGetProperty(nameof(FramePaddingSize), out jsonProperty))
				jsonProperty.TryGetInt64(out framePaddingSize);
			if (savedState.TryGetProperty(nameof(ByteOrdering), out jsonProperty))
				Enum.TryParse(jsonProperty.GetString(), out byteOrdering);
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

			// load filtering parameters
			var blueColorAdjustment = 0.0;
			var brightnessAdjustment = 0.0;
			var contrastAdjustment = 0.0;
			var greenColorAdjustment = 0.0;
			var isGrayscaleFilterEnabled = false;
			var redColorAdjustment = 0.0;
			if (savedState.TryGetProperty(nameof(BlueColorAdjustment), out jsonProperty))
				jsonProperty.TryGetDouble(out blueColorAdjustment);
			if (savedState.TryGetProperty(nameof(BrightnessAdjustment), out jsonProperty))
				jsonProperty.TryGetDouble(out brightnessAdjustment);
			if (savedState.TryGetProperty(nameof(ContrastAdjustment), out jsonProperty))
				jsonProperty.TryGetDouble(out contrastAdjustment);
			if (savedState.TryGetProperty(nameof(GreenColorAdjustment), out jsonProperty))
				jsonProperty.TryGetDouble(out greenColorAdjustment);
			if (savedState.TryGetProperty(nameof(IsGrayscaleFilterEnabled), out jsonProperty))
				isGrayscaleFilterEnabled = jsonProperty.ValueKind != JsonValueKind.False;
			if (savedState.TryGetProperty(nameof(RedColorAdjustment), out jsonProperty))
				jsonProperty.TryGetDouble(out redColorAdjustment);

			// load displaying parameters
			var fitToViewport = true;
			var frameNumber = 1L;
			var isHistogramsVisible = this.PersistentState.GetValueOrDefault(IsInitHistogramsPanelVisible);
			var rotation = 0;
			var scale = 1.0;
			if (savedState.TryGetProperty(nameof(FitRenderedImageToViewport), out jsonProperty))
				fitToViewport = jsonProperty.ValueKind != JsonValueKind.False;
			if (savedState.TryGetProperty(nameof(FrameNumber), out jsonProperty) && jsonProperty.TryGetInt64(out frameNumber))
				frameNumber = Math.Max(1, frameNumber);
			if (savedState.TryGetProperty(nameof(EffectiveRenderedImageRotation), out jsonProperty))
				jsonProperty.TryGetInt32(out rotation);
			if (savedState.TryGetProperty(nameof(IsHistogramsVisible), out jsonProperty))
				isHistogramsVisible = jsonProperty.ValueKind != JsonValueKind.False;
			if (savedState.TryGetProperty(nameof(RenderedImageScale), out jsonProperty))
				jsonProperty.TryGetDouble(out scale);

			// open source file
			await this.OpenSourceFile(fileName);
			if (!this.IsSourceFileOpened)
			{
				this.Logger.LogError($"Unable to restore source file '{fileName}'");
				return;
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
			this.SetValue(DemosaicingProperty, demosaicing);
			this.SetValue(ImageWidthProperty, width);
			this.SetValue(ImageHeightProperty, height);
			for (var i = effectiveBits.Length - 1; i >= 0; --i)
				this.ChangeEffectiveBits(i, effectiveBits[i]);
			for (var i = pixelStrides.Length - 1; i >= 0; --i)
				this.ChangePixelStride(i, pixelStrides[i]);
			for (var i = rowStrides.Length - 1; i >= 0; --i)
				this.ChangeRowStride(i, rowStrides[i]);

			// apply filtering parameters
			this.SetValue(BlueColorAdjustmentProperty, blueColorAdjustment);
			this.SetValue(BrightnessAdjustmentProperty, brightnessAdjustment);
			this.SetValue(ContrastAdjustmentProperty, contrastAdjustment);
			this.SetValue(GreenColorAdjustmentProperty, greenColorAdjustment);
			this.SetValue(IsGrayscaleFilterEnabledProperty, isGrayscaleFilterEnabled);
			this.SetValue(RedColorAdjustmentProperty, redColorAdjustment);

			// apply displaying parameters
			this.EffectiveRenderedImageRotation = rotation;
			this.FitRenderedImageToViewport = fitToViewport;
			this.FrameNumber = frameNumber;
			this.SetValue(IsHistogramsVisibleProperty, isHistogramsVisible);
			this.RenderedImageScale = scale;

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
			this.EffectiveRenderedImageRotation = (int)(this.EffectiveRenderedImageRotation + 0.5) switch
			{
				0 => 270.0,
				180 => 90.0,
				270 => 180.0,
				_ => 0.0,
			};
			this.OnPropertyChanged(nameof(this.EffectiveRenderedImageRotation));
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
			this.EffectiveRenderedImageRotation = (int)(this.EffectiveRenderedImageRotation + 0.5) switch
			{
				0 => 90.0,
				90 => 180.0,
				180 => 270.0,
				_ => 0.0,
			};
			this.OnPropertyChanged(nameof(this.EffectiveRenderedImageRotation));
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
				this.Logger.LogError($"Unable to add profile '{name}'");
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
		async Task<bool> SaveFilteredImage(string? fileName)
		{
			// check state
			if (fileName == null)
				return false;
			if (!this.canSaveFilteredImage.Value)
				return false;

			// open file
			this.canSaveFilteredImage.Update(false);
			this.SetValue(IsSavingFilteredImageProperty, true);
			var stream = await Task.Run(() =>
			{
				try
				{
					return new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
				}
				catch (Exception ex)
				{
					this.Logger.LogError(ex, $"Unable to open {fileName}");
					this.canSaveFilteredImage.Update(!this.IsFilteringRenderedImage);
					this.SetValue(IsSavingFilteredImageProperty, false);
					return null;
				}
			});
			if (stream == null)
			{
				this.canSaveFilteredImage.Update(!this.IsFilteringRenderedImage);
				this.SetValue(IsSavingFilteredImageProperty, false);
				return false;
			}

			// save
			var task = this.SaveFilteredImage(stream);
			_ = this.WaitForNecessaryTaskAsync(task);
			var result = await task;

			// close file
			await Task.Run(() =>
			{
				try
				{
					stream.Close();
				}
				catch
				{ }
			});

			// complete
			this.canSaveFilteredImage.Update(!this.IsFilteringRenderedImage);
			this.SetValue(IsSavingFilteredImageProperty, false);
			return result;
		}
		async Task<bool> SaveFilteredImage(Stream stream)
		{
			if (this.filteredImageFrame != null)
				return await this.SaveImage(this.filteredImageFrame.BitmapBuffer, stream);
			return false;
		}


		/// <summary>
		/// Command for saving filtered image to file or stream.
		/// </summary>
		public ICommand SaveFilteredImageCommand { get; }


		// Save given image.
		async Task<bool> SaveImage(IBitmapBuffer imageBuffer, Stream stream)
		{
			using var sharedImageBuffer = imageBuffer.Share();
			return await Task.Run(async () =>
			{
				try
				{
#if WINDOWS10_0_17763_0_OR_GREATER
					using var bitmap = sharedImageBuffer.CreateSystemDrawingBitmap();
					bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
#else
					using var bitmap = await sharedImageBuffer.CreateAvaloniaBitmapAsync();
					bitmap.Save(stream);
#endif
					return true;
				}
				catch (Exception ex)
				{
					this.Logger.LogError(ex, "Unable to save rendered image");
					return false;
				}
			});
		}


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
				this.Logger.LogError(ex, $"Failed to save profile '{profile.Name}'");
			}
		}


		/// <summary>
		/// Command to save parameters to current profile.
		/// </summary>
		public ICommand SaveProfileCommand { get; }


		// Save rendered image.
		async Task<bool> SaveRenderedImage(string? fileName)
		{
			// check state
			if (fileName == null)
				return false;
			if (!this.canSaveRenderedImage.Value)
				return false;

			// open file
			this.canSaveRenderedImage.Update(false);
			this.SetValue(IsSavingRenderedImageProperty, true);
			var stream = await Task.Run(() =>
			{
				try
				{
					return new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
				}
				catch (Exception ex)
				{
					this.Logger.LogError(ex, $"Unable to open {fileName}");
					this.canSaveRenderedImage.Update(!this.IsRenderingImage);
					this.SetValue(IsSavingRenderedImageProperty, false);
					return null;
				}
			});
			if (stream == null)
			{
				this.canSaveRenderedImage.Update(!this.IsRenderingImage);
				this.SetValue(IsSavingRenderedImageProperty, false);
				return false;
			}

			// save
			var task = this.SaveRenderedImage(stream);
			_ = this.WaitForNecessaryTaskAsync(task);
			var result = await task;

			// close file
			await Task.Run(() =>
			{
				try
				{
					stream.Close();
				}
				catch
				{ }
			});

			// complete
			this.canSaveRenderedImage.Update(!this.IsRenderingImage);
			this.SetValue(IsSavingRenderedImageProperty, false);
			return result;
		}
		async Task<bool> SaveRenderedImage(Stream stream)
		{
			if (this.renderedImageFrame != null)
				return await this.SaveImage(this.renderedImageFrame.BitmapBuffer, stream);
			return false;
		}


		/// <summary>
		/// Command for saving rendered image to file or stream.
		/// </summary>
		public ICommand SaveRenderedImageCommand { get; }


		/// <summary>
		/// Save instance state in JSON format.
		/// </summary>
		public void SaveState(Utf8JsonWriter writer)
		{
			writer.WriteStartObject();
			if (!string.IsNullOrEmpty(this.SourceFileName))
			{
				// file and profile
				writer.WriteString(nameof(SourceFileName), this.SourceFileName.AsNonNull());

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
				writer.WriteBoolean(nameof(Demosaicing), this.Demosaicing);
				writer.WriteNumber(nameof(ImageWidth), this.ImageWidth);
				writer.WriteNumber(nameof(ImageHeight), this.ImageHeight);
				writer.WritePropertyName("EffectiveBits");
				writer.WriteStartArray();
				foreach (var n in this.effectiveBits)
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
				writer.WriteBoolean(nameof(IsGrayscaleFilterEnabled), this.IsGrayscaleFilterEnabled);

				// displaying parameters
				writer.WriteNumber(nameof(EffectiveRenderedImageRotation), (int)(this.EffectiveRenderedImageRotation + 0.5));
				writer.WriteBoolean(nameof(FitRenderedImageToViewport), this.fitRenderedImageToViewport);
				writer.WriteNumber(nameof(FrameNumber), this.FrameNumber);
				writer.WriteBoolean(nameof(IsHistogramsVisible), this.IsHistogramsVisible);
				writer.WriteNumber(nameof(RenderedImageScale), this.renderedImageScale);
			}
			writer.WriteEndObject();
		}


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
			var renderedImageBuffer = this.renderedImageFrame?.BitmapBuffer;
			if (renderedImageBuffer == null 
				|| x < 0 || x >= renderedImageBuffer.Width
				|| y < 0 || y >= renderedImageBuffer.Height)
			{
				if (this.HasSelectedRenderedImagePixel)
				{
					this.SetValue(HasSelectedRenderedImagePixelProperty, false);
					this.SetValue(SelectedRenderedImagePixelColorProperty, default);
					this.SetValue(SelectedRenderedImagePixelPositionXProperty, -1);
					this.SetValue(SelectedRenderedImagePixelPositionYProperty, -1);
				}
			}
			else
			{
				// get color of pixel
				var color = renderedImageBuffer.Memory.Pin((baseAddress) =>
				{
					var pixelPtr = (byte*)baseAddress + renderedImageBuffer.GetPixelOffset(x, y);
					return renderedImageBuffer.Format switch
					{
						BitmapFormat.Bgra32 => new Color(pixelPtr[3], pixelPtr[2], pixelPtr[1], pixelPtr[0]),
						_ => default,
					};
				});

				// update state
				this.SetValue(SelectedRenderedImagePixelColorProperty, color);
				this.SetValue(SelectedRenderedImagePixelPositionXProperty, x);
				this.SetValue(SelectedRenderedImagePixelPositionYProperty, y);
				this.SetValue(HasSelectedRenderedImagePixelProperty, true);
			}
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
			if (this.fitRenderedImageToViewport || !this.IsSourceFileOpened)
			{
				this.canZoomIn.Update(false);
				this.canZoomOut.Update(false);
			}
			else
			{
				this.canZoomIn.Update(this.renderedImageScale < (this.MaxRenderedImageScale - 0.001));
				this.canZoomOut.Update(this.renderedImageScale > (this.MinRenderedImageScale + 0.001));
			}
		}


		// Update title.
		void UpdateTitle()
		{
			// check state
			if (this.IsDisposed)
				return;

			// generate title
			string? title = null;
			if (this.SourceFileName != null)
				title = Path.GetFileName(this.SourceFileName);
			else
				title = this.Application.GetString("Session.EmptyTitle");

			// update property
			if (this.Title != title)
			{
				this.Title = title;
				this.OnPropertyChanged(nameof(this.Title));
			}
		}


		// Write current parameters to given profile.
		void WriteParametersToProfile(ImageRenderingProfile profile)
		{
			profile.Renderer = this.ImageRenderer;
			profile.DataOffset = this.DataOffset;
			profile.FramePaddingSize = this.FramePaddingSize;
			profile.ByteOrdering = this.ByteOrdering;
			profile.Demosaicing = this.Demosaicing;
			profile.Width = this.ImageWidth;
			profile.Height = this.ImageHeight;
			profile.EffectiveBits = this.effectiveBits;
			profile.PixelStrides = this.pixelStrides;
			profile.RowStrides = this.rowStrides;
		}


		// Zoom-in rendered image.
		void ZoomIn()
		{
			if (!this.canZoomIn.Value)
				return;
			this.RenderedImageScale = this.RenderedImageScale.Let((it) =>
			{
				if (it <= 0.999)
					return (Math.Floor(it * 20) + 1) / 20;
				return (Math.Floor(it * 2) + 1) / 2;
			});
		}


		/// <summary>
		/// Command of zooming-in rendered image.
		/// </summary>
		public ICommand ZoomInCommand { get; }


		// Zoom-out rendered image.
		void ZoomOut()
		{
			if (!this.canZoomOut.Value)
				return;
			this.RenderedImageScale = this.RenderedImageScale.Let((it) =>
			{
				if (it <= 1.001)
					return (Math.Ceiling(it * 20) - 1) / 20;
				return (Math.Ceiling(it * 2) - 1) / 2;
			});
		}


		/// <summary>
		/// Command of zooming-out rendered image.
		/// </summary>
		public ICommand ZoomOutCommand { get; }
	}
}
