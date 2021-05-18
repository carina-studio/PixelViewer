using Avalonia.Media;
using Avalonia.Media.Imaging;
using Carina.PixelViewer.Collections;
using Carina.PixelViewer.IO;
using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Platform;
using Carina.PixelViewer.Threading;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// A session of rendering and displaying image.
	/// </summary>
	class Session : BaseViewModel
	{
		// Observer for IsLoadingProfiles.
		class IsLoadingProfilesObserver : IObserver<bool>
		{
			// Fields.
			readonly Session owner;

			// Constructor.
			public IsLoadingProfilesObserver(Session owner)
			{
				this.owner = owner;
			}

			// Implementations.
			public void OnCompleted()
			{ }
			public void OnError(Exception error)
			{ }
			public void OnNext(bool value) => this.owner.OnLoadingProfilesStateChanged(value);
		}


		/// <summary>
		/// Interface of profile.
		/// </summary>
		public interface IProfile
		{
			/// <summary>
			/// Name of profile.
			/// </summary>
			public abstract string Name { get; }
		}


		// Implementation of Profile.
		class ProfileImpl : INotifyPropertyChanged, IProfile
		{
			// Fields.
			public readonly int[] EffectiveBits = new int[4];
			public volatile string? FileName;
			public int Height;
			public readonly int[] PixelStrides = new int[4];
			public IImageRenderer? Renderer;
			public readonly int[] RowStrides = new int[4];
			public int Width;

			// Constructor.
			public ProfileImpl() // Constructor for default profile
			{
				this.Name = App.Current.GetStringNonNull("SessionControl.DefaultProfile");
				App.Current.Settings.PropertyChanged += this.OnSettingsChanged;
			}
			public ProfileImpl(string name)
			{
				this.Name = name;
			}
			public ProfileImpl(ProfileImpl template)
			{
				this.Name = template.Name;
				this.Renderer = template.Renderer;
				this.Width = template.Width;
				this.Height = template.Height;
				Array.Copy(template.EffectiveBits, this.EffectiveBits, this.EffectiveBits.Length);
				Array.Copy(template.PixelStrides, this.PixelStrides, this.PixelStrides.Length);
				Array.Copy(template.RowStrides, this.RowStrides, this.RowStrides.Length);
			}

			// Name.
			public string Name { get; private set; }

			// Called when settings changed.
			void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
			{
				if (e.PropertyName == nameof(Carina.PixelViewer.Settings.AutoSelectLanguage) && this == DefaultProfile)
				{
					this.Name = App.Current.GetStringNonNull("SessionControl.DefaultProfile");
					this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Name)));
				}
			}

			// Raised when property changed.
			public event PropertyChangedEventHandler? PropertyChanged;

			// To readable string.
			public override string ToString() => this.Name;
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
		/// Default profile.
		/// </summary>
		public static readonly IProfile DefaultProfile = new ProfileImpl();


		// Constants.
		const int RenderImageDelay = 1000;


		// Static fields.
		static readonly MutableObservableValue<bool> IsLoadingProfiles = new MutableObservableValue<bool>();
		static readonly IList<IProfile> ReadOnlySharedProfileList;
		static readonly ObservableCollection<IProfile> SharedProfileList = new ObservableCollection<IProfile>();
		static long TotalRenderedImagesMemoryUsage;


		// Fields.
		readonly MutableObservableValue<bool> canOpenSourceFile = new MutableObservableValue<bool>(true);
		readonly MutableObservableValue<bool> canSaveAsNewProfile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveOrDeleteProfile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveRenderedImage = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canZoomIn = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canZoomOut = new MutableObservableValue<bool>();
		readonly int[] effectiveBits = new int[4];
		bool fitRenderedImageToViewport = true;
		readonly MutableObservableValue<bool> hasImagePlane1 = new MutableObservableValue<bool>(true);
		readonly MutableObservableValue<bool> hasImagePlane2 = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> hasImagePlane3 = new MutableObservableValue<bool>();
		bool hasPendingImageRendering;
		readonly MutableObservableValue<bool> hasSelectedRenderedImagePixel = new MutableObservableValue<bool>();
		IImageDataSource? imageDataSource;
		readonly MutableObservableValue<int> imageHeight = new MutableObservableValue<int>(1);
		readonly MutableObservableValue<int> imagePlaneCount = new MutableObservableValue<int>(1);
		readonly MutableObservableValue<IImageRenderer> imageRenderer;
		CancellationTokenSource? imageRenderingCancellationTokenSource;
		readonly MutableObservableValue<int> imageWidth = new MutableObservableValue<int>(1);
		readonly MutableObservableValue<bool> insufficientMemoryForRenderedImage = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> isAdjustableEffectiveBits1 = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> isAdjustableEffectiveBits2 = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> isAdjustableEffectiveBits3 = new MutableObservableValue<bool>();
		bool isFirstImageRenderingForSource = true;
		bool isImageDimensionsEvaluationNeeded = true;
		bool isImagePlaneOptionsResetNeeded = true;
		IDisposable? isLoadingProfilesObserverSubscriptionToken;
		readonly MutableObservableValue<bool> isRenderingImage = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> isSavingRenderedImage = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> isSourceFileOpened = new MutableObservableValue<bool>();
		readonly int[] pixelStrides = new int[4];
		readonly MutableObservableValue<IProfile> profile = new MutableObservableValue<IProfile>(DefaultProfile);
		readonly string profilesDirectoryPath;
		readonly MutableObservableValue<IBitmap?> renderedImage = new MutableObservableValue<IBitmap?>();
		IBitmapBuffer? renderedImageBuffer;
		IDisposable? renderedImageMemoryUsageToken;
		double renderedImageScale = 1.0;
		readonly ScheduledOperation renderImageOperation;
		readonly int[] rowStrides = new int[4];
		readonly MutableObservableValue<Color> selectedRenderedImagePixelColor = new MutableObservableValue<Color>();
		readonly MutableObservableValue<int> selectedRenderedImagePixelPositionX = new MutableObservableValue<int>(-1);
		readonly MutableObservableValue<int> selectedRenderedImagePixelPositionY = new MutableObservableValue<int>(-1);
		readonly MutableObservableValue<string?> sourceFileName = new MutableObservableValue<string?>();
		readonly MutableObservableValue<string?> sourceFileSizeString = new MutableObservableValue<string?>();


		// Static initializer.
		static Session()
		{
			ReadOnlySharedProfileList = new ReadOnlyObservableCollection<IProfile>(SharedProfileList);
		}


		/// <summary>
		/// Initialize new <see cref="Session"/> instance.
		/// </summary>
		public Session()
		{
			// create commands
			this.CloseSourceFileCommand = ReactiveCommand.Create(this.CloseSourceFile, this.isSourceFileOpened);
			this.DeleteProfileCommand = ReactiveCommand.Create(this.DeleteProfile, this.canSaveOrDeleteProfile);
			this.EvaluateImageDimensionsCommand = ReactiveCommand.Create<AspectRatio>(this.EvaluateImageDimensions, this.isSourceFileOpened);
			this.OpenSourceFileCommand = ReactiveCommand.Create((string filePath) => this.OpenSourceFile(filePath), this.canOpenSourceFile);
			this.RotateLeftCommand = ReactiveCommand.Create(this.RotateLeft, this.isSourceFileOpened);
			this.RotateRightCommand = ReactiveCommand.Create(this.RotateRight, this.isSourceFileOpened);
			this.SaveAsNewProfileCommand = ReactiveCommand.Create((string name) => this.SaveAsNewProfile(name), this.canSaveAsNewProfile);
			this.SaveProfileCommand = ReactiveCommand.Create(() => this.SaveProfile(), this.canSaveOrDeleteProfile);
			this.SaveRenderedImageCommand = ReactiveCommand.Create((Action<object?>)this.SaveRenderedImage, this.canSaveRenderedImage);
			this.ZoomInCommand = ReactiveCommand.Create(this.ZoomIn, this.canZoomIn);
			this.ZoomOutCommand = ReactiveCommand.Create(this.ZoomOut, this.canZoomOut);

			// setup operations
			this.renderImageOperation = new ScheduledOperation(this, this.RenderImage);

			// setup profiles
			this.profilesDirectoryPath = Path.Combine(App.Current.Directory, "Profiles");
			this.LoadProfiles();
			SharedProfileListChanging += this.OnSharedProfileListChanging;

			// select default image renderer
			this.imageRenderer = new MutableObservableValue<IImageRenderer>(this.SelectDefaultImageRenderer());

			// setup observable values
			this.ObservePropertyValue(this.hasImagePlane1, nameof(HasImagePlane1));
			this.ObservePropertyValue(this.hasImagePlane2, nameof(HasImagePlane2));
			this.ObservePropertyValue(this.hasImagePlane3, nameof(HasImagePlane3));
			this.ObservePropertyValue(this.hasSelectedRenderedImagePixel, nameof(HasSelectedRenderedImagePixel));
			this.ObservePropertyValue(this.imageHeight, nameof(ImageHeight));
			this.ObservePropertyValue(this.imagePlaneCount, nameof(ImagePlaneCount));
			this.ObservePropertyValue(this.imageRenderer, nameof(ImageRenderer));
			this.ObservePropertyValue(this.imageWidth, nameof(ImageWidth));
			this.ObservePropertyValue(this.insufficientMemoryForRenderedImage, nameof(InsufficientMemoryForRenderedImage));
			this.ObservePropertyValue(this.isAdjustableEffectiveBits1, nameof(IsAdjustableEffectiveBits1));
			this.ObservePropertyValue(this.isAdjustableEffectiveBits2, nameof(IsAdjustableEffectiveBits2));
			this.ObservePropertyValue(this.isAdjustableEffectiveBits3, nameof(IsAdjustableEffectiveBits3));
			this.ObservePropertyValue(this.isRenderingImage, nameof(IsRenderingImage));
			this.ObservePropertyValue(this.isSavingRenderedImage, nameof(IsSavingRenderedImage));
			this.ObservePropertyValue(this.isSourceFileOpened, nameof(IsSourceFileOpened));
			this.ObservePropertyValue(this.profile, nameof(Profile));
			this.ObservePropertyValue(this.renderedImage, nameof(RenderedImage));
			this.ObservePropertyValue(this.selectedRenderedImagePixelColor, nameof(SelectedRenderedImagePixelColor));
			this.ObservePropertyValue(this.selectedRenderedImagePixelPositionX, nameof(SelectedRenderedImagePixelPositionX));
			this.ObservePropertyValue(this.selectedRenderedImagePixelPositionY, nameof(SelectedRenderedImagePixelPositionY));
			this.ObservePropertyValue(this.sourceFileName, nameof(SourceFileName));
			this.ObservePropertyValue(this.sourceFileSizeString, nameof(SourceFileSizeString));

			// setup title
			this.UpdateTitle();
		}


		// Cancel rendering image.
		bool CancelRenderingImage(bool cancelPendingRendering = false)
		{
			// stop timer
			this.renderImageOperation.Cancel();

			// cancel
			if (this.imageRenderingCancellationTokenSource == null)
				return false;
			this.Logger.Warn($"Cancel rendering image for source '{this.SourceFileName}'");
			this.imageRenderingCancellationTokenSource.Cancel();
			this.imageRenderingCancellationTokenSource = null;

			// update state
			this.isRenderingImage.Update(false);
			if (cancelPendingRendering)
				this.hasPendingImageRendering = false;

			// complete
			return true;
		}


		// Change effective bits of given image plane.
		void ChangeEffectiveBits(int index, int effectiveBits)
		{
			this.VerifyAccess();
			this.ThrowIfDisposed();
			if (this.effectiveBits[index] == effectiveBits)
				return;
			this.effectiveBits[index] = effectiveBits;
			this.OnEffectiveBitsChanged(index);
			this.renderImageOperation.Reschedule();
		}


		// Change pixel stride of given image plane.
		void ChangePixelStride(int index, int pixelStride)
		{
			this.VerifyAccess();
			this.ThrowIfDisposed();
			if (this.pixelStrides[index] == pixelStride)
				return;
			this.pixelStrides[index] = pixelStride;
			this.OnPixelStrideChanged(index);
			this.renderImageOperation.Reschedule();
		}


		// Change row stride of given image plane.
		void ChangeRowStride(int index, int rowStride)
		{
			this.VerifyAccess();
			this.ThrowIfDisposed();
			if (this.rowStrides[index] == rowStride)
				return;
			this.rowStrides[index] = rowStride;
			this.OnRowStrideChanged(index);
			this.renderImageOperation.Reschedule();
		}


		// Close and clear current source file.
		void ClearSourceFile()
		{
			// close source file
			this.CloseSourceFile();

			// update state
			this.sourceFileName.Update(null);
			this.sourceFileSizeString.Update(null);

			// update title
			this.UpdateTitle();
		}


		// Close current source file.
		void CloseSourceFile()
		{
			// clear selected pixel
			this.SelectRenderedImagePixel(-1, -1);

			// update state
			this.isSourceFileOpened.Update(false);
			this.canSaveRenderedImage.Update(false);
			this.UpdateCanSaveDeleteProfile();
			var renderedImage = this.renderedImage.Value;
			if (renderedImage != null)
			{
				this.renderedImage.Update(null);
				renderedImage.Dispose();
				this.renderedImageBuffer = this.renderedImageBuffer.DisposeAndReturnNull();
				this.renderedImageMemoryUsageToken = this.renderedImageMemoryUsageToken.DisposeAndReturnNull();
			}
			if (Math.Abs(this.EffectiveRenderedImageRotation) > 0.1)
			{
				this.EffectiveRenderedImageRotation = 0.0;
				this.OnPropertyChanged(nameof(this.EffectiveRenderedImageRotation));
			}
			this.insufficientMemoryForRenderedImage.Update(false);

			// update zooming state
			this.UpdateCanZoomInOut();

			// cancel rendering image
			this.CancelRenderingImage(true);

			// dispose image data source
			var imageDataSource = this.imageDataSource;
			var sourceFileName = this.SourceFileName;
			this.imageDataSource = null;
			if (imageDataSource != null)
			{
				_ = Task.Run(() =>
				{
					this.Logger.Info($"Dispose source for '{sourceFileName}'");
					imageDataSource?.Dispose();
				});
			}
		}


		/// <summary>
		/// Command for closing opened source file.
		/// </summary>
		public ICommand CloseSourceFileCommand { get; }


		// Compare profiles.
		static int CompareProfiles(IProfile x, IProfile y) => x.Name.CompareTo(y.Name);


		// Delete current profile.
		void DeleteProfile()
		{
			// check state
			if (!this.canSaveOrDeleteProfile.Value)
				return;
			var profile = (ProfileImpl)this.profile.Value;
			if (profile == DefaultProfile)
			{
				this.Logger.Error("Cannot delete default profile");
				return;
			}

			// remove profile
			this.SwitchToProfileWithoutApplying(DefaultProfile);
			var index = SharedProfileList.IndexOf(profile);
			if (index >= 0)
			{
				SharedProfileListChanging?.Invoke(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, profile, index));
				SharedProfileList.RemoveAt(index);
			}

			// delete
			Task.Run(() =>
			{
				if(profile.FileName != null)
				{
					var filePath = Path.Combine(this.profilesDirectoryPath, profile.FileName);
					try
					{
						File.Delete(filePath);
						this.Logger.Debug($"Delete profile '{profile.Name}' and file '{filePath}'");
					}
					catch (Exception ex)
					{
						this.Logger.Warn(ex, $"Error occurred while deleting profile '{profile.Name}' and file '{filePath}'");
					}
				}
			});
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
				this.CloseSourceFile();

			// detach from shared profile list
			SharedProfileListChanging -= this.OnSharedProfileListChanging;

			// unsubscribe observable values
			this.isLoadingProfilesObserverSubscriptionToken?.Dispose();

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
			this.imageRenderer.Value.EvaluateDimensions(this.imageDataSource, aspectRatio)?.Also((it) =>
			{
				this.ImageWidth = it.Width;
				this.ImageHeight = it.Height;
				this.renderImageOperation.ExecuteIfScheduled();
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
				this.ThrowIfDisposed();
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
		/// Generate proper name for new profile according to current parameters.
		/// </summary>
		/// <returns>Name for new profile.</returns>
		public string GenerateNameForNewProfile()
		{
			var name = $"{this.imageWidth}x{this.imageHeight} [{this.imageRenderer.Value.Format.Name}]";
			if (!SharedProfileList.Any((it) => it.Name == name))
				return name;
			for (var i = 1; i <= 1000; ++i)
			{
				var alternativeName = $"{name} ({i})";
				if (!SharedProfileList.Any((it) => it.Name == alternativeName))
					return alternativeName;
			}
			return "";
		}


		/// <summary>
		/// Check whether 1st image plane exists or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool HasImagePlane1 { get => this.hasImagePlane1.Value; }


		/// <summary>
		/// Check whether 2nd image plane exists or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool HasImagePlane2 { get => this.hasImagePlane2.Value; }


		/// <summary>
		/// Check whether 3rd image plane exists or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool HasImagePlane3 { get => this.hasImagePlane3.Value; }


		/// <summary>
		/// Check whether there is a pixel selected on rendered image or not.
		/// </summary>
		public bool HasSelectedRenderedImagePixel { get => this.hasSelectedRenderedImagePixel.Value; }


		/// <summary>
		/// Get or set the requested height of <see cref="RenderedImage"/> in pixels.
		/// </summary>
		public int ImageHeight
		{
			get => this.imageHeight.Value;
			set
			{
				this.VerifyAccess();
				this.ThrowIfDisposed();
				if (this.imageHeight.Value == value)
					return;
				if (value <= 0)
					throw new ArgumentOutOfRangeException();
				this.imageHeight.Update(value);
				this.renderImageOperation.Reschedule(RenderImageDelay);
			}
		}


		/// <summary>
		/// Get number of image planes according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public int ImagePlaneCount { get => this.imagePlaneCount.Value; }


		/// <summary>
		/// Get or set <see cref="IImageRenderer"/> for rendering image from current source file.
		/// </summary>
		public IImageRenderer ImageRenderer
		{
			get => this.imageRenderer.Value;
			set
			{
				// check state
				this.VerifyAccess();
				this.ThrowIfDisposed();
				if (this.imageRenderer == value)
					return;
				if (!ImageRenderers.All.Contains(value))
					throw new ArgumentException("Given image renderer is not part of available image renderer list.");

				// render image
				this.imageRenderer.Update(value);
				if (this.Settings.EvaluateImageDimensionsAfterChangingRenderer)
					this.isImageDimensionsEvaluationNeeded = true;
				this.isImagePlaneOptionsResetNeeded = true;
				this.renderImageOperation.Reschedule();
			}
		}


		/// <summary>
		/// Get or set the requested width of <see cref="RenderedImage"/> in pixels.
		/// </summary>
		public int ImageWidth
		{
			get => this.imageWidth.Value;
			set
			{
				this.VerifyAccess();
				this.ThrowIfDisposed();
				if (this.imageWidth.Value == value)
					return;
				if (value <= 0)
					throw new ArgumentOutOfRangeException();
				this.imageWidth.Update(value);
				this.isImagePlaneOptionsResetNeeded = true;
				this.renderImageOperation.Reschedule(RenderImageDelay);
			}
		}


		/// <summary>
		/// Value to indicate whether there is insufficient memory for rendered image or not.
		/// </summary>
		public bool InsufficientMemoryForRenderedImage { get => this.insufficientMemoryForRenderedImage.Value; }


		/// <summary>
		/// Check whether effective bits for 1st image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustableEffectiveBits1 { get => this.isAdjustableEffectiveBits1.Value; }


		/// <summary>
		/// Check whether effective bits for 2nd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustableEffectiveBits2 { get => this.isAdjustableEffectiveBits2.Value; }


		/// <summary>
		/// Check whether effective bits for 3rd image plane is adjustable or not according to current <see cref="ImageRenderer"/>.
		/// </summary>
		public bool IsAdjustableEffectiveBits3 { get => this.isAdjustableEffectiveBits3.Value; }


		/// <summary>
		/// Check whether image is being rendered or not.
		/// </summary>
		public bool IsRenderingImage { get => this.isRenderingImage.Value; }


		/// <summary>
		/// Check whether rendered image is being saved or not.
		/// </summary>
		public bool IsSavingRenderedImage { get => this.isSavingRenderedImage.Value; }


		/// <summary>
		/// Check whether source image file has been opened or not.
		/// </summary>
		public bool IsSourceFileOpened { get => this.isSourceFileOpened.Value; }


		// Load profile from file.
		IProfile? LoadProfileFromFile(string fileName)
		{
			try
			{
				// open file
				using var stream = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite);
				using var jsonDocument = JsonDocument.Parse(stream);
				var jsonObject = jsonDocument.RootElement.Let((it) =>
				{
					if (it.ValueKind == JsonValueKind.Object)
						return it;
					throw new Exception($"Root element in '{fileName}' is not an object.");
				});

				// get name
				if (!jsonObject.TryGetProperty("Name", out var jsonElement) || jsonElement.ValueKind != JsonValueKind.String)
				{
					this.Logger.Error($"No 'Name' property in '{fileName}'");
					return null;
				}
				var profile = new ProfileImpl(jsonElement.GetString());

				// get renderer
				if (!jsonObject.TryGetProperty("Format", out jsonElement) || jsonElement.ValueKind != JsonValueKind.String)
				{
					this.Logger.Error($"No 'Format' property in '{fileName}'");
					return null;
				}
				if (!ImageRenderers.TryFindByFormatName(jsonElement.GetString(), out profile.Renderer))
				{
					this.Logger.Error($"Invalid 'Format' property in '{fileName}': {jsonElement.GetString()}");
					return null;
				}

				// get dimensions
				if (!jsonObject.TryGetProperty("Width", out jsonElement) || jsonElement.ValueKind != JsonValueKind.Number)
				{
					this.Logger.Error($"No 'Width' property in '{fileName}'");
					return null;
				}
				profile.Width = jsonElement.GetInt32();
				if (!jsonObject.TryGetProperty("Height", out jsonElement) || jsonElement.ValueKind != JsonValueKind.Number)
				{
					this.Logger.Error($"No 'Height' property in '{fileName}'");
					return null;
				}
				profile.Height = jsonElement.GetInt32();

				// get effective bits
				var arrayLength = 0;
				var index = 0;
				if (jsonObject.TryGetProperty("EffectiveBits", out jsonElement) && jsonElement.ValueKind == JsonValueKind.Array)
				{
					arrayLength = Math.Min(profile.EffectiveBits.Length, jsonElement.GetArrayLength());
					index = 0;
					foreach (var element in jsonElement.EnumerateArray())
					{
						if (element.ValueKind != JsonValueKind.Number)
						{
							this.Logger.Error($"Invalid effective-bits[{index}] in '{fileName}'");
							return null;
						}
						profile.EffectiveBits[index++] = element.GetInt32();
					}
				}

				// get pixel-strides
				if (!jsonObject.TryGetProperty("PixelStrides", out jsonElement) || jsonElement.ValueKind != JsonValueKind.Array)
				{
					this.Logger.Error($"No 'PixelStrides' property in '{fileName}'");
					return null;
				}
				arrayLength = Math.Min(profile.PixelStrides.Length, jsonElement.GetArrayLength());
				index = 0;
				foreach(var element in jsonElement.EnumerateArray())
				{
					if (element.ValueKind != JsonValueKind.Number)
					{
						this.Logger.Error($"Invalid pixel-stride[{index}] in '{fileName}'");
						return null;
					}
					profile.PixelStrides[index++] = element.GetInt32();
				}

				// get row-strides
				if (!jsonObject.TryGetProperty("RowStrides", out jsonElement) || jsonElement.ValueKind != JsonValueKind.Array)
				{
					this.Logger.Error($"No 'RowStrides' property in '{fileName}'");
					return null;
				}
				arrayLength = Math.Min(profile.RowStrides.Length, jsonElement.GetArrayLength());
				index = 0;
				foreach (var element in jsonElement.EnumerateArray())
				{
					if (element.ValueKind != JsonValueKind.Number)
					{
						this.Logger.Error($"Invalid row-stride[{index}] in '{fileName}'");
						return null;
					}
					profile.RowStrides[index++] = element.GetInt32();
				}

				// complete
				profile.FileName = Path.GetFileName(fileName);
				this.Logger.Debug($"Load profile '{profile.Name}' from '{fileName}'");
				return profile;
			}
			catch (Exception ex)
			{
				this.Logger.Error(ex, $"Unable to load profile from '{fileName}'");
				return null;
			}
		}


		// Load profiles.
		async void LoadProfiles()
		{
			// subscribe state
			this.isLoadingProfilesObserverSubscriptionToken = IsLoadingProfiles.Subscribe(new IsLoadingProfilesObserver(this));

			// check state
			if (SharedProfileList.IsNotEmpty())
				return;

			// add default profile
			SharedProfileList.Add(DefaultProfile);

			// load profiles
			this.Logger.Warn("Load profiles");
			IsLoadingProfiles.Update(true);
			var profiles = await Task.Run(() =>
			{
				var profiles = new List<IProfile>();
				try
				{
					if (Directory.Exists(this.profilesDirectoryPath))
					{
						foreach (var fileName in Directory.EnumerateFiles(this.profilesDirectoryPath, "*.json"))
						{
							this.LoadProfileFromFile(fileName)?.Also((it) => profiles.Add(it));
						}
					}
				}
				catch (Exception ex)
				{
					this.Logger.Error(ex, "Error occurred while loading profiles");
				}
				return profiles;
			});
			if (profiles == null)
				return;
			this.Logger.Debug($"{profiles.Count} profile(s) loaded");

			// add profiles
			foreach (var profile in profiles)
			{
				var index = SharedProfileList.BinarySearch(profile, CompareProfiles);
				if (index >= 0)
				{
					this.Logger.Error($"Duplicate profile '{profile.Name}'");
					continue;
				}
				index = ~index;
				SharedProfileListChanging?.Invoke(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, profile, index));
				SharedProfileList.Insert(index, profile);
			}

			// complete
			IsLoadingProfiles.Update(false);
		}


		/// <summary>
		/// Get maximum scaling ratio of rendered image.
		/// </summary>
		public double MaxRenderedImageScale { get; } = 10.0;


		/// <summary>
		/// Get minimum scaling ratio of rendered image.
		/// </summary>
		public double MinRenderedImageScale { get; } = 0.1;


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


		// Raise PropertyChanged event for row stride.
		void OnRowStrideChanged(int index) => this.OnPropertyChanged(index switch
		{
			0 => nameof(this.RowStride1),
			1 => nameof(this.RowStride2),
			2 => nameof(this.RowStride3),
			_ => throw new ArgumentOutOfRangeException(),
		});


		// Called when settings changed.
		protected override void OnSettingsChanged(string propertyName)
		{
			base.OnSettingsChanged(propertyName);
			if (propertyName == nameof(Settings.AutoSelectLanguage))
				this.UpdateTitle();
		}


		// Called before changing shared profile list.
		void OnSharedProfileListChanging(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action == NotifyCollectionChangedAction.Remove)
			{
				if (!e.OldItems.Contains(this.profile))
					return;
				this.Logger.Warn($"Current profile '{this.profile}' will be deleted");
				this.SwitchToProfileWithoutApplying(DefaultProfile);
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
				this.Logger.Error($"Cannot open '{fileName}' in current state");
				return;
			}

			// close current source file
			this.CloseSourceFile();

			// update state
			this.canOpenSourceFile.Update(false);
			this.sourceFileName.Update(fileName);

			// update title
			this.UpdateTitle();

			// create image data source
			var imageDataSource = await Task.Run<IImageDataSource?>(() =>
			{
				try
				{
					this.Logger.Info($"Create source for '{fileName}'");
					return new FileImageDataSource(fileName);
				}
				catch (Exception ex)
				{
					this.Logger.Error(ex, $"Unable to create source for '{fileName}'");
					return null;
				}
			});
			if (this.IsDisposed)
			{
				this.Logger.Warn($"Source for '{fileName}' created after disposing.");
				if (imageDataSource != null)
					_ = Task.Run(imageDataSource.Dispose);
				return;
			}
			if (imageDataSource == null)
			{
				// reset state
				this.sourceFileName.Update(null);
				this.isSourceFileOpened.Update(false);
				this.canOpenSourceFile.Update(true);

				// update title
				this.UpdateTitle();

				// stop opening file
				return;
			}
			this.imageDataSource = imageDataSource;

			// update state
			this.isSourceFileOpened.Update(true);
			this.canOpenSourceFile.Update(true);
			this.sourceFileSizeString.Update(imageDataSource.Size.ToFileSizeString());
			this.UpdateCanSaveDeleteProfile();

			// reset to default renderer
			if (this.Settings.UseDefaultImageRendererAfterOpeningSourceFile)
			{
				this.Logger.Warn($"Use default image renderer after opening source '{fileName}'");
				var defaultImageRenderer = this.SelectDefaultImageRenderer();
				if (this.imageRenderer != defaultImageRenderer)
				{
					this.imageRenderer.Update(defaultImageRenderer);
					if (this.Settings.EvaluateImageDimensionsAfterChangingRenderer)
						this.isImageDimensionsEvaluationNeeded = true;
					this.isImagePlaneOptionsResetNeeded = true;
				}
			}

			// render image
			if (this.Settings.EvaluateImageDimensionsAfterOpeningSourceFile && this.profile.Value == DefaultProfile)
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
		public IProfile Profile
		{
			get => this.profile.Value;
			set
			{
				// check state
				this.VerifyAccess();
				this.ThrowIfDisposed();

				// check profile
				if (this.profile == value)
					return;
				if (!SharedProfileList.Contains(value) || !(value is ProfileImpl profileImpl))
					throw new ArgumentException("Unkown profile.");

				// change profile
				this.SwitchToProfileWithoutApplying(value);

				// update state
				this.UpdateCanSaveDeleteProfile();

				// apply profile
				if (value != DefaultProfile)
				{
					// renderer
					this.imageRenderer.Update(profileImpl.Renderer ?? this.SelectDefaultImageRenderer());

					// dimensions
					this.imageWidth.Update(profileImpl.Width);
					this.imageHeight.Update(profileImpl.Height);

					// plane options
					for (var i = this.imageRenderer.Value.Format.PlaneCount - 1; i >= 0; --i)
					{
						this.effectiveBits[i] = profileImpl.EffectiveBits[i];
						this.pixelStrides[i] = profileImpl.PixelStrides[i];
						this.rowStrides[i] = profileImpl.RowStrides[i];
						this.OnEffectiveBitsChanged(i);
						this.OnPixelStrideChanged(i);
						this.OnRowStrideChanged(i);
					}

					// render image
					this.isImageDimensionsEvaluationNeeded = false;
					this.isImagePlaneOptionsResetNeeded = false;
					this.renderImageOperation.Reschedule();
				}
			}
		}


		/// <summary>
		/// Get list of available profiles.
		/// </summary>
		public IList<IProfile> Profiles { get => ReadOnlySharedProfileList; }


		// Release token for rendered image memory usage.
		void ReleaseRenderedImageMemoryUsage(RenderedImageMemoryUsageToken token)
		{
			var maxUsage = this.Settings.MaxRenderedImagesMemoryUsageMB << 20;
			TotalRenderedImagesMemoryUsage -= token.DataSize;
			this.Logger.Debug($"Release {token.DataSize.ToFileSizeString()} for rendered image, total: {TotalRenderedImagesMemoryUsage.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
		}


		/// <summary>
		/// Get rendered image.
		/// </summary>
		public IBitmap? RenderedImage { get => this.renderedImage.Value; }


		/// <summary>
		/// Get or set scaling ratio of rendered image.
		/// </summary>
		public double RenderedImageScale
		{
			get => this.renderedImageScale;
			set
			{
				this.VerifyAccess();
				this.ThrowIfDisposed();
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
			var imageRenderer = this.imageRenderer.Value;
			var sourceFileName = this.sourceFileName.Value;

			// render later
			if (!this.isFirstImageRenderingForSource && cancelled)
			{
				this.Logger.Warn("Rendering image, start next rendering after cancellation completed");
				this.hasPendingImageRendering = true;
				return;
			}
			this.isFirstImageRenderingForSource = false;
			this.hasPendingImageRendering = false;

			// evaluate dimensions
			if (this.isImageDimensionsEvaluationNeeded)
			{
				this.Logger.Info($"Evaluate dimensions of image for '{sourceFileName}'");
				this.isImageDimensionsEvaluationNeeded = false;
				imageRenderer.EvaluateDimensions(imageDataSource, this.Settings.DefaultImageDimensionsEvaluationAspectRatio)?.Also((it) =>
				{
					this.imageWidth.Update(it.Width);
					this.imageHeight.Update(it.Height);
				});
			}

			// sync format information
			var planeDescriptors = imageRenderer.Format.PlaneDescriptors;
			if (this.imagePlaneCount.Value != planeDescriptors.Count)
			{
				this.imagePlaneCount.Update(planeDescriptors.Count);
				this.hasImagePlane2.Update(planeDescriptors.Count >= 2);
				this.hasImagePlane3.Update(planeDescriptors.Count >= 3);
			}
			for (var i = planeDescriptors.Count - 1; i >= 0; --i)
			{
				(i switch
				{
					0 => this.isAdjustableEffectiveBits1,
					1 => this.isAdjustableEffectiveBits2,
					2 => this.isAdjustableEffectiveBits3,
					_ => throw new ArgumentException(),
				}).Update(planeDescriptors[i].IsAdjustableEffectiveBits);
			}

			// prepare plane options
			var planeOptionsList = new List<ImagePlaneOptions>(imageRenderer.CreateDefaultPlaneOptions(this.imageWidth.Value, this.imageHeight.Value));
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
			var renderedImageDataSize = ((long)this.imageWidth.Value * this.imageHeight.Value * imageRenderer.RenderedFormat.GetByteSize() * 2); // need double space because Avalonia will copy the bitmap data
			var memoryUsageToken = this.RequestRenderedImageMemoryUsage(renderedImageDataSize);
			if (memoryUsageToken == null)
			{
				this.Logger.Warn("Unable to request memory usage for rendered image, dispose current rendered image first");
				this.renderedImage.Value?.Let((it) =>
				{
					this.renderedImage.Update(null);
					it.Dispose();
					this.renderedImageBuffer = this.renderedImageBuffer.DisposeAndReturnNull();
					this.renderedImageMemoryUsageToken = this.renderedImageMemoryUsageToken.DisposeAndReturnNull();
				});
				memoryUsageToken = this.RequestRenderedImageMemoryUsage(renderedImageDataSize);
			}
			if (memoryUsageToken == null)
			{
				this.Logger.Error("Unable to request memory usage for rendered image");
				this.insufficientMemoryForRenderedImage.Update(true);
				return;
			}
			IBitmapBuffer? renderedImageBuffer = null;
			try
			{
				renderedImageBuffer = new BitmapBuffer(imageRenderer.RenderedFormat, this.imageWidth.Value, this.imageHeight.Value);
			}
			catch (OutOfMemoryException ex)
			{
				this.Logger.Error(ex, "Insufficient memory for rendered image");
				renderedImageBuffer?.Dispose();
				memoryUsageToken.Dispose();
				this.insufficientMemoryForRenderedImage.Update(true);
				return;
			}

			// update state
			this.canSaveRenderedImage.Update(false);
			this.insufficientMemoryForRenderedImage.Update(false);
			this.isRenderingImage.Update(true);

			// cancel scheduled rendering
			this.renderImageOperation.Cancel();

			// render
			this.Logger.Debug($"Render image for '{sourceFileName}', dimensions: {this.imageWidth}x{this.imageHeight}");
			var cancellationTokenSource = new CancellationTokenSource();
			this.imageRenderingCancellationTokenSource = cancellationTokenSource;
			await imageRenderer.Render(imageDataSource, renderedImageBuffer, new ImageRenderingOptions(), planeOptionsList, cancellationTokenSource.Token);

			// check whether rendering has been cancelled or not
			if (cancellationTokenSource.IsCancellationRequested)
			{
				this.Logger.Warn($"Image rendering for '{sourceFileName}' has been cancelled");
				this.SynchronizationContext.Post(() =>
				{
					renderedImageBuffer.Dispose();
					memoryUsageToken.Dispose();
				});
				if (this.hasPendingImageRendering)
				{
					this.Logger.Warn("Start next rendering");
					this.renderImageOperation.Schedule();
				}
				return;
			}
			this.imageRenderingCancellationTokenSource = null;
			if (this.IsDisposed)
				return;

			// update state
			this.Logger.Info($"Image for '{sourceFileName}' rendered");
			this.renderedImage.Value?.Let((prevRenderedImage) =>
			{
				var prevRenderedImageBuffer = this.renderedImageBuffer;
				var prevMemoryUsageToken = this.renderedImageMemoryUsageToken;
				this.SynchronizationContext.Post(() =>
				{
					prevRenderedImage.Dispose();
					prevRenderedImageBuffer?.Dispose();
					prevMemoryUsageToken?.Dispose();
				});
			});
			this.renderedImageMemoryUsageToken = memoryUsageToken;
			this.renderedImageBuffer = renderedImageBuffer;
			this.renderedImage.Update(renderedImageBuffer.CreateAvaloniaBitmap());
			this.canSaveRenderedImage.Update(!this.isSavingRenderedImage.Value);
			this.isRenderingImage.Update(false);
		}


		// Request token for rendered image memory usage.
		IDisposable? RequestRenderedImageMemoryUsage(long dataSize)
		{
			var maxUsage = this.Settings.MaxRenderedImagesMemoryUsageMB << 20;
			TotalRenderedImagesMemoryUsage += dataSize;
			if (TotalRenderedImagesMemoryUsage <= maxUsage)
			{
				this.Logger.Debug($"Request {dataSize.ToFileSizeString()} for rendered image, total: {TotalRenderedImagesMemoryUsage.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
				return new RenderedImageMemoryUsageToken(this, dataSize);
			}
			TotalRenderedImagesMemoryUsage -= dataSize;
			this.Logger.Error($"Unable to request {dataSize.ToFileSizeString()} for rendered image, total: {TotalRenderedImagesMemoryUsage.ToFileSizeString()}, max: {maxUsage.ToFileSizeString()}");
			return null;
		}


		// Rotate rendered image counter-clockwise.
		void RotateLeft()
		{
			if (!this.isSourceFileOpened.Value)
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
			if (!this.isSourceFileOpened.Value)
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
		async void SaveAsNewProfile(string name)
		{
			// check state
			if (!this.canSaveAsNewProfile.Value)
				return;

			// check name
			if (name.Length == 0)
			{
				this.Logger.Error("Cannot create profile with empty name");
				return;
			}

			// create profile
			var profile = new ProfileImpl(name).Also((it) => this.WriteParametersToProfile(it));

			// insert profile
			var index = SharedProfileList.BinarySearch(profile, CompareProfiles);
			if (index >= 0)
			{
				this.Logger.Error($"Cannot create existent profile '{name}'");
				return;
			}
			index = ~index;
			SharedProfileListChanging?.Invoke(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, profile, index));
			SharedProfileList.Insert(index, profile);

			// switch to profile
			this.SwitchToProfileWithoutApplying(profile);

			// save
			var saved = await Task<bool>.Run(() => this.SaveProfileToFile(profile));
			if (!saved)
			{
				this.Logger.Error($"Unable to save profile '{name}' to file");
				this.SwitchToProfileWithoutApplying(DefaultProfile);
				index = SharedProfileList.IndexOf(profile);
				if (index >= 0)
				{
					SharedProfileListChanging?.Invoke(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, profile, index));
					SharedProfileList.RemoveAt(index);
				}
				return;
			}
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
			var profile = (ProfileImpl)this.profile.Value;
			if (profile == DefaultProfile)
			{
				this.Logger.Error("Cannot save default profile");
				return;
			}

			// update parameters
			this.WriteParametersToProfile(profile);

			// save
			var saved = await Task<bool>.Run(() => this.SaveProfileToFile(profile));
			if (!saved)
				this.Logger.Error($"Failed to save profile '{profile.Name}'");
		}


		/// <summary>
		/// Command to save parameters to current profile.
		/// </summary>
		public ICommand SaveProfileCommand { get; }


		// Save profile to file.
		bool SaveProfileToFile(ProfileImpl profile)
		{
			// generate file name
			var fileName = profile.FileName;
			var filePath = "";
			if (fileName == null)
			{
				var fileNameBuilder = new StringBuilder(profile.Name);
				for (var i = fileNameBuilder.Length - 1; i >= 0; --i)
				{
					var c = fileNameBuilder[i];
					if (!char.IsDigit(c) && !char.IsLetter(c) && c != '_' && c != '-')
						fileNameBuilder[i] = '_';
				}
				fileName = fileNameBuilder.ToString();
				lock (typeof(ProfileImpl))
				{
					try
					{
						filePath = Path.Combine(this.profilesDirectoryPath, $"{fileName}.json");
						if (File.Exists(filePath) || Directory.Exists(filePath))
						{
							for (var i = 1; ; ++i)
							{
								filePath = Path.Combine(this.profilesDirectoryPath, $"{fileName}_{i}.json");
								if (!File.Exists(filePath) && !Directory.Exists(filePath))
								{
									fileName = $"{fileName}_{i}";
									break;
								}
								if (i >= 1000)
									throw new Exception($"Unable to find proper alternative file name based-on '{fileName}' in {this.profilesDirectoryPath}.");
							}
						}
					}
					catch (Exception ex)
					{
						this.Logger.Error(ex, $"Unable to generate file name for profile '{profile.Name}'");
						return false;
					}
				}
				fileName += ".json";
				profile.FileName = fileName;
			}

			// create directory
			try
			{
				if (!Directory.Exists(this.profilesDirectoryPath))
					Directory.CreateDirectory(this.profilesDirectoryPath);
			}
			catch (Exception ex)
			{
				this.Logger.Error(ex, $"Unable to create directory '{this.profilesDirectoryPath}'");
				return false;
			}

			// save to file
			filePath = Path.Combine(this.profilesDirectoryPath, fileName);
			try
			{
				// open file
				using var stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
				using var jsonWriter = new Utf8JsonWriter(stream, new JsonWriterOptions()
				{
					Indented = true,
				});

				// write name
				jsonWriter.WriteStartObject();
				jsonWriter.WriteString("Name", profile.Name);

				// write renderer
				jsonWriter.WriteString("Format", profile.Renderer?.Format?.Name ?? "");

				// write dimensions
				jsonWriter.WriteNumber("Width", profile.Width);
				jsonWriter.WriteNumber("Height", profile.Height);

				// write plane options
				var planeCount = profile.Renderer?.Format?.PlaneCount ?? 1;
				jsonWriter.WriteStartArray("EffectiveBits");
				for (var i = 0; i < planeCount; ++i)
					jsonWriter.WriteNumberValue(profile.EffectiveBits[i]);
				jsonWriter.WriteEndArray();
				jsonWriter.WriteStartArray("PixelStrides");
				for (var i = 0; i < planeCount; ++i)
					jsonWriter.WriteNumberValue(profile.PixelStrides[i]);
				jsonWriter.WriteEndArray();
				jsonWriter.WriteStartArray("RowStrides");
				for (var i = 0; i < planeCount; ++i)
					jsonWriter.WriteNumberValue(profile.RowStrides[i]);
				jsonWriter.WriteEndArray();

				// complete
				jsonWriter.WriteEndObject();
				this.Logger.Debug($"Save profile '{profile.Name}' to '{filePath}'");
			}
			catch (Exception ex)
			{
				this.Logger.Error(ex, $"Unable to save profile '{profile.Name}' to '{filePath}'");
				return false;
			}
			return true;
		}


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
					this.Logger.Error(ex, $"Unable to open {fileName}");
					return null;
				}
			});
			if (stream == null)
				return false;

			// save
			var result = await this.SaveRenderedImage(stream);

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
				this.Logger.Error("No rendered image to save");
				return false;
			}

			// update state
			this.canSaveRenderedImage.Update(false);
			this.isSavingRenderedImage.Update(true);

			// share bitmap data
			IBitmapBuffer sharedImageBuffer = renderedImageBuffer.Share();

			// save
			var result = await Task.Run(() =>
			{
				try
				{
					unsafe
					{
						using var bitmap = sharedImageBuffer.CreateSystemDrawingBitmap();
						bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
					}
					return true;
				}
				catch(Exception ex)
				{
					this.Logger.Error(ex, "Unable to save rendered image");
					return false;
				}
				finally
				{
					sharedImageBuffer.Dispose();
				}
			});

			// complete
			this.isSavingRenderedImage.Update(false);
			this.canSaveRenderedImage.Update(!this.isRenderingImage.Value);
			return result;
		}


		/// <summary>
		/// Command for saving rendered image to file or stream.
		/// </summary>
		public ICommand SaveRenderedImageCommand { get; }


		// Select default image renderer according to settings.
		IImageRenderer SelectDefaultImageRenderer()
		{
			if (ImageRenderers.TryFindByFormatName(this.Settings.DefaultImageRendererFormatName, out var imageRenderer))
				return imageRenderer.EnsureNonNull();
			return ImageRenderers.All.SingleOrDefault((candidate) => candidate is L8ImageRenderer) ?? ImageRenderers.All[0];
		}


		/// <summary>
		/// Get color of selected pixel on rendered image.
		/// </summary>
		public Color SelectedRenderedImagePixelColor { get => this.selectedRenderedImagePixelColor.Value; }


		/// <summary>
		/// Get horizontal position of selected pixel on rendered image. Return -1 if no pixel selected.
		/// </summary>
		public int SelectedRenderedImagePixelPositionX { get => this.selectedRenderedImagePixelPositionX.Value; }


		/// <summary>
		/// Get vertical position of selected pixel on rendered image. Return -1 if no pixel selected.
		/// </summary>
		public int SelectedRenderedImagePixelPositionY { get => this.selectedRenderedImagePixelPositionY.Value; }


		/// <summary>
		/// Select pixel on rendered image.
		/// </summary>
		/// <param name="x">Horizontal position of selected pixel.</param>
		/// <param name="y">Vertical position of selected pixel.</param>
		public unsafe void SelectRenderedImagePixel(int x, int y)
		{
			var renderedImageBuffer = this.renderedImageBuffer;
			if (renderedImageBuffer == null 
				|| x < 0 || x >= renderedImageBuffer.Width
				|| y < 0 || y >= renderedImageBuffer.Height)
			{
				if (this.hasSelectedRenderedImagePixel.Value)
				{
					this.hasSelectedRenderedImagePixel.Update(false);
					this.selectedRenderedImagePixelColor.Update(default);
					this.selectedRenderedImagePixelPositionX.Update(-1);
					this.selectedRenderedImagePixelPositionY.Update(-1);
				}
			}
			else
			{
				// get color of pixel
				var color = renderedImageBuffer.Memory.UnsafeAccess((baseAddress) =>
				{
					var pixelPtr = (byte*)baseAddress + renderedImageBuffer.GetPixelOffset(x, y);
					return renderedImageBuffer.Format switch
					{
						BitmapFormat.Bgra32 => new Color(pixelPtr[3], pixelPtr[2], pixelPtr[1], pixelPtr[0]),
						_ => default,
					};
				});

				// update state
				this.selectedRenderedImagePixelColor.Update(color);
				this.selectedRenderedImagePixelPositionX.Update(x);
				this.selectedRenderedImagePixelPositionY.Update(y);
				this.hasSelectedRenderedImagePixel.Update(true);
			}
		}


		/// <summary>
		/// Raised before changing shared profile list.
		/// </summary>
		static event NotifyCollectionChangedEventHandler? SharedProfileListChanging;


		/// <summary>
		/// Get name of source image file.
		/// </summary>
		public string? SourceFileName { get => this.sourceFileName.Value; }


		/// <summary>
		/// Get description of size of source image file.
		/// </summary>
		public string? SourceFileSizeString { get => this.sourceFileSizeString.Value; }


		// Switch profile without applying parameters.
		void SwitchToProfileWithoutApplying(IProfile profile)
		{
			this.profile.Update(profile);
			this.UpdateCanSaveDeleteProfile();
		}


		/// <summary>
		/// Get title of session.
		/// </summary>
		public string? Title { get; private set; }


		// Update CanSaveOrDeleteProfile and CanSaveAsNewProfile according to current state.
		void UpdateCanSaveDeleteProfile()
		{
			if (!this.isSourceFileOpened.Value || IsLoadingProfiles.Value)
			{
				this.canSaveAsNewProfile.Update(false);
				this.canSaveOrDeleteProfile.Update(false);
			}
			else
			{
				this.canSaveAsNewProfile.Update(true);
				this.canSaveOrDeleteProfile.Update(this.profile.Value != DefaultProfile);
			}
		}


		// Update CanZoomIn and CanZoomOut according to current state.
		void UpdateCanZoomInOut()
		{
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
				title = this.GetString("Session.EmptyTitle");

			// update property
			if (this.Title != title)
			{
				this.Title = title;
				this.OnPropertyChanged(nameof(this.Title));
			}
		}


		// Write current parameters to given profile.
		void WriteParametersToProfile(ProfileImpl profile)
		{
			profile.Renderer = this.imageRenderer.Value;
			profile.Width = this.imageWidth.Value;
			profile.Height = this.imageHeight.Value;
			Array.Copy(this.effectiveBits, profile.EffectiveBits, profile.EffectiveBits.Length);
			Array.Copy(this.pixelStrides, profile.PixelStrides, profile.PixelStrides.Length);
			Array.Copy(this.rowStrides, profile.RowStrides, profile.RowStrides.Length);
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
