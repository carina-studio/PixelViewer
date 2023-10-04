using ASControls = CarinaStudio.AppSuite.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Controls;
using CarinaStudio.Data.Converters;
using CarinaStudio.Input;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform;

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
	/// <summary>
	/// <see cref="IValueConverter"/> which maps boolean to <see cref="ScrollBarVisibility.Auto"/>(True) and <see cref="ScrollBarVisibility.Disabled"/>(False).
	/// </summary>
	public static readonly IValueConverter BooleanToScrollBarVisibilityConverter = new BooleanToValueConverter<ScrollBarVisibility>(ScrollBarVisibility.Auto, ScrollBarVisibility.Disabled);


	// Constants.
	private const int AttachedScreenCheckingInterval = 500;
	const int BrightnessAdjustmentGroup = 1;
	const int ColorAdjustmentGroup = 2;
	const int ContrastAdjustmentGroup = 3;
	const int HidePanelsByImageViewerSizeDelay = 500;
	const int ResetPointerPressedOnFilterParamsUIDelay = 1000;
	const int StopUsingSmallRenderedImageDelay = 1000;


	// Static fields.
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
	static readonly StyledProperty<Rect> SelectedImageDisplayPixelBoundsProperty = AvaloniaProperty.Register<SessionControl, Rect>(nameof(SelectedImageDisplayPixelBounds));
	static readonly StyledProperty<bool> ShowProcessInfoProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(ShowProcessInfo));
	static readonly StyledProperty<bool> ShowSelectedRenderedImagePixelArgbColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelArgbColor));
	static readonly StyledProperty<bool> ShowSelectedRenderedImagePixelLabColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelLabColor));
	static readonly StyledProperty<bool> ShowSelectedRenderedImagePixelXyzColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelXyzColor));
	static readonly StyledProperty<StatusBarState> StatusBarStateProperty = AvaloniaProperty.Register<SessionControl, StatusBarState>(nameof(StatusBarState), StatusBarState.None);


	// Fields.
	readonly ContextMenu alignToIntegerMenu;
	private Screen? attachedScreen;
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
	private readonly ScheduledAction checkAttachedScreenAction;
	readonly ToggleButton colorAdjustmentButton;
	readonly Popup colorAdjustmentPopup;
	readonly Border colorAdjustmentPopupBorder;
	readonly ComboBox colorSpaceComboBox;
	readonly ToggleButton evaluateImageDimensionsButton;
	readonly ContextMenu evaluateImageDimensionsMenu;
	readonly ToggleButton fileActionsButton;
	readonly ContextMenu fileActionsMenu;
	readonly ScheduledAction hidePanelsByImageViewerSizeAction;
	readonly ToggleButton histogramsButton;
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
	bool keepHistogramsVisible;
	bool keepRenderingParamsPanelVisible;
	PointerEventArgs? latestPointerEventArgsOnImage;
	readonly double minImageViewerSizeToHidePanels;
	readonly ToggleButton otherActionsButton;
	readonly ContextMenu otherActionsMenu;
	readonly HashSet<Key> pressedKeys = new();
	readonly ColumnDefinition renderingParamsPanelColumn;
	readonly ScrollViewer renderingParamsPanelScrollViewer;
	readonly ScheduledAction resetPointerPressedOnBrightnessAdjustmentUIAction;
	readonly ScheduledAction resetPointerPressedOnColorAdjustmentUIAction;
	readonly ScheduledAction resetPointerPressedOnContrastAdjustmentUIAction;
	readonly ScheduledAction stopUsingSmallRenderedImageAction;
	Vector? targetImageViewportCenter;
	readonly ScheduledAction updateEffectiveRenderedImageAction;
	readonly ScheduledAction updateEffectiveRenderedImageIntModeAction;
	readonly ScheduledAction updateImageCursorAction;
	readonly ScheduledAction updateImageFilterParamsPopupOpacityAction;
	readonly ScheduledAction updateImageViewerShadowMarginAction;
	readonly ScheduledAction updateIsImageViewerScrollableAction;
	readonly ScheduledAction updateSelectedImageDisplayPixelBoundsAction;
	readonly ScheduledAction updateStatusBarStateAction;
	bool useSmallRenderedImage;


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
		this.ShowEvaluateImageDimensionsMenuCommand = new Command(() =>
		{
			if (this.evaluateImageDimensionsMenu == null)
				return;
			this.evaluateImageDimensionsMenu.PlacementTarget ??= this.evaluateImageDimensionsButton;
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
			it.GetObservable(BoundsProperty).Subscribe(new Observer<Rect>(_ => this.ReportImageViewportSize()));
			it.GetObservable(ScrollViewer.ExtentProperty).Subscribe(new Observer<Size>(_ =>
			{
				this.updateIsImageViewerScrollableAction?.Schedule();
				if (this.targetImageViewportCenter.HasValue)
				{
					var center = this.targetImageViewportCenter.Value;
					this.targetImageViewportCenter = null;
					this.ScrollImageScrollViewer(center, new Vector(0.5, 0.5));
				}
			}));
			it.GetObservable(ScrollViewer.ViewportProperty).Subscribe(new Observer<Size>(_ =>
			{
				this.updateIsImageViewerScrollableAction?.Schedule();
			}));
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
			foreach (var item in it.Items)
			{
				if (item is MenuItem menuItem && menuItem.Name == "editConfigMenuItem")
				{
					menuItem.IsVisible = true;
					break;
				}
			}
#endif
			it.Closed += (_, _) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = false);
			it.Opened += (_, _) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = true);
		});
		SetupFilterParamsSliderAndButtons("redColorAdjustment", ColorAdjustmentGroup);
		SetupFilterParamsSliderAndButtons("saturationAdjustment", ColorAdjustmentGroup);
		this.renderingParamsPanelColumn = this.Get<Grid>("workingAreaGrid").ColumnDefinitions.Last().Also(column =>
		{
			column.GetObservable(ColumnDefinition.WidthProperty).Subscribe(new Observer<GridLength>((_) =>
			{
				(this.DataContext as Session)?.Let(it => it.RenderingParametersPanelSize = column.Width.Value);
			}));
		});
		this.renderingParamsPanelScrollViewer = this.Get<ScrollViewer>(nameof(renderingParamsPanelScrollViewer));
		SetupFilterParamsSliderAndButtons("shadowAdjustment", BrightnessAdjustmentGroup);
#if DEBUG
		this.Get<Button>("testButton").IsVisible = true;
#endif
		SetupFilterParamsSliderAndButtons("vibranceAdjustment", ColorAdjustmentGroup);

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
		this.stopUsingSmallRenderedImageAction = new(() =>
		{
			if (this.useSmallRenderedImage)
			{
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
				cursorType = StandardCursorType.None;
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
		this.updateSelectedImageDisplayPixelBoundsAction = new(() =>
		{
			if (this.DataContext is not Session session || !this.GetValue(IsPointerOverImageProperty))
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
				if (session.IsSourceFileOpened)
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
	/// Copy file name.
	/// </summary>
	public void CopyFileName()
	{
		if (this.DataContext is not Session session || !session.IsSourceFileOpened)
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
		if (this.DataContext is not Session session || !session.IsSourceFileOpened)
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
#pragma warning disable IDE0060
	public async Task<bool> DropDataAsync(IDataObject data, KeyModifiers keyModifiers)
#pragma warning restore IDE0060
	{
		// get file names
		var fileNames = data.GetFiles()?.Let(it =>
		{
			var fileNames = new List<string>();
			foreach (var file in it)
			{
				var fileName = file.TryGetLocalPath();
				if (!string.IsNullOrEmpty(fileName))
					fileNames.Add(fileName);
			}
			return fileNames;
		});
		if (fileNames.IsNullOrEmpty())
			return false;

		// get window
		if (this.attachedWindow == null)
			return false;

		// check file count
		if (fileNames.Count > 8)
		{
			await new ASControls.MessageDialog()
			{
				Icon = ASControls.MessageDialogIcon.Warning,
				Message = this.GetResourceObservable("String/SessionControl.MaxDragDropFileCountReached"),
			}.ShowDialog(this.attachedWindow);
			return false;
		}

		// open files
		if (fileNames.Count > 1)
		{
			// select profile
			var profile = await new ImageRenderingProfileSelectionDialog().Also(it =>
			{
				it.Bind(ImageRenderingProfileSelectionDialog.MessageProperty, this.GetResourceObservable("String/SessionControl.SelectProfileToOpenFiles"));
			}).ShowDialog<ImageRenderingProfile?>(this.attachedWindow);
			if (profile == null)
				return false;

			// get workspace
			if (this.DataContext is not Session session || session.Owner is not Workspace workspace)
				return false;

			// create sessions
			var index = workspace.Sessions.IndexOf(session);
			if (index >= 0)
				++index;
			else
				index = workspace.Sessions.Count;
			foreach (var fileName in fileNames)
			{
				if (session.SourceFileName != null)
					workspace.CreateAndAttachSession(index++, fileName, profile);
				else
				{
					session.OpenSourceFileCommand.TryExecute(fileName);
					session.Profile = profile;
				}
			}
		}
		else if (this.Settings.GetValueOrDefault(SettingKeys.CreateNewSessionForDragDropFile)
				&& this.DataContext is Session session
				&& session.SourceFileName != null
				&& session.Owner is Workspace workspace)
		{
			workspace.CreateAndAttachSession(fileNames[0]);
		}
		else
			this.OpenSourceFile(fileNames[0]);
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
		var image = ((IAvaloniaApplication)App.Current).FindResourceOrDefault<IImage?>(resourceKey) ?? throw new ArgumentException();
		var imageSize = image.Size;
		var maxSide = ((IAvaloniaApplication)App.Current).FindResourceOrDefault("Double/Cursor.MaxSide", 30.0);
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
	public async void MoveToSpecificFrame()
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
		if (this.alignToIntegerMenu.PlacementTarget is not Control control)
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
		var imageRendererTemplate = this.imageRendererComboBox.ItemTemplate;
		this.imageRendererComboBox.ItemTemplate = null;
		this.imageRendererComboBox.ItemTemplate = imageRendererTemplate;
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
		this.canShowEvaluateImageDimensionsMenu.Update(session.IsSourceFileOpened);

		// setup histograms panel
		Grid.SetColumnSpan(this.imageViewerGrid, session.IsRenderingParametersPanelVisible ? 1 : 3);
		this.renderingParamsPanelColumn.Width = new GridLength(session.RenderingParametersPanelSize, GridUnitType.Pixel);

		// update rendered image
		this.updateEffectiveRenderedImageAction.Schedule();
		this.updateEffectiveRenderedImageIntModeAction.Schedule();
		
		// update state
		this.ReportImageViewportSize();
		this.ReportScreenPixelDensity();
		this.updateImageViewerShadowMarginAction.Schedule();
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
    void OnColorSpaceCustomNameChanged(object? sender, Media.ColorSpaceEventArgs e)
    {
        // [Workaround] Force refreshing content of ComboBox
        var template = this.colorSpaceComboBox.ItemTemplate;
        this.colorSpaceComboBox.ItemTemplate = null;
        this.colorSpaceComboBox.ItemTemplate = template;
    }


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
		if (e.Data.HasFileNames())
		{
			e.DragEffects = DragDropEffects.Copy;
			e.Handled = true;
		}
		else
			e.DragEffects = DragDropEffects.None;
	}


	// Called when drop.
	void OnDrop(object? sender, DragEventArgs e)
	{
		_ = this.DropDataAsync(e.Data, e.KeyModifiers);
		e.Handled = true;
	}


	// Called when double tap on image.
	void OnImageDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (this.DataContext is not Session session)
			return;
		if (session.FitImageToViewport)
			session.FitImageToViewport = false;
		else if (session.ZoomInCommand.CanExecute(null))
			session.ZoomInCommand.TryExecute();
		else if (session.ZoomToCommand.CanExecute(1.0))
			session.ZoomToCommand.TryExecute(1.0);
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
		if (sender is Visual imageControl && this.DataContext is Session session)
		{
			var image = session.RenderedImage;
			if (image != null)
			{
				var position = e.GetPosition(imageControl);
				var imageBounds = imageControl.Bounds;
				var relativeX = (position.X / imageBounds.Width);
				var relativeY = (position.Y / imageBounds.Height);
				session.SelectRenderedImagePixel((int)(image.Size.Width * relativeX), (int)(image.Size.Height * relativeY));
			}
			else
				session.SelectRenderedImagePixel(-1, -1);
			this.updateSelectedImageDisplayPixelBoundsAction.Execute();
		}
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
					notification.BindToResource(ASControls.Notification.IconProperty, this, "Image/Icon.Success.Colored");
					notification.Bind(ASControls.Notification.MessageProperty, new FormattedString().Also(it =>
					{
						it.BindToResource(FormattedString.FormatProperty, this, "String/SessionControl.ImageSavingSucceeded");
						it.Arg1 = e.FileName;
					}));
				}
				else
				{
					notification.BindToResource(ASControls.Notification.IconProperty, this, "Image/Icon.Error.Colored");
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
					this.OpenSourceFile();
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
					this.SaveImage();
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
		if (!this.imageScrollViewer.IsPointerOver || (e.KeyModifiers & KeyModifiers.Control) == 0)
			return;
		if (this.DataContext is not Session session || !session.IsSourceFileOpened || session.FitImageToViewport)
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
											_ = this.Application.ShowApplicationOptionsDialogAsync(this.attachedWindow, ApplicationOptionsDialogSection.MaxRenderedImagesMemoryUsage.ToString());
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
			case nameof(Session.ImageDisplayScale):
                if (!session.FitImageToViewport)
				{
					var viewportSize = this.imageScrollViewer.Viewport;
					var viewportOffset = this.imageScrollViewer.Offset;
					var contentSize = this.imageScrollViewer.Extent;
					var centerX = (viewportOffset.X + viewportSize.Width / 2) / contentSize.Width;
					var centerY = (viewportOffset.Y + viewportSize.Height / 2) / contentSize.Height;
					this.targetImageViewportCenter = new Vector(centerX, centerY);
				}
                this.updateSelectedImageDisplayPixelBoundsAction.Execute();
				break;
			case nameof(Session.ImageDisplaySize):
				this.updateEffectiveRenderedImageIntModeAction.Schedule();
				break;
			case nameof(Session.IsHistogramsVisible):
				if (session.IsHistogramsVisible)
					this.keepHistogramsVisible = true;
				this.updateImageViewerShadowMarginAction.Schedule();
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
					Grid.SetColumnSpan(this.imageViewerGrid, 1);
					this.keepRenderingParamsPanelVisible = true;
				}
				else
					Grid.SetColumnSpan(this.imageViewerGrid, 3);
				this.updateImageViewerShadowMarginAction.Schedule();
				break;
			case nameof(Session.IsSourceFileOpened):
				this.canShowEvaluateImageDimensionsMenu.Update(session.IsSourceFileOpened);
				this.updateStatusBarStateAction.Schedule();
				break;
			case nameof(Session.IsZooming):
				if (session.IsZooming)
					this.StartUsingSmallRenderedImage();
				else if (!this.stopUsingSmallRenderedImageAction.IsScheduled)
					this.stopUsingSmallRenderedImageAction.Execute();
				break;
			case nameof(Session.QuarterSizeRenderedImage):
			case nameof(Session.RenderedImage):
				this.updateEffectiveRenderedImageAction.Execute();
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
	public async void OnTestButtonClick()
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
		else if (property == HeightProperty 
		         || property == WidthProperty)
		{
			this.StartUsingSmallRenderedImage();
			this.stopUsingSmallRenderedImageAction.Reschedule(StopUsingSmallRenderedImageDelay);
		}
		else if (property == CarinaStudio.AppSuite.Controls.Window.IsOpenedProperty)
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


	// Open source file.
	async void OpenSourceFile()
	{
		// find window
		if (this.attachedWindow == null)
		{
			Logger.LogError("No window to show open file dialog");
			return;
		}

		// select file
		var fileName = (await this.attachedWindow.StorageProvider.OpenFilePickerAsync(new())).Let(it => 
			it.Count == 1 ? it[0].TryGetLocalPath() : null);
		if (fileName == null)
			return;

		// open file
		this.OpenSourceFile(fileName);
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
	async void SaveAsNewProfile()
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
	async void SaveImage()
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
			FileTypeChoices = new FilePickerFileType[]
			{
				new(app.GetStringNonNull("FileType.Jpeg"))
				{
					Patterns = new[] { "*.jpg", "*.jpeg", "*.jpe", "*.jfif" },
				},
				new(app.GetStringNonNull("FileType.Png"))
				{
					Patterns = new[] { "*.png" },
				},
				new(app.GetStringNonNull("FileType.RawBgra"))
				{
					Patterns = new[] { "*.bgra" },
				}
			},
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
	/// Bounds of selected pixel for displaying.
	/// </summary>
	public Rect SelectedImageDisplayPixelBounds => this.GetValue(SelectedImageDisplayPixelBoundsProperty);


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
			this.Application.ShowApplicationOptionsDialogAsync(this.attachedWindow, ApplicationOptionsDialogSection.ColorSpaceManagement.ToString());
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
			this.useSmallRenderedImage = true;
			this.updateEffectiveRenderedImageAction.Schedule();
			this.updateEffectiveRenderedImageIntModeAction.Schedule();
		}
	}


	/// <summary>
	/// Status bar state.
	/// </summary>
	public StatusBarState StatusBarState => this.GetValue(StatusBarStateProperty);


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
