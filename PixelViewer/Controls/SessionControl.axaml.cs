using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using Carina.PixelViewer.Animation;
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
using System.Windows.Input;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// <see cref="Control"/>(View) of <see cref="Session"/>.
	/// </summary>
	class SessionControl : UserControl<IAppSuiteApplication>
	{
		/// <summary>
		/// <see cref="IValueConverter"/> which maps boolean to <see cref="Stretch.Uniform"/>(True) and <see cref="Stretch.None"/>(False).
		/// </summary>
		public static readonly IValueConverter BooleanToMediaStretchConverter = new BooleanToValueConverter<Stretch>(Stretch.Uniform, Stretch.None);
		/// <summary>
		/// <see cref="IValueConverter"/> which maps boolean to <see cref="ScrollBarVisibility.Auto"/>(True) and <see cref="ScrollBarVisibility.Disabled"/>(False).
		/// </summary>
		public static readonly IValueConverter BooleanToScrollBarVisibilityConverter = new BooleanToValueConverter<ScrollBarVisibility>(ScrollBarVisibility.Auto, ScrollBarVisibility.Disabled);
		/// <summary>
		/// Property of <see cref="EffectiveRenderedImageScale"/>.
		/// </summary>
		public static readonly AvaloniaProperty<double> EffectiveRenderedImageScaleProperty = AvaloniaProperty.Register<SessionControl, double>(nameof(EffectiveRenderedImageScale), 1.0);


		// Static fields.
		static readonly AvaloniaProperty<bool> IsImageViewerScrollableProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(IsImageViewerScrollable));
		static readonly AvaloniaProperty<bool> ShowProcessInfoProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(ShowProcessInfo));
		static readonly AvaloniaProperty<StatusBarState> StatusBarStateProperty = AvaloniaProperty.Register<SessionControl, StatusBarState>(nameof(StatusBarState), StatusBarState.None);


		// Fields.
		readonly ToggleButton brightnessAndContrastAdjustmentButton;
		readonly Popup brightnessAndContrastAdjustmentPopup;
		readonly MutableObservableValue<bool> canOpenSourceFile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canResetBrightnessAndContrastAdjustment = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveAsNewProfile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveImage = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canShowEvaluateImageDimensionsMenu = new MutableObservableValue<bool>();
		readonly ToggleButton colorAdjustmentButton;
		readonly Popup colorAdjustmentPopup;
		readonly ScheduledAction commitHistogramsPanelVisibilityAction;
		readonly ToggleButton evaluateImageDimensionsButton;
		readonly ContextMenu evaluateImageDimensionsMenu;
		readonly ToggleButton fileActionsButton;
		readonly ContextMenu fileActionsMenu;
		readonly ToggleButton histogramsButton;
		readonly Control histogramsPanel;
		readonly int histogramsPanelTransitionDuration;
		readonly double histogramsPanelTransitionX;
		Vector? imagePointerPressedContentPosition;
		readonly ScrollViewer imageScrollViewer;
		readonly ToggleButton otherActionsButton;
		readonly ContextMenu otherActionsMenu;
		Vector? targetImageViewportCenter;
		readonly ScheduledAction updateIsImageViewerScrollableAction;
		readonly ScheduledAction updateStatusBarStateAction;


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
			this.brightnessAndContrastAdjustmentButton = this.FindControl<ToggleButton>(nameof(brightnessAndContrastAdjustmentButton)).AsNonNull();
			this.brightnessAndContrastAdjustmentPopup = this.FindControl<Popup>(nameof(brightnessAndContrastAdjustmentPopup)).AsNonNull().Also(it =>
			{
				it.PlacementTarget = this.brightnessAndContrastAdjustmentButton;
				it.Closed += (_, e) => this.SynchronizationContext.Post(() => this.brightnessAndContrastAdjustmentButton.IsChecked = false);
				it.Opened += (_, e) => this.SynchronizationContext.Post(() => this.brightnessAndContrastAdjustmentButton.IsChecked = true);
			});
			this.colorAdjustmentButton = this.FindControl<ToggleButton>(nameof(colorAdjustmentButton)).AsNonNull();
			this.colorAdjustmentPopup = this.FindControl<Popup>(nameof(colorAdjustmentPopup)).AsNonNull().Also(it =>
			{
				it.PlacementTarget = this.colorAdjustmentButton;
				it.Closed += (_, e) => this.SynchronizationContext.Post(() => this.colorAdjustmentButton.IsChecked = false);
				it.Opened += (_, e) => this.SynchronizationContext.Post(() => this.colorAdjustmentButton.IsChecked = true);
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
			this.histogramsButton = this.FindControl<ToggleButton>(nameof(histogramsButton)).AsNonNull();
			this.histogramsPanel = this.FindControl<Control>(nameof(histogramsPanel)).AsNonNull();
			this.imageScrollViewer = this.FindControl<ScrollViewer>(nameof(this.imageScrollViewer)).AsNonNull();
			this.otherActionsButton = this.FindControl<ToggleButton>(nameof(otherActionsButton)).AsNonNull();
			this.otherActionsMenu = ((ContextMenu)this.Resources[nameof(otherActionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = true);
			});
#if DEBUG
			this.FindControl<Button>("testButton").AsNonNull().IsVisible = true;
#endif

			// load resources
			if (this.Application.TryGetResource<double>("Double/SessionControl.Histogram.Width", out var doubleValue) && doubleValue.HasValue)
				this.histogramsPanelTransitionX = doubleValue.Value / -2;
			if (this.Application.TryGetResource<TimeSpan>("TimeSpan/SessionControl.HistogramsPanel.Transition", out var duration) && duration.HasValue)
				this.histogramsPanelTransitionDuration = (int)duration.Value.TotalMilliseconds;

			// create scheduled actions
			this.commitHistogramsPanelVisibilityAction = new ScheduledAction(() =>
			{
				if (this.DataContext is Session session)
					this.histogramsPanel.IsVisible = session.IsHistogramsVisible;
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


		// Animate histograms panel according to current state
		void AnimateHistogramsPanel()
		{
			if (this.DataContext is Session session && session.IsHistogramsVisible)
			{
				this.histogramsPanel.IsVisible = true;
				this.histogramsPanel.Opacity = 1;
				(this.histogramsPanel.RenderTransform as TranslateTransform)?.Let(it => it.X = 0);
			}
			else
			{
				this.histogramsPanel.Opacity = 0;
				(this.histogramsPanel.RenderTransform as TranslateTransform)?.Let(it => it.X = this.histogramsPanelTransitionX);
			}
		}


		// Check for application update.
		void CheckForAppUpdate()
		{
			this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Let(async (window) =>
			{
				using var updater = new CarinaStudio.AppSuite.ViewModels.ApplicationUpdater();
				var result = await new CarinaStudio.AppSuite.Controls.ApplicationUpdateDialog(updater)
				{
					CheckForUpdateWhenShowing = true
				}.ShowDialog(window);
				if (result == ApplicationUpdateDialogResult.ShutdownNeeded)
				{
					Logger.LogWarning("Shut down to continue updating");
					this.Application.Shutdown();
				}
			});
		}


		// Copy file name.
		void CopyFileName()
		{
			if (this.DataContext is not Session session || !session.IsSourceFileOpened)
				return;
			session.SourceFileName?.Let(it =>
			{
				_ = ((App)this.Application).Clipboard.SetTextAsync(Path.GetFileName(it));
			});
		}


		// Copy file path.
		void CopyFilePath()
		{
			if (this.DataContext is not Session session || !session.IsSourceFileOpened)
				return;
			session.SourceFileName?.Let(it =>
			{
				_ = ((App)this.Application).Clipboard.SetTextAsync(it);
			});
		}


		/// <summary>
		/// Get effective scaling ratio of rendered image.
		/// </summary>
		public double EffectiveRenderedImageScale { get => this.GetValue<double>(EffectiveRenderedImageScaleProperty); }


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
			var window = this.FindLogicalAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
				return;

			// select frame number
			var selectFrameNumber = await new FrameNumberSelectionDialog()
			{
				FrameCount = session.FrameCount,
				InitialFrameNumber = session.FrameNumber,
			}.ShowDialog<int?>(window);
			if (selectFrameNumber == null)
				return;

			// move to frame
			if (this.DataContext == session)
				session.FrameNumber = selectFrameNumber.Value;
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
			this.AddHandler(PointerWheelChangedEvent, this.OnPointerWheelChanged, Avalonia.Interactivity.RoutingStrategies.Tunnel);

			// attach to settings
			var settings = this.Settings;
			settings.SettingChanged += this.OnSettingChanged;
			this.SetValue<bool>(ShowProcessInfoProperty, settings.GetValueOrDefault(SettingKeys.ShowProcessInfo));
		}


		// Called when detached from logical tree.
		protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
		{
			// disable drag-drop
			this.RemoveHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.RemoveHandler(DragDrop.DropEvent, this.OnDrop);

			// remove event handlers
			this.RemoveHandler(PointerWheelChangedEvent, this.OnPointerWheelChanged);

			// detach from settings
			this.Settings.SettingChanged -= this.OnSettingChanged;

			// call base
			base.OnDetachedFromLogicalTree(e);
		}


		// Called when drag over.
		void OnDragOver(object? sender, DragEventArgs e)
		{
			if (e.Data.TryGetSingleFileName(out var fileName))
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
			if (e.Data.TryGetSingleFileName(out var fileName) && fileName != null)
			{
				this.OpenSourceFile(fileName);
				e.Handled = true;
			}
		}


		// Called when key up.
		protected override void OnKeyUp(Avalonia.Input.KeyEventArgs e)
		{
			// call base
			base.OnKeyUp(e);

			// check focus
			var focusedElement = Avalonia.Input.FocusManager.Instance.Current;
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
			if ((e.KeyModifiers & KeyModifiers.Control) != 0)
			{
				switch (e.Key)
				{
					case Avalonia.Input.Key.D0:
						{
							session.FitRenderedImageToViewport = true;
							break;
						}
					case Avalonia.Input.Key.D1:
						{
							session.RenderedImageScale = 1.0;
							session.FitRenderedImageToViewport = false;
							break;
						}
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
			else if (e.KeyModifiers == 0)
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
		}


		// Called when pointer leave from image.
		void OnImagePointerLeave(object sender, PointerEventArgs e)
		{
			(this.DataContext as Session)?.SelectRenderedImagePixel(-1, -1);
		}


		// Called when pointer moved on image.
		void OnImagePointerMoved(object sender, PointerEventArgs e)
		{
			// move image
			this.imagePointerPressedContentPosition?.Let(it =>
			{
				var point = e.GetCurrentPoint(this.imageScrollViewer);
				if (point.Properties.IsLeftButtonPressed)
				{
					var bounds = this.imageScrollViewer.Bounds;
					if (!bounds.IsEmpty)
						this.ScrollImageScrollViewer(it, new Vector(point.Position.X / bounds.Width, point.Position.Y / bounds.Height));
				}
				else
					this.imagePointerPressedContentPosition = null;
			});

			// select pixel on image
			var position = e.GetPosition(sender as IVisual);
			(this.DataContext as Session)?.SelectRenderedImagePixel((int)(position.X + 0.5), (int)(position.Y + 0.5));
		}


		// Called when pressing on image viewer.
		void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (e.Pointer.Type == PointerType.Mouse && this.IsImageViewerScrollable)
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
					}
				}
			}
		}


		// Called when releasing pointer from image viewer.
		void OnImagePointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			this.imagePointerPressedContentPosition = null;
		}


		// Called when pressing on image scroll viewer.
		void OnImageScrollViewerPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			this.imageScrollViewer.Focus();
		}


		// Called when property of image scroll viewer changed.
		void OnImageScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == ScrollViewer.ExtentProperty)
			{
				this.updateIsImageViewerScrollableAction.Schedule();
				if (this.targetImageViewportCenter.HasValue)
				{
					this.SynchronizationContext.Post(() =>
					{
						var center = this.targetImageViewportCenter.Value;
						this.targetImageViewportCenter = null;
						this.ScrollImageScrollViewer(center, new Vector(0.5, 0.5));
					});
				}
			}
			else if (e.Property == ScrollViewer.ViewportProperty)
				this.updateIsImageViewerScrollableAction.Schedule();
		}


		// Called when changing mouse wheel.
		void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
		{
			if (!this.imageScrollViewer.IsPointerOver || (e.KeyModifiers & KeyModifiers.Control) == 0)
				return;
			if (this.DataContext is not Session session || !session.IsSourceFileOpened || session.FitRenderedImageToViewport)
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
					newSession.SaveAsNewProfileCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveFilteredImageCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveRenderedImageCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					this.canOpenSourceFile.Update(newSession.OpenSourceFileCommand.CanExecute(null));
					this.canResetBrightnessAndContrastAdjustment.Update(newSession.ResetBrightnessAdjustmentCommand.CanExecute(null)
						|| newSession.ResetContrastAdjustmentCommand.CanExecute(null));
					this.canSaveAsNewProfile.Update(newSession.SaveAsNewProfileCommand.CanExecute(null));
					this.canSaveImage.Update(newSession.SaveFilteredImageCommand.CanExecute(null) 
						|| newSession.SaveRenderedImageCommand.CanExecute(null));
					this.canShowEvaluateImageDimensionsMenu.Update(newSession.IsSourceFileOpened);

					// setup histograms panel
					this.histogramsButton.IsChecked = newSession.IsHistogramsVisible;
					this.histogramsPanel.DisableTransitionsAndRun(() =>
					{
						this.AnimateHistogramsPanel();
						this.histogramsPanel.IsVisible = newSession.IsHistogramsVisible;
						this.commitHistogramsPanelVisibilityAction.Cancel();
					});
				}
				else
				{
					this.canOpenSourceFile.Update(false);
					this.canResetBrightnessAndContrastAdjustment.Update(false);
					this.canSaveAsNewProfile.Update(false);
					this.canSaveImage.Update(false);
					this.canShowEvaluateImageDimensionsMenu.Update(false);
				}
				this.UpdateEffectiveRenderedImageScale();
				this.updateStatusBarStateAction.Schedule();
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
				|| sender == session.ResetContrastAdjustmentCommand)
			{
				this.canResetBrightnessAndContrastAdjustment.Update(session.ResetBrightnessAdjustmentCommand.CanExecute(null)
					|| session.ResetContrastAdjustmentCommand.CanExecute(null));
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
				case nameof(Session.EffectiveRenderedImageScale):
					this.UpdateEffectiveRenderedImageScale();
					break;
				case nameof(Session.FitRenderedImageToViewport):
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
				case nameof(Session.IsHistogramsVisible):
					this.AnimateHistogramsPanel();
					if (session.IsHistogramsVisible)
						this.commitHistogramsPanelVisibilityAction.Cancel();
					else
						this.commitHistogramsPanelVisibilityAction.Reschedule(this.histogramsPanelTransitionDuration);
					this.histogramsButton.IsChecked = session.IsHistogramsVisible;
					break;
				case nameof(Session.IsSourceFileOpened):
					this.canShowEvaluateImageDimensionsMenu.Update((sender as Session)?.IsSourceFileOpened ?? false);
					this.updateStatusBarStateAction.Schedule();
					break;
				case nameof(Session.RenderedImage):
					this.UpdateEffectiveRenderedImageScale();
					break;
				case nameof(Session.RenderedImageScale):
                    {
						var viewportSize = this.imageScrollViewer.Viewport;
						var viewportOffset = this.imageScrollViewer.Offset;
						var contentSize = this.imageScrollViewer.Extent;
						var centerX = (viewportOffset.X + viewportSize.Width / 2) / contentSize.Width;
						var centerY = (viewportOffset.Y + viewportSize.Height / 2) / contentSize.Height;
						this.targetImageViewportCenter = new Vector(centerX, centerY);
					}
					break;
			}
		}


		// Called when setting changed.
		void OnSettingChanged(object? sender, SettingChangedEventArgs e)
		{
			if (e.Key == SettingKeys.ShowProcessInfo)
				this.SetValue<bool>(ShowProcessInfoProperty, (bool)e.Value);
		}


		// Called when test button clicked.
		void OnTestButtonClick()
		{
			this.Application.Restart(AppSuiteApplication.RestoreMainWindowsArgument);
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
			var window = this.FindAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
			{
				Logger.LogError("No window to show open file dialog");
				return;
			}

			// select file
			var fileName = (await new OpenFileDialog().ShowAsync(window)).Let((it) =>
			{
				if (it.IsNotEmpty())
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
			var window = this.FindAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
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
				}.ShowDialog(window);
				if (string.IsNullOrWhiteSpace(name))
					return;

				// check name
				if (ImageRenderingProfiles.ValidateNewUserDefinedProfileName(name))
					break;

				// show message for duplicate name
				await new MessageDialog()
				{
					Icon = CarinaStudio.AppSuite.Controls.MessageDialogIcon.Warning,
					Message = string.Format(this.Application.GetStringNonNull("SessionControl.DuplicateNameOfProfile"), name),
				}.ShowDialog(window);
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
			var window = this.FindAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
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
				}.ShowDialog(window);
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
					filter.Name = app.GetString("FileType.Png");
					filter.Extensions.Add("png");
				}));
				dialog.Filters.Add(new FileDialogFilter().Also((filter) =>
				{
					filter.Name = app.GetString("FileType.All");
					filter.Extensions.Add("*");
				}));
			}).ShowAsync(window);
			if (fileName == null)
				return;

			// save
			if (saveFilteredImage)
				session.SaveFilteredImageCommand.TryExecute(fileName);
			else
				session.SaveRenderedImageCommand.TryExecute(fileName);
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


		// Show application info.
		void ShowAppInfo()
        {
			this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Let(async (window) =>
			{
				using var appInfo = new AppInfo();
				await new ApplicationInfoDialog(appInfo).ShowDialog(window);
			});
        }


		// Show application options.
		void ShowAppOptions() => this.ShowAppOptions(ApplicationOptionsDialogSection.First);
		void ShowAppOptions(ApplicationOptionsDialogSection initSection)
		{
			this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Let(async (window) =>
			{
				switch (await new ApplicationOptionsDialog() { InitialFocusedSection = initSection }.ShowDialog<ApplicationOptionsDialogResult>(window))
				{
					case ApplicationOptionsDialogResult.RestartApplicationNeeded:
						Logger.LogWarning("Need to restart application");
						if (this.Application.IsDebugMode)
							this.Application.Restart($"{App.DebugArgument} {App.RestoreMainWindowsArgument}");
						else
							this.Application.Restart(App.RestoreMainWindowsArgument);
						break;
					case ApplicationOptionsDialogResult.RestartMainWindowsNeeded:
						Logger.LogWarning("Need to restart main windows");
						this.Application.RestartMainWindows();
						break;
				}
			});
		}


		// Show color space management settings in application options.
		void ShowColorSpaceManagementOptions() => this.ShowAppOptions(ApplicationOptionsDialogSection.ColorSpaceManagement);


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


		// Show or hide histograms panel.
		void ShowHideHistograms()
        {
			if (this.DataContext is not Session session)
				return;
			session.IsHistogramsVisible = !session.IsHistogramsVisible;
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


		// Status bar state.
		StatusBarState StatusBarState { get => this.GetValue<StatusBarState>(StatusBarStateProperty); }


		// Update effective scale of rendered image.
		void UpdateEffectiveRenderedImageScale()
		{
			// get session
			if (!(this.DataContext is Session session))
				return;

			// get base scale
			var scale = session.EffectiveRenderedImageScale;

			// apply screen DPI
			session.RenderedImage?.Let((renderedImage) =>
			{
				this.FindAncestorOfType<Avalonia.Controls.Window>()?.Let((window) =>
				{
					var screenDpi = window.Screens.Primary.PixelDensity;
					scale *= (Math.Min(renderedImage.Dpi.X, renderedImage.Dpi.Y) / 96.0 / screenDpi);
				});
			});

			// update
			if (Math.Abs(this.EffectiveRenderedImageScale - scale) > 0.0001)
				this.SetValue<double>(EffectiveRenderedImageScaleProperty, scale);
		}
	}
}
