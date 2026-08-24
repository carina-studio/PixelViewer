using ASControls = CarinaStudio.AppSuite.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.AppSuite.Input;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Controls;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Cursor = Avalonia.Input.Cursor;
using Key = Avalonia.Input.Key;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using Microsoft.Extensions.Logging;
using MouseButton = Avalonia.Input.MouseButton;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

// ReSharper disable PrivateFieldCanBeConvertedToLocalVariable

namespace Carina.PixelViewer.Controls;

/// <summary>
/// <see cref="Control"/>(View) of <see cref="Session"/>.
/// </summary>
class SessionControl : UserControl<IAppSuiteApplication>
{
	/// <summary>
	/// Maximum value of RGB gain.
	/// </summary>
	public static readonly double MaxRgbGain = Media.ImageRenderers.ImageRenderingOptions.MaxRgbGain;
	/// <summary>
	/// Minimum value of RGB gain.
	/// </summary>
	public static readonly double MinRgbGain = Media.ImageRenderers.ImageRenderingOptions.MinRgbGain;


	/// <summary>
	/// <see cref="IValueConverter"/> which maps boolean to opacity of label of image format category.
	/// </summary>
	public static readonly IValueConverter BooleanToImageFormatCategoryLabelOpacityConverter = new FuncValueConverter<bool, double>(isEnabled => 
		isEnabled
			? 1.0
			: IAppSuiteApplication.CurrentOrNull?.FindResourceOrDefault("Double/SessionControl.ImageFormatCategoryLabel.Opacity.Disabled", 0.5) ?? 0.5);

	// Constants.
	const int AttachedScreenCheckingInterval = 500;
	const int BrightnessAdjustmentGroup = 1;
	const int ColorAdjustmentGroup = 2;
	const int ContrastAdjustmentGroup = 3;
	const int HidePanelsByImageViewerSizeDelay = 500;
	const int ResetPointerPressedOnFilterParamsUIDelay = 1000;
	const int ShowProgressBarDelay = 100;
	const int StopUsingSmallRenderedImageDelay = 800;
	const int StopUsingSmallRenderedImageDelayFast = 500;


	// Static fields.
	static readonly StyledProperty<bool> CanShowProgressBarProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(CanShowProgressBar));
	static readonly StyledProperty<IImage?> EffectiveRenderedImageProperty = AvaloniaProperty.Register<SessionControl, IImage?>(nameof(EffectiveRenderedImage));
	static readonly StyledProperty<BitmapInterpolationMode> EffectiveRenderedImageInterpolationModeProperty = AvaloniaProperty.Register<SessionControl, BitmapInterpolationMode>(nameof(EffectiveRenderedImageInterpolationMode), BitmapInterpolationMode.None);
	static readonly StyledProperty<bool> HideImageViewerScrollBarsAutomaticallyProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(HideImageViewerScrollBarsAutomatically), true);
	static readonly Dictionary<int, Cursor> ImageDraggingCursors = new();
	static readonly StyledProperty<Thickness> ImageViewerShadowsMarginProperty = AvaloniaProperty.Register<SessionControl, Thickness>(nameof(ImageViewerShadowsMargin), new Thickness(-100, 0, 0, 0));
	static readonly StyledProperty<bool> IsImageViewerScrollableProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(IsImageViewerScrollable));
	static readonly StyledProperty<bool> IsPointerOverImageProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerOverImage");
	static readonly StyledProperty<bool> IsPointerPressedOnBrightnessAdjustmentUIProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerPressedOnBrightnessAdjustmentUI");
	static readonly StyledProperty<bool> IsPointerPressedOnColorAdjustmentUIProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerPressedOnColorAdjustmentUI");
	static readonly StyledProperty<bool> IsPointerPressedOnContrastAdjustmentUIProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerPressedOnContrastAdjustmentUI");
	static readonly StyledProperty<bool> IsPointerPressedOnImageProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerPressedOnImage");
	static readonly StyledProperty<Point> PointerPositionOnImageControlProperty = AvaloniaProperty.Register<SessionControl, Point>("PointerPositionOnImageControl");
	static readonly StyledProperty<string> SelectedImageDisplayPixelArgbStringProperty = AvaloniaProperty.Register<SessionControl, string>(nameof(SelectedImageDisplayPixelArgbString), "");
	static readonly StyledProperty<Rect> SelectedImageDisplayPixelBoundsProperty = AvaloniaProperty.Register<SessionControl, Rect>(nameof(SelectedImageDisplayPixelBounds));
	static readonly StyledProperty<bool> ShowProcessInfoProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(ShowProcessInfo));
	static readonly StyledProperty<bool> ShowSelectedRenderedImagePixelArgbColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelArgbColor));
	static readonly StyledProperty<bool> ShowSelectedRenderedImagePixelLabColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelLabColor));
	static readonly StyledProperty<bool> ShowSelectedRenderedImagePixelXyzColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelXyzColor));
	static readonly StyledProperty<StatusBarState> StatusBarStateProperty = AvaloniaProperty.Register<SessionControl, StatusBarState>(nameof(StatusBarState), StatusBarState.None);


	// Fields.
	readonly ContextMenu alignToIntegerMenu;
	Screen? attachedScreen;
	Avalonia.Controls.Window? attachedWindow;
	readonly ToggleButton brightnessAndContrastAdjustmentButton;
	readonly Popup brightnessAndContrastAdjustmentPopup;
	readonly Border brightnessAndContrastAdjustmentPopupBorder;
	readonly ObservableCommandState<string> canOpenSourceFile = new();
	readonly ObservableCommandState<object?> canResetBrightnessAdjustment = new();
	readonly ForwardedObservableBoolean canResetBrightnessAndContrastAdjustment;
	readonly ObservableCommandState<object?> canResetColorAdjustment = new();
	readonly ForwardedObservableBoolean canResetColorAndVibranceAdjustment;
	readonly ObservableCommandState<object?> canResetContrastAdjustment = new();
	readonly ObservableCommandState<object?> canResetHighlightAdjustment = new();
	readonly ObservableCommandState<object?> canResetSaturationAdjustment = new();
	readonly ObservableCommandState<object?> canResetShadowAdjustment = new();
	readonly ObservableCommandState<object?> canResetVibranceAdjustment = new();
	readonly ObservableCommandState<string> canSaveAsNewProfile = new();
	readonly ObservableCommandState<Session.ImageSavingParams> canSaveFilteredImage = new();
	readonly ObservableCommandState<Session.ImageSavingParams> canSaveRenderedImage = new();
	readonly ForwardedObservableBoolean canSaveImage;
	readonly MutableObservableValue<bool> canShowEvaluateImageDimensionsMenu = new();
	readonly ScheduledAction checkAttachedScreenAction;
	readonly ToggleButton colorAdjustmentButton;
	readonly Popup colorAdjustmentPopup;
	readonly Border colorAdjustmentPopupBorder;
	readonly ComboBox colorSpaceComboBox;
	readonly ToggleButton evaluateImageDimensionsButton;
	readonly ContextMenu evaluateImageDimensionsMenu;
	readonly ToggleButton fileActionsButton;
	readonly ContextMenu fileActionsMenu;
	readonly ToggleButton framePlaybackOptionsButton;
	readonly Popup framePlaybackOptionsPopup;
	// Pivot point in scrollviewer coords supplied by the active gesture handler (cursor for trackpad, focal point for pinch).
	Vector? gesturePivotInViewport;
	readonly ScheduledAction hidePanelsByImageViewerSizeAction;
	readonly ToggleButton histogramsButton;
	readonly ColumnDefinition histogramsPanelColumn;
	readonly Image image;
	readonly Panel imageContainerBorder;
	StandardCursorType imageCursorType = StandardCursorType.Arrow;
	Vector? imagePointerPressedContentPosition;
	readonly ComboBox imageRendererComboBox;
	readonly ScrollViewer imageScrollViewer;
	readonly Thickness imageScrollViewerPadding;
	readonly Control imageViewerGrid;
	ASControls.Notification? insufficientMemoryForRenderedImagesNotification;
	bool isFirstImageViewerBoundsChanged = true;
	// True while a gesture handler (pinch / trackpad magnify) is driving the current zoom.
	// Read by OnSessionPropertyChanged to choose the pivot source and skip its own pivot capture/clear.
	bool isZoomingByGesture;
	bool keepHistogramsVisible;
	bool keepRenderingParamsPanelVisible;
	PointerEventArgs? latestPointerEventArgsOnImage;
	readonly double minImageViewerSizeToHidePanels;
	readonly ToggleButton otherActionsButton;
	readonly ContextMenu otherActionsMenu;
	// Image scale at the start of the current pinch gesture; non-null indicates a pinch is in progress.
	double? pinchInitialScale;
	readonly HashSet<Key> pressedKeys = new();
	readonly ColumnDefinition renderingParamsPanelColumn;
	readonly ScrollViewer renderingParamsPanelScrollViewer;
	readonly ScheduledAction resetPointerPressedOnBrightnessAdjustmentUIAction;
	readonly ScheduledAction resetPointerPressedOnColorAdjustmentUIAction;
	readonly ScheduledAction resetPointerPressedOnContrastAdjustmentUIAction;
	readonly ScheduledAction showProgressBarAction;
	readonly ScheduledAction stopUsingSmallRenderedImageAction;
	Vector? targetImageViewportCenter;
	// Pivot expressed as a fraction of the scrollviewer's content extent — the content point
	// to keep anchored at targetImageViewportPivotAnchor as layout changes during a zoom.
	Vector? targetImageViewportPivot;
	// Viewport position (fraction 0-1) where targetImageViewportPivot should land after layout.
	// Together they define ScrollImageScrollViewer(content=pivot, viewport=anchor) — single-shot precise pivot zoom.
	Vector? targetImageViewportPivotAnchor;
	readonly ScheduledAction updateEffectiveRenderedImageAction;
	readonly ScheduledAction updateEffectiveRenderedImageIntModeAction;
	readonly ScheduledAction updateImageCursorAction;
	readonly ScheduledAction updateImageFilterParamsPopupOpacityAction;
	readonly ScheduledAction updateImageViewerScrollBarsAction;
	readonly ScheduledAction updateImageViewerShadowMarginAction;
	readonly ScheduledAction updateIsImageViewerScrollableAction;
	readonly ScheduledAction updateSelectedImageDisplayPixelArgbStringAction;
	readonly ScheduledAction updateSelectedImageDisplayPixelBoundsAction;
	readonly ScheduledAction updateStatusBarStateAction;
	bool useSmallRenderedImage;
	readonly ComboBox yuvToBgraConverterComboBox;


	/// <summary>
	/// Initialize new <see cref="SessionControl"/> instance.
	/// </summary>
	public SessionControl()
	{
		// create command state observables
		this.canResetBrightnessAndContrastAdjustment = new(ForwardedObservableBoolean.CombinationMode.Or,
			false,
			this.canResetBrightnessAdjustment,
			this.canResetContrastAdjustment,
			this.canResetHighlightAdjustment,
			this.canResetShadowAdjustment);
		this.canResetColorAndVibranceAdjustment = new(ForwardedObservableBoolean.CombinationMode.Or,
			false,
			this.canResetColorAdjustment,
			this.canResetSaturationAdjustment,
			this.canResetVibranceAdjustment);
		this.canSaveImage = new(ForwardedObservableBoolean.CombinationMode.Or,
			false,
			this.canSaveFilteredImage,
			this.canSaveRenderedImage);

		// create commands
		this.DecreaseSliderValueCommand = new Command<Slider>(this.DecreaseSliderValue);
		this.IncreaseSliderValueCommand = new Command<Slider>(this.IncreaseSliderValue);
		this.OpenSourceFileCommand = new Command(this.OpenSourceFile, this.canOpenSourceFile);
		this.ResetBrightnessAndContrastAdjustmentCommand = new Command(this.ResetBrightnessAndContrastAdjustment, this.canResetBrightnessAndContrastAdjustment);
		this.ResetColorAdjustmentCommand = new Command(this.ResetColorAdjustment, this.canResetColorAndVibranceAdjustment);
		this.SaveAsNewProfileCommand = new Command(this.SaveAsNewProfile, this.canSaveAsNewProfile);
		this.SaveImageCommand = new Command(this.SaveImage, this.canSaveImage);
		this.SetFramePlaybackRateCommand = new Command<int>(this.SetFramePlaybackRate);
		this.ShowEvaluateImageDimensionsMenuCommand = new Command(() =>
		{
			if (this.evaluateImageDimensionsMenu == null)
				return;
			if (this.evaluateImageDimensionsMenu.PlacementTarget is null)
			{
				this.evaluateImageDimensionsMenu.Bind(MinWidthProperty, new Binding { Path = $"{nameof(Bounds)}.{nameof(Size.Width)}", Source = this.evaluateImageDimensionsButton });
				this.evaluateImageDimensionsMenu.PlacementTarget = this.evaluateImageDimensionsButton;
			}
			this.evaluateImageDimensionsMenu.Open(this.evaluateImageDimensionsButton);
		}, this.canShowEvaluateImageDimensionsMenu);

		// load layout
		AvaloniaXamlLoader.Load(this);

		// setup controls
		void SetupFilterParamsSliderAndButtons(string name, int group)
		{
			var doubleClickedHandler = group switch
			{
				BrightnessAdjustmentGroup => (Action<Control>)this.OnDoubleClickedOnBrightnessAdjustmentUI,
				ColorAdjustmentGroup => this.OnDoubleClickedOnColorAdjustmentUI,
				ContrastAdjustmentGroup => this.OnDoubleClickedOnContrastAdjustmentUI,
				_ => throw new ArgumentException(),
			};
			var pointerPressedHandler = group switch
			{
				BrightnessAdjustmentGroup => (EventHandler<PointerPressedEventArgs>)this.OnPointerPressedOnBrightnessAdjustmentUI,
				ColorAdjustmentGroup => this.OnPointerPressedOnColorAdjustmentUI,
				ContrastAdjustmentGroup => this.OnPointerPressedOnContrastAdjustmentUI,
				_ => throw new ArgumentException(),
			};
			var pointerReleasedHandler = group switch
			{
				BrightnessAdjustmentGroup => (EventHandler<PointerReleasedEventArgs>)this.OnPointerReleasedOnBrightnessAdjustmentUI,
				ColorAdjustmentGroup => this.OnPointerReleasedOnColorAdjustmentUI,
				ContrastAdjustmentGroup => this.OnPointerReleasedOnContrastAdjustmentUI,
				_ => throw new ArgumentException(),
			};
			this.Get<Control>($"{name}DecreaseButton").Also(it =>
			{
				it.AddHandler(PointerPressedEvent, pointerPressedHandler, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, pointerReleasedHandler, RoutingStrategies.Tunnel);
			});
			this.Get<Control>($"{name}IncreaseButton").Also(it =>
			{
				it.AddHandler(PointerPressedEvent, pointerPressedHandler, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, pointerReleasedHandler, RoutingStrategies.Tunnel);
			});
			this.Get<Slider>($"{name}Slider").Also(it =>
			{
				it.AddHandler(PointerPressedEvent, pointerPressedHandler, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, pointerReleasedHandler, RoutingStrategies.Tunnel);
				it.TemplateApplied += (_, e) =>
				{
					e.NameScope.Find<Track>("PART_Track")?.Thumb?.Let(thumb =>
					{
						var doubleTappedWatch = new Stopwatch();
						thumb.DoubleTapped += (_, _) =>
						{
							doubleTappedWatch.Start();
						};
						thumb.AddHandler(PointerReleasedEvent, (_, e) =>
						{
							if (e.InitialPressMouseButton == MouseButton.Left
							    && doubleTappedWatch.IsRunning)
							{
								if (doubleTappedWatch.ElapsedMilliseconds <= 300)
									doubleClickedHandler(it);
								doubleTappedWatch.Reset();
							}
						}, RoutingStrategies.Tunnel);
					});
				};
			});
		}
		SetupFilterParamsSliderAndButtons("blueColorAdjustment", ColorAdjustmentGroup);
		SetupFilterParamsSliderAndButtons("brightnessAdjustment", BrightnessAdjustmentGroup);
		this.alignToIntegerMenu = ((ContextMenu)this.Resources[nameof(alignToIntegerMenu)].AsNonNull()).Also(it =>
		{
			it.Closed += (_, _) => this.SynchronizationContext.Post(() =>
			{
				if (it.PlacementTarget is ToggleButton toggleButton)
				{
					toggleButton.IsChecked = false;
					(toggleButton.Tag as Control)?.Focus();
				}
			});
			it.Opened += (_, _) => this.SynchronizationContext.Post(() =>
			{
				if (it.PlacementTarget is ToggleButton toggleButton)
					toggleButton.IsChecked = true;
			});
		});
		this.brightnessAndContrastAdjustmentButton = this.Get<ToggleButton>(nameof(brightnessAndContrastAdjustmentButton));
		this.brightnessAndContrastAdjustmentPopup = this.Get<Popup>(nameof(brightnessAndContrastAdjustmentPopup)).Also(it =>
		{
			it.PlacementTarget = this.brightnessAndContrastAdjustmentButton;
			it.Closed += (_, _) => 
			{
				this.resetPointerPressedOnBrightnessAdjustmentUIAction?.ExecuteIfScheduled();
				this.resetPointerPressedOnContrastAdjustmentUIAction?.ExecuteIfScheduled();
				this.SynchronizationContext.Post(() => this.brightnessAndContrastAdjustmentButton.IsChecked = false);
			};
			it.Opened += (_, _) => this.SynchronizationContext.Post(() => 
			{
				this.brightnessAndContrastAdjustmentButton.IsChecked = true;
				this.SynchronizationContext.PostDelayed(() =>
					ToolTip.SetIsOpen(this.brightnessAndContrastAdjustmentButton, false),
					100);
			});

			// [Workaround] Prevent handling pointer event by parent button
			it.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true);
		});
		this.brightnessAndContrastAdjustmentPopupBorder = this.Get<Border>(nameof(brightnessAndContrastAdjustmentPopupBorder));
		this.colorAdjustmentButton = this.Get<ToggleButton>(nameof(colorAdjustmentButton));
		this.colorAdjustmentPopup = this.Get<Popup>(nameof(colorAdjustmentPopup)).Also(it =>
		{
			it.PlacementTarget = this.colorAdjustmentButton;
			it.Closed += (_, _) => 
			{
				this.resetPointerPressedOnColorAdjustmentUIAction?.ExecuteIfScheduled();
				this.SynchronizationContext.Post(() => this.colorAdjustmentButton.IsChecked = false);
			};
			it.Opened += (_, _) => this.SynchronizationContext.Post(() => 
			{
				this.colorAdjustmentButton.IsChecked = true;
				this.SynchronizationContext.PostDelayed(() =>
					ToolTip.SetIsOpen(this.colorAdjustmentButton, false),
					100);
			});

			// [Workaround] Prevent handling pointer event by parent button
			it.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true);
		});
		this.colorAdjustmentPopupBorder = this.Get<Border>(nameof(colorAdjustmentPopupBorder));
		this.colorSpaceComboBox = this.Get<ComboBox>(nameof(colorSpaceComboBox));
		SetupFilterParamsSliderAndButtons("contrastAdjustment", ContrastAdjustmentGroup);
		this.evaluateImageDimensionsButton = this.Get<ToggleButton>(nameof(this.evaluateImageDimensionsButton));
		this.evaluateImageDimensionsMenu = ((ContextMenu)this.Resources[nameof(evaluateImageDimensionsMenu)].AsNonNull()).Also(it =>
		{
			it.Closed += (_, _) => this.SynchronizationContext.Post(() => this.evaluateImageDimensionsButton.IsChecked = false);
			it.Opened += (_, _) => this.SynchronizationContext.Post(() => this.evaluateImageDimensionsButton.IsChecked = true);
		});
		this.fileActionsButton = this.Get<ToggleButton>(nameof(this.fileActionsButton));
		this.fileActionsMenu = ((ContextMenu)this.Resources[nameof(fileActionsMenu)].AsNonNull()).Also(it =>
		{
			it.Closed += (_, _) => this.SynchronizationContext.Post(() => this.fileActionsButton.IsChecked = false);
			it.Opened += (_, _) => this.SynchronizationContext.Post(() => this.fileActionsButton.IsChecked = true);
		});
		this.framePlaybackOptionsButton = this.Get<ToggleButton>(nameof(framePlaybackOptionsButton));
		this.framePlaybackOptionsPopup = this.Get<Popup>(nameof(framePlaybackOptionsPopup)).Also(it =>
		{
			it.PlacementTarget = this.framePlaybackOptionsButton;
			it.Closed += (_, _) => this.SynchronizationContext.Post(() => this.framePlaybackOptionsButton.IsChecked = false);
			it.Opened += (_, _) => this.SynchronizationContext.Post(() =>
			{
				this.framePlaybackOptionsButton.IsChecked = true;
				this.SynchronizationContext.PostDelayed(() =>
					ToolTip.SetIsOpen(this.framePlaybackOptionsButton, false),
					100);
			});

			// [Workaround] Prevent handling pointer event by parent button
			it.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true);
		});
		SetupFilterParamsSliderAndButtons("greenColorAdjustment", ColorAdjustmentGroup);
		SetupFilterParamsSliderAndButtons("highlightAdjustment", BrightnessAdjustmentGroup);
		this.histogramsButton = this.Get<ToggleButton>(nameof(histogramsButton));
		this.image = this.Get<Image>(nameof(image));
		this.imageContainerBorder = this.Get<Panel>(nameof(imageContainerBorder)).Also(it =>
		{
			it.GetObservable(BoundsProperty).Subscribe(new Observer<Rect>(_ =>
			{
				if (this.GetValue(IsPointerOverImageProperty) && this.latestPointerEventArgsOnImage != null)
					this.SetValue(PointerPositionOnImageControlProperty, this.latestPointerEventArgsOnImage.GetCurrentPoint(it).Position);
			}));
		});
		this.imageRendererComboBox = this.Get<ComboBox>(nameof(imageRendererComboBox));
		this.imageScrollViewer = this.Get<ScrollViewer>(nameof(this.imageScrollViewer)).Also(it =>
		{
			it.GetObservable(BoundsProperty).Subscribe(_ => this.ReportImageViewportSize(), skipOnNextDuringSubscription: true);
			it.GetObservable(ScrollViewer.ExtentProperty).Subscribe(this.OnImageScrollViewerExtentChanged, skipOnNextDuringSubscription: true);
			it.GetObservable(ScrollViewer.ViewportProperty).Subscribe(_ =>
			{
				this.updateIsImageViewerScrollableAction?.Schedule();
			}, skipOnNextDuringSubscription: true);
		});
		this.imageViewerGrid = this.Get<Control>(nameof(imageViewerGrid)).Also(it =>
		{
			it.GetObservable(BoundsProperty).Subscribe(new Observer<Rect>(_ =>
			{
				if (this.isFirstImageViewerBoundsChanged)
				{
					this.isFirstImageViewerBoundsChanged = false;
					this.hidePanelsByImageViewerSizeAction?.Reschedule();
				}
				else
					this.hidePanelsByImageViewerSizeAction?.Schedule(HidePanelsByImageViewerSizeDelay);
			}));
		});
		this.otherActionsButton = this.Get<ToggleButton>(nameof(otherActionsButton));
		this.otherActionsMenu = ((ContextMenu)this.Resources[nameof(otherActionsMenu)].AsNonNull()).Also(it =>
		{
#if DEBUG
			var toolsMenuItem = it.Items.OfType<MenuItem>().FirstOrDefault(item => item.Name == "toolsMenuItem");
			if (toolsMenuItem is not null)
			{
				toolsMenuItem.IsVisible = true;
				var editConfigMenuItem = toolsMenuItem.Items.OfType<MenuItem>().FirstOrDefault(item => item.Name == "editConfigMenuItem");
				if (editConfigMenuItem is not null)
					editConfigMenuItem.IsVisible = true;
			}
#endif
			it.Closed += (_, _) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = false);
			it.Opened += (_, _) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = true);
		});
		SetupFilterParamsSliderAndButtons("redColorAdjustment", ColorAdjustmentGroup);
		SetupFilterParamsSliderAndButtons("saturationAdjustment", ColorAdjustmentGroup);
		var workingAreaColumnDefs = this.Get<Grid>("workingAreaGrid").ColumnDefinitions;
		this.histogramsPanelColumn = workingAreaColumnDefs.First().Also(column =>
		{
			column.GetObservable(ColumnDefinition.WidthProperty).Subscribe(new Observer<GridLength>(_ =>
			{
				if (this.DataContext is Session session && session.IsHistogramsVisible)
					session.HistogramsPanelSize = column.Width.Value;
			}));
		});
		this.renderingParamsPanelColumn = workingAreaColumnDefs.Last().Also(column =>
		{
			column.GetObservable(ColumnDefinition.WidthProperty).Subscribe(new Observer<GridLength>(_ =>
			{
				if (this.DataContext is Session session && session.IsRenderingParametersPanelVisible)
					session.RenderingParametersPanelSize = column.Width.Value;
			}));
		});
		this.renderingParamsPanelScrollViewer = this.Get<ScrollViewer>(nameof(renderingParamsPanelScrollViewer));
		SetupFilterParamsSliderAndButtons("shadowAdjustment", BrightnessAdjustmentGroup);
#if DEBUG
		this.Get<Button>("testButton").IsVisible = true;
#endif
		SetupFilterParamsSliderAndButtons("vibranceAdjustment", ColorAdjustmentGroup);
		this.yuvToBgraConverterComboBox = this.Get<ComboBox>(nameof(yuvToBgraConverterComboBox));

		// load resources
		this.minImageViewerSizeToHidePanels = this.Application.FindResourceOrDefault<double>("Double/SessionControl.ImageViewer.MinSizeToHidePanels");
		this.imageScrollViewerPadding = this.Application.FindResourceOrDefault<Thickness>("Thickness/SessionControl.ImageViewer.Padding");

		// create scheduled actions
		this.checkAttachedScreenAction = new(() =>
		{
			var screen = this.attachedWindow?.Screens.ScreenFromWindow(this.attachedWindow);
			if (this.attachedScreen?.Equals(screen) ?? screen is null)
				return;
			this.attachedScreen = screen;
			this.OnAttachedScreenChanged();
		});
		this.hidePanelsByImageViewerSizeAction = new(() =>
		{
			if (this.imageViewerGrid.Bounds.Width > this.minImageViewerSizeToHidePanels)
			{
				this.keepHistogramsVisible = false;
				this.keepRenderingParamsPanelVisible = false;
				return;
			}
			if (this.DataContext is not Session session)
				return;
			if (session.IsRenderingParametersPanelVisible && !this.keepRenderingParamsPanelVisible)
			{
				session.IsRenderingParametersPanelVisible = false;
				return;
			}
			else
				this.keepRenderingParamsPanelVisible = false;
			if (!this.keepHistogramsVisible)
				session.IsHistogramsVisible = false;
			else
				this.keepHistogramsVisible = false;
		});
		this.resetPointerPressedOnBrightnessAdjustmentUIAction = new(() =>
			this.SetValue(IsPointerPressedOnBrightnessAdjustmentUIProperty, false));
		this.resetPointerPressedOnColorAdjustmentUIAction = new(() =>
			this.SetValue(IsPointerPressedOnColorAdjustmentUIProperty, false));
		this.resetPointerPressedOnContrastAdjustmentUIAction = new(() =>
			this.SetValue(IsPointerPressedOnContrastAdjustmentUIProperty, false));
		this.showProgressBarAction = new(() =>
			this.SetValue(CanShowProgressBarProperty, true));
		this.stopUsingSmallRenderedImageAction = new(() =>
		{
			if (this.useSmallRenderedImage)
			{
				this.Logger.LogTrace("Stop using small rendered image");
				this.useSmallRenderedImage = false;
				this.updateEffectiveRenderedImageAction?.Schedule();
				this.updateEffectiveRenderedImageIntModeAction?.Schedule();
			}
		});
		this.updateEffectiveRenderedImageAction = new(() =>
		{
			if (this.DataContext is not Session session)
				this.SetValue(EffectiveRenderedImageProperty, null);
			else if (this.useSmallRenderedImage && session.HasQuarterSizeRenderedImage)
				this.SetValue(EffectiveRenderedImageProperty, session.QuarterSizeRenderedImage);
			else
			{
				var image = session.RenderedImage;
				if (image != null)
				{
					var displaySize = session.ImageDisplaySize;
					if (session.HasQuarterSizeRenderedImage 
						&& image.Size.Width >= displaySize.Width * 2 
						&& image.Size.Height >= displaySize.Height * 2)
					{
						this.SetValue(EffectiveRenderedImageProperty, session.QuarterSizeRenderedImage);
					}
					else
						this.SetValue(EffectiveRenderedImageProperty, session.RenderedImage);
				}
				else
					this.SetValue(EffectiveRenderedImageProperty, null);
			}
		});
		this.updateEffectiveRenderedImageIntModeAction = new(() =>
		{
			if (this.DataContext is not Session session)
				return;
			if (this.useSmallRenderedImage)
				this.SetValue(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.None);
			else
			{
				var image = this.GetValue(EffectiveRenderedImageProperty);
				if (image is not null)
				{
					// [Workaround] Make sure that instance is valid.
					try
					{
						_ = image.Size;
					}
					catch
					{
						image = null;
					}
				}
				if (image is not null)
				{
					var displaySize = session.ImageDisplaySize;
					if (image.Size.Width - 1 > displaySize.Width || image.Size.Height - 1 > displaySize.Height)
						this.SetValue(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.HighQuality);
					else
						this.SetValue(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.None);
				}
			}
		});
		this.updateImageCursorAction = new(() =>
		{
			var screen = this.attachedWindow?.Screens.ScreenFromWindow(this.attachedWindow);
			if (screen is null)
				return;
			var screenScaling = (int)(screen.Scaling * 100 + 0.5);
			ImageDraggingCursors.TryGetValue(screenScaling, out var draggingCursor);
			var cursorType = StandardCursorType.Arrow;
			if (this.GetValue(IsPointerOverImageProperty))
			{
				if (this.GetValue(IsPointerPressedOnImageProperty)
				    && this.IsImageViewerScrollable)
				{
					if (draggingCursor is null)
					{
						draggingCursor = LoadCursor("Image/Cursor.Hand", screen);
						ImageDraggingCursors[screenScaling] = draggingCursor;
					}
					this.image.Cursor = draggingCursor;
					return;
				}
			}
			if (this.imageCursorType != cursorType
			    || this.image.Cursor == draggingCursor)
            {
				this.imageCursorType = cursorType;
				this.image.Cursor = new Cursor(cursorType);
            }
		});
		this.updateImageFilterParamsPopupOpacityAction = new(() =>
		{
			this.brightnessAndContrastAdjustmentPopupBorder.Opacity = (this.GetValue(IsPointerPressedOnBrightnessAdjustmentUIProperty) || this.GetValue(IsPointerPressedOnContrastAdjustmentUIProperty)) ? 0.5 : 1;
			this.colorAdjustmentPopupBorder.Opacity = this.GetValue(IsPointerPressedOnColorAdjustmentUIProperty) ? 0.5 : 1;
		});
		this.updateImageViewerScrollBarsAction = new(() =>
		{
			var scrollBarVisibility = this.DataContext is Session session && (!session.FitImageToViewport || session.IsZooming)
				? ScrollBarVisibility.Auto
				: ScrollBarVisibility.Disabled;
			this.imageScrollViewer.HorizontalScrollBarVisibility = scrollBarVisibility;
			this.imageScrollViewer.VerticalScrollBarVisibility = scrollBarVisibility;
		});
		this.updateImageViewerShadowMarginAction = new(() =>
		{
			var session = this.DataContext as Session;
			var leftMargin = session?.IsHistogramsVisible == true ? 0 : -100;
			var rightMargin = session?.IsRenderingParametersPanelVisible == true ? 0 : -100;
			this.SetValue(ImageViewerShadowsMarginProperty, new(leftMargin, 0, rightMargin, 0));
		});
		this.updateIsImageViewerScrollableAction = new(() =>
		{
			var contentSize = this.imageScrollViewer.Extent;
			var viewport = this.imageScrollViewer.Viewport;
			this.SetValue(IsImageViewerScrollableProperty, contentSize.Width > viewport.Width || contentSize.Height > viewport.Height);
		});
		this.updateSelectedImageDisplayPixelArgbStringAction = new(() =>
		{
			if (this.DataContext is not Session session)
			{
				this.SetValue(SelectedImageDisplayPixelArgbStringProperty, "");
				return;
			}
			var color = session.SelectedRenderedImagePixelColor;
			var hasAlpha = session.IsAlphaChannelAvailable;
			var prefix = hasAlpha ? "ARGB" : "RGB";
			var format = this.Settings.GetValueOrDefault(SettingKeys.SelectedRenderedImagePixelArgbColorFormat);
			var text = format switch
			{
				Media.ArgbColorFormat.Fixed8Bit => Global.Run(() =>
				{
					var c8 = color.Color;
					return hasAlpha
						? $"{prefix}({c8.A:D3}, {c8.R:D3}, {c8.G:D3}, {c8.B:D3})"
						: $"{prefix}({c8.R:D3}, {c8.G:D3}, {c8.B:D3})";
				}),
				Media.ArgbColorFormat.Normalized => hasAlpha
					? $"{prefix}({color.A / 65535.0:F4}, {color.R / 65535.0:F4}, {color.G / 65535.0:F4}, {color.B / 65535.0:F4})"
					: $"{prefix}({color.R / 65535.0:F4}, {color.G / 65535.0:F4}, {color.B / 65535.0:F4})",
				_ => Global.Run(() =>
				{
					var bits = Math.Clamp(session.SourceImageEffectiveBits, 1, 16);
					var shift = 16 - bits;
					var maxValue = (1 << bits) - 1;
					var width = maxValue.ToString().Length;
					var pad = $"D{width}";
					return hasAlpha
						? $"{prefix}({(color.A >> shift).ToString(pad)}, {(color.R >> shift).ToString(pad)}, {(color.G >> shift).ToString(pad)}, {(color.B >> shift).ToString(pad)})"
						: $"{prefix}({(color.R >> shift).ToString(pad)}, {(color.G >> shift).ToString(pad)}, {(color.B >> shift).ToString(pad)})";
				}),
			};
			this.SetValue(SelectedImageDisplayPixelArgbStringProperty, text);
		});
		this.updateSelectedImageDisplayPixelBoundsAction = new(() =>
		{
			if (this.DataContext is not Session session || !this.GetValue(IsPointerOverImageProperty) || session.IsZooming || !session.HasSelectedRenderedImagePixel)
			{
				this.SetValue(SelectedImageDisplayPixelBoundsProperty, default);
				return;
			}
			var x = (double)Math.Max(0, session.SelectedRenderedImagePixelPositionX);
			var y = (double)Math.Max(0, session.SelectedRenderedImagePixelPositionY);
			var scale = session.ImageDisplayScale;
			if (this.attachedScreen is not null)
				scale /= this.attachedScreen.Scaling; // [Workaround]
			x = (int)(x * scale + 0.5);
			y = (int)(y * scale + 0.5);
			if (scale <= 6.999)
			{
				x -= (7.0 - scale) * 0.5;
				y -= (7.0 - scale) * 0.5;
				this.SetValue(SelectedImageDisplayPixelBoundsProperty, new(x - 1, y - 1, 9, 9));
			}
			else
				this.SetValue(SelectedImageDisplayPixelBoundsProperty, new(x - 1, y - 1, scale + 2, scale + 2));
		});
		this.updateStatusBarStateAction = new(() =>
		{
			this.SetValue(StatusBarStateProperty, Global.Run(() =>
			{
				if (this.DataContext is not Session session)
					return StatusBarState.Inactive;
				if (session.HasRenderingError || session.InsufficientMemoryForRenderedImage)
					return StatusBarState.Error;
				if (session.IsSourceOpened)
					return StatusBarState.Active;
				return StatusBarState.Inactive;
			}));
		});
		
		// attach to self
		this.GetObservable(EffectiveRenderedImageInterpolationModeProperty).Subscribe(new Observer<BitmapInterpolationMode>(mode =>
		{
			RenderOptions.SetBitmapInterpolationMode(this.image, mode);
		}));
	}


	/// <summary>
	/// Whether the progress bar of image processing can be shown or not.
	/// </summary>
	/// <remarks>Showing is delayed after image processing started, so that short processing such as rendering each frame
	/// when playing frames doesn't make the progress bar flash. Hiding is performed immediately.</remarks>
	public bool CanShowProgressBar => this.GetValue(CanShowProgressBarProperty);


	/// <summary>
	/// Copy file name.
	/// </summary>
	public void CopyFileName()
	{
		if (this.DataContext is not Session session || !session.IsSourceOpened)
			return;
		session.SourceFileName?.Let(it =>
		{
			_ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(Path.GetFileName(it));
		});
	}


	/// <summary>
	/// Copy file path.
	/// </summary>
	public void CopyFilePath()
	{
		if (this.DataContext is not Session session || !session.IsSourceOpened)
			return;
		session.SourceFileName?.Let(it =>
		{
			_ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(it);
		});
	}


	// Decrease value of given slider.
	void DecreaseSliderValue(Slider slider)
	{
		var value = Math.Max(slider.Minimum, slider.Value - slider.TickFrequency);
		slider.Value = Math.Abs(value) <= 0.001 ? 0 : value;
	}


	/// <summary>
	/// Command to decrease value of given slider.
	/// </summary>
	public ICommand DecreaseSliderValueCommand { get; }


	/// <summary>
	/// Drop data to this control.
	/// </summary>
	/// <param name="data">Dropped data.</param>
	/// <param name="keyModifiers">Key modifiers.</param>
	/// <returns>True if data has been accepted.</returns>
	public async Task<bool> DropDataAsync(IDataTransfer data, KeyModifiers keyModifiers)
	{
		// get file names
		var fileNames = Global.RunOrDefault(() => data.TryGetFiles()?.Let(it =>
		{
			var fileNames = new List<string>();
			foreach (var file in it)
			{
				var fileName = file.TryGetLocalPath();
				if (!string.IsNullOrEmpty(fileName))
					fileNames.Add(fileName);
			}
			return fileNames;
		}));
		if (fileNames.IsNullOrEmpty())
			return false;

		// get window
		if (this.attachedWindow == null)
			return false;

		// open files (prompt for open mode when multiple files are dropped)
		await this.OpenFilesAsync(fileNames, preferNewSession: true);
		return true;
	}


	// Effective rendered image to display.
	IImage? EffectiveRenderedImage => this.GetValue(EffectiveRenderedImageProperty);


	// Interpolation mode for rendered image.
	BitmapInterpolationMode EffectiveRenderedImageInterpolationMode => this.GetValue(EffectiveRenderedImageInterpolationModeProperty);


	/// <summary>
	/// Hide scroll bars of image viewer automatically.
	/// </summary>
	public bool HideImageViewerScrollBarsAutomatically => this.GetValue(HideImageViewerScrollBarsAutomaticallyProperty);


	/// <summary>
	/// Margin of shadows of image viewer.
	/// </summary>
	public Thickness ImageViewerShadowsMargin => this.GetValue(ImageViewerShadowsMarginProperty);


	// Increase value of given slider.
	void IncreaseSliderValue(Slider slider)
	{
		var value = Math.Min(slider.Maximum, slider.Value + slider.TickFrequency);
		slider.Value = Math.Abs(value) <= 0.001 ? 0 : value;
	}


	/// <summary>
	/// Command to increase value of given slider.
	/// </summary>
	public ICommand IncreaseSliderValueCommand { get; }


	// Check whether image viewer is scrollable in current state or not.
	bool IsImageViewerScrollable => this.GetValue(IsImageViewerScrollableProperty);
	
	
	// Load cursor from resource.
	static Cursor LoadCursor(string resourceKey, Screen screen)
	{
		var image = IAppSuiteApplication.Current.FindResourceOrDefault<IImage?>(resourceKey) ?? throw new ArgumentException();
		var imageSize = image.Size;
		var maxSide = IAppSuiteApplication.Current.FindResourceOrDefault("Double/Cursor.MaxSide", 30.0);
		var scaleX = maxSide / imageSize.Width;
		var scaleY = maxSide / imageSize.Height;
		var scale = Math.Min(scaleX, scaleY);
		var cursorWidth = (int)(imageSize.Width * scale * screen.Scaling + 0.5);
		var cursorHeight = (int)(imageSize.Height * scale * screen.Scaling + 0.5);
		var cursorBitmap = new RenderTargetBitmap(new(cursorWidth, cursorHeight));
		using var cursorDrawingContext = cursorBitmap.CreateDrawingContext();
		image.Draw(cursorDrawingContext, new(default, imageSize), new(0, 0, cursorWidth, cursorHeight));
		return new(cursorBitmap, new(cursorWidth >> 1, cursorHeight >> 1));
	}


	/// <summary>
	/// Move to specific frame.
	/// </summary>
	public async Task MoveToSpecificFrame()
	{
		// check state
		if (this.DataContext is not Session session)
			return;
		if (!session.HasMultipleFrames)
			return;

		// find window
		if (this.attachedWindow == null)
			return;

		// select frame number
		var selectFrameNumber = await new FrameNumberSelectionDialog()
		{
			FrameCount = session.FrameCount,
			InitialFrameNumber = session.FrameNumber,
		}.ShowDialog<int?>(this.attachedWindow);
		if (selectFrameNumber == null)
			return;

		// move to frame
		if (this.DataContext == session)
			session.FrameNumber = selectFrameNumber.Value;
	}


	/// <summary>
	/// Called when clicking on menu item of align to integer.
	/// </summary>
	public void OnAlignToIntegerMenuItemClick(object? sender, RoutedEventArgs e)
	{
		if (this.DataContext is not Session session)
			return;
		if (sender is not MenuItem menuItem || !int.TryParse(menuItem.Tag as string, out var bytes))
			return;
		if (this.alignToIntegerMenu.PlacementTarget is not { } control)
			return;
		(control.Name switch
		{
			"alignImageHeightButton" => session.AlignImageHeightCommand,
			"alignImageWidthButton" => session.AlignImageWidthCommand,
			"alignRowStride1Button" => session.AlignRowStride1Command,
			"alignRowStride2Button" => session.AlignRowStride2Command,
			"alignRowStride3Button" => session.AlignRowStride3Command,
			_ => null,
		})?.TryExecute(bytes);
	}


	// Application string resources updated.
	void OnApplicationStringsUpdated(object? sender, EventArgs e)
	{
		// refresh names shown by combo boxes, the names are resolved from string resources without change notification
		RefreshComboBoxContent(this.colorSpaceComboBox);
		RefreshComboBoxContent(this.imageRendererComboBox);
		RefreshComboBoxContent(this.yuvToBgraConverterComboBox);
	}
	
	
	// Called when attached screen changed.
	void OnAttachedScreenChanged()
	{
		this.Logger.LogWarning("Attached screen changed");
		this.ReportScreenPixelDensity();
		this.updateSelectedImageDisplayPixelBoundsAction.Schedule();
	}


	// Called when attached to logical tree.
	protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
	{
		// call base
		base.OnAttachedToLogicalTree(e);

		// enable drag-drop
		this.AddHandler(DragDrop.DragOverEvent, this.OnDragOver);
		this.AddHandler(DragDrop.DropEvent, this.OnDrop);

		// add event handlers
		this.Application.StringsUpdated += this.OnApplicationStringsUpdated;
		Media.ColorSpace.CustomNameChanged += this.OnColorSpaceCustomNameChanged;
		this.AddHandler(PointerWheelChangedEvent, this.OnPointerWheelChanged, RoutingStrategies.Tunnel);

		// attach to settings
		var settings = this.Settings;
		settings.SettingChanged += this.OnSettingChanged;
		this.SetValue(HideImageViewerScrollBarsAutomaticallyProperty, settings.GetValueOrDefault(SettingKeys.HideImageViewerScrollBarsAutomatically));
		this.SetValue(ShowProcessInfoProperty, settings.GetValueOrDefault(SettingKeys.ShowProcessInfo));
		this.SetValue(ShowSelectedRenderedImagePixelArgbColorProperty, settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelArgbColor));
		this.SetValue(ShowSelectedRenderedImagePixelLabColorProperty, settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelLabColor));
		this.SetValue(ShowSelectedRenderedImagePixelXyzColorProperty, settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelXyzColor));

		// attach to window
		this.attachedWindow = this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Also(it =>
		{
			it.PropertyChanged += this.OnWindowPropertyChanged;
		});
	}
	
	
	// Called to attach to session.
	void OnAttachToSession(Session session)
	{
		// attach
		session.ImageSavingCompleted += this.OnImageSavingCompleted;
		session.PropertyChanged += this.OnSessionPropertyChanged;
		this.canOpenSourceFile.Bind(session.OpenSourceFileCommand, "");
		this.canResetBrightnessAdjustment.Bind(session.ResetBrightnessAdjustmentCommand);
		this.canResetColorAdjustment.Bind(session.ResetColorAdjustmentCommand);
		this.canResetContrastAdjustment.Bind(session.ResetContrastAdjustmentCommand);
		this.canResetHighlightAdjustment.Bind(session.ResetHighlightAdjustmentCommand);
		this.canResetSaturationAdjustment.Bind(session.ResetSaturationAdjustmentCommand);
		this.canResetShadowAdjustment.Bind(session.ResetShadowAdjustmentCommand);
		this.canResetVibranceAdjustment.Bind(session.ResetVibranceAdjustmentCommand);
		this.canSaveAsNewProfile.Bind(session.SaveAsNewProfileCommand, "");
		this.canSaveFilteredImage.Bind(session.SaveFilteredImageCommand, new Session.ImageSavingParams());
		this.canSaveRenderedImage.Bind(session.SaveRenderedImageCommand, new Session.ImageSavingParams());
		this.canShowEvaluateImageDimensionsMenu.Update(session.IsSourceOpened);
		this.UpdateCanShowProgressBar();

		// setup panels
		Grid.SetColumnSpan(this.imageViewerGrid, session.IsRenderingParametersPanelVisible ? 2 : 4);
		if (session.IsRenderingParametersPanelVisible)
		{
			this.renderingParamsPanelColumn.MinWidth = Session.MinRenderingParametersPanelSize;
			this.renderingParamsPanelColumn.Width = new GridLength(session.RenderingParametersPanelSize, GridUnitType.Pixel);
		}
		else
		{
			this.renderingParamsPanelColumn.MinWidth = 0;
			this.renderingParamsPanelColumn.Width = new GridLength(0, GridUnitType.Pixel);
		}
		if (session.IsHistogramsVisible)
		{
			this.histogramsPanelColumn.MinWidth = Session.MinHistogramsPanelSize;
			this.histogramsPanelColumn.Width = new GridLength(session.HistogramsPanelSize, GridUnitType.Pixel);
		}
		else
		{
			this.histogramsPanelColumn.MinWidth = 0;
			this.histogramsPanelColumn.Width = new GridLength(0, GridUnitType.Pixel);
		}

		// update rendered image
		this.updateEffectiveRenderedImageAction.Schedule();
		this.updateEffectiveRenderedImageIntModeAction.Schedule();
		
		// update state
		this.ReportImageViewportSize();
		this.ReportScreenPixelDensity();
		this.updateImageViewerShadowMarginAction.Schedule();
		this.updateSelectedImageDisplayPixelArgbStringAction.Schedule();
		this.updateSelectedImageDisplayPixelBoundsAction.Schedule();
		this.updateStatusBarStateAction.Schedule();
	}


	// Called when attached to visual tree.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
		// call base
        base.OnAttachedToVisualTree(e);

		// update state
		this.isFirstImageViewerBoundsChanged = true;

		// [Workaround] Force refreshing status bar state to make background applied as expected
		this.SetValue(StatusBarStateProperty, StatusBarState.None);
		this.updateStatusBarStateAction.Reschedule();
    }


	// Called when custom name of color space changed.
    void OnColorSpaceCustomNameChanged(object? sender, Media.ColorSpaceEventArgs e) =>
        RefreshComboBoxContent(this.colorSpaceComboBox);


    // Called when detached from logical tree.
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
	{
		// disable drag-drop
		this.RemoveHandler(DragDrop.DragOverEvent, this.OnDragOver);
		this.RemoveHandler(DragDrop.DropEvent, this.OnDrop);

		// remove event handlers
		this.Application.StringsUpdated -= this.OnApplicationStringsUpdated;
		Media.ColorSpace.CustomNameChanged -= this.OnColorSpaceCustomNameChanged;
		this.RemoveHandler(PointerWheelChangedEvent, this.OnPointerWheelChanged);

		// detach from settings
		this.Settings.SettingChanged -= this.OnSettingChanged;

		// detach from window
		this.attachedWindow = this.attachedWindow?.Let(it =>
		{
			it.PropertyChanged -= this.OnWindowPropertyChanged;
			return (Avalonia.Controls.Window?)null;
		});
		this.checkAttachedScreenAction.Execute();

		// call base
		base.OnDetachedFromLogicalTree(e);
	}
    
    
    // Called to detach from session.
    void OnDetachFromSession(Session session)
    {
	    // detach
	    session.ImageSavingCompleted -= this.OnImageSavingCompleted;
	    session.PropertyChanged -= this.OnSessionPropertyChanged;
	    this.canOpenSourceFile.Unbind();
	    this.canResetBrightnessAdjustment.Unbind();
	    this.canResetColorAdjustment.Unbind();
	    this.canResetContrastAdjustment.Unbind();
	    this.canResetHighlightAdjustment.Unbind();
	    this.canResetSaturationAdjustment.Unbind();
	    this.canResetShadowAdjustment.Unbind();
	    this.canResetVibranceAdjustment.Unbind();
	    this.canSaveAsNewProfile.Unbind();
	    this.canSaveFilteredImage.Unbind();
	    this.canSaveRenderedImage.Unbind();
	    this.canShowEvaluateImageDimensionsMenu.Update(false);
	    this.showProgressBarAction.Cancel(); // no pending showing of progress bar is carried to the next session
	    this.SetValue(CanShowProgressBarProperty, false);
	    this.updateEffectiveRenderedImageAction.Execute();
	    
	    // dismiss notification
	    if (this.insufficientMemoryForRenderedImagesNotification is not null)
	    {
		    this.insufficientMemoryForRenderedImagesNotification.Dismiss();
		    this.insufficientMemoryForRenderedImagesNotification = null;
	    }
	    
	    // update state
	    this.keepHistogramsVisible = false;
	    this.keepRenderingParamsPanelVisible = false;
	    this.ReportImageViewportSize();
	    this.ReportScreenPixelDensity();
	    this.updateSelectedImageDisplayPixelBoundsAction.Schedule();
	    this.updateStatusBarStateAction.Schedule();
    }
    
    
    // Called when double clicked on brightness adjustment UI.
    void OnDoubleClickedOnBrightnessAdjustmentUI(Control control)
    {
	    if (this.DataContext is not Session session)
		    return;
	    string controlName = control.Name ?? "";
	    if (controlName.StartsWith("brightness"))
		    session.BrightnessAdjustment = 0;
	    else if (controlName.StartsWith("highlight"))
		    session.HighlightAdjustment = 0;
	    else if (controlName.StartsWith("shadow"))
		    session.ShadowAdjustment = 0;
    }
    
    
    // Called when double clicked on color adjustment UI.
    void OnDoubleClickedOnColorAdjustmentUI(Control control)
    {
	    if (this.DataContext is not Session session)
		    return;
	    string controlName = control.Name ?? "";
	    if (controlName.StartsWith("red"))
		    session.RedColorAdjustment = 0;
	    else if (controlName.StartsWith("green"))
		    session.GreenColorAdjustment = 0;
	    else if (controlName.StartsWith("blue"))
		    session.BlueColorAdjustment = 0;
	    else if (controlName.StartsWith("saturation"))
		    session.SaturationAdjustment = 0;
	    else if (controlName.StartsWith("vibrance"))
		    session.VibranceAdjustment = 0;
    }
    
    
    // Called when double clicked on contrast adjustment UI.
    void OnDoubleClickedOnContrastAdjustmentUI(Control control)
    {
	    if (this.DataContext is not Session session)
		    return;
	    session.ContrastAdjustment = 0;
    }


	// Called when drag over.
	void OnDragOver(object? sender, DragEventArgs e)
	{
		if (e.DataTransfer.HasFiles())
		{
			e.DragEffects = DragDropEffects.Copy;
			e.Handled = true;
		}
		else
			e.DragEffects = DragDropEffects.None;
	}


	// Called when dropped.
	void OnDrop(object? sender, DragEventArgs e)
	{
		_ = this.DropDataAsync(e.DataTransfer, e.KeyModifiers);
		e.Handled = true;
	}


	// Called when complete dragging splitter of histograms panel.
	void OnHistogramsPanelSplitterDragCompleted(object? sender, VectorEventArgs e) =>
		this.stopUsingSmallRenderedImageAction.Schedule();


	// Called when start dragging splitter of histograms panel.
	void OnHistogramsPanelSplitterDragStarted(object? sender, VectorEventArgs e) =>
		this.StartUsingSmallRenderedImage();


	// Called when double tap on image.
	void OnImageDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (this.DataContext is not Session session)
			return;
		if (session.FitImageToViewport)
		{
			session.FitImageToViewport = false;
			if (session.ZoomToCommand.CanExecute(1.0))
				session.ZoomToCommand.TryExecute(1.0);
		}
		else
			session.FitImageToViewport = true;
	}


	// Called when pinching on image viewer.
	void OnImagePinch(object? sender, PinchEventArgs e)
	{
		// check state
		if (this.DataContext is not Session session || session.RenderedImage is null)
			return;

		// capture initial state on the first event of the gesture
		if (this.pinchInitialScale is null)
		{
			var initialScale = session.ImageDisplayScale;
			if (!double.IsFinite(initialScale))
				return;
			var contentSize = this.imageScrollViewer.Extent;
			if (contentSize.Width <= 0 || contentSize.Height <= 0)
				return;
			this.pinchInitialScale = initialScale;
		}

		// refresh focal point each frame so the anchor follows the user's fingers
		this.gesturePivotInViewport = new Vector(e.ScaleOrigin.X, e.ScaleOrigin.Y);

		// use small image while gesture streams; the debounced restore fires once events stop
		this.StartUsingSmallRenderedImage();
		this.stopUsingSmallRenderedImageAction.Reschedule(StopUsingSmallRenderedImageDelayFast);

		// drive zoom (pivot fields tell OnSessionPropertyChanged how to anchor)
		this.isZoomingByGesture = true;
		try
		{
			if (session.FitImageToViewport)
				session.FitImageToViewport = false;
			else
				this.SetupTargetImageViewportPivot();
			var newScale = this.pinchInitialScale.Value * e.Scale;
			session.ZoomTo(newScale, animate: false);
		}
		finally
		{
			this.isZoomingByGesture = false;
		}

		// mark handled
		e.Handled = true;
	}


	// Called when pinch ended on image viewer.
	void OnImagePinchEnded(object? sender, PinchEndedEventArgs e)
	{
		// remember the pinched scale so subsequent zoom commands operate from here
		if (this.pinchInitialScale is not null && this.DataContext is Session session)
		{
			var finalScale = session.ImageDisplayScale;
			if (double.IsFinite(finalScale))
				session.RequestedImageDisplayScale = finalScale;
		}

		// clear gesture state
		this.pinchInitialScale = null;
		this.gesturePivotInViewport = null;
		this.targetImageViewportPivot = null;
		this.targetImageViewportPivotAnchor = null;

		// mark handled
		e.Handled = true;
	}


	// Called when pointer leave from image.
	void OnImagePointerLeave(object? sender, PointerEventArgs e)
	{
		this.latestPointerEventArgsOnImage = null;
		this.SetValue(IsPointerOverImageProperty, false);
		this.SetValue(PointerPositionOnImageControlProperty, new Point(-1, -1));
		(this.DataContext as Session)?.SelectRenderedImagePixel(-1, -1);
		this.updateSelectedImageDisplayPixelBoundsAction.Execute();
	}


	// Called when pointer moved on image.
	void OnImagePointerMoved(object? sender, PointerEventArgs e)
	{
		// report position
		var point = e.GetCurrentPoint(this.imageContainerBorder);
		this.latestPointerEventArgsOnImage = e;
		this.SetValue(PointerPositionOnImageControlProperty, point.Position);
		this.SetValue(IsPointerOverImageProperty, true);

		// move image
		this.imagePointerPressedContentPosition?.Let(it =>
		{
			point = e.GetCurrentPoint(this.imageScrollViewer);
			if (point.Properties.IsLeftButtonPressed)
			{
				var bounds = this.imageScrollViewer.Bounds;
				if (bounds.Width > 0 && bounds.Height > 0)
					this.ScrollImageScrollViewer(it, new Vector(point.Position.X / bounds.Width, point.Position.Y / bounds.Height));
			}
			else
			{
				this.imagePointerPressedContentPosition = null;
				this.stopUsingSmallRenderedImageAction.Schedule();
			}
		});

		// select pixel on image
		this.SelectImageDisplayPixel(e);
	}


	// Called when pressing on image viewer.
	void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.Pointer.Type == PointerType.Mouse)
		{
			this.SetValue(IsPointerPressedOnImageProperty, true);
			if (this.IsImageViewerScrollable)
			{
				var pointer = e.GetCurrentPoint(this.imageScrollViewer);
				if (pointer.Properties.IsLeftButtonPressed)
				{
					var contentSize = this.imageScrollViewer.Extent;
					var offset = this.imageScrollViewer.Offset;
					if (contentSize.Width > 0 && contentSize.Height > 0)
					{
						this.imagePointerPressedContentPosition = new Vector(
							(pointer.Position.X + offset.X) / contentSize.Width, 
							(pointer.Position.Y + offset.Y) / contentSize.Height);
						this.StartUsingSmallRenderedImage();
					}
				}
			}
		}
	}


	// Called when releasing pointer from image viewer.
	void OnImagePointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		this.imagePointerPressedContentPosition = null;
		this.stopUsingSmallRenderedImageAction.Schedule();
		this.SetValue(IsPointerPressedOnImageProperty, false);
	}


	// Called when magnify gesture on touchpad detected.
	void OnImagePointerTouchPadGestureMagnify(object? sender, PointerDeltaEventArgs e)
	{
		// check state
		if (this.DataContext is not Session session || session.RenderedImage is null)
			return;

		// extract scale delta following ULogViewer LogChart pattern
		var delta = e.Delta;
		var magnitude = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
		if (magnitude < 0.001)
			return;
		var sign = Math.Sign(delta.X);
		if (sign == 0)
			sign = Math.Sign(delta.Y);
		if (sign == 0)
			return;

		// validate scrollviewer state
		var initialScale = session.ImageDisplayScale;
		var contentSize = this.imageScrollViewer.Extent;
		if (!double.IsFinite(initialScale) || contentSize.Width <= 0 || contentSize.Height <= 0)
			return;

		// expose the gesture pivot (current cursor position) so OnSessionPropertyChanged anchors the zoom here
		var pivot = e.GetPosition(this.imageScrollViewer);
		this.gesturePivotInViewport = new Vector(pivot.X, pivot.Y);

		// use small image while gesture streams; the debounced restore fires once events stop
		this.StartUsingSmallRenderedImage();
		this.stopUsingSmallRenderedImageAction.Reschedule(StopUsingSmallRenderedImageDelayFast);

		// drive zoom around the captured pivot
		this.isZoomingByGesture = true;
		try
		{
			if (session.FitImageToViewport)
				session.FitImageToViewport = false;
			else
				this.SetupTargetImageViewportPivot();

			var newScale = initialScale * (1.0 + sign * magnitude);
			var appliedScale = session.ZoomTo(newScale, animate: false);
			if (double.IsFinite(appliedScale))
				session.RequestedImageDisplayScale = appliedScale;
		}
		finally
		{
			this.isZoomingByGesture = false;
		}

		// re-select pixel under cursor since the image scale changed
		this.SelectImageDisplayPixel(e);

		// mark handled
		e.Handled = true;
	}


	// Called when image saving completed.
	void OnImageSavingCompleted(object? sender, Session.ImageSavingCompletedEventArgs e)
	{
		if (this.attachedWindow is null)
			return;
		// ReSharper disable once SuspiciousTypeConversion.Global
		if (this.attachedWindow is ASControls.INotificationPresenter notificationPresenter)
		{
			notificationPresenter.AddNotification(new ASControls.Notification().Also(notification =>
			{
				if (e.IsSucceeded)
				{
					if (Platform.IsOpeningFileManagerSupported)
					{
						notification.Actions = new List<ASControls.NotificationAction>
						{
							new ASControls.NotificationAction().Also(action =>
							{
								action.Command = new Command(() =>
								{
									Platform.OpenFileManager(e.FileName);
									notification.Dismiss();
								});
								action.BindToResource(ASControls.NotificationAction.NameProperty, this, "String/SessionControl.ShowFileInExplorer");
							})
						};
					}
					notification.BindToResource(ASControls.Notification.IconProperty, this, "Image/Icon.Success.Colored.Gradient");
					notification.Bind(ASControls.Notification.MessageProperty, new FormattedString().Also(it =>
					{
						it.BindToResource(FormattedString.FormatProperty, this, "String/SessionControl.ImageSavingSucceeded");
						it.Arg1 = e.FileName;
					}));
				}
				else
				{
					notification.BindToResource(ASControls.Notification.IconProperty, this, "Image/Icon.Error.Colored.Gradient");
					notification.Bind(ASControls.Notification.MessageProperty, new FormattedString().Also(it =>
					{
						it.BindToResource(FormattedString.FormatProperty, this, "String/SessionControl.ImageSavingFailed");
						it.Arg1 = e.FileName;
					}));
				}
			}));
		}
		else
		{
			new ASControls.MessageDialog().Also(it =>
			{
				if (e.IsSucceeded)
				{
					it.Icon = ASControls.MessageDialogIcon.Success;
					it.Message = new FormattedString().Also(it =>
					{
						it.BindToResource(FormattedString.FormatProperty, this, "String/SessionControl.ImageSavingSucceeded");
						it.Arg1 = e.FileName;
					});
				}
				else
				{
					it.Icon = ASControls.MessageDialogIcon.Error;
					it.Message = new FormattedString().Also(it =>
					{
						it.BindToResource(FormattedString.FormatProperty, this, "String/SessionControl.ImageSavingFailed");
						it.Arg1 = e.FileName;
					});
				}
			}).ShowDialog(this.attachedWindow);
		}
	}
	
	
	// Called when extent of image scroll viewer changed.
	void OnImageScrollViewerExtentChanged(Size extent)
	{
		// log and update scrollability
		this.Logger.LogTrace("Image viewer extent changed to {x:F1}x{y:F1}", extent.Width, extent.Height);
		this.updateIsImageViewerScrollableAction.Schedule();

		// apply pivot anchor (precise single-shot, also keeps animated zoom on-pivot per frame)
		if (this.targetImageViewportPivot.HasValue && this.targetImageViewportPivotAnchor.HasValue)
		{
			var pivot = this.targetImageViewportPivot.Value;
			var anchor = this.targetImageViewportPivotAnchor.Value;
			this.targetImageViewportCenter = null;
			this.ScrollImageScrollViewer(pivot, anchor);
			// keep pivot fields set across animated zoom frames; clear after a one-shot apply
			if (this.DataContext is not Session s || !s.IsZooming)
			{
				this.targetImageViewportPivot = null;
				this.targetImageViewportPivotAnchor = null;
			}
			return;
		}

		// apply view-center fallback (e.g. fit-mode rearrange or non-pivot layout change)
		if (this.targetImageViewportCenter.HasValue)
		{
			var center = this.targetImageViewportCenter.Value;
			this.targetImageViewportCenter = null;
			this.ScrollImageScrollViewer(center, new Vector(0.5, 0.5));
		}
	}


	// Called when pressing on image scroll viewer.
	void OnImageScrollViewerPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		this.imageScrollViewer.Focusable = true;
		this.imageScrollViewer.Focus();
		this.imageScrollViewer.Focusable = false;
	}


	// Called when complete dragging splitter of options panel.
	void OnOptionsPanelSplitterDragCompleted(object? sender, VectorEventArgs e) =>
		this.stopUsingSmallRenderedImageAction.Schedule();


	// Called when start dragging splitter of options panel.
	void OnOptionsPanelSplitterDragStarted(object? sender, VectorEventArgs e) =>
		this.StartUsingSmallRenderedImage();
	
	
	// Called to handle key down event.
    internal void OnPreviewKeyDown(KeyEventArgs e)
    {
		// call base
		if (e.Handled)
			return;

		// check focus
		var isFocusedOnEditor = this.attachedWindow?.FocusManager?.GetFocusedElement()?.Let(it => 
			it is TextBox || it is NumericUpDown) ?? false;

		// get session
		if (this.DataContext is not Session session)
			return;

		// handle key event
		this.pressedKeys.Add(e.Key);
		var isCtrlPressed = Platform.IsMacOS 
			? (e.KeyModifiers & KeyModifiers.Meta) != 0 
			: (e.KeyModifiers & KeyModifiers.Control) != 0;
		if (isCtrlPressed)
		{
			switch (e.Key)
			{
				case Key.D0:
					if (!isFocusedOnEditor)
					{
						session.FitImageToViewport = true;
						e.Handled = true;
					}
					break;
				case Key.D1:
					if (!isFocusedOnEditor)
					{
						if (session.FitImageToViewport)
						{
							session.RequestedImageDisplayScale = 1.0;
							session.FitImageToViewport = false;
						}
						else
							session.ZoomToCommand.TryExecute(1.0);
						e.Handled = true;
					}
					break;
				case Key.O:
				{
					_ = this.OpenSourceFile();
					e.Handled = true;
					break;
				}
				case Key.OemPlus:
					if (!isFocusedOnEditor)
					{
						if (session.FitImageToViewport)
						{
							session.RequestedImageDisplayScale = session.ImageDisplayScale;
							session.FitImageToViewport = false;
						}
						session.ZoomInCommand.Execute(null);
						e.Handled = true;
					}
					break;
				case Key.OemMinus:
					if (!isFocusedOnEditor)
					{
						if (session.FitImageToViewport)
						{
							session.RequestedImageDisplayScale = session.ImageDisplayScale;
							session.FitImageToViewport = false;
						}
						session.ZoomOutCommand.Execute(null);
						e.Handled = true;
					}
					break;
				case Key.S:
					_ = this.SaveImage();
					e.Handled = true;
					break;
			}
		}
	}


    // Called to handle key up event.
	internal void OnPreviewKeyUp(KeyEventArgs e)
	{
		// call base
		if (e.Handled)
		{
			this.pressedKeys.Remove(e.Key);
			return;
		}

		// check focus
		var focusedElement = this.attachedWindow?.FocusManager?.GetFocusedElement();
		if (focusedElement is Visual focusedVisual)
		{
			if (focusedElement is TextBox || focusedElement is NumericUpDown)
			{
				this.pressedKeys.Remove(e.Key);
				return;
			}
			if (focusedVisual.FindAncestorOfType<SessionControl>(true) != this)
			{
				this.pressedKeys.Remove(e.Key);
				return;
			}
		}

		// prevent handling key without pressing
		if (!this.pressedKeys.Contains(e.Key))
			return;

		// get session
		if (this.DataContext is not Session session)
			return;

		// handle key event
		if (e.KeyModifiers == 0)
		{
			switch (e.Key)
			{
				case Key.End:
					session.MoveToLastFrameCommand.TryExecute();
					break;
				case Key.Home:
					session.MoveToFirstFrameCommand.TryExecute();
					break;
				case Key.PageDown:
					session.MoveToNextFrameCommand.TryExecute();
					break;
				case Key.PageUp:
					session.MoveToPreviousFrameCommand.TryExecute();
					break;
				default:
					return;
			}
			e.Handled = true;
		}
		this.pressedKeys.Remove(e.Key);
	}
	

	// Called when pointer pressed on brightness adjustment UI.
	void OnPointerPressedOnBrightnessAdjustmentUI(object? sender, PointerEventArgs e)
	{
		if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
		{
			this.resetPointerPressedOnBrightnessAdjustmentUIAction.Cancel();
			this.SetValue(IsPointerPressedOnBrightnessAdjustmentUIProperty, true);
		}
	}
	

	// Called when pointer pressed on color adjustment UI.
	void OnPointerPressedOnColorAdjustmentUI(object? sender, PointerEventArgs e)
	{
		if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
		{
			this.resetPointerPressedOnColorAdjustmentUIAction.Cancel();
			this.SetValue(IsPointerPressedOnColorAdjustmentUIProperty, true);
		}
	}


	// Called when pointer pressed on contrast adjustment UI.
	void OnPointerPressedOnContrastAdjustmentUI(object? sender, PointerEventArgs e)
	{
		if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
		{
			this.resetPointerPressedOnContrastAdjustmentUIAction.Cancel();
			this.SetValue(IsPointerPressedOnContrastAdjustmentUIProperty, true);
		}
	}


	// Called when pointer released on brightness adjustment UI.
	void OnPointerReleasedOnBrightnessAdjustmentUI(object? sender, PointerReleasedEventArgs e) =>
		this.resetPointerPressedOnBrightnessAdjustmentUIAction.Reschedule(ResetPointerPressedOnFilterParamsUIDelay);


	// Called when pointer released on color adjustment UI.
	void OnPointerReleasedOnColorAdjustmentUI(object? sender, PointerReleasedEventArgs e) =>
		this.resetPointerPressedOnColorAdjustmentUIAction.Reschedule(ResetPointerPressedOnFilterParamsUIDelay);
	

	// Called when pointer released on contrast adjustment UI.
	void OnPointerReleasedOnContrastAdjustmentUI(object? sender, PointerReleasedEventArgs e) =>
		this.resetPointerPressedOnContrastAdjustmentUIAction.Reschedule(ResetPointerPressedOnFilterParamsUIDelay);


	// Called when changing mouse wheel.
	void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
	{
		if (!this.imageScrollViewer.IsPointerOver)
			return;
		if (this.targetImageViewportPivot.HasValue)
		{
			this.Logger.LogDebug("Drop viewport pivot");
			this.targetImageViewportPivot = null;
			this.targetImageViewportPivotAnchor = null;
		}
		if (this.latestPointerEventArgsOnImage is not null)
			this.SelectImageDisplayPixel(this.latestPointerEventArgsOnImage);
		if ((e.KeyModifiers & KeyModifiers.Control) == 0)
			return;
		if (this.DataContext is not Session session || !session.IsSourceOpened || session.FitImageToViewport)
			return;
		var zoomed = false;
		if (e.Delta.Y > 0)
		{
			for (var i = (int)(e.Delta.Y + 0.5); i > 0; --i)
			{
				if (session.ZoomInCommand.TryExecute())
					zoomed = true;
			}
		}
		else if (e.Delta.Y < 0)
		{
			for (var i = (int)(e.Delta.Y - 0.5); i < 0; ++i)
			{
				if (session.ZoomOutCommand.TryExecute())
					zoomed = true;
			}
		}
		e.Handled = zoomed;
	}


	// Called when property changed.
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		var property = change.Property;
		if (property == DataContextProperty)
		{
			if (change.OldValue is Session oldSession)
				this.OnDetachFromSession(oldSession);
			if (change.NewValue is Session newSession)
				this.OnAttachToSession(newSession);
		}
		else if (property == EffectiveRenderedImageProperty)
			this.updateEffectiveRenderedImageIntModeAction.Schedule();
		else if (property == IsImageViewerScrollableProperty
			|| property == IsPointerOverImageProperty
			|| property == IsPointerPressedOnImageProperty)
        {
			this.updateImageCursorAction.Schedule();
        }
		else if (property == IsPointerPressedOnBrightnessAdjustmentUIProperty
			|| property == IsPointerPressedOnColorAdjustmentUIProperty
			|| property == IsPointerPressedOnContrastAdjustmentUIProperty)
		{
			this.updateImageFilterParamsPopupOpacityAction.Schedule();
		}
    }


	// Called when property of session changed.
	void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not Session session)
			return;
		switch (e.PropertyName)
		{
			case nameof(Session.FitImageToViewport):
				{
					// [Workaround] rearrange scroll viewer of the image viewer
					var padding = this.imageScrollViewer.Padding;
					this.imageScrollViewer.Padding = new Thickness(-1);
					this.imageScrollViewer.Padding = padding;
					this.targetImageViewportCenter = new Vector(0.5, 0.5);
					
					// update scroll viewer
					this.updateImageViewerScrollBarsAction.Schedule();
					break;
				}
			case nameof(Session.HasRenderingError):
			case nameof(Session.InsufficientMemoryForRenderedImage):
				if (session.InsufficientMemoryForRenderedImage)
				{
					if (this.insufficientMemoryForRenderedImagesNotification is null && this.attachedWindow is not null)
					{
						if (this.attachedWindow is ASControls.INotificationPresenter notificationPresenter)
						{
							this.insufficientMemoryForRenderedImagesNotification = new ASControls.Notification().Also(notification =>
							{
								notification.Actions = new List<ASControls.NotificationAction>
								{
									new ASControls.NotificationAction().Also(action =>
									{
										action.Command = new Command(() =>
										{
											_ = this.Application.ShowApplicationOptionsDialogAsync(this.attachedWindow, nameof(ApplicationOptionsDialogSection.MaxRenderedImagesMemoryUsage));
											notification.Dismiss();
										});
										action.BindToResource(ASControls.NotificationAction.NameProperty, this, "String/SessionControl.ApplicationOptions");
									})
								};
								notification.BindToResource(ASControls.Notification.IconProperty, this, "Image/Icon.Warning.Colored");
								notification.BindToResource(ASControls.Notification.MessageProperty, this, "String/SessionControl.InsufficientMemoryForRenderedImage");
								notification.Timeout = null;
							});
							notificationPresenter.AddNotification(this.insufficientMemoryForRenderedImagesNotification);
						}
						else
						{
							_ = new ASControls.MessageDialog().Also(it =>
							{
								it.Icon = ASControls.MessageDialogIcon.Warning;
								it.Message = this.Application.GetObservableString("SessionControl.InsufficientMemoryForRenderedImage");
							}).ShowDialog(this.attachedWindow);
						}
					}
				}
				else if (this.insufficientMemoryForRenderedImagesNotification is not null)
				{
					this.insufficientMemoryForRenderedImagesNotification.Dismiss();
					this.insufficientMemoryForRenderedImagesNotification = null;
				}
				this.updateStatusBarStateAction.Schedule();
				break;
			case nameof(Session.ImageDisplaySize):
			{
				// log the new size
				var imageSize = session.ImageDisplaySize;
				this.Logger.LogTrace("Image display size: {w:F1}x{h:F1}", imageSize.Width, imageSize.Height);

				// refresh pivot per frame for active zoom (cursor stable for animated zoom; updated per event for gesture)
				if (this.targetImageViewportPivot.HasValue)
					this.SetupTargetImageViewportPivot();
				// no pivot path active — preserve current view center for non-zoom layout changes
				else if (!session.FitImageToViewport)
				{
					var viewportSize = this.imageScrollViewer.Viewport;
					var viewportOffset = this.imageScrollViewer.Offset;
					var contentSize = this.imageScrollViewer.Extent;
					if (contentSize.Width > 0 && contentSize.Height > 0)
					{
						this.targetImageViewportCenter = new Vector(
							(viewportOffset.X + viewportSize.Width / 2) / contentSize.Width,
							(viewportOffset.Y + viewportSize.Height / 2) / contentSize.Height);
					}
				}

				// schedule dependent updates
				this.updateSelectedImageDisplayPixelBoundsAction.Schedule();
				this.updateEffectiveRenderedImageIntModeAction.Schedule();
				break;
			}
			case nameof(Session.IsHistogramsVisible):
				if (session.IsHistogramsVisible)
				{
					this.keepHistogramsVisible = true;
					this.histogramsPanelColumn.MinWidth = Session.MinHistogramsPanelSize;
					this.histogramsPanelColumn.Width = new GridLength(session.HistogramsPanelSize, GridUnitType.Pixel);
				}
				else
				{
					this.histogramsPanelColumn.MinWidth = 0;
					this.histogramsPanelColumn.Width = new GridLength(0, GridUnitType.Pixel);
				}
				this.updateImageViewerShadowMarginAction.Schedule();
				break;
			case nameof(Session.IsProcessingImage):
				this.UpdateCanShowProgressBar();
				break;
			case nameof(Session.IsRenderingImage):
				if (session.IsRenderingImage && this.insufficientMemoryForRenderedImagesNotification is not null)
				{
					this.insufficientMemoryForRenderedImagesNotification.Dismiss();
					this.insufficientMemoryForRenderedImagesNotification = null;
				}
				break;
			case nameof(Session.IsRenderingParametersPanelVisible):
				if (session.IsRenderingParametersPanelVisible)
				{
					Grid.SetColumnSpan(this.imageViewerGrid, 2);
					this.keepRenderingParamsPanelVisible = true;
					this.renderingParamsPanelColumn.MinWidth = Session.MinRenderingParametersPanelSize;
					this.renderingParamsPanelColumn.Width = new GridLength(session.RenderingParametersPanelSize, GridUnitType.Pixel);
				}
				else
				{
					Grid.SetColumnSpan(this.imageViewerGrid, 4);
					this.renderingParamsPanelColumn.MinWidth = 0;
					this.renderingParamsPanelColumn.Width = new GridLength(0, GridUnitType.Pixel);
				}
				this.updateImageViewerShadowMarginAction.Schedule();
				break;
			case nameof(Session.IsSourceOpened):
				this.canShowEvaluateImageDimensionsMenu.Update(session.IsSourceOpened);
				this.updateStatusBarStateAction.Schedule();
				break;
			case nameof(Session.IsZooming):
				if (session.IsZooming)
				{
					this.Logger.LogTrace("Start zooming, fit image to viewport: {fitToViewport}", session.FitImageToViewport);
					this.StartUsingSmallRenderedImage();
				}
				else
				{
					this.Logger.LogTrace("Stop zooming");
					if (!this.stopUsingSmallRenderedImageAction.IsScheduled)
						this.stopUsingSmallRenderedImageAction.Execute();
					if (this.latestPointerEventArgsOnImage is not null)
						this.SelectImageDisplayPixel(this.latestPointerEventArgsOnImage);
				}
				this.SetupTargetImageViewportPivot();
				this.updateImageViewerScrollBarsAction.Schedule();
				this.updateSelectedImageDisplayPixelBoundsAction.Schedule();
				break;
			case nameof(Session.QuarterSizeRenderedImage):
			case nameof(Session.RenderedImage):
				this.updateEffectiveRenderedImageAction.Execute();
				if (this.latestPointerEventArgsOnImage is not null) // select pixel of new image which is pointed by pointer
					this.SelectImageDisplayPixel(this.latestPointerEventArgsOnImage);
				break;
			case nameof(Session.IsAlphaChannelAvailable):
			case nameof(Session.SelectedRenderedImagePixelColor):
			case nameof(Session.SourceImageEffectiveBits):
				this.updateSelectedImageDisplayPixelArgbStringAction.Schedule();
				break;
			case nameof(Session.SelectedRenderedImagePixelPositionX):
			case nameof(Session.SelectedRenderedImagePixelPositionY):
				this.updateSelectedImageDisplayPixelBoundsAction.Schedule();
				break;
		}
	}


	// Called when setting changed.
	void OnSettingChanged(object? sender, SettingChangedEventArgs e)
	{
		if (e.Key == SettingKeys.HideImageViewerScrollBarsAutomatically)
			this.SetValue(HideImageViewerScrollBarsAutomaticallyProperty, (bool)e.Value);
		else if (e.Key == SettingKeys.SelectedRenderedImagePixelArgbColorFormat)
			this.updateSelectedImageDisplayPixelArgbStringAction.Schedule();
		else if (e.Key == SettingKeys.ShowProcessInfo)
			this.SetValue(ShowProcessInfoProperty, (bool)e.Value);
		else if (e.Key == SettingKeys.ShowSelectedRenderedImagePixelArgbColor)
			this.SetValue(ShowSelectedRenderedImagePixelArgbColorProperty, (bool)e.Value);
		else if (e.Key == SettingKeys.ShowSelectedRenderedImagePixelLabColor)
			this.SetValue(ShowSelectedRenderedImagePixelLabColorProperty, (bool)e.Value);
		else if (e.Key == SettingKeys.ShowSelectedRenderedImagePixelXyzColor)
			this.SetValue(ShowSelectedRenderedImagePixelXyzColorProperty, (bool)e.Value);
	}


	/// <summary>
	/// Called when test button clicked.
	/// </summary>
	public async Task OnTestButtonClick()
	{
		if (this.attachedWindow == null)
			return;
		var fileName = (await this.attachedWindow.StorageProvider.OpenFilePickerAsync(new())).Let(it => 
			it.Count == 1 ? it[0].TryGetLocalPath() : null);
		if (string.IsNullOrEmpty(fileName))
			return;
		
		using var dataSource = await Media.FFmpegVideoDataSource.TryCreateAsync(this.Application, fileName);
	}


	// Called when property of window changed.
	void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		var property = e.Property;
		if (property == BoundsProperty)
		{
			if (this.attachedWindow is not CarinaStudio.Controls.Window csWindow || csWindow.IsOpened)
				this.checkAttachedScreenAction.Schedule(AttachedScreenCheckingInterval);
		}
		else if (property == HeightProperty || property == WidthProperty)
		{
			this.StartUsingSmallRenderedImage();
			this.stopUsingSmallRenderedImageAction.Reschedule(StopUsingSmallRenderedImageDelay);
		}
		else if (property == CarinaStudio.Controls.Window.IsOpenedProperty)
		{
			if ((bool)e.NewValue.AsNonNull())
				this.checkAttachedScreenAction.Execute();
		}
		else if (property == Avalonia.Controls.Window.WindowStateProperty)
		{
			if ((WindowState)e.OldValue.AsNonNull() == WindowState.Maximized 
				|| (WindowState)e.NewValue.AsNonNull() == WindowState.Maximized)
			{
				this.StartUsingSmallRenderedImage();
				this.stopUsingSmallRenderedImageAction.Reschedule(StopUsingSmallRenderedImageDelay);
			}
		}
	}


	/// <summary>
	/// Open brightness and contrast adjustment UI.
	/// </summary>
	public void OpenBrightnessAndContrastAdjustmentPopup() => 
		this.brightnessAndContrastAdjustmentPopup.Open();


	/// <summary>
	/// Open color adjustment UI.
	/// </summary>
	public void OpenColorAdjustmentPopup() =>
		this.colorAdjustmentPopup.Open();


	/// <summary>
	/// Open frame playback options UI.
	/// </summary>
	public void OpenFramePlaybackOptionsPopup() =>
		this.framePlaybackOptionsPopup.Open();


	// Open source file.
	async Task OpenSourceFile()
	{
		// find window
		if (this.attachedWindow == null)
		{
			Logger.LogError("No window to show open file dialog");
			return;
		}

		// select files
		var files = await this.attachedWindow.StorageProvider.OpenFilePickerAsync(new() { AllowMultiple = true });
		var fileNames = new List<string>(files.Count);
		foreach (var file in files)
		{
			var fileName = file.TryGetLocalPath();
			if (!string.IsNullOrEmpty(fileName))
				fileNames.Add(fileName);
		}
		if (fileNames.Count == 0)
			return;

		// open files
		await this.OpenFilesAsync(fileNames, preferNewSession: false);
	}


	// Open the given files. When multiple files are selected the user is asked whether to view them
	// independently (one session per file) or play them as a single frame sequence. The files replace the
	// source of the current session unless a new session is preferred, such as opening files by dragging and dropping.
	async Task OpenFilesAsync(IList<string> fileNames, bool preferNewSession)
	{
		// check state
		if (fileNames.Count == 0 || this.attachedWindow == null)
			return;
		if (this.DataContext is not Session session)
		{
			Logger.LogError("No session to open files");
			return;
		}

		// single file
		if (fileNames.Count == 1)
		{
			if (preferNewSession
				&& this.Settings.GetValueOrDefault(SettingKeys.CreateNewSessionForDragDropFile)
				&& session.IsSourceOpened
				&& session.Owner is Workspace singleFileWorkspace)
			{
				singleFileWorkspace.CreateAndAttachSession(fileNames[0]);
			}
			else
				this.OpenSourceFile(fileNames[0]);
			return;
		}

		// ask how to open multiple files
		var app = this.Application;
		var mode = await new ASControls.MessageDialog
		{
			Buttons = ASControls.MessageDialogButtons.YesNoCancel,
			CustomNoText = app.GetObservableString("SessionControl.SelectMultiFileOpenMode.Default"),
			CustomYesText = app.GetObservableString("SessionControl.SelectMultiFileOpenMode.FrameSequence"),
			Icon = ASControls.MessageDialogIcon.Question,
			Message = new FormattedString().Also(it =>
			{
				it.Arg1 = fileNames.Count;
				it.Bind(FormattedString.FormatProperty, app.GetObservableString("SessionControl.SelectMultiFileOpenMode"));
			}),
			Title = app.GetObservableString("SessionControl.SelectMultiFileOpenMode.Title"),
		}.ShowDialog(this.attachedWindow);
		if (mode is not (ASControls.MessageDialogResult.Yes or ASControls.MessageDialogResult.No))
			return; // cancelled, or dialog closed without selecting a mode

		// play as a single frame sequence
		if (mode == ASControls.MessageDialogResult.Yes)
		{
			if (preferNewSession
				&& this.Settings.GetValueOrDefault(SettingKeys.CreateNewSessionForDragDropFile)
				&& session.IsSourceOpened
				&& session.Owner is Workspace sequenceWorkspace)
			{
				var index = sequenceWorkspace.Sessions.IndexOf(session);
				var newSession = sequenceWorkspace.CreateAndAttachSession(index >= 0 ? index + 1 : sequenceWorkspace.Sessions.Count);
				newSession.OpenSourceFilesCommand.TryExecute(fileNames);
			}
			else
				session.OpenSourceFilesCommand.TryExecute(fileNames);
			return;
		}

		// view independently: one session per file
		if (fileNames.Count > 8)
		{
			await new ASControls.MessageDialog()
			{
				Icon = ASControls.MessageDialogIcon.Warning,
				Message = this.GetResourceObservable("String/SessionControl.MaxDragDropFileCountReached"),
			}.ShowDialog(this.attachedWindow);
			return;
		}
		var profile = await new ImageRenderingProfileSelectionDialog().Also(it =>
		{
			it.Bind(ImageRenderingProfileSelectionDialog.MessageProperty, this.GetResourceObservable("String/SessionControl.SelectProfileToOpenFiles"));
		}).ShowDialog<ImageRenderingProfile?>(this.attachedWindow);
		if (profile == null)
			return;
		if (session.Owner is not Workspace workspace)
			return;
		var independentIndex = workspace.Sessions.IndexOf(session);
		if (independentIndex >= 0)
			++independentIndex;
		else
			independentIndex = workspace.Sessions.Count;
		// state of session is updated asynchronously after opening the file, so the current session is tracked by the local state instead
		var isCurrentSessionUsed = session.IsSourceOpened;
		foreach (var fileName in fileNames)
		{
			if (isCurrentSessionUsed)
				workspace.CreateAndAttachSession(independentIndex++, fileName, profile);
			else
			{
				session.OpenSourceFileCommand.TryExecute(fileName);
				session.Profile = profile;
				isCurrentSessionUsed = true;
			}
		}
	}
	void OpenSourceFile(string fileName)
	{
		// check state
		if (this.DataContext is not Session session)
		{
			Logger.LogError("No session to open source file");
			return;
		}
		var command = session.OpenSourceFileCommand;
		if (!command.CanExecute(fileName))
		{
			Logger.LogError("Cannot change source file in current state");
			return;
		}

		// open file
		command.Execute(fileName);
	}


	/// <summary>
	/// <see cref="ICommand"/> to open source file.
	/// </summary>
	public ICommand OpenSourceFileCommand { get; }


	// [Workaround] Force refreshing content shown by given combo box, including the content of its selection box.
	static void RefreshComboBoxContent(ComboBox comboBox)
	{
		var template = comboBox.ItemTemplate;
		comboBox.ItemTemplate = null;
		comboBox.ItemTemplate = template;
	}


	// Report viewport of image to Session.
	void ReportImageViewportSize()
	{
		if (this.DataContext is not Session session)
			return;
		var bounds = this.imageScrollViewer.Bounds;
		var padding = this.imageScrollViewerPadding;
		var width = Math.Max(0, bounds.Width - padding.Left - padding.Right);
		var height = Math.Max(0, bounds.Height - padding.Top - padding.Bottom);
		session.ImageViewportSize = new Size(width, height);
	}


	// Report screen pixel density to Session.
	void ReportScreenPixelDensity()
	{
		if (this.DataContext is not Session session)
			return;
		if (this.attachedScreen is null)
			return;
		session.ScreenPixelDensity = this.attachedScreen.Scaling;
	}


	// Reset brightness and contrast.
	void ResetBrightnessAndContrastAdjustment()
    {
		if (this.DataContext is Session session)
		{
			session.ResetBrightnessAdjustmentCommand.TryExecute();
			session.ResetContrastAdjustmentCommand.TryExecute();
			session.ResetHighlightAdjustmentCommand.TryExecute();
			session.ResetShadowAdjustmentCommand.TryExecute();
		}
    }


	// Command to reset brightness and contrast.
	public ICommand ResetBrightnessAndContrastAdjustmentCommand { get; }


	// Reset color adjustment.
	void ResetColorAdjustment()
	{
		if (this.DataContext is not Session session)
			return;
		session.ResetColorAdjustmentCommand.TryExecute();
		session.ResetSaturationAdjustmentCommand.TryExecute();
		session.ResetVibranceAdjustmentCommand.TryExecute();
	}


	// Command to reset color adjustment.
	public ICommand ResetColorAdjustmentCommand { get; }


	// Save as new profile.
	async Task SaveAsNewProfile()
	{
		// check state
		if (this.DataContext is not Session session)
		{
			Logger.LogError("No session to save as new profile");
			return;
		}

		// find window
		if (this.attachedWindow == null)
		{
			Logger.LogError("No window to show dialog");
			return;
		}

		// get name
		var name = session.GenerateNameForNewProfile();
		while (true)
		{
			// input name
			name = await new ASControls.TextInputDialog()
			{
				InitialText = name,
				Message = this.GetResourceObservable("String/SessionControl.InputNameOfProfile"),
			}.ShowDialog(this.attachedWindow);
			if (string.IsNullOrWhiteSpace(name))
				return;

			// check name
			if (ImageRenderingProfiles.ValidateNewUserDefinedProfileName(name))
				break;

			// show message for duplicate name
			await new ASControls.MessageDialog()
			{
				Icon = ASControls.MessageDialogIcon.Warning,
				Message = string.Format(this.Application.GetStringNonNull("SessionControl.DuplicateNameOfProfile"), name),
			}.ShowDialog(this.attachedWindow);
		}

		// save as new profile
		session.SaveAsNewProfileCommand.Execute(name);
	}


	/// <summary>
	/// <see cref="ICommand"/> to save parameters as new profile.
	/// </summary>
	public ICommand SaveAsNewProfileCommand { get; }


	// Save image to file.
	async Task SaveImage()
	{
		// check state
		if (this.DataContext is not Session session)
		{
			Logger.LogError("No session to save rendered image");
			return;
		}

		// find window
		if (this.attachedWindow is null)
		{
			Logger.LogError("No window to show dialog");
			return;
		}

		// stop playing frames to keep the image being saved same as the image user sees
		session.StopPlayingFrames();

		// select image to save
		var saveFilteredImage = false;
		if (session.IsFilteringRenderedImageNeeded)
		{
			var result = await new ASControls.MessageDialog()
			{
				Buttons = ASControls.MessageDialogButtons.YesNoCancel,
				DefaultResult = ASControls.MessageDialogResult.Yes,
				Icon = ASControls.MessageDialogIcon.Question,
				Message = this.GetResourceObservable("String/SessionControl.ConfirmSavingFilteredImage")
			}.ShowDialog(this.attachedWindow);
			if (result == ASControls.MessageDialogResult.Cancel)
				return;
			saveFilteredImage = (result == ASControls.MessageDialogResult.Yes);
		}

		// select file
		var app = (App)this.Application;
		var fileName = (await this.attachedWindow.StorageProvider.SaveFilePickerAsync(new()
		{
			FileTypeChoices =
			[
				new(app.GetStringNonNull("FileType.Jpeg"))
				{
					Patterns = [ "*.jpg", "*.jpeg", "*.jpe", "*.jfif" ],
				},
				new(app.GetStringNonNull("FileType.Png"))
				{
					Patterns = [ "*.png" ],
				},
				new(app.GetStringNonNull("FileType.RawBgra"))
				{
					Patterns = [ "*.bgra" ],
				},
				new(app.GetStringNonNull("FileType.Tiff"))
				{
					Patterns = [ "*.tif", "*.tiff" ],
				}
			],
			SuggestedFileName = session.SourceFileName?.Let(it => Path.GetFileNameWithoutExtension(it) + ".jpg") ?? $"Export_{session.ImageWidth}x{session.ImageHeight}.jpg"
		}))?.Let(it => it.TryGetLocalPath());
		if (string.IsNullOrEmpty(fileName))
			return;

		// check format
		var fileFormat = (Media.FileFormat?)null;
		if (Media.FileFormats.TryGetFormatsByFileName(fileName, out var fileFormats))
			fileFormat = fileFormats.First();

		// setup parameters
		var parameters = new Session.ImageSavingParams();
		if (fileFormat == Media.FileFormats.Jpeg)
		{
			var jpegOptions = await new JpegImageEncodingOptionsDialog().ShowDialog<Media.ImageEncoders.ImageEncodingOptions?>(this.attachedWindow);
			if (jpegOptions == null)
				return;
			parameters.Options = jpegOptions.Value;
		}
		parameters.FileName = fileName;

		// find encoder
		if (fileFormat != null && Media.ImageEncoders.ImageEncoders.TryGetEncoderByFormat(fileFormat, out var encoder))
			parameters.Encoder = encoder;

		// save
		if (saveFilteredImage)
			session.SaveFilteredImageCommand.TryExecute(parameters);
		else
			session.SaveRenderedImageCommand.TryExecute(parameters);
	}


	/// <summary>
	/// <see cref="ICommand"/> to save image to file.
	/// </summary>
	public ICommand SaveImageCommand { get; }


	// Scroll given point of image scroll viewer to specific position of viewport.
	void ScrollImageScrollViewer(Vector contentPosition, Vector viewportPosition)
	{
		var viewportSize = this.imageScrollViewer.Viewport;
		var contentSize = this.imageScrollViewer.Extent;
		var offsetX = (contentSize.Width * contentPosition.X) - (viewportSize.Width * viewportPosition.X);
		var offsetY = (contentSize.Height * contentPosition.Y) - (viewportSize.Height * viewportPosition.Y);
		if (offsetX < 0)
			offsetX = 0;
		else if (offsetX + viewportSize.Width > contentSize.Width)
			offsetX = contentSize.Width - viewportSize.Width;
		if (offsetY < 0)
			offsetY = 0;
		else if (offsetY + viewportSize.Height > contentSize.Height)
			offsetY = contentSize.Height - viewportSize.Height;
		this.imageScrollViewer.Offset = new Vector(offsetX, offsetY);
	}
	
	
	/// <summary>
	/// Get formatted ARGB string of the selected pixel on the rendered image. Format depends on <see cref="SettingKeys.SelectedRenderedImagePixelArgbColorFormat"/>.
	/// </summary>
	public string SelectedImageDisplayPixelArgbString => this.GetValue(SelectedImageDisplayPixelArgbStringProperty);


	/// <summary>
	/// Bounds of selected pixel for displaying.
	/// </summary>
	public Rect SelectedImageDisplayPixelBounds => this.GetValue(SelectedImageDisplayPixelBoundsProperty);


	// Select pixel on rendered image.
	void SelectImageDisplayPixel(PointerEventArgs e)
	{
		if (this.DataContext is not Session session)
			return;
		var image = session.RenderedImage;
		if (image is not null)
		{
			var position = e.GetPosition(this.image);
			var imageBounds = this.image.Bounds;
			if (position.X >= 0 && position.X < imageBounds.Width && position.Y >= 0 && position.Y < imageBounds.Height)
			{
				var relativeX = (position.X / imageBounds.Width);
				var relativeY = (position.Y / imageBounds.Height);
				session.SelectRenderedImagePixel((int)(image.Size.Width * relativeX), (int)(image.Size.Height * relativeY));
			}
			else
				session.SelectRenderedImagePixel(-1, -1);
		}
		else
			session.SelectRenderedImagePixel(-1, -1);
		this.updateSelectedImageDisplayPixelBoundsAction.Execute();
	}


	// Set frame rate of playing frames to the given value.
	void SetFramePlaybackRate(int frameRate)
	{
		if (this.DataContext is not Session session)
			return;
		session.IsFramePlaybackRateUnlimited = false; // selecting a frame rate explicitly means playing with limited frame rate
		session.FramePlaybackRate = frameRate;
	}


	/// <summary>
	/// <see cref="ICommand"/> to set frame rate of playing frames. The parameter is <see cref="int"/>.
	/// </summary>
	public ICommand SetFramePlaybackRateCommand { get; }


	// Setup proper pivot for zooming image.
	void SetupTargetImageViewportPivot()
	{
		// check state
		if (this.DataContext is not Session session)
			return;

		// compute pivot from gesture or cursor while a zoom is in progress
		if (session.IsZooming || this.isZoomingByGesture)
		{
			var viewportSize = this.imageScrollViewer.Viewport;
			var viewportOffset = this.imageScrollViewer.Offset;
			var contentSize = this.imageScrollViewer.Extent;
			if (viewportSize.Width <= 0 || viewportSize.Height <= 0 || contentSize.Width <= 0 || contentSize.Height <= 0)
				return;
			Vector pivotInViewport;
			if (this.isZoomingByGesture && this.gesturePivotInViewport.HasValue)
				pivotInViewport = this.gesturePivotInViewport.Value;
			else
			{
				pivotInViewport = (this.latestPointerEventArgsOnImage is not null && !session.FitImageToViewport)
					? this.latestPointerEventArgsOnImage.GetCurrentPoint(this.imageScrollViewer).Position
					: new(viewportSize.Width / 2, viewportSize.Height / 2);
			}
			var pivot = new Vector(
				(pivotInViewport.X + viewportOffset.X) / contentSize.Width,
				(pivotInViewport.Y + viewportOffset.Y) / contentSize.Height);
			var anchor = new Vector(pivotInViewport.X / viewportSize.Width, pivotInViewport.Y / viewportSize.Height);
			this.targetImageViewportPivot = pivot;
			this.targetImageViewportPivotAnchor = anchor;
			this.Logger.LogTrace("Update viewport pivot, content: ({px:F3}, {py:F3}), anchor: ({ax:F3}, {ay:F3})", pivot.X, pivot.Y, anchor.X, anchor.Y);
		}
		// no active zoom — drop any stale pivot so non-zoom layout changes don't apply it
		else
		{
			this.targetImageViewportPivot = null;
			this.targetImageViewportPivotAnchor = null;
		}
	}


	/// <summary>
	/// Show menu of aligning to integer.
	/// </summary>
	public void ShowAlignToIntegerMenu(object? parameters)
	{
		if (parameters is not Control control)
			return;
		this.alignToIntegerMenu.PlacementTarget = control;
		this.alignToIntegerMenu.Open(control);
	}


	/// <summary>
	/// Show color space info.
	/// </summary>
	public void ShowColorSpaceInfo()
	{
		if (this.DataContext is not Session session || this.attachedWindow == null)
			return;
		var colorSpace = session.ColorSpace;
		_ = new ColorSpaceInfoDialog()
		{
			ColorSpace = colorSpace,
			IsReadOnly = !colorSpace.IsUserDefined,
		}.ShowDialog(this.attachedWindow);
	}


	/// <summary>
	/// Show color space management settings in application options.
	/// </summary>
	public void ShowColorSpaceManagementOptions()
	{
		if (this.attachedWindow != null)
			this.Application.ShowApplicationOptionsDialogAsync(this.attachedWindow, nameof(ApplicationOptionsDialogSection.ColorSpaceManagement));
	}


	/// <summary>
	/// <see cref="ICommand"/> to show menu of image dimensions evaluation.
	/// </summary>
	public ICommand ShowEvaluateImageDimensionsMenuCommand { get; }


	/// <summary>
	/// Show file actions.
	/// </summary>
	public void ShowFileActions()
	{
		this.fileActionsMenu.PlacementTarget ??= this.fileActionsButton;
		this.fileActionsMenu.Open(this.fileActionsButton);
	}


	/// <summary>
	/// Show other actions.
	/// </summary>
	public void ShowOtherActions()
	{
		this.otherActionsMenu.PlacementTarget ??= this.otherActionsButton;
		this.otherActionsMenu.Open(this.otherActionsButton);
	}


	/// <summary>
	/// Show process info on UI or not.
	/// </summary>
	public bool ShowProcessInfo => this.GetValue(ShowProcessInfoProperty);


	/// <summary>
	/// Show screen color space info.
	/// </summary>
	public void ShowScreenColorSpaceInfo()
	{
		if (this.DataContext is not Session session 
			|| session.Owner is not Workspace workspace
			|| this.attachedWindow == null)
		{
			return;
		}
		var colorSpace = workspace.EffectiveScreenColorSpace;
		_ = new ColorSpaceInfoDialog()
		{
			ColorSpace = colorSpace,
			IsReadOnly = !colorSpace.IsUserDefined,
		}.ShowDialog(this.attachedWindow);
	}


	/// <summary>
	/// Show file in file explorer.
	/// </summary>
	public void ShowSourceFileInFileExplorer()
    {
		if (!Platform.IsOpeningFileManagerSupported)
			return;
		if (this.DataContext is not Session session)
			return;
		var fileName = session.SourceFileName;
		if (!string.IsNullOrEmpty(fileName))
			Platform.OpenFileManager(fileName);
	}


	// Start using small rendered image.
	void StartUsingSmallRenderedImage()
	{
		if (this.DataContext is not Session session)
			return;
		if (!session.FitImageToViewport && session.ImageDisplayScale > 0.999)
			return;
		this.stopUsingSmallRenderedImageAction.Cancel();
		if (!this.useSmallRenderedImage)
		{
			this.Logger.LogTrace("Start using small rendered image");
			this.useSmallRenderedImage = true;
			this.updateEffectiveRenderedImageAction.Schedule();
			this.updateEffectiveRenderedImageIntModeAction.Schedule();
		}
	}


	/// <summary>
	/// Status bar state.
	/// </summary>
	public StatusBarState StatusBarState => this.GetValue(StatusBarStateProperty);


	// Update whether the progress bar of image processing can be shown or not.
	void UpdateCanShowProgressBar()
	{
		if (this.DataContext is Session session && session.IsProcessingImage)
		{
			// delay showing so that short processing, such as rendering each frame when playing frames, doesn't make the progress bar flash
			if (!this.GetValue(CanShowProgressBarProperty))
				this.showProgressBarAction.Schedule(ShowProgressBarDelay);
		}
		else
		{
			this.showProgressBarAction.Cancel();
			this.SetValue(CanShowProgressBarProperty, false);
		}
	}


	/// <summary>
	/// Zoom image to 100%.
	/// </summary>
	public void ZoomToOriginalImageSize()
	{
		if (this.DataContext is not Session session)
			return;
		session.FitImageToViewport = false;
		session.ZoomToCommand.TryExecute(1.0);
	}
}
