using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using Carina.PixelViewer.Data.Converters;
using Carina.PixelViewer.Input;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
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
	class SessionControl : UserControl
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
		static readonly ILogger Logger = App.Current.LoggerFactory.CreateLogger(nameof(SessionControl));
		static readonly AvaloniaProperty<bool> ShowProcessInfoProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(ShowProcessInfo));
		static readonly AvaloniaProperty<StatusBarState> StatusBarStateProperty = AvaloniaProperty.Register<SessionControl, StatusBarState>(nameof(StatusBarState), StatusBarState.None);


		// Fields.
		readonly MutableObservableValue<bool> canOpenSourceFile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveAsNewProfile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveRenderedImage = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canShowEvaluateImageDimensionsMenu = new MutableObservableValue<bool>();
		readonly ToggleButton evaluateImageDimensionsButton;
		readonly ContextMenu evaluateImageDimensionsMenu;
		readonly ToggleButton fileActionsButton;
		readonly ContextMenu fileActionsMenu;
		readonly ScrollViewer imageScrollViewer;
		readonly ToggleButton otherActionsButton;
		readonly ContextMenu otherActionsMenu;
		readonly ScheduledAction updateStatusBarStateAction;


		/// <summary>
		/// Initialize new <see cref="SessionControl"/> instance.
		/// </summary>
		public SessionControl()
		{
			// create commands
			this.OpenSourceFileCommand = new Command(this.OpenSourceFile, this.canOpenSourceFile);
			this.SaveAsNewProfileCommand = new Command(() => this.SaveAsNewProfile(), this.canSaveAsNewProfile);
			this.SaveRenderedImageCommand = new Command(() => this.SaveRenderedImage(), this.canSaveRenderedImage);
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
			this.evaluateImageDimensionsButton = this.FindControl<ToggleButton>(nameof(this.evaluateImageDimensionsButton)).AsNonNull();
			this.evaluateImageDimensionsMenu = ((ContextMenu)this.Resources[nameof(evaluateImageDimensionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => App.Current.SynchronizationContext.Post(() => this.evaluateImageDimensionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => App.Current.SynchronizationContext.Post(() => this.evaluateImageDimensionsButton.IsChecked = true);
			});
			this.fileActionsButton = this.FindControl<ToggleButton>(nameof(this.fileActionsButton)).AsNonNull();
			this.fileActionsMenu = ((ContextMenu)this.Resources[nameof(fileActionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => App.Current.SynchronizationContext.Post(() => this.fileActionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => App.Current.SynchronizationContext.Post(() => this.fileActionsButton.IsChecked = true);
			});
			this.imageScrollViewer = this.FindControl<ScrollViewer>(nameof(this.imageScrollViewer)).AsNonNull();
			this.otherActionsButton = this.FindControl<ToggleButton>(nameof(otherActionsButton)).AsNonNull();
			this.otherActionsMenu = ((ContextMenu)this.Resources[nameof(otherActionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => App.Current.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => App.Current.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = true);
			});

			// create scheduled actions
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


		// Check for application update.
		void CheckForAppUpdate()
        {
			this.FindLogicalAncestorOfType<Window>()?.Let(async (window) =>
			{
				using var updater = new CarinaStudio.AppSuite.ViewModels.ApplicationUpdater();
				var result = await new CarinaStudio.AppSuite.Controls.ApplicationUpdateDialog(updater)
				{
					CheckForUpdateWhenShowing = true
				}.ShowDialog(window);
				if (result == CarinaStudio.AppSuite.Controls.ApplicationUpdateDialogResult.ShutdownNeeded)
				{
					Logger.LogWarning("Shut down to continue updating");
					App.Current.Shutdown();
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
				_ = App.Current.Clipboard.SetTextAsync(Path.GetFileName(it));
			});
        }


		// Copy file path.
		void CopyFilePath()
		{
			if (this.DataContext is not Session session || !session.IsSourceFileOpened)
				return;
			session.SourceFileName?.Let(it =>
			{
				_ = App.Current.Clipboard.SetTextAsync(it);
			});
		}


		/// <summary>
		/// Get effective scaling ratio of rendered image.
		/// </summary>
		public double EffectiveRenderedImageScale { get => this.GetValue<double>(EffectiveRenderedImageScaleProperty); }


		// Move to specific frame.
		async void MoveToSpecificFrame()
        {
			// check state
			if (this.DataContext is not Session session)
				return;
			if (!session.HasMultipleFrames)
				return;

			// find window
			var window = this.FindLogicalAncestorOfType<Window>();
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

			// attach to settings
			var settings = App.Current.Settings;
			settings.SettingChanged += this.OnSettingChanged;
			this.SetValue<bool>(ShowProcessInfoProperty, settings.GetValueOrDefault(SettingKeys.ShowProcessInfo));
		}


		// Called when detached from logical tree.
		protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
		{
			// disable drag-drop
			this.RemoveHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.RemoveHandler(DragDrop.DropEvent, this.OnDrop);

			// detach from settings
			App.Current.Settings.SettingChanged -= this.OnSettingChanged;

			// call base
			base.OnDetachedFromLogicalTree(e);
		}


		// Called when drag over.
		void OnDragOver(object? sender, DragEventArgs e)
		{
			if (e.Data.HasSingleFileName())
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
			e.Data.GetSingleFileName()?.Let((it) =>
			{
				this.OpenSourceFile(it);
				e.Handled = true;
			});
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
							this.SaveRenderedImage();
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
			var position = e.GetPosition(sender as IVisual);
			(this.DataContext as Session)?.SelectRenderedImagePixel((int)(position.X + 0.5), (int)(position.Y + 0.5));
		}


		// Called when pressing on image scroll viewer.
		void OnImageScrollViewerPointerPressed(object sender, PointerPressedEventArgs e)
		{
			this.imageScrollViewer.Focus();
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
					oldSession.SaveAsNewProfileCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.SaveRenderedImageCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
				}
				if (change.NewValue.Value is Session newSession)
				{
					newSession.PropertyChanged += this.OnSessionPropertyChanged;
					newSession.OpenSourceFileCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveAsNewProfileCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveRenderedImageCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					this.canOpenSourceFile.Update(newSession.OpenSourceFileCommand.CanExecute(null));
					this.canSaveAsNewProfile.Update(newSession.SaveAsNewProfileCommand.CanExecute(null));
					this.canSaveRenderedImage.Update(newSession.SaveRenderedImageCommand.CanExecute(null));
					this.canShowEvaluateImageDimensionsMenu.Update(newSession.IsSourceFileOpened);
				}
				else
				{
					this.canOpenSourceFile.Update(false);
					this.canSaveAsNewProfile.Update(false);
					this.canSaveRenderedImage.Update(false);
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
			else if (sender == session.SaveAsNewProfileCommand)
				this.canSaveAsNewProfile.Update(session.SaveAsNewProfileCommand.CanExecute(null));
			else if (sender == session.SaveRenderedImageCommand)
				this.canSaveRenderedImage.Update(session.SaveRenderedImageCommand.CanExecute(null));
		}


		// Called when property of session changed.
		void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
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
						break;
					}
				case nameof(Session.HasRenderingError):
				case nameof(Session.InsufficientMemoryForRenderedImage):
					this.updateStatusBarStateAction.Schedule();
					break;
				case nameof(Session.IsSourceFileOpened):
					this.canShowEvaluateImageDimensionsMenu.Update((sender as Session)?.IsSourceFileOpened ?? false);
					this.updateStatusBarStateAction.Schedule();
					break;
				case nameof(Session.RenderedImage):
					this.UpdateEffectiveRenderedImageScale();
					break;
			}
		}


		// Called when setting changed.
		void OnSettingChanged(object? sender, SettingChangedEventArgs e)
		{
			if (e.Key == SettingKeys.ShowProcessInfo)
				this.SetValue<bool>(ShowProcessInfoProperty, (bool)e.Value);
		}


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
			var window = this.FindAncestorOfType<Window>();
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
			var window = this.FindAncestorOfType<Window>();
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
				name = await new CarinaStudio.AppSuite.Controls.TextInputDialog()
				{
					InitialText = name,
					Message = App.Current.GetString("SessionControl.InputNameOfProfile"),
				}.ShowDialog(window);
				if (string.IsNullOrWhiteSpace(name))
					return;

				// check name
				if (ImageRenderingProfiles.ValidateNewUserDefinedProfileName(name))
					break;

				// show message for duplicate name
				await new CarinaStudio.AppSuite.Controls.MessageDialog()
				{
					Icon = CarinaStudio.AppSuite.Controls.MessageDialogIcon.Warning,
					Message = string.Format(App.Current.GetStringNonNull("SessionControl.DuplicateNameOfProfile"), name),
				}.ShowDialog(window);
			}

			// save as new profile
			session.SaveAsNewProfileCommand.Execute(name);
		}


		/// <summary>
		/// <see cref="ICommand"/> to save parameters as new profile.
		/// </summary>
		public ICommand SaveAsNewProfileCommand { get; }


		// Save rendered image to file.
		async void SaveRenderedImage()
		{
			// check state
			if (!(this.DataContext is Session session))
			{
				Logger.LogError("No session to save rendered image");
				return;
			}
			var command = session.SaveRenderedImageCommand;
			if (!command.CanExecute(null) || !this.canSaveRenderedImage.Value)
			{
				Logger.LogError("Cannot save rendered image in current state");
				return;
			}

			// find window
			var window = this.FindAncestorOfType<Window>();
			if (window == null)
			{
				Logger.LogError("No window to show dialog");
				return;
			}

			// select file
			var fileName = await new SaveFileDialog().Also((dialog) =>
			{
				var app = App.Current;
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
			command.Execute(fileName);
		}


		/// <summary>
		/// <see cref="ICommand"/> to save rendered image to file.
		/// </summary>
		public ICommand SaveRenderedImageCommand { get; }


		// Show application info.
		void ShowAppInfo()
        {
			this.FindLogicalAncestorOfType<Window>()?.Let(async (window) =>
			{
				using var appInfo = new AppInfo();
				await new CarinaStudio.AppSuite.Controls.ApplicationInfoDialog(appInfo).ShowDialog(window);
			});
        }


		// Show application options.
		void ShowAppOptions()
        {
			this.FindLogicalAncestorOfType<Window>()?.Let(async (window) =>
			{
				await new ApplicationOptionsDialog().ShowDialog(window);
				if (App.Current.IsRestartingMainWindowsNeeded)
				{
					Logger.LogWarning("Need to restart main windows");
					App.Current.RestartMainWindows();
				}
			});
		}


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
				this.FindAncestorOfType<Window>()?.Let((window) =>
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
