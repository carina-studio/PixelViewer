//#define DEMO

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Carina.PixelViewer.Controls;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Collections;
using CarinaStudio.Input;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Key = Avalonia.Input.Key;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

using AsTabControl = CarinaStudio.AppSuite.Controls.TabControl;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Main window of PixelViewer.
	/// </summary>
	class MainWindow : MainWindow<Workspace>, INotificationPresenter
	{
		// Static fields.
		static readonly StyledProperty<bool> HasMultipleSessionsProperty = AvaloniaProperty.Register<MainWindow, bool>("HasMultipleSessions");
		static bool IsRefreshingAppIconOnMacOSHintDialogShown;


		// Constants.
		const string DraggingSessionKey = "DraggingSettion";


		// Fields.
		Session? attachedActivatedSession;
		bool isPerformingContentRelayout;
		readonly AsTabControl mainTabControl;
		readonly ObservableList<TabItem> mainTabItems = new();
		readonly NotificationPresenter notificationPresenter;
		readonly ScheduledAction relayoutContentAction;
		readonly ScheduledAction updateTitleBarAction;


		/// <summary>
		/// Initialize new <see cref="MainWindow"/> instance.
		/// </summary>
		public MainWindow()
		{
			// create commands
			this.CloseMainTabItemCommand = new Command<TabItem>(this.CloseMainTabItem);
			this.MoveSessionToNewWorkspaceCommand = new Command<TabItem>(this.MoveSessionToNewWorkspace);
			this.ResetSessionTitleCommand = new Command<TabItem>(this.ResetSessionTitle);
			this.SetCustomSessionTitleCommand = new Command<TabItem>(this.SetCustomSessionTitle);

			// initialize Avalonia resources
			AvaloniaXamlLoader.Load(this);
			if (Platform.IsMacOS)
				NativeMenu.SetMenu(this, (NativeMenu)this.Resources["nativeMenu"].AsNonNull());

			// setup controls
			var baseBorder = this.Get<Border>("baseBorder").Also(it =>
			{
				it.LayoutUpdated += (_, _) =>
				{
					if (this.isPerformingContentRelayout)
					{
						// [Workaround] Trigger layout to make sure that content will be placed correctly after changing size of window by code
						this.isPerformingContentRelayout = false;
						it.Padding = new Thickness();
					}
				};
			});
			this.notificationPresenter = this.Get<NotificationPresenter>(nameof(notificationPresenter));

			// setup main tab control
			this.mainTabControl = this.Get<AsTabControl>("tabControl").Also(it =>
			{
				it.SelectionChanged += (_, _) => this.OnMainTabControlSelectionChanged();
			});
			this.mainTabItems.AddRange(this.mainTabControl.Items.Cast<TabItem>());
			this.mainTabControl.Items.Clear();
			this.mainTabControl.ItemsSource = this.mainTabItems;

			// create scheduled actions
			this.relayoutContentAction = new(() =>
			{
				// [Workaround] Trigger layout to make sure that content will be placed correctly after changing size of window by code
				this.isPerformingContentRelayout = true;
				baseBorder.Padding = new Thickness(0, 0, 0, -1);
			});
			this.updateTitleBarAction = new(() =>
			{
				if (this.IsClosed)
					return;
				var session = this.attachedActivatedSession;
				if (session is null)
					this.TaskbarIconProgressState = TaskbarIconProgressState.None;
				else if (session.InsufficientMemoryForRenderedImage
					|| session.HasRenderingError)
				{
					this.TaskbarIconProgressState = TaskbarIconProgressState.Error;
				}
				else if (session.IsRenderingImage
					|| session.IsSavingRenderedImage)
				{
					this.TaskbarIconProgressState = TaskbarIconProgressState.Indeterminate;
				}
				else
					this.TaskbarIconProgressState = TaskbarIconProgressState.None;
			});
			
			// intercept key events
			this.AddHandler(KeyDownEvent, this.OnPreviewKeyDown, RoutingStrategies.Tunnel);
			this.AddHandler(KeyUpEvent, this.OnPreviewKeyUp, RoutingStrategies.Tunnel);
		}
		
		
		/// <inheritdoc/>
		public void AddNotification(Notification notification)
		{
			if (notification.Icon is null)
				notification.BindToResource(Notification.IconProperty, this, "Image/Icon.Information.Colored");
			this.notificationPresenter.AddNotification(notification);
		}


		// Create tab item for given session.
		TabItem AttachTabItemToSession(Session session)
		{
			// create session control
			var sessionControl = new SessionControl
			{
				DataContext = session,
			};

			// create tab item header
			var header = this.DataTemplates[0].Build(session);
			if (Platform.IsMacOS)
				header?.Let(it => it.ContextMenu = null);

			// create tab item
			var tabItem = new TabItem
			{
				Content = sessionControl,
				DataContext = session,
				Header = header,
			};
			return tabItem;
		}


		// Attach to activated session.
		void AttachToActivatedSession(Session session)
		{
			if (this.attachedActivatedSession == session)
				return;
			this.DetachFromActivatedSession();
			this.attachedActivatedSession = session;
			session.PropertyChanged += this.OnActivatedSessionPropertyChanged;
			this.updateTitleBarAction.Schedule();
		}


		/// <summary>
		/// Close current tab item.
		/// </summary>
		public void CloseCurrentMainTabItem() =>
			(this.mainTabControl.SelectedItem as TabItem)?.Let(this.CloseMainTabItem);


		// Close given tab item.
		void CloseMainTabItem(TabItem tabItem)
		{
			// check session
			if (tabItem.DataContext is not Session session)
				return;

			// close file or session
			(this.DataContext as Workspace)?.Let(workspace =>
			{
				if (workspace.Sessions.Count == 1
				    && workspace.Sessions[0] == session
				    && !this.HasMultipleMainWindows)
				{
					if (!session.IsSourceFileOpened || session.ClearSourceFileCommand.TryExecute())
						return;
				}
				workspace.DetachAndCloseSession(session);
			});
		}


		/// <summary>
		/// Create tab item and its session.
		/// </summary>
		public void CreateMainTabItem() =>
			(this.DataContext as Workspace)?.Let(it => it.CreateAndAttachSession(it.Sessions.Count));
		

		/// <summary>
		/// Command to close given tab item.
		/// </summary>
		public ICommand CloseMainTabItemCommand { get; }


		/// <summary>
		/// Create new main window.
		/// </summary>
		public void CreateMainWindow() =>
			this.Application.ShowMainWindowAsync();


		// Detach from activated session.
		void DetachFromActivatedSession()
        {
			if (this.attachedActivatedSession is null)
				return;
			this.attachedActivatedSession.PropertyChanged -= this.OnActivatedSessionPropertyChanged;
			this.updateTitleBarAction.Schedule();
        }


		// Detach tab item from session.
		static void DetachTabItemFromSession(TabItem tabItem)
		{
			(tabItem.Header as Control)?.Let((it) => it.DataContext = null);
			(tabItem.Content as Control)?.Let((it) => it.DataContext = null);
			tabItem.DataContext = null;
		}
		
		
		// Dispose native menu item properly.
		void DisposeNativeMenuItem(NativeMenuItem menuItem)
		{
			menuItem.Menu?.Let(menu =>
			{
				foreach (var item in menu.Items)
				{
					if (item is NativeMenuItem menuItem)
						this.DisposeNativeMenuItem(menuItem);
				}
				menu.Items.Clear();
			});
		}


		// Find index of main tab item attached to given session.
		int FindMainTabItemIndex(Session session)
		{
			for (var i = this.mainTabItems.Count - 1; i >= 0; --i)
			{
				if (this.mainTabItems[i].DataContext == session)
					return i;
			}
			return -1;
		}


		/// <summary>
		/// Move current session to new workspace.
		/// </summary>
		public void MoveCurrentSessionToNewWorkspace() =>
			(this.mainTabControl.SelectedItem as TabItem)?.Let(this.MoveSessionToNewWorkspace);


		// Move given session to new workspace.
		async void MoveSessionToNewWorkspace(TabItem tabItem)
        {
			// check state
			if (tabItem.DataContext is not Session session)
				return;

			// create new window
			if (!await this.Application.ShowMainWindowAsync(newWindow =>
            {
				if (newWindow.DataContext is Workspace newWorkspace)
					MoveSessionToNewWorkspace(session, newWorkspace);
			}))
			{
				this.Logger.LogError("Unable to create new main window for session to be moved");
			}
        }
		static void MoveSessionToNewWorkspace(Session session, Workspace newWorkspace)
		{
			// find empty session
			var emptySession = newWorkspace.Sessions.FirstOrDefault();

			// transfer session
			newWorkspace.AttachSession(0, session);
			newWorkspace.ActivatedSession = session;

			// close empty session
			if (emptySession is not null)
				newWorkspace.DetachAndCloseSession(emptySession);
		}


		/// <summary>
		/// Command to move given session to new workspace.
		/// </summary>
		public ICommand MoveSessionToNewWorkspaceCommand { get; }


		// Called when property of activated session changed.
		void OnActivatedSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch(e.PropertyName)
            {
				case nameof(Session.HasRenderingError):
				case nameof(Session.InsufficientMemoryForRenderedImage):
				case nameof(Session.IsRenderingImage):
				case nameof(Session.IsSavingRenderedImage):
					this.updateTitleBarAction.Schedule();
					break;
            }
		}


		// Attach to view-model.
		protected override void OnAttachToViewModel(Workspace workspace)
		{
			// call base
			base.OnAttachToViewModel(workspace);

			// create tab items
			foreach (var session in workspace.Sessions)
			{
				var tabItem = this.AttachTabItemToSession(session);
				this.mainTabItems.Insert(this.mainTabItems.Count - 1, tabItem);
			}

			// attach to workspace
			workspace.Window = this;
			(workspace.Sessions as INotifyCollectionChanged)?.Let((it) => it.CollectionChanged += this.OnSessionsChanged);
			this.SetValue(HasMultipleSessionsProperty, workspace.Sessions.Count > 1);

			// select tab item according to activated session
			if (workspace.Sessions.IsNotEmpty())
			{
				var tabIndex = workspace.ActivatedSession is not null ? this.FindMainTabItemIndex(workspace.ActivatedSession) : -1;
				if (tabIndex > 0)
					this.mainTabControl.SelectedIndex = tabIndex;
				else
					this.mainTabControl.SelectedIndex = 0;
			}
			else
				workspace.CreateAndAttachSession();

			// attach to activated session
			workspace.ActivatedSession?.Let(this.AttachToActivatedSession);
		}


		/// <inheritdoc/>
		protected override void OnClosed(EventArgs e)
		{
			// [Workaround] Remove bindings to window to prevent window leakage
			if (Platform.IsMacOS) 
			{
				NativeMenu.GetMenu(this)?.Let(menu =>
				{
					foreach (var item in menu.Items)
					{
						if (item is NativeMenuItem menuItem)
							this.DisposeNativeMenuItem(menuItem);
					}
					menu.Items.Clear();
				});
			}
			
			// call base
			base.OnClosed(e);
		}


		// Detach from view-model.
        protected override void OnDetachFromViewModel(Workspace workspace)
        {
			// detach from activated session
			this.DetachFromActivatedSession();

			// clear tab items
			this.mainTabControl.SelectedIndex = 0;
			for (var i = this.mainTabItems.Count - 1; i > 0; --i)
			{
				DetachTabItemFromSession(this.mainTabItems[i]);
				this.mainTabItems.RemoveAt(i);
			}

			// detach from workspace
			workspace.Window = null;
			this.SetValue(HasMultipleSessionsProperty, false);
			(workspace.Sessions as INotifyCollectionChanged)?.Let(it => it.CollectionChanged -= this.OnSessionsChanged);

			// call base
			base.OnDetachFromViewModel(workspace);
        }


		// Called when drag leave tab item.
		void OnDragLeaveTabItem(object? sender, TabItemEventArgs e)
		{
			if (e.Item is not TabItem tabItem)
				return;
			ItemInsertionIndicator.SetInsertingItemAfter(tabItem, false);
			ItemInsertionIndicator.SetInsertingItemBefore(tabItem, false);
		}


		// Called when drag over.
		void OnDragOverTabItem(object? sender, DragOnTabItemEventArgs e)
		{
			// check state
			if (e.Handled || e.Item is not TabItem tabItem)
				return;
			
			// setup
			e.DragEffects = DragDropEffects.None;
			e.Handled = true;

			// handle file dragging
			if (e.Data.HasFileNames())
			{
				if (e.ItemIndex < this.mainTabItems.Count - 1)
					this.mainTabControl.SelectedIndex = e.ItemIndex;
				e.DragEffects = DragDropEffects.Copy;
				return;
			}
			
			// handle session dragging
			if (e.Data.Get(DraggingSessionKey) is Session session && e.ItemIndex != this.mainTabItems.Count - 1)
			{
				// find source position
				var workspace = (Workspace)session.Owner.AsNonNull();
				var srcIndex = workspace.Sessions.IndexOf(session);
				if (srcIndex < 0)
					return;
				
				// select target position
				var targetIndex = e.PointerPosition.X <= e.HeaderVisual.Bounds.Width / 2
					? e.ItemIndex
					: e.ItemIndex + 1;
				
				// update insertion indicator
				if (workspace != this.DataContext
					|| (srcIndex != targetIndex && srcIndex + 1 != targetIndex))
				{
					var insertAfter = (targetIndex != e.ItemIndex);
					ItemInsertionIndicator.SetInsertingItemAfter(tabItem, insertAfter);
					ItemInsertionIndicator.SetInsertingItemBefore(tabItem, !insertAfter);
				}
				else
				{
					ItemInsertionIndicator.SetInsertingItemAfter(tabItem, false);
					ItemInsertionIndicator.SetInsertingItemBefore(tabItem, false);
				}
				
				// complete
				this.mainTabControl.ScrollHeaderIntoView(e.ItemIndex);
				e.DragEffects = DragDropEffects.Move;
			}
		}


		// Called when drop.
		void OnDropOnTabItem(object? sender, DragOnTabItemEventArgs e)
		{
			// drag and drop is not supported properly on Linux
			if (Platform.IsLinux)
				return;
			
			// check state
			if (e.Handled || e.Item is not TabItem tabItem)
				return;
			
			// clear insertion indicators
			ItemInsertionIndicator.SetInsertingItemAfter(tabItem, false);
			ItemInsertionIndicator.SetInsertingItemBefore(tabItem, false);
			
			// drop files
			if (e.Data.HasFileNames())
			{
				// find tab
				if (e.ItemIndex >= this.mainTabItems.Count - 1)
				{
					if ((this.DataContext as Workspace)?.CreateAndAttachSession() is null)
						return;
				}

				// drop data
				(this.mainTabItems[e.ItemIndex].Content as SessionControl)?.Let(it =>
				{
					_ = it.DropDataAsync(e.Data, e.KeyModifiers);
				});

				// complete
				e.Handled = true;
				return;
			}

			// drop session
			if (e.Data.Get(DraggingSessionKey) is Session session)
			{
				// find source position
				var srcWorkspace = (Workspace)session.Owner.AsNonNull();
				var srcIndex = srcWorkspace.Sessions.IndexOf(session);
				if (srcIndex < 0)
					return;
				
				// select target position
				var targetIndex = e.PointerPosition.X <= e.HeaderVisual.Bounds.Width / 2
					? e.ItemIndex
					: e.ItemIndex + 1;
				
				// move session
				if (srcWorkspace == this.DataContext)
				{
					if (srcIndex != targetIndex && srcIndex + 1 != targetIndex)
					{
						if (srcIndex < targetIndex)
							srcWorkspace.MoveSession(srcIndex, targetIndex - 1);
						else
							srcWorkspace.MoveSession(srcIndex, targetIndex);
					}
				}
				else if (this.DataContext is Workspace targetWorkspace)
				{
					// attach to target workspace
					targetWorkspace.AttachSession(targetIndex, session);
					targetWorkspace.ActivatedSession = session;
					
					// activate
					this.ActivateAndBringToFront();
				}

				// [Workaround] Sometimes the content of tab item will gone after moving tab item
				(this.Content as Control)?.Let(it =>
				{
					var margin = it.Margin;
					it.Margin = new(0, 0, 0, -1);
					this.SynchronizationContext.Post(() => it.Margin = margin);
				});

				// complete
				e.Handled = true;
			}
		}


		/// <inheritdoc/>
		protected override void OnInitialDialogsClosed()
		{
			base.OnInitialDialogsClosed();
			this.ShowPixelViewerInitialDialogs();
		}


		// Called when selection of main tab control changed.
        void OnMainTabControlSelectionChanged()
		{
			if (this.mainTabControl.SelectedIndex >= this.mainTabItems.Count - 1 && !this.IsClosed)
			{
				if (this.mainTabItems.Count > 1)
					this.CreateMainTabItem();
			}
			else
			{
				// update activated session
				var sessionControl = this.mainTabControl.SelectedItem as Control;
				var session = sessionControl?.DataContext as Session;
				(this.DataContext as Workspace)?.Let(workspace =>
				{
					workspace.ActivatedSession = session;
				});

				// focus on content later to make sure that view has been attached to visual tree
				this.SynchronizationContext.Post(() =>
				{
					((this.mainTabControl.SelectedItem as TabItem)?.Content as IInputElement)?.Focus();
				});
			}
		}


		/// <inheritdoc/>
		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);
#if DEMO
			this.SynchronizationContext.Post(() =>
			{
				this.WindowState = WindowState.Normal;
				(this.Screens.ScreenFromWindow(this.PlatformImpl) ?? this.Screens.Primary)?.Let(screen =>
				{
					var workingArea = screen.WorkingArea;
					var w = (workingArea.Width * 0.95) / 4;
					var h = (workingArea.Height * 0.95) / 3;
					var u = Math.Min(w, h);
					var sysDecorSizes = this.GetSystemDecorationSizes();
					if (Platform.IsNotMacOS)
						u /= screen.PixelDensity;
					this.Width = u * 4;
					this.Height = this.ExtendClientAreaToDecorationsHint
						? u * 3
						: u * 3 - sysDecorSizes.Top - sysDecorSizes.Bottom;
				});
			});
#endif
		}
		
		
		// Called when previewing key down.
		void OnPreviewKeyDown(object?  sender, KeyEventArgs e)
		{
			var isCtrlPressed = Platform.IsMacOS
				? (e.KeyModifiers & KeyModifiers.Meta) != 0
				: (e.KeyModifiers & KeyModifiers.Control) != 0;
			if (isCtrlPressed)
			{
				switch (e.Key)
				{
					case Key.Left:
						if (this.mainTabControl.SelectedIndex > 0)
							--this.mainTabControl.SelectedIndex;
						else if (this.mainTabItems.Count > 1)
							this.mainTabControl.SelectedIndex = this.mainTabItems.Count - 2;
						e.Handled = true;
						break;
					case Key.N:
						if (Platform.IsNotMacOS) // Will be triggered through NativeMenu on macOS
							this.CreateMainWindow();
						e.Handled = true;
						break;
					case Key.Right:
						if (this.mainTabControl.SelectedIndex < this.mainTabItems.Count - 2)
							++this.mainTabControl.SelectedIndex;
						else if (this.mainTabItems.Count > 1)
							this.mainTabControl.SelectedIndex = 0;
						e.Handled = true;
						break;
					case Key.T:
						if (Platform.IsNotMacOS) // Will be triggered through NativeMenu on macOS
							this.CreateMainTabItem();
						e.Handled = true;
						break;
					case Key.W:
						if (Platform.IsNotMacOS) // Will be triggered through NativeMenu on macOS
							this.CloseCurrentMainTabItem();
						e.Handled = true;
						break;
				}
			}
			((this.mainTabControl.SelectedItem as TabItem)?.Content as SessionControl)?.OnPreviewKeyDown(e);
		}
		
		
		// Called when previewing key up.
		void OnPreviewKeyUp(object?  sender, KeyEventArgs e)
		{
			((this.mainTabControl.SelectedItem as TabItem)?.Content as SessionControl)?.OnPreviewKeyUp(e);
		}


		// Called when property changed.
		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			var property = change.Property;
			if (property == HeightProperty 
				|| property == WidthProperty
				|| property == WindowStateProperty)
			{
				// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
				if (this.WindowState == WindowState.Normal)
					this.relayoutContentAction?.Reschedule();
				else
					this.relayoutContentAction?.Cancel();
				// ReSharper restore ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
			}
		}


		// Called when collection of sessions has been changed.
		void OnSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				{
					var tabIndex = e.NewStartingIndex;
					foreach (Session? session in e.NewItems.AsNonNull())
					{
						if (session is null)
							continue;
						var tabItem = this.AttachTabItemToSession(session);
						this.mainTabItems.Insert(tabIndex, tabItem);
						this.mainTabControl.SelectedIndex = tabIndex++;
					}
					break;
				}
				case NotifyCollectionChangedAction.Move:
				{
					var selectedIndex = this.mainTabControl.SelectedIndex;
					if (selectedIndex == e.OldStartingIndex)
						this.StopRendering();
					this.mainTabItems.Move(e.OldStartingIndex, e.NewStartingIndex);
					if (selectedIndex == e.OldStartingIndex)
					{
						this.mainTabControl.SelectedIndex = e.NewStartingIndex;
						this.StartRendering();
					}
					break;
				}
				case NotifyCollectionChangedAction.Remove:
				{
					foreach (Session? session in e.OldItems.AsNonNull())
					{
						if (session is null)
							continue;
						var tabIndex = this.FindMainTabItemIndex(session);
						if (tabIndex < 0)
							continue;
						if (this.mainTabControl.SelectedIndex == tabIndex)
						{
							if (tabIndex > 0)
								this.mainTabControl.SelectedIndex = (tabIndex - 1);
							else if (tabIndex < this.mainTabItems.Count - 2)
								this.mainTabControl.SelectedIndex = (tabIndex + 1);
							else
								this.mainTabControl.SelectedIndex = -1;
						}
						DetachTabItemFromSession(this.mainTabItems[tabIndex]);
						this.mainTabItems.RemoveAt(tabIndex);
					}
					(this.DataContext as Workspace)?.Let((it) =>
					{
						if (it.Sessions.IsEmpty() && !this.IsClosed)
						{
							if (this.HasMultipleMainWindows)
							{
								this.Logger.LogWarning("Close window because all sessions were closed");
								this.Close();
							}
							else
								it.CreateAndAttachSession();
						}
					});
					break;
				} 
			}
			this.SetValue(HasMultipleSessionsProperty, (this.DataContext as Workspace)?.Sessions.Count > 1);
		}


		// Called when tab item dragged.
		void OnTabItemDragged(object? sender, TabItemDraggedEventArgs e)
		{
			// prevent dragging tab on Linux because drag-and-drop is not supported properly
			if (Platform.IsLinux)
				return;

			// get session
			if((e.Item as TabItem)?.DataContext is not Session session)
				return;
			
			// prepare dragging data
			var data = new DataObject();
			data.Set(DraggingSessionKey, session);

			// start dragging session
			DragDrop.DoDragDrop(e.PointerEventArgs, data, DragDropEffects.Move);
		}


		// Property of view-model changed.
        protected override void OnViewModelPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnViewModelPropertyChanged(e);
			if (this.DataContext is not Workspace workspace)
				return;
			if (e.PropertyName == nameof(Workspace.ActivatedSession))
			{
				var activatedSession = workspace.ActivatedSession;
				var tabIndex = activatedSession is not null ? this.FindMainTabItemIndex(activatedSession) : -1;
				if (tabIndex < 0)
				{
					if (activatedSession is not null)
						this.mainTabControl.SelectedIndex = 0;
				}
				else if (this.mainTabControl.SelectedIndex != tabIndex)
					this.mainTabControl.SelectedIndex = tabIndex;
				if (activatedSession is not null)
					this.AttachToActivatedSession(activatedSession);
				else
					this.DetachFromActivatedSession();
			}
        }


		/// <summary>
		/// Reset title of current session.
		/// </summary>
		public void ResetCurrentSessionTitle() =>
			(this.mainTabControl.SelectedItem as TabItem)?.Let(this.ResetSessionTitle);


		// Reset title of session.
		void ResetSessionTitle(TabItem tabItem)
        {
			if (tabItem.DataContext is Session session)
				session.CustomTitle = null;
		}


		/// <summary>
		/// Command to reset title of session.
		/// </summary>
		public ICommand ResetSessionTitleCommand { get; }


		/// <summary>
		/// Set custom title of current session.
		/// </summary>
		public void SetCurrentCustomSessionTitle() =>
			(this.mainTabControl.SelectedItem as TabItem)?.Let(this.SetCustomSessionTitle);


		// Set custom title of session.
		async void SetCustomSessionTitle(TabItem tabItem)
		{
			// check session
			if (tabItem.DataContext is not Session session)
				return;

			// input custom title
			var customTitle = await new TextInputDialog()
			{
				InitialText = session.CustomTitle,
				Message = this.GetResourceObservable("String/MainWindow.SetCustomSessionTitle.Message"),
			}.ShowDialog(this);
			if (string.IsNullOrWhiteSpace(customTitle))
				return;

			// set title
			session.CustomTitle = customTitle;
		}


		/// <summary>
		/// Command to set custom title of session.
		/// </summary>
		public ICommand SetCustomSessionTitleCommand { get; }


		// Show PixelViewer specific initial dialogs.
		async void ShowPixelViewerInitialDialogs()
		{
			// check state
			if (this.IsClosed || this.Application.IsShutdownStarted)
				return;
			
			// hint for refreshing application icon on macOS
			var appVersion = this.Application.Assembly.GetName().Version;
			var prevVersion = this.Application.PreviousVersion;
			if (Platform.IsMacOS 
			    && appVersion?.Major == 3
			    && (prevVersion?.Major).GetValueOrDefault() < 3
			    && !this.Application.IsFirstLaunch
			    && !IsRefreshingAppIconOnMacOSHintDialogShown)
			{
				IsRefreshingAppIconOnMacOSHintDialogShown = true;
				var result = await new MessageDialog
				{
					Buttons = MessageDialogButtons.YesNo,
					Icon = MessageDialogIcon.Information,
					Message = this.Application.GetObservableString("MainWindow.RefreshingAppIconOnMacOSHint"),
				}.ShowDialog(this);
				if (result == MessageDialogResult.Yes)
					Platform.OpenLink("https://carinastudio.azurewebsites.net/PixelViewer/InstallAndUpgrade#Upgrade");
			}
		}
	}
}
