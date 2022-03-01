using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Visuals.Media.Imaging;
using Avalonia.VisualTree;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Controls;
using CarinaStudio.Data.Converters;
using CarinaStudio.Input;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// <see cref="Control"/>(View) of <see cref="Session"/>.
	/// </summary>
	class SessionControl : UserControl<IAppSuiteApplication>
	{
		/// <summary>
		/// <see cref="IValueConverter"/> which maps boolean to <see cref="ScrollBarVisibility.Auto"/>(True) and <see cref="ScrollBarVisibility.Disabled"/>(False).
		/// </summary>
		public static readonly IValueConverter BooleanToScrollBarVisibilityConverter = new BooleanToValueConverter<ScrollBarVisibility>(ScrollBarVisibility.Auto, ScrollBarVisibility.Disabled);


		// Constants.
		const int HidePanelsByImageViewerSizeDelay = 500;
		const int StopUsingSmallRenderedImageDelay = 1000;


		// Static fields.
		static readonly AvaloniaProperty<IImage?> EffectiveRenderedImageProperty = AvaloniaProperty.Register<SessionControl, IImage?>(nameof(EffectiveRenderedImage));
		static readonly AvaloniaProperty<BitmapInterpolationMode> EffectiveRenderedImageInterpolationModeProperty = AvaloniaProperty.Register<SessionControl, BitmapInterpolationMode>(nameof(EffectiveRenderedImageInterpolationMode), BitmapInterpolationMode.Default);
		static readonly AvaloniaProperty<bool> IsImageViewerScrollableProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(IsImageViewerScrollable));
		static readonly AvaloniaProperty<bool> IsPointerOverImageProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerOverImage");
		static readonly AvaloniaProperty<bool> IsPointerPressedOnBrightnessAdjustmentUIProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerPressedOnBrightnessAdjustmentUI");
		static readonly AvaloniaProperty<bool> IsPointerPressedOnColorAdjustmentUIProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerPressedOnColorAdjustmentUI");
		static readonly AvaloniaProperty<bool> IsPointerPressedOnContrastAdjustmentUIProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerPressedOnContrastAdjustmentUI");
		static readonly AvaloniaProperty<bool> IsPointerPressedOnImageProperty = AvaloniaProperty.Register<SessionControl, bool>("IsPointerPressedOnImage");
		static readonly AvaloniaProperty<Point> PointerPositionOnImageControlProperty = AvaloniaProperty.Register<SessionControl, Point>("PointerPositionOnImageControl");
		static readonly AvaloniaProperty<bool> ShowProcessInfoProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(ShowProcessInfo));
		static readonly AvaloniaProperty<bool> ShowSelectedRenderedImagePixelArgbColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelArgbColor));
		static readonly AvaloniaProperty<bool> ShowSelectedRenderedImagePixelLabColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelLabColor));
		static readonly AvaloniaProperty<bool> ShowSelectedRenderedImagePixelXyzColorProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(SettingKeys.ShowSelectedRenderedImagePixelXyzColor));
		static readonly AvaloniaProperty<StatusBarState> StatusBarStateProperty = AvaloniaProperty.Register<SessionControl, StatusBarState>(nameof(StatusBarState), StatusBarState.None);


		// Fields.
		Avalonia.Controls.Window? attachedWindow;
		readonly ToggleButton brightnessAndContrastAdjustmentButton;
		readonly Popup brightnessAndContrastAdjustmentPopup;
		readonly Border brightnessAndContrastAdjustmentPopupBorder;
		readonly MutableObservableValue<bool> canOpenSourceFile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canResetBrightnessAndContrastAdjustment = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveAsNewProfile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveImage = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canShowEvaluateImageDimensionsMenu = new MutableObservableValue<bool>();
		readonly ToggleButton colorAdjustmentButton;
		readonly Popup colorAdjustmentPopup;
		readonly Border colorAdjustmentPopupBorder;
		readonly ToggleButton evaluateImageDimensionsButton;
		readonly ContextMenu evaluateImageDimensionsMenu;
		readonly ToggleButton fileActionsButton;
		readonly ContextMenu fileActionsMenu;
		readonly ScheduledAction hidePanelsByImageViewerSizeAction;
		readonly ToggleButton histogramsButton;
		readonly Image image;
		readonly Border imageContainerBorder;
		StandardCursorType imageCursorType = StandardCursorType.Arrow;
		Vector? imagePointerPressedContentPosition;
		readonly ComboBox imageRendererComboBox;
		readonly ScrollViewer imageScrollViewer;
		readonly Thickness imageScrollViewerPadding;
		readonly Control imageViewerGrid;
		bool isFirstImageViewerBoundsChanged = true;
		bool keepHistogramsVisible;
		bool keepRenderingParamsPanelVisible;
		PointerEventArgs? latestPointerEventArgsOnImage;
		readonly double minImageViewerSizeToHidePanels;
		readonly ToggleButton otherActionsButton;
		readonly ContextMenu otherActionsMenu;
		readonly HashSet<Avalonia.Input.Key> pressedKeys = new();
		readonly ColumnDefinition renderingParamsPanelColumn;
		readonly ScrollViewer renderingParamsPanelScrollViewer;
		readonly ScheduledAction stopUsingSmallRenderedImageAction;
		Vector? targetImageViewportCenter;
		readonly ScheduledAction updateEffectiveRenderedImageAction;
		readonly ScheduledAction updateEffectiveRenderedImageIntModeAction;
		readonly ScheduledAction updateImageCursorAction;
		readonly ScheduledAction updateImageFilterParamsPopupOpacityAction;
		readonly ScheduledAction updateIsImageViewerScrollableAction;
		readonly ScheduledAction updateStatusBarStateAction;
		bool useSmallRenderedImage;


		/// <summary>
		/// Initialize new <see cref="SessionControl"/> instance.
		/// </summary>
		public SessionControl()
		{
			// create commands
			this.OpenSourceFileCommand = new Command(this.OpenSourceFile, this.canOpenSourceFile);
			this.ResetBrightnessAndContrastAdjustmentCommand = new Command(this.ResetBrightnessAndContrastAdjustment, this.canResetBrightnessAndContrastAdjustment);
			this.SaveAsNewProfileCommand = new Command(() => this.SaveAsNewProfile(), this.canSaveAsNewProfile);
			this.SaveImageCommand = new Command(() => this.SaveImage(), this.canSaveImage);
			this.ShowEvaluateImageDimensionsMenuCommand = new Command(() =>
			{
				if (this.evaluateImageDimensionsMenu == null)
					return;
				if (this.evaluateImageDimensionsMenu.PlacementTarget == null)
					this.evaluateImageDimensionsMenu.PlacementTarget = this.evaluateImageDimensionsButton;
				this.evaluateImageDimensionsMenu.Open(this.evaluateImageDimensionsButton);
			}, this.canShowEvaluateImageDimensionsMenu);
			this.canOpenSourceFile.Update(true);

			// load layout
			AvaloniaXamlLoader.Load(this);

			// [Workaround] setup initial command state after loading XAML
			this.canOpenSourceFile.Update(false);

			// setup controls
			this.FindControl<Slider>("blueColorAdjustmentSlider").AsNonNull().Also(it =>
			{
				it.AddHandler(PointerPressedEvent, this.OnPointerPressedOnColorAdjustmentUI, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, this.OnPointerReleasedOnColorAdjustmentUI, RoutingStrategies.Tunnel);
			});
			this.FindControl<Slider>("brightnessAdjustmentSlider").AsNonNull().Also(it =>
			{
				it.AddHandler(PointerPressedEvent, this.OnPointerPressedOnBrightnessAdjustmentUI, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, this.OnPointerReleasedOnBrightnessAdjustmentUI, RoutingStrategies.Tunnel);
			});
			this.brightnessAndContrastAdjustmentButton = this.FindControl<ToggleButton>(nameof(brightnessAndContrastAdjustmentButton)).AsNonNull();
			this.brightnessAndContrastAdjustmentPopup = this.FindControl<Popup>(nameof(brightnessAndContrastAdjustmentPopup)).AsNonNull().Also(it =>
			{
				it.PlacementTarget = this.brightnessAndContrastAdjustmentButton;
				it.Closed += (_, e) => this.SynchronizationContext.Post(() => this.brightnessAndContrastAdjustmentButton.IsChecked = false);
				it.Opened += (_, e) => this.SynchronizationContext.Post(() => this.brightnessAndContrastAdjustmentButton.IsChecked = true);
			});
			this.brightnessAndContrastAdjustmentPopupBorder = this.FindControl<Border>(nameof(brightnessAndContrastAdjustmentPopupBorder)).AsNonNull();
			this.colorAdjustmentButton = this.FindControl<ToggleButton>(nameof(colorAdjustmentButton)).AsNonNull();
			this.colorAdjustmentPopup = this.FindControl<Popup>(nameof(colorAdjustmentPopup)).AsNonNull().Also(it =>
			{
				it.PlacementTarget = this.colorAdjustmentButton;
				it.Closed += (_, e) => this.SynchronizationContext.Post(() => this.colorAdjustmentButton.IsChecked = false);
				it.Opened += (_, e) => this.SynchronizationContext.Post(() => this.colorAdjustmentButton.IsChecked = true);
			});
			this.colorAdjustmentPopupBorder = this.FindControl<Border>(nameof(colorAdjustmentPopupBorder)).AsNonNull();
			this.FindControl<Slider>("contrastAdjustmentSlider").AsNonNull().Also(it =>
			{
				it.AddHandler(PointerPressedEvent, this.OnPointerPressedOnContrastAdjustmentUI, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, this.OnPointerReleasedOnContrastAdjustmentUI, RoutingStrategies.Tunnel);
			});
			this.evaluateImageDimensionsButton = this.FindControl<ToggleButton>(nameof(this.evaluateImageDimensionsButton)).AsNonNull();
			this.evaluateImageDimensionsMenu = ((ContextMenu)this.Resources[nameof(evaluateImageDimensionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => this.SynchronizationContext.Post(() => this.evaluateImageDimensionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => this.SynchronizationContext.Post(() => this.evaluateImageDimensionsButton.IsChecked = true);
			});
			this.fileActionsButton = this.FindControl<ToggleButton>(nameof(this.fileActionsButton)).AsNonNull();
			this.fileActionsMenu = ((ContextMenu)this.Resources[nameof(fileActionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => this.SynchronizationContext.Post(() => this.fileActionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => this.SynchronizationContext.Post(() => this.fileActionsButton.IsChecked = true);
			});
			this.FindControl<Slider>("greenColorAdjustmentSlider").AsNonNull().Also(it =>
			{
				it.AddHandler(PointerPressedEvent, this.OnPointerPressedOnColorAdjustmentUI, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, this.OnPointerReleasedOnColorAdjustmentUI, RoutingStrategies.Tunnel);
			});
			this.FindControl<Slider>("highlightAdjustmentSlider").AsNonNull().Also(it =>
			{
				it.AddHandler(PointerPressedEvent, this.OnPointerPressedOnBrightnessAdjustmentUI, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, this.OnPointerReleasedOnBrightnessAdjustmentUI, RoutingStrategies.Tunnel);
			});
			this.histogramsButton = this.FindControl<ToggleButton>(nameof(histogramsButton)).AsNonNull();
			this.image = this.FindControl<Image>(nameof(image)).AsNonNull();
			this.imageContainerBorder = this.FindControl<Border>(nameof(imageContainerBorder)).AsNonNull().Also(it =>
			{
				it.GetObservable(BoundsProperty).Subscribe(_ =>
				{
					if (this.GetValue<bool>(IsPointerOverImageProperty) && this.latestPointerEventArgsOnImage != null)
						this.SetValue<Point>(PointerPositionOnImageControlProperty, this.latestPointerEventArgsOnImage.GetCurrentPoint(it).Position);
				});
			});
			this.imageRendererComboBox = this.FindControl<ComboBox>(nameof(imageRendererComboBox)).AsNonNull();
			this.imageScrollViewer = this.FindControl<ScrollViewer>(nameof(this.imageScrollViewer)).AsNonNull().Also(it =>
			{
				it.GetObservable(BoundsProperty).Subscribe(_ => this.ReportImageViewportSize());
				it.GetObservable(ScrollViewer.ExtentProperty).Subscribe(_ =>
				{
					this.updateIsImageViewerScrollableAction?.Schedule();
					if (this.targetImageViewportCenter.HasValue)
					{
						var center = this.targetImageViewportCenter.Value;
						this.targetImageViewportCenter = null;
						this.ScrollImageScrollViewer(center, new Vector(0.5, 0.5));
					}
				});
				it.GetObservable(ScrollViewer.ViewportProperty).Subscribe(_ =>
				{
					this.updateIsImageViewerScrollableAction?.Schedule();
				});
			});
			this.imageViewerGrid = this.FindControl<Control>(nameof(imageViewerGrid)).AsNonNull().Also(it =>
			{
				it.GetObservable(BoundsProperty).Subscribe(new Observer<Rect>((_) =>
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
			this.otherActionsButton = this.FindControl<ToggleButton>(nameof(otherActionsButton)).AsNonNull().Also(it =>
			{
				if (CarinaStudio.Platform.IsMacOS)
					it.IsVisible = false;
			});
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
				it.MenuClosed += (_, e) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = true);
			});
			this.FindControl<Slider>("redColorAdjustmentSlider").AsNonNull().Also(it =>
			{
				it.AddHandler(PointerPressedEvent, this.OnPointerPressedOnColorAdjustmentUI, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, this.OnPointerReleasedOnColorAdjustmentUI, RoutingStrategies.Tunnel);
			});
			this.renderingParamsPanelColumn = this.FindControl<Grid>("workingAreaGrid").AsNonNull().ColumnDefinitions.Last().Also(column =>
			{
				column.GetObservable(ColumnDefinition.WidthProperty).Subscribe(new Observer<GridLength>((_) =>
				{
					(this.DataContext as Session)?.Let(it => it.RenderingParametersPanelSize = column.Width.Value);
				}));
			});
			this.renderingParamsPanelScrollViewer = this.FindControl<ScrollViewer>(nameof(renderingParamsPanelScrollViewer)).AsNonNull();
			this.FindControl<Slider>("shadowAdjustmentSlider").AsNonNull().Also(it =>
			{
				it.AddHandler(PointerPressedEvent, this.OnPointerPressedOnBrightnessAdjustmentUI, RoutingStrategies.Tunnel);
				it.AddHandler(PointerReleasedEvent, this.OnPointerReleasedOnBrightnessAdjustmentUI, RoutingStrategies.Tunnel);
			});
#if DEBUG
			this.FindControl<Button>("testButton").AsNonNull().IsVisible = true;
#endif

			// load resources
			if (this.Application.TryGetResource<double>("Double/SessionControl.ImageViewer.MinSizeToHidePanels", out var doubleRes))
				this.minImageViewerSizeToHidePanels = doubleRes.GetValueOrDefault();
			if (this.Application.TryGetResource<Thickness>("Thickness/SessionControl.ImageViewer.Padding", out var thicknessRes))
				this.imageScrollViewerPadding = thicknessRes.GetValueOrDefault();

			// create scheduled actions
			this.hidePanelsByImageViewerSizeAction = new ScheduledAction(() =>
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
			this.stopUsingSmallRenderedImageAction = new ScheduledAction(() =>
			{
				if (this.useSmallRenderedImage)
				{
					this.useSmallRenderedImage = false;
					this.updateEffectiveRenderedImageAction?.Schedule();
					this.updateEffectiveRenderedImageIntModeAction?.Schedule();
				}
			});
			this.updateEffectiveRenderedImageAction = new ScheduledAction(() =>
			{
				if (this.DataContext is not Session session)
					this.SetValue<IImage?>(EffectiveRenderedImageProperty, null);
				else if (this.useSmallRenderedImage && session.HasQuarterSizeRenderedImage)
					this.SetValue<IImage?>(EffectiveRenderedImageProperty, session.QuarterSizeRenderedImage);
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
							this.SetValue<IImage?>(EffectiveRenderedImageProperty, session.QuarterSizeRenderedImage);
						}
						else
							this.SetValue<IImage?>(EffectiveRenderedImageProperty, session.RenderedImage);
					}
				}
			});
			this.updateEffectiveRenderedImageIntModeAction = new ScheduledAction(() =>
			{
				if (this.DataContext is not Session session)
					return;
				if (useSmallRenderedImage)
					this.SetValue<BitmapInterpolationMode>(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.LowQuality);
				else
				{
					var image = this.GetValue<IImage?>(EffectiveRenderedImageProperty);
					if (image != null)
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
					if (image != null)
					{
						var displaySize = session.ImageDisplaySize;
						if (image.Size.Width >= displaySize.Width || image.Size.Height >= displaySize.Height)
							this.SetValue<BitmapInterpolationMode>(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.HighQuality);
						else
							this.SetValue<BitmapInterpolationMode>(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.LowQuality);
					}
				}
			});
			this.updateImageCursorAction = new ScheduledAction(() =>
			{
				var cursorType = StandardCursorType.Arrow;
				if (this.GetValue<bool>(IsPointerOverImageProperty)
					&& this.GetValue<bool>(IsPointerPressedOnImageProperty) 
					&& this.IsImageViewerScrollable)
				{
					cursorType = StandardCursorType.SizeAll;
				}
				if (this.imageCursorType != cursorType)
                {
					this.imageCursorType = cursorType;
					this.image.Cursor = new Avalonia.Input.Cursor(cursorType);
                }
			});
			this.updateImageFilterParamsPopupOpacityAction = new ScheduledAction(() =>
			{
				this.brightnessAndContrastAdjustmentPopupBorder.Opacity = (this.GetValue<bool>(IsPointerPressedOnBrightnessAdjustmentUIProperty) || this.GetValue<bool>(IsPointerPressedOnContrastAdjustmentUIProperty)) ? 0.5 : 1;
				this.colorAdjustmentPopupBorder.Opacity = this.GetValue<bool>(IsPointerPressedOnColorAdjustmentUIProperty) ? 0.5 : 1;
			});
			this.updateIsImageViewerScrollableAction = new ScheduledAction(() =>
			{
				var contentSize = this.imageScrollViewer.Extent;
				var viewport = this.imageScrollViewer.Viewport;
				this.SetValue<bool>(IsImageViewerScrollableProperty, contentSize.Width > viewport.Width || contentSize.Height > viewport.Height);
			});
			this.updateStatusBarStateAction = new ScheduledAction(() =>
			{
				this.SetValue<StatusBarState>(StatusBarStateProperty, Global.Run(() =>
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
		}


		// Copy file name.
		void CopyFileName()
		{
			if (this.DataContext is not Session session || !session.IsSourceFileOpened)
				return;
			session.SourceFileName?.Let(it =>
			{
				_ = ((App)this.Application)?.Clipboard?.SetTextAsync(Path.GetFileName(it));
			});
		}


		// Copy file path.
		void CopyFilePath()
		{
			if (this.DataContext is not Session session || !session.IsSourceFileOpened)
				return;
			session.SourceFileName?.Let(it =>
			{
				_ = ((App)this.Application)?.Clipboard?.SetTextAsync(it);
			});
		}


		/// <summary>
		/// Drop data to this control.
		/// </summary>
		/// <param name="data">Dropped data.</param>
		/// <param name="keyModifiers">Key modifiers.</param>
		/// <returns>True if data has been accepted.</returns>
		public async Task<bool> DropDataAsync(IDataObject data, KeyModifiers keyModifiers)
		{
			// get file names
			var fileNames = data.GetFileNames()?.ToArray();
			if (fileNames == null || fileNames.IsEmpty())
				return false;

			// get window
			if (this.attachedWindow == null)
				return false;

			// check file count
			if (fileNames.Length > 8)
			{
				await new MessageDialog()
				{
					Icon = MessageDialogIcon.Warning,
					Message = this.Application.GetString("SessionControl.MaxDragDropFileCountReached"),
				}.ShowDialog(this.attachedWindow);
				return false;
			}

			// open files
			if (fileNames.Length > 1)
			{
				// select profile
				var profile = await new ImageRenderingProfileSelectionDialog()
				{
					Message = this.Application.GetString("SessionControl.SelectProfileToOpenFiles"),
				}.ShowDialog<ImageRenderingProfile?>(this.attachedWindow);
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
		IImage? EffectiveRenderedImage { get => this.GetValue<IImage?>(EffectiveRenderedImageProperty); }


		// Interpolation mode for rendered image.
		BitmapInterpolationMode EffectiveRenderedImageInterpolationMode { get => this.GetValue<BitmapInterpolationMode>(EffectiveRenderedImageInterpolationModeProperty); }


		// Check whether image viewer is scrollable in current state or not.
		bool IsImageViewerScrollable { get => this.GetValue<bool>(IsImageViewerScrollableProperty); }


		// OS type.
		bool IsNotMacOS { get; } = !CarinaStudio.Platform.IsMacOS;


		// Move to specific frame.
		async void MoveToSpecificFrame()
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


		// Application string resources updated.
		void OnApplicationStringsUpdated(object? sender, EventArgs e)
		{
			var imageRendererTemplate = this.imageRendererComboBox.ItemTemplate;
			this.imageRendererComboBox.ItemTemplate = null;
			this.imageRendererComboBox.ItemTemplate = imageRendererTemplate;
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
			this.AddHandler(PointerWheelChangedEvent, this.OnPointerWheelChanged, Avalonia.Interactivity.RoutingStrategies.Tunnel);

			// attach to settings
			var settings = this.Settings;
			settings.SettingChanged += this.OnSettingChanged;
			this.SetValue<bool>(ShowProcessInfoProperty, settings.GetValueOrDefault(SettingKeys.ShowProcessInfo));
			this.SetValue<bool>(ShowSelectedRenderedImagePixelArgbColorProperty, settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelArgbColor));
			this.SetValue<bool>(ShowSelectedRenderedImagePixelLabColorProperty, settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelLabColor));
			this.SetValue<bool>(ShowSelectedRenderedImagePixelXyzColorProperty, settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelXyzColor));

			// attach to window
			this.attachedWindow = this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Also(it =>
			{
				it.PropertyChanged += this.OnWindowPropertyChanged;
			});
		}


		// Called when attached to visual tree.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
			// call base
            base.OnAttachedToVisualTree(e);

			// update state
			this.isFirstImageViewerBoundsChanged = true;

			// [Workaround] Force refreshing status bar state to make background applied as expected
			this.SetValue<StatusBarState>(StatusBarStateProperty, StatusBarState.None);
			this.updateStatusBarStateAction.Reschedule();

			// report pixel density
			this.ReportScreenPixelDensity();
		}


        // Called when detached from logical tree.
        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
		{
			// disable drag-drop
			this.RemoveHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.RemoveHandler(DragDrop.DropEvent, this.OnDrop);

			// remove event handlers
			this.Application.StringsUpdated -= this.OnApplicationStringsUpdated;
			this.RemoveHandler(PointerWheelChangedEvent, this.OnPointerWheelChanged);

			// detach from settings
			this.Settings.SettingChanged -= this.OnSettingChanged;

			// detach from window
			this.attachedWindow = this.attachedWindow?.Let(it =>
			{
				it.PropertyChanged -= this.OnWindowPropertyChanged;
				return (Avalonia.Controls.Window?)null;
			});

			// call base
			base.OnDetachedFromLogicalTree(e);
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


        // Called when key down.
        protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
        {
			// call base
			base.OnKeyDown(e);
			if (e.Handled)
				return;

			// check focus
			var focusedElement = Avalonia.Input.FocusManager.Instance?.Current;
			if (focusedElement != null)
			{
				if (focusedElement is TextBox || focusedElement is NumericUpDown)
					return;
				if (focusedElement.FindAncestorOfType<SessionControl>(true) != this)
					return;
			}

			// get session
			if (this.DataContext is not Session session)
				return;

			// handle key event
			this.pressedKeys.Add(e.Key);
			var isCtrlPressed = CarinaStudio.Platform.IsMacOS 
				? this.pressedKeys.Contains(Avalonia.Input.Key.LWin) || this.pressedKeys.Contains(Avalonia.Input.Key.RWin) 
				: (e.KeyModifiers & KeyModifiers.Control) != 0;
			if (isCtrlPressed)
			{
				switch (e.Key)
				{
					case Avalonia.Input.Key.D0:
						{
							session.FitImageToViewport = true;
							break;
						}
					case Avalonia.Input.Key.D1:
						{
							if (session.FitImageToViewport)
							{
								session.RequestedImageDisplayScale = 1.0;
								session.FitImageToViewport = false;
							}
							else
								session.ZoomToCommand.TryExecute(1.0);
							break;
						}
					case Avalonia.Input.Key.N:
						if (CarinaStudio.Platform.IsMacOS)
							return;
						this.FindAncestorOfType<MainWindow>()?.CreateMainWindow();
						break;
					case Avalonia.Input.Key.O:
						{
							this.OpenSourceFile();
							break;
						}
					case Avalonia.Input.Key.OemPlus:
						{
							session.ZoomInCommand.Execute(null);
							break;
						}
					case Avalonia.Input.Key.OemMinus:
						{
							session.ZoomOutCommand.Execute(null);
							break;
						}
					case Avalonia.Input.Key.S:
						{
							this.SaveImage();
							break;
						}
					default:
						return;
				}
				e.Handled = true;
			}
		}


        // Called when key up.
        protected override void OnKeyUp(Avalonia.Input.KeyEventArgs e)
		{
			// call base
			base.OnKeyUp(e);
			if (e.Handled)
			{
				this.pressedKeys.Remove(e.Key);
				return;
			}

			// check focus
			var focusedElement = Avalonia.Input.FocusManager.Instance?.Current;
			if (focusedElement != null)
			{
				if (focusedElement is TextBox || focusedElement is NumericUpDown)
				{
					this.pressedKeys.Remove(e.Key);
					return;
				}
				if (focusedElement.FindAncestorOfType<SessionControl>(true) != this)
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
			var isCmdPressed = (this.pressedKeys.Contains(Avalonia.Input.Key.LWin) || this.pressedKeys.Contains(Avalonia.Input.Key.RWin));
			if (e.KeyModifiers == 0 && !isCmdPressed)
			{
				switch (e.Key)
				{
					case Avalonia.Input.Key.End:
						session.MoveToLastFrameCommand.TryExecute();
						break;
					case Avalonia.Input.Key.Home:
						session.MoveToFirstFrameCommand.TryExecute();
						break;
					case Avalonia.Input.Key.PageDown:
						session.MoveToNextFrameCommand.TryExecute();
						break;
					case Avalonia.Input.Key.PageUp:
						session.MoveToPreviousFrameCommand.TryExecute();
						break;
					default:
						return;
				}
				e.Handled = true;
			}
			this.pressedKeys.Remove(e.Key);
		}


		// Called when double tap on image.
		void OnImageDoubleTapped(object? sender, RoutedEventArgs e)
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
			this.SetValue<bool>(IsPointerOverImageProperty, false);
			this.SetValue<Point>(PointerPositionOnImageControlProperty, new Point(-1, -1));
			(this.DataContext as Session)?.SelectRenderedImagePixel(-1, -1);
		}


		// Called when pointer moved on image.
		void OnImagePointerMoved(object? sender, PointerEventArgs e)
		{
			// report position
			var point = e.GetCurrentPoint(this.imageContainerBorder);
			this.latestPointerEventArgsOnImage = e;
			this.SetValue<Point>(PointerPositionOnImageControlProperty, point.Position);
			this.SetValue<bool>(IsPointerOverImageProperty, true);

			// move image
			this.imagePointerPressedContentPosition?.Let(it =>
			{
				point = e.GetCurrentPoint(this.imageScrollViewer);
				if (point.Properties.IsLeftButtonPressed)
				{
					var bounds = this.imageScrollViewer.Bounds;
					if (!bounds.IsEmpty)
						this.ScrollImageScrollViewer(it, new Vector(point.Position.X / bounds.Width, point.Position.Y / bounds.Height));
				}
				else
				{
					this.imagePointerPressedContentPosition = null;
					this.stopUsingSmallRenderedImageAction.Schedule();
				}
			});

			// select pixel on image
			if (sender is IVisual imageControl && this.DataContext is Session session)
			{
				var image = session.RenderedImage;
				if (image != null)
				{
					var position = e.GetPosition(imageControl);
					var imageBounds = imageControl.Bounds;
					var relativeX = (position.X / imageBounds.Width);
					var relativeY = (position.Y / imageBounds.Height);
					session.SelectRenderedImagePixel((int)(image.Size.Width * relativeX + 0.5), (int)(image.Size.Height * relativeY + 0.5));
				}
				else
					session.SelectRenderedImagePixel(-1, -1);
			}
		}


		// Called when pressing on image viewer.
		void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (e.Pointer.Type == PointerType.Mouse)
			{
				this.SetValue<bool>(IsPointerPressedOnImageProperty, true);
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
			this.SetValue<bool>(IsPointerPressedOnImageProperty, false);
		}


		// Called when pressing on image scroll viewer.
		void OnImageScrollViewerPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			this.imageScrollViewer.Focus();
		}


		// Called when complete dragging splitter of options panel.
		void OnOptionsPanelSplitterDragCompleted(object? sender, VectorEventArgs e) =>
			this.stopUsingSmallRenderedImageAction.Schedule();


		// Called when start dragging splitter of options panel.
		void OnOptionsPanelSplitterDragStarted(object? sender, VectorEventArgs e) =>
			this.StartUsingSmallRenderedImage();
		

		// Called when pointer pressed on brightness adjustment UI.
		void OnPointerPressedOnBrightnessAdjustmentUI(object? sender, PointerEventArgs e)
		{
			if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
				this.SetValue<bool>(IsPointerPressedOnBrightnessAdjustmentUIProperty, true);
		}


		// Called when pointer released on brightness adjustment UI.
		void OnPointerReleasedOnBrightnessAdjustmentUI(object? sender, PointerReleasedEventArgs e) =>
			this.SetValue<bool>(IsPointerPressedOnBrightnessAdjustmentUIProperty, false);
		

		// Called when pointer pressed on color adjustment UI.
		void OnPointerPressedOnColorAdjustmentUI(object? sender, PointerEventArgs e)
		{
			if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
				this.SetValue<bool>(IsPointerPressedOnColorAdjustmentUIProperty, true);
		}


		// Called when pointer released on color adjustment UI.
		void OnPointerReleasedOnColorAdjustmentUI(object? sender, PointerReleasedEventArgs e) =>
			this.SetValue<bool>(IsPointerPressedOnColorAdjustmentUIProperty, false);
		

		// Called when pointer pressed on contrast adjustment UI.
		void OnPointerPressedOnContrastAdjustmentUI(object? sender, PointerEventArgs e)
		{
			if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
				this.SetValue<bool>(IsPointerPressedOnContrastAdjustmentUIProperty, true);
		}


		// Called when pointer released on contrast adjustment UI.
		void OnPointerReleasedOnContrastAdjustmentUI(object? sender, PointerReleasedEventArgs e) =>
			this.SetValue<bool>(IsPointerPressedOnContrastAdjustmentUIProperty, false);


		// Called when changing mouse wheel.
		void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
		{
			if (Avalonia.Input.FocusManager.Instance?.Current is TextBox textBox && textBox.FindAncestorOfType<NumericUpDown>() != null)
			{
				this.renderingParamsPanelScrollViewer.Focus();
				e.Handled = true;
				return;
			}
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
		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
		{
			base.OnPropertyChanged(change);
			var property = change.Property;
			if (property == DataContextProperty)
			{
				if (change.OldValue.Value is Session oldSession)
				{
					oldSession.PropertyChanged -= this.OnSessionPropertyChanged;
					oldSession.OpenSourceFileCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.ResetBrightnessAdjustmentCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.ResetContrastAdjustmentCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.ResetHighlightAdjustmentCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.ResetShadowAdjustmentCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.SaveAsNewProfileCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.SaveFilteredImageCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.SaveRenderedImageCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
				}
				if (change.NewValue.Value is Session newSession)
				{
					// attach to session
					newSession.PropertyChanged += this.OnSessionPropertyChanged;
					newSession.OpenSourceFileCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.ResetBrightnessAdjustmentCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.ResetContrastAdjustmentCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.ResetHighlightAdjustmentCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.ResetShadowAdjustmentCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveAsNewProfileCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveFilteredImageCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveRenderedImageCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					this.canOpenSourceFile.Update(newSession.OpenSourceFileCommand.CanExecute(null));
					this.canResetBrightnessAndContrastAdjustment.Update(newSession.ResetBrightnessAdjustmentCommand.CanExecute(null)
						|| newSession.ResetContrastAdjustmentCommand.CanExecute(null)
						|| newSession.ResetHighlightAdjustmentCommand.CanExecute(null)
						|| newSession.ResetShadowAdjustmentCommand.CanExecute(null));
					this.canSaveAsNewProfile.Update(newSession.SaveAsNewProfileCommand.CanExecute(null));
					this.canSaveImage.Update(newSession.SaveFilteredImageCommand.CanExecute(null)
						|| newSession.SaveRenderedImageCommand.CanExecute(null));
					this.canShowEvaluateImageDimensionsMenu.Update(newSession.IsSourceFileOpened);

					// setup histograms panel
					Grid.SetColumnSpan(this.imageViewerGrid, newSession.IsRenderingParametersPanelVisible ? 1 : 3);
					this.renderingParamsPanelColumn.Width = new GridLength(newSession.RenderingParametersPanelSize, GridUnitType.Pixel);

					// update rendered image
					this.updateEffectiveRenderedImageAction.Schedule();
					this.updateEffectiveRenderedImageIntModeAction.Schedule();
				}
				else
				{
					this.canOpenSourceFile.Update(false);
					this.canResetBrightnessAndContrastAdjustment.Update(false);
					this.canSaveAsNewProfile.Update(false);
					this.canSaveImage.Update(false);
					this.canShowEvaluateImageDimensionsMenu.Update(false);
					this.updateEffectiveRenderedImageAction.Execute();
				}
				this.keepHistogramsVisible = false;
				this.keepRenderingParamsPanelVisible = false;
				this.ReportImageViewportSize();
				this.ReportScreenPixelDensity();
				this.updateStatusBarStateAction.Schedule();
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


        // Called when CanExecute of command of session has been changed.
        void OnSessionCommandCanExecuteChanged(object? sender, EventArgs e)
		{
			if (!(this.DataContext is Session session))
				return;
			if (sender == session.OpenSourceFileCommand)
				this.canOpenSourceFile.Update(session.OpenSourceFileCommand.CanExecute(null));
			else if (sender == session.ResetBrightnessAdjustmentCommand
				|| sender == session.ResetContrastAdjustmentCommand
				|| sender == session.ResetHighlightAdjustmentCommand
				|| sender == session.ResetShadowAdjustmentCommand)
			{
				this.canResetBrightnessAndContrastAdjustment.Update(session.ResetBrightnessAdjustmentCommand.CanExecute(null)
					|| session.ResetContrastAdjustmentCommand.CanExecute(null)
					|| session.ResetHighlightAdjustmentCommand.CanExecute(null)
					|| session.ResetShadowAdjustmentCommand.CanExecute(null));
			}
			else if (sender == session.SaveAsNewProfileCommand)
				this.canSaveAsNewProfile.Update(session.SaveAsNewProfileCommand.CanExecute(null));
			else if (sender == session.SaveFilteredImageCommand
				|| sender == session.SaveRenderedImageCommand)
			{
				this.canSaveImage.Update(session.SaveFilteredImageCommand.CanExecute(null)
					|| session.SaveRenderedImageCommand.CanExecute(null));
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
					break;
				case nameof(Session.ImageDisplaySize):
					this.updateEffectiveRenderedImageIntModeAction.Schedule();
					break;
				case nameof(Session.IsHistogramsVisible):
					if (session.IsHistogramsVisible)
						this.keepHistogramsVisible = true;
					break;
				case nameof(Session.IsRenderingParametersPanelVisible):
					if (session.IsRenderingParametersPanelVisible)
					{
						Grid.SetColumnSpan(this.imageViewerGrid, 1);
						this.keepRenderingParamsPanelVisible = true;
					}
					else
						Grid.SetColumnSpan(this.imageViewerGrid, 3);
					break;
				case nameof(Session.IsSourceFileOpened):
					this.canShowEvaluateImageDimensionsMenu.Update((sender as Session)?.IsSourceFileOpened ?? false);
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
			}
		}


		// Called when setting changed.
		void OnSettingChanged(object? sender, SettingChangedEventArgs e)
		{
			if (e.Key == SettingKeys.ShowProcessInfo)
				this.SetValue<bool>(ShowProcessInfoProperty, (bool)e.Value);
			else if (e.Key == SettingKeys.ShowSelectedRenderedImagePixelArgbColor)
				this.SetValue<bool>(ShowSelectedRenderedImagePixelArgbColorProperty, (bool)e.Value);
			else if (e.Key == SettingKeys.ShowSelectedRenderedImagePixelLabColor)
				this.SetValue<bool>(ShowSelectedRenderedImagePixelLabColorProperty, (bool)e.Value);
			else if (e.Key == SettingKeys.ShowSelectedRenderedImagePixelXyzColor)
				this.SetValue<bool>(ShowSelectedRenderedImagePixelXyzColorProperty, (bool)e.Value);
		}


		// Called when test button clicked.
		void OnTestButtonClick()
		{
			//this.Application.Restart(AppSuiteApplication.RestoreMainWindowsArgument);
		}


		// Called when property of window changed.
		void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
		{
			var property = e.Property;
			if (property == Avalonia.Controls.Window.HeightProperty 
				|| property == Avalonia.Controls.Window.WidthProperty)
			{
				this.StartUsingSmallRenderedImage();
				this.stopUsingSmallRenderedImageAction.Reschedule(StopUsingSmallRenderedImageDelay);
			}
			else if (property == CarinaStudio.AppSuite.Controls.Window.IsOpenedProperty)
			{
				if ((bool)((object?)e.NewValue).AsNonNull())
					this.ReportScreenPixelDensity();
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


		// Open brightness and contrast adjustment UI.
		void OpenBrightnessAndContrastAdjustmentPopup() => this.brightnessAndContrastAdjustmentPopup.Open();


		// Open color adjustment UI.
		void OpenColorAdjustmentPopup() => this.colorAdjustmentPopup.Open();


		// Open source file.
		async void OpenSourceFile()
		{
			// check state
			if (!this.canOpenSourceFile.Value)
			{
				Logger.LogError("Cannot open source file in current state");
				return;
			}

			// find window
			if (this.attachedWindow == null)
			{
				Logger.LogError("No window to show open file dialog");
				return;
			}

			// select file
			var fileName = (await new OpenFileDialog().ShowAsync(this.attachedWindow)).Let((it) =>
			{
				if (it != null && it.IsNotEmpty())
					return it[0];
				return null;
			});
			if (fileName == null)
				return;

			// open file
			this.OpenSourceFile(fileName);
		}
		void OpenSourceFile(string fileName)
		{
			// check state
			if (!(this.DataContext is Session session))
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
			if (this.attachedWindow == null)
				return;
			session.ScreenPixelDensity = (this.attachedWindow.Screens.ScreenFromVisual(this.attachedWindow) ?? this.attachedWindow.Screens.Primary).PixelDensity;
		}


		// Reset brightness and contrast.
		void ResetBrightnessAndContrastAdjustment()
        {
			// check state
			if (!this.canResetBrightnessAndContrastAdjustment.Value)
				return;

			// reset
			if (this.DataContext is Session session)
			{
				session.ResetBrightnessAdjustmentCommand.TryExecute();
				session.ResetContrastAdjustmentCommand.TryExecute();
				session.ResetHighlightAdjustmentCommand.TryExecute();
				session.ResetShadowAdjustmentCommand.TryExecute();
			}
        }


		// Command to reset brightness and contrast.
		ICommand ResetBrightnessAndContrastAdjustmentCommand { get; }


		// Save as new profile.
		async void SaveAsNewProfile()
		{
			// check state
			if (!(this.DataContext is Session session))
			{
				Logger.LogError("No session to save as new profile");
				return;
			}
			if (!this.canSaveAsNewProfile.Value || !session.SaveAsNewProfileCommand.CanExecute(null))
			{
				Logger.LogError("Cannot save as new profile in current state");
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
				name = await new TextInputDialog()
				{
					InitialText = name,
					Message = this.Application.GetString("SessionControl.InputNameOfProfile"),
				}.ShowDialog(this.attachedWindow);
				if (string.IsNullOrWhiteSpace(name))
					return;

				// check name
				if (ImageRenderingProfiles.ValidateNewUserDefinedProfileName(name))
					break;

				// show message for duplicate name
				await new MessageDialog()
				{
					Icon = MessageDialogIcon.Warning,
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
			if (!this.canSaveImage.Value)
				return;

			// find window
			if (this.attachedWindow == null)
			{
				Logger.LogError("No window to show dialog");
				return;
			}

			// select image to save
			var saveFilteredImage = false;
			if (session.IsFilteringRenderedImageNeeded)
			{
				var result = await new MessageDialog()
				{
					Buttons = MessageDialogButtons.YesNoCancel,
					Icon = MessageDialogIcon.Question,
					Message = this.Application.GetString("SessionControl.ConfirmSavingFilteredImage")
				}.ShowDialog(this.attachedWindow);
				if (result == MessageDialogResult.Cancel)
					return;
				saveFilteredImage = (result == MessageDialogResult.Yes);
			}

			// select file
			var fileName = await new SaveFileDialog().Also((dialog) =>
			{
				var app = (App)this.Application;
				dialog.Filters.Add(new FileDialogFilter().Also((filter) =>
				{
					filter.Name = app.GetString("FileType.Jpeg");
					filter.Extensions.Add("jpg");
					filter.Extensions.Add("jpeg");
					filter.Extensions.Add("jpe");
					filter.Extensions.Add("jfif");
				}));
				dialog.Filters.Add(new FileDialogFilter().Also((filter) =>
				{
					filter.Name = app.GetString("FileType.Png");
					filter.Extensions.Add("png");
				}));
				dialog.Filters.Add(new FileDialogFilter().Also((filter) =>
				{
					filter.Name = app.GetString("FileType.RawBgra");
					filter.Extensions.Add("bgra");
				}));
				dialog.InitialFileName = session.SourceFileName?.Let(it => Path.GetFileNameWithoutExtension(it) + ".jpg") ?? $"Export_{session.ImageWidth}x{session.ImageHeight}.jpg";
			}).ShowAsync(this.attachedWindow);
			if (fileName == null)
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


		// Show color space management settings in application options.
		void ShowColorSpaceManagementOptions() => 
			this.FindAncestorOfType<MainWindow>()?.ShowAppOptions(ApplicationOptionsDialogSection.ColorSpaceManagement);


		/// <summary>
		/// <see cref="ICommand"/> to show menu of image dimensions evaluation.
		/// </summary>
		public ICommand ShowEvaluateImageDimensionsMenuCommand { get; }


		// Show file actions.
		void ShowFileActions()
		{
			if (this.fileActionsMenu.PlacementTarget == null)
				this.fileActionsMenu.PlacementTarget = this.fileActionsButton;
			this.fileActionsMenu.Open(this.fileActionsButton);
		}


		// Show other actions.
		void ShowOtherActions()
		{
			if (this.otherActionsMenu.PlacementTarget == null)
				this.otherActionsMenu.PlacementTarget = this.otherActionsButton;
			this.otherActionsMenu.Open(this.otherActionsButton);
		}


		// Show process info on UI or not.
		bool ShowProcessInfo { get => this.GetValue<bool>(ShowProcessInfoProperty); }


		// Show file in file explorer.
		void ShowSourceFileInFileExplorer()
        {
			if (!CarinaStudio.Platform.IsOpeningFileManagerSupported)
				return;
			if (this.DataContext is not Session session)
				return;
			var fileName = session.SourceFileName;
			if (!string.IsNullOrEmpty(fileName))
				CarinaStudio.Platform.OpenFileManager(fileName);
		}


		// Start using small rendered image.
		void StartUsingSmallRenderedImage()
		{
			if (this.DataContext is not Session session)
				return;
			if (!session.FitImageToViewport && session.ImageDisplayScale >= 2)
				return;
			this.stopUsingSmallRenderedImageAction.Cancel();
			if (!this.useSmallRenderedImage)
			{
				this.useSmallRenderedImage = true;
				this.updateEffectiveRenderedImageAction.Schedule();
				this.updateEffectiveRenderedImageIntModeAction.Schedule();
			}
		}


		// Status bar state.
		StatusBarState StatusBarState { get => this.GetValue<StatusBarState>(StatusBarStateProperty); }
	}
}
