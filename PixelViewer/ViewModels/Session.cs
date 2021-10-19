using Avalonia.Media;
using Avalonia.Media.Imaging;
using Carina.PixelViewer.Media;
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
		/// Property of <see cref="ByteOrdering"/>.
		/// </summary>
		public static readonly ObservableProperty<ByteOrdering> ByteOrderingProperty = ObservableProperty.Register<Session, ByteOrdering>(nameof(ByteOrdering), ByteOrdering.BigEndian);
		/// <summary>
		/// Property of <see cref="DataOffset"/>.
		/// </summary>
		public static readonly ObservableProperty<long> DataOffsetProperty = ObservableProperty.Register<Session, long>(nameof(DataOffset), 0L);
		/// <summary>
		/// Property of <see cref="FrameCount"/>.
		/// </summary>
		public static readonly ObservableProperty<int> FrameCountProperty = ObservableProperty.Register<Session, int>(nameof(FrameCount), 0);
		/// <summary>
		/// Property of <see cref="FrameNumber"/>.
		/// </summary>
		public static readonly ObservableProperty<int> FrameNumberProperty = ObservableProperty.Register<Session, int>(nameof(FrameNumber), 0);
		/// <summary>
		/// Property of <see cref="FramePaddingSize"/>.
		/// </summary>
		public static readonly ObservableProperty<long> FramePaddingSizeProperty = ObservableProperty.Register<Session, long>(nameof(FramePaddingSize), 0L);
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
		/// Property of <see cref="IsSavingRenderedImage"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsSavingRenderedImageProperty = ObservableProperty.Register<Session, bool>(nameof(IsSavingRenderedImage));
		/// <summary>
		/// Property of <see cref="IsSourceFileOpened"/>.
		/// </summary>
		public static readonly ObservableProperty<bool> IsSourceFileOpenedProperty = ObservableProperty.Register<Session, bool>(nameof(IsSourceFileOpened));
		/// <summary>
		/// Property of <see cref="Profile"/>.
		/// </summary>
		public static readonly ObservableProperty<ImageRenderingProfile> ProfileProperty = ObservableProperty.Register<Session, ImageRenderingProfile>(nameof(Profile), ImageRenderingProfile.Default);
		/// <summary>
		/// Property of <see cref="RenderedImage"/>.
		/// </summary>
		public static readonly ObservableProperty<IBitmap?> RenderedImageProperty = ObservableProperty.Register<Session, IBitmap?>(nameof(RenderedImage));
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


		// Constants.
		const int RenderImageDelay = 500;


		// Static fields.
		static long TotalRenderedImagesMemoryUsage;


		// Fields.
		readonly MutableObservableBoolean canApplyProfile = new MutableObservableBoolean();
		readonly MutableObservableBoolean canMoveToNextFrame = new MutableObservableBoolean();
		readonly MutableObservableBoolean canMoveToPreviousFrame = new MutableObservableBoolean();
		readonly MutableObservableBoolean canOpenSourceFile = new MutableObservableBoolean(true);
		readonly MutableObservableBoolean canSaveAsNewProfile = new MutableObservableBoolean();
		readonly MutableObservableBoolean canSaveOrDeleteProfile = new MutableObservableBoolean();
		readonly MutableObservableBoolean canSaveRenderedImage = new MutableObservableBoolean();
		readonly MutableObservableBoolean canZoomIn = new MutableObservableBoolean();
		readonly MutableObservableBoolean canZoomOut = new MutableObservableBoolean();
		readonly int[] effectiveBits = new int[ImageFormat.MaxPlaneCount];
		ImageRenderingProfile? fileFormatProfile;
		bool fitRenderedImageToViewport = true;
		bool hasPendingImageRendering;
		IImageDataSource? imageDataSource;
		CancellationTokenSource? imageRenderingCancellationTokenSource;
		bool isFirstImageRenderingForSource = true;
		bool isImageDimensionsEvaluationNeeded = true;
		bool isImagePlaneOptionsResetNeeded = true;
		readonly int[] pixelStrides = new int[ImageFormat.MaxPlaneCount];
		readonly SortedObservableList<ImageRenderingProfile> profiles = new SortedObservableList<ImageRenderingProfile>(CompareProfiles);
		IBitmapBuffer? renderedImageBuffer;
		IDisposable? renderedImageMemoryUsageToken;
		double renderedImageScale = 1.0;
		readonly ScheduledAction renderImageOperation;
		readonly int[] rowStrides = new int[ImageFormat.MaxPlaneCount];
		readonly ScheduledAction updateIsProcessingImageAction;


		/// <summary>
		/// Initialize new <see cref="Session"/> instance.
		/// </summary>
		public Session(Workspace workspace) : base(workspace)
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
			this.OpenSourceFileCommand = new Command<string>(filePath => this.OpenSourceFile(filePath), this.canOpenSourceFile);
			this.RotateLeftCommand = new Command(this.RotateLeft, isSrcFileOpenedObservable);
			this.RotateRightCommand = new Command(this.RotateRight, isSrcFileOpenedObservable);
			this.SaveAsNewProfileCommand = new Command<string>(name => this.SaveAsNewProfile(name), this.canSaveAsNewProfile);
			this.SaveProfileCommand = new Command(() => this.SaveProfile(), this.canSaveOrDeleteProfile);
			this.SaveRenderedImageCommand = new Command<object?>(this.SaveRenderedImage, this.canSaveRenderedImage);
			this.ZoomInCommand = new Command(this.ZoomIn, this.canZoomIn);
			this.ZoomOutCommand = new Command(this.ZoomOut, this.canZoomOut);

			// setup operations
			this.renderImageOperation = new ScheduledAction(this, this.RenderImage);
			this.updateIsProcessingImageAction = new ScheduledAction(() =>
			{
				if (this.IsDisposed)
					return;
				this.SetValue(IsProcessingImageProperty, this.IsOpeningSourceFile || this.IsRenderingImage || this.IsSavingRenderedImage);
			});

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
				if (this.renderImageOperation.IsScheduled)
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
		/// Get or set byte ordering.
		/// </summary>
		public ByteOrdering ByteOrdering
        {
			get => this.GetValue(ByteOrderingProperty);
			set => this.SetValue(ByteOrderingProperty, value);
		}


		// Cancel rendering image.
		bool CancelRenderingImage(bool cancelPendingRendering = false)
		{
			// stop timer
			this.renderImageOperation.Cancel();

			// cancel
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
			this.renderImageOperation.Reschedule(RenderImageDelay);
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
			this.renderImageOperation.Reschedule(RenderImageDelay);
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
			this.renderImageOperation.Reschedule(RenderImageDelay);
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
				this.SetValue(IsSourceFileOpenedProperty, false);
				this.canMoveToNextFrame.Update(false);
				this.canMoveToPreviousFrame.Update(false);
				this.canSaveRenderedImage.Update(false);
				this.SetValue(SourceDataSizeProperty, 0);
				this.UpdateCanSaveDeleteProfile();
			}
			var renderedImage = this.RenderedImage;
			if (renderedImage != null)
			{
				if (!disposing)
					this.SetValue(RenderedImageProperty, null);
				renderedImage.Dispose();
				this.renderedImageBuffer = this.renderedImageBuffer.DisposeAndReturnNull();
				this.renderedImageMemoryUsageToken = this.renderedImageMemoryUsageToken.DisposeAndReturnNull();
			}
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
		/// Get or set offset to first byte of data to render image.
		/// </summary>
		public long DataOffset
        {
			get => this.GetValue(DataOffsetProperty);
			set => this.SetValue(DataOffsetProperty, value);
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
					this.renderImageOperation.ExecuteIfScheduled();
				}
			});
		}


		/// <summary>
		/// Command for image dimension evaluation.
		/// </summary>
		public ICommand EvaluateImageDimensionsCommand { get; }


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
		public int FrameCount { get => this.GetValue(FrameCountProperty); }


		/// <summary>
		/// Get of set index of frame to render.
		/// </summary>
		public int FrameNumber
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
		/// Check whether rendered image is being saved or not.
		/// </summary>
		public bool IsSavingRenderedImage { get => this.GetValue(IsSavingRenderedImageProperty); }


		/// <summary>
		/// Check whether source image file has been opened or not.
		/// </summary>
		public bool IsSourceFileOpened { get => this.GetValue(IsSourceFileOpenedProperty); }


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
			if (property == ByteOrderingProperty)
			{
				if (this.HasMultipleByteOrderings)
					this.renderImageOperation.Reschedule();
			}
			else if (property == DataOffsetProperty
				|| property == FramePaddingSizeProperty
				|| property == ImageHeightProperty)
			{
				this.renderImageOperation.Reschedule(RenderImageDelay);
			}
			else if (property == FrameCountProperty)
				this.SetValue(HasMultipleFramesProperty, (int)newValue.AsNonNull() > 1);
			else if (property == FrameNumberProperty)
				this.renderImageOperation.Reschedule();
			else if (property == ImageRendererProperty)
			{
				if (ImageRenderers.All.Contains(newValue))
				{
					if (this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterChangingRenderer))
						this.isImageDimensionsEvaluationNeeded = true;
					this.SetValue(HasMultipleByteOrderingsProperty, ((IImageRenderer)newValue.AsNonNull()).Format.HasMultipleByteOrderings);
					this.isImagePlaneOptionsResetNeeded = true;
					this.renderImageOperation.Reschedule();
				}
				else
					this.Logger.LogError($"{newValue} is not part of available image renderer list");
			}
			else if (property == ImageWidthProperty)
			{
				if (this.Settings.GetValueOrDefault(SettingKeys.ResetImagePlaneOptionsAfterChangingImageDimensions))
					this.isImagePlaneOptionsResetNeeded = true;
				this.renderImageOperation.Reschedule(RenderImageDelay);
			}
			else if (property == IsOpeningSourceFileProperty
				|| property == IsRenderingImageProperty
				|| property == IsSavingRenderedImageProperty)
			{
				this.updateIsProcessingImageAction.Schedule();
			}
			else if (property == ProfileProperty)
			{
				this.canApplyProfile.Update(((ImageRenderingProfile)newValue.AsNonNull()).Type != ImageRenderingProfileType.Default);
				this.ApplyProfile();
			}
			else if (property == RenderedImageProperty)
				this.SetValue(HasRenderedImageProperty, newValue != null);
        }


		// Raise PropertyChanged event for row stride.
		void OnRowStrideChanged(int index) => this.OnPropertyChanged(index switch
		{
			0 => nameof(this.RowStride1),
			1 => nameof(this.RowStride2),
			2 => nameof(this.RowStride3),
			_ => throw new ArgumentOutOfRangeException(),
		});


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
        async void OpenSourceFile(string? fileName)
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
			this.renderImageOperation.Reschedule();

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


		// Release token for rendered image memory usage.
		void ReleaseRenderedImageMemoryUsage(RenderedImageMemoryUsageToken token)
		{
			var maxUsage = this.Settings.GetValueOrDefault(SettingKeys.MaxRenderedImagesMemoryUsageMB) << 20;
			TotalRenderedImagesMemoryUsage -= token.DataSize;
			this.Logger.LogDebug($"Release {token.DataSize.ToFileSizeString()} for rendered image, total: {TotalRenderedImagesMemoryUsage.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
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


		// Render image according to current state.
		async void RenderImage()
		{
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

			// create rendered image
			var renderedImageDataSize = ((long)this.ImageWidth * this.ImageHeight * imageRenderer.RenderedFormat.GetByteSize() * 2); // need double space because Avalonia will copy the bitmap data
			var memoryUsageToken = this.RequestRenderedImageMemoryUsage(renderedImageDataSize);
			if (memoryUsageToken == null)
			{
				this.Logger.LogWarning("Unable to request memory usage for rendered image, dispose current rendered image first");
				this.RenderedImage?.Let((it) =>
				{
					this.SetValue(RenderedImageProperty, null);
					it.Dispose();
					this.renderedImageBuffer = this.renderedImageBuffer.DisposeAndReturnNull();
					this.renderedImageMemoryUsageToken = this.renderedImageMemoryUsageToken.DisposeAndReturnNull();
				});
				memoryUsageToken = this.RequestRenderedImageMemoryUsage(renderedImageDataSize);
			}
			if (memoryUsageToken == null)
			{
				this.Logger.LogError("Unable to request memory usage for rendered image");
				this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
				return;
			}
			IBitmapBuffer? renderedImageBuffer = null;
			try
			{
				renderedImageBuffer = new BitmapBuffer(imageRenderer.RenderedFormat, this.ImageWidth, this.ImageHeight);
			}
			catch (OutOfMemoryException ex)
			{
				this.Logger.LogError(ex, "Insufficient memory for rendered image");
				renderedImageBuffer?.Dispose();
				memoryUsageToken.Dispose();
				this.SetValue(InsufficientMemoryForRenderedImageProperty, true);
				return;
			}

			// calculate frame count and index
			var renderingOptions = new ImageRenderingOptions()
			{
				ByteOrdering = this.ByteOrdering,
				DataOffset = this.DataOffset,
			};
			var frameNumber = this.FrameNumber;
			var frameDataSize = imageRenderer.EvaluateSourceDataSize(this.ImageWidth, this.ImageHeight, renderingOptions, planeOptionsList);
			try
			{
				var totalDataSize = imageDataSource.Size - this.DataOffset;
				var frameCount = (int)Math.Max(1, totalDataSize / frameDataSize);
				if (frameNumber < 1)
				{
					frameNumber = 1;
					this.SetValue(FrameNumberProperty, 1);
				}
				else if (frameNumber > frameCount)
				{
					frameNumber = frameCount;
					this.SetValue(FrameNumberProperty, frameCount);
				}
				this.SetValue(FrameCountProperty, frameCount);
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, $"Unable to update frame count and index of '{this.SourceFileName}'");
				renderedImageBuffer?.Dispose();
				memoryUsageToken.Dispose();
				return;
			}

			// update state
			this.canSaveRenderedImage.Update(false);
			this.SetValue(InsufficientMemoryForRenderedImageProperty, false);
			this.SetValue(IsRenderingImageProperty, true);

			// cancel scheduled rendering
			this.renderImageOperation.Cancel();

			// render
			this.Logger.LogDebug($"Render image for '{sourceFileName}', dimensions: {this.ImageWidth}x{this.ImageHeight}");
			var cancellationTokenSource = new CancellationTokenSource();
			var exception = (Exception?)null;
			this.imageRenderingCancellationTokenSource = cancellationTokenSource;
			try
			{
				renderingOptions.DataOffset += ((frameDataSize + this.FramePaddingSize) * (frameNumber - 1));
				await imageRenderer.Render(imageDataSource, renderedImageBuffer, renderingOptions, planeOptionsList, cancellationTokenSource.Token);
			}
			catch (Exception ex)
			{
				exception = ex;
			}

			// check whether rendering has been cancelled or not
			if (cancellationTokenSource.IsCancellationRequested)
			{
				this.Logger.LogWarning($"Image rendering for '{sourceFileName}' has been cancelled");
				this.SynchronizationContext.Post(() =>
				{
					renderedImageBuffer.Dispose();
					memoryUsageToken.Dispose();
				});
				if (this.hasPendingImageRendering)
				{
					this.Logger.LogWarning("Start next rendering");
					this.renderImageOperation.Schedule();
				}
				return;
			}
			this.imageRenderingCancellationTokenSource = null;
			if (this.IsDisposed)
				return;

			// update state
			if (exception == null)
				this.Logger.LogDebug($"Image for '{sourceFileName}' rendered");
			else
			{
				this.Logger.LogError(exception, $"Error occurred while rendering image for '{sourceFileName}'");
				renderedImageBuffer.Dispose();
				memoryUsageToken.Dispose();
			}
			this.RenderedImage?.Let((prevRenderedImage) =>
			{
				var prevRenderedImageBuffer = this.renderedImageBuffer;
				var prevMemoryUsageToken = this.renderedImageMemoryUsageToken;
				this.SynchronizationContext.Post(async () =>
				{
					await this.WaitForNecessaryTasksAsync();
					prevRenderedImage.Dispose();
					prevRenderedImageBuffer?.Dispose();
					prevMemoryUsageToken?.Dispose();
				});
			});
			this.renderedImageMemoryUsageToken = memoryUsageToken;
			this.renderedImageBuffer = renderedImageBuffer;
			if (exception == null)
			{
				this.SetValue(HasRenderingErrorProperty, false);
				this.SetValue(RenderedImageProperty, renderedImageBuffer.CreateAvaloniaBitmap());
				this.SetValue(SourceDataSizeProperty, frameDataSize);
				this.canMoveToNextFrame.Update(frameNumber < this.FrameCount);
				this.canMoveToPreviousFrame.Update(frameNumber > 1);
				this.canSaveRenderedImage.Update(!this.IsSavingRenderedImage);
			}
			else
			{
				this.SetValue(HasRenderingErrorProperty, true);
				this.SetValue(RenderedImageProperty, null);
				this.canMoveToNextFrame.Update(false);
				this.canMoveToPreviousFrame.Update(false);
				this.canSaveRenderedImage.Update(false);
			}
			this.SetValue(IsRenderingImageProperty, false);
		}


		// Request token for rendered image memory usage.
		IDisposable? RequestRenderedImageMemoryUsage(long dataSize)
		{
			var maxUsage = this.Settings.GetValueOrDefault(SettingKeys.MaxRenderedImagesMemoryUsageMB) << 20;
			TotalRenderedImagesMemoryUsage += dataSize;
			if (TotalRenderedImagesMemoryUsage <= maxUsage)
			{
				this.Logger.LogDebug($"Request {dataSize.ToFileSizeString()} for rendered image, total: {TotalRenderedImagesMemoryUsage.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
				return new RenderedImageMemoryUsageToken(this, dataSize);
			}
			TotalRenderedImagesMemoryUsage -= dataSize;
			this.Logger.LogError($"Unable to request {dataSize.ToFileSizeString()} for rendered image, total: {TotalRenderedImagesMemoryUsage.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
			return null;
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
		void SaveRenderedImage(object? param)
		{
			if (param is string fileName)
				_ = this.SaveRenderedImage(fileName);
			else if (param is Stream stream)
				_ = this.SaveRenderedImage(stream);
			else
				throw new ArgumentException($"Invalid parameter to save rendered image: {param}.");
		}
		async Task<bool> SaveRenderedImage(string? fileName)
		{
			// check state
			if (fileName == null)
				return false;
			if (!this.canSaveRenderedImage.Value)
				return false;

			// open file
			var stream = await Task.Run(() =>
			{
				try
				{
					return new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
				}
				catch (Exception ex)
				{
					this.Logger.LogError(ex, $"Unable to open {fileName}");
					return null;
				}
			});
			if (stream == null)
				return false;

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
			return result;
		}
		async Task<bool> SaveRenderedImage(Stream? stream)
		{
			// check state
			if (stream == null)
				return false;
			if (!this.canSaveRenderedImage.Value)
				return false;
			var renderedImageBuffer = this.renderedImageBuffer;
			if (renderedImageBuffer == null)
			{
				this.Logger.LogError("No rendered image to save");
				return false;
			}

			// update state
			this.canSaveRenderedImage.Update(false);
			this.SetValue(IsSavingRenderedImageProperty, true);

			// share bitmap data
			IBitmapBuffer sharedImageBuffer = renderedImageBuffer.Share();

			// save
			var result = await Task.Run(() =>
			{
				try
				{
					unsafe
					{
#if WINDOWS10_0_17763_0_OR_GREATER
						using var bitmap = sharedImageBuffer.CreateSystemDrawingBitmap();
						bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
#else
						using var bitmap = sharedImageBuffer.CreateAvaloniaBitmap();
						bitmap.Save(stream);
#endif
					}
					return true;
				}
				catch(Exception ex)
				{
					this.Logger.LogError(ex, "Unable to save rendered image");
					return false;
				}
				finally
				{
					sharedImageBuffer.Dispose();
				}
			});

			// complete
			this.SetValue(IsSavingRenderedImageProperty, false);
			this.canSaveRenderedImage.Update(!this.IsRenderingImage);
			return result;
		}


		/// <summary>
		/// Command for saving rendered image to file or stream.
		/// </summary>
		public ICommand SaveRenderedImageCommand { get; }


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
			var renderedImageBuffer = this.renderedImageBuffer;
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
					return (Math.Floor(it * 10) + 1) / 10;
				return Math.Floor(it) + 1;
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
					return (Math.Ceiling(it * 10) - 1) / 10;
				return Math.Ceiling(it) - 1;
			});
		}


		/// <summary>
		/// Command of zooming-out rendered image.
		/// </summary>
		public ICommand ZoomOutCommand { get; }
	}
}
