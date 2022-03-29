using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Carina.PixelViewer.Controls;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.AppSuite.ViewModels;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Input;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
#if WINDOWS10_0_17763_0_OR_GREATER
using Microsoft.WindowsAPICodePack.Taskbar;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;

using AsTabControl = CarinaStudio.AppSuite.Controls.TabControl;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Main window of PixelViewer.
	/// </summary>
	class MainWindow : MainWindow<Workspace>
	{
		// Statis fields.
		static readonly AvaloniaProperty<bool> HasMultipleSessionsProperty = AvaloniaProperty.Register<MainWindow, bool>("HasMultipleSessions");


		// Constants.
		const string DraggingSessionKey = "DraggingSettion";


		// Fields.
		Session? attachedActivatedSession;
		readonly Border baseBorder;
		bool isPerformingContentRelayout;
		readonly AsTabControl mainTabControl;
		readonly ObservableList<TabItem> mainTabItems = new ObservableList<TabItem>();
		readonly ScheduledAction relayoutContentAction;
		readonly ScheduledAction updateTitleBarAction;


		/// <summary>
		/// Initialize new <see cref="MainWindow"/> instance.
		/// </summary>
		public MainWindow()
		{
			// initialize Avalonia resources
			InitializeComponent();
			if (CarinaStudio.Platform.IsMacOS)
				NativeMenu.SetMenu(this, this.Resources["nativeMenu"] as NativeMenu);

			// setup controls
			this.baseBorder = this.FindControl<Border>(nameof(baseBorder)).AsNonNull().Also(it => 
			{
				it.GetObservable(BoundsProperty).Subscribe(_ =>
				{
					if (this.isPerformingContentRelayout)
					{
						// [Workaround] Trigger layout to make sure that content will be placed correctly after changing size of window by code
						this.isPerformingContentRelayout = false;
						it.Padding = new Thickness();
					}
				});
			});

			// setup main tab control
			this.mainTabControl = this.FindControl<AsTabControl>("tabControl").AsNonNull().Also((it) =>
			{
				it.SelectionChanged += (s, e) => this.OnMainTabControlSelectionChanged();
			});
			this.mainTabItems.AddRange(((IList)this.mainTabControl.Items).Cast<TabItem>());
			this.mainTabControl.Items = this.mainTabItems;

			// create scheduled actions
			this.relayoutContentAction = new ScheduledAction(() =>
			{
				// [Workaround] Trigger layout to make sure that content will be placed correctly after changing size of window by code
				this.isPerformingContentRelayout = true;
				this.baseBorder.Padding = new Thickness(0, 0, 0, -1);
			});
			this.updateTitleBarAction = new ScheduledAction(() =>
			{
				if (this.IsClosed)
					return;
#if WINDOWS10_0_17763_0_OR_GREATER
				if (!TaskbarManager.IsPlatformSupported)
					return;
				if (this.attachedActivatedSession == null)
					TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
				else if (this.attachedActivatedSession.InsufficientMemoryForRenderedImage)
					TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Error);
				else if (this.attachedActivatedSession.IsRenderingImage
					|| this.attachedActivatedSession.IsSavingRenderedImage)
				{
					TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Indeterminate);
				}
				else
					TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
#endif
			});

			// trigger system color space update
			this.GetObservable(IsActiveProperty).Subscribe(isActive =>
			{
				if (isActive)
					Media.ColorSpace.InvalidateSystemScreenColorSpace();
			});
		}


		// Create tab item for given session.
		TabItem AttachTabItemToSession(Session session)
		{
			// create session control
			var sessionControl = new SessionControl()
			{
				DataContext = session,
			};

			// create tab item header
			var header = this.DataTemplates[0].Build(session);
			if (CarinaStudio.Platform.IsMacOS)
				(header as Control)?.Let(it => it.ContextMenu = null);

			// create tab item
			var tabItem = new TabItem()
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
		/// Check for application update.
		/// </summary>
		public async void CheckForAppUpdate()
		{
			this.VerifyAccess();
			using var updater = new CarinaStudio.AppSuite.ViewModels.ApplicationUpdater();
			var result = await new CarinaStudio.AppSuite.Controls.ApplicationUpdateDialog(updater)
			{
				CheckForUpdateWhenShowing = true
			}.ShowDialog(this);
			if (result == ApplicationUpdateDialogResult.ShutdownNeeded)
			{
				Logger.LogWarning("Shut down to continue updating");
				this.Application.Shutdown();
			}
		}


		// Close current tab item.
		void CloseCurrentMainTabItem() =>
			(this.mainTabControl.SelectedItem as TabItem)?.Let(it => this.CloseMainTabItem(it));


		// Close given tab item.
		void CloseMainTabItem(TabItem tabItem)
		{
			// check session
			if (tabItem.DataContext is not Session session)
				return;

			// close session
			(this.DataContext as Workspace)?.DetachAndCloseSession(session);
		}


		// Create tab item and its session.
		void CreateMainTabItem() =>
			(this.DataContext as Workspace)?.Let(it => it.CreateAndAttachSession(it.Sessions.Count));


		/// <summary>
		/// Create new main window.
		/// </summary>
		public void CreateMainWindow() =>
			this.Application.ShowMainWindow();


		// Detach from activated session.
		void DetachFromActivatedSession()
        {
			if (this.attachedActivatedSession == null)
				return;
			this.attachedActivatedSession.PropertyChanged -= this.OnActivatedSessionPropertyChanged;
			this.updateTitleBarAction.Schedule();
        }


		// Detach tab item from session.
		void DetachTabItemFromSession(TabItem tabItem)
		{
			(tabItem.Header as IControl)?.Let((it) => it.DataContext = null);
			(tabItem.Content as IControl)?.Let((it) => it.DataContext = null);
			tabItem.DataContext = null;
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


		// Initialize Avalonia component.
		private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


		// Move current session to new workspace.
		void MoveCurrentSessionToNewWorkspace() =>
			(this.mainTabControl.SelectedItem as TabItem)?.Let(it => this.MoveSessionToNewWorkspace(it));


		// Move given session to new workspace.
		void MoveSessionToNewWorkspace(TabItem tabItem)
        {
			// check state
			if (tabItem.DataContext is not Session session)
				return;

			// create new window
			if (!this.Application.ShowMainWindow(newWindow =>
            {
				if (newWindow.DataContext is Workspace newWorkspace)
					this.MoveSessionToNewWorkspace(session, newWorkspace);
			}))
			{
				this.Logger.LogError("Unable to create new main window for session to be moved");
				return;
			}
        }
		void MoveSessionToNewWorkspace(Session session, Workspace newWorkspace)
		{
			// find empty session
			var emptySession = newWorkspace.Sessions.FirstOrDefault();

			// transfer session
			newWorkspace.AttachSession(0, session);
			newWorkspace.ActivatedSession = session;

			// close empty session
			if (emptySession != null)
				newWorkspace.DetachAndCloseSession(emptySession);
		}


		// Called when property of activated session changed.
		void OnActivatedSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch(e.PropertyName)
            {
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
			(workspace.Sessions as INotifyCollectionChanged)?.Let((it) => it.CollectionChanged += this.OnSessionsChanged);
			this.SetValue<bool>(HasMultipleSessionsProperty, workspace.Sessions.Count > 1);

			// select tab item according to activated session
			if (workspace.Sessions.IsNotEmpty())
			{
				var tabIndex = workspace.ActivatedSession != null ? this.FindMainTabItemIndex(workspace.ActivatedSession) : -1;
				if (tabIndex > 0)
					this.mainTabControl.SelectedIndex = tabIndex;
				else
					this.mainTabControl.SelectedIndex = 0;
			}
			else
				workspace.CreateAndAttachSession();

			// attach to activated session
			workspace.ActivatedSession?.Let(it => this.AttachToActivatedSession(it));
		}


		/// <inheritdoc/>
		protected override ApplicationInfo OnCreateApplicationInfo() => new AppInfo();


        // Detach from view-model.
        protected override void OnDetachFromViewModel(Workspace workspace)
        {
			// detach from activated session
			this.DetachFromActivatedSession();

			// clear tab items
			this.mainTabControl.SelectedIndex = 0;
			for (var i = this.mainTabItems.Count - 1; i > 0; --i)
			{
				this.DetachTabItemFromSession(this.mainTabItems[i]);
				this.mainTabItems.RemoveAt(i);
			}

			// detach from workspace
			this.SetValue<bool>(HasMultipleSessionsProperty, false);
			(workspace.Sessions as INotifyCollectionChanged)?.Let((it) => it.CollectionChanged -= this.OnSessionsChanged);

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
			var session = e.Data.Get(DraggingSessionKey) as Session;
			if (session != null && e.ItemIndex != this.mainTabItems.Count - 1)
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
				return;
			}
		}


		// Called when drop.
		void OnDropOnTabItem(object? sender, DragOnTabItemEventArgs e)
		{
			// drag and drop is not supported properly on Linux
			if (CarinaStudio.Platform.IsLinux)
				return;
			
			// check state
			if (e.Handled || e.Item is not TabItem tabItem)
				return;
			
			// clear insertion indicators
			ItemInsertionIndicator.SetInsertingItemAfter(tabItem, false);
			ItemInsertionIndicator.SetInsertingItemBefore(tabItem, false);
			
			// drop files
			var session = (Session?)null;
			if (e.Data.HasFileNames())
			{
				// find tab
				if (e.ItemIndex >= this.mainTabItems.Count - 1)
				{
					session = (this.DataContext as Workspace)?.CreateAndAttachSession();
					if (session == null)
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
			session = e.Data.Get(DraggingSessionKey) as Session;
			if (session != null)
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
				}
				e.Handled = true;
				return;
			}
		}


		// Handle key down.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
			if (e.Handled || (e.KeyModifiers & KeyModifiers.Control) == 0 || CarinaStudio.Platform.IsMacOS)
				return;
			switch (e.Key)
			{
				case Key.T:
					this.CreateMainTabItem();
					break;
				case Key.W:
					this.CloseCurrentMainTabItem();
					break;
				default:
					return;
			}
			e.Handled = true;
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
				var selectedSession = (this.mainTabControl.SelectedItem as IControl)?.DataContext as Session;
				(this.DataContext as Workspace)?.Let((workspace) =>
				{
					workspace.ActivatedSession = selectedSession;
				});

				// focus on content later to make sure that view has been attached to visual tree
				SynchronizationContext.Current?.Post(() =>
				{
					((this.mainTabControl.SelectedItem as TabItem)?.Content as IInputElement)?.Focus();
				});
			}
		}


		// Called when property changed.
		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
		{
			base.OnPropertyChanged<T>(change);
			var property = change.Property;
			if (property == HeightProperty 
				|| property == WidthProperty
				|| property == WindowStateProperty)
			{
				if (this.WindowState == WindowState.Normal)
					this.relayoutContentAction?.Reschedule();
				else
					this.relayoutContentAction?.Cancel();
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
							if (session == null)
								continue;
							var tabItem = this.AttachTabItemToSession(session);
							this.mainTabItems.Insert(tabIndex, tabItem);
							this.mainTabControl.SelectedIndex = tabIndex++;
						}
					}
					break;
				case NotifyCollectionChangedAction.Move:
					this.mainTabItems.Move(e.OldStartingIndex, e.NewStartingIndex);
					break;
				case NotifyCollectionChangedAction.Remove:
					{
						foreach (Session? session in e.OldItems.AsNonNull())
						{
							if (session == null)
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
							this.DetachTabItemFromSession(this.mainTabItems[tabIndex]);
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
					}
					break;
			}
			this.SetValue<bool>(HasMultipleSessionsProperty, (this.DataContext as Workspace)?.Sessions?.Count > 1);
		}


		// Called when tab item dragged.
		void OnTabItemDragged(object? sender, TabItemDraggedEventArgs e)
		{
			// prevent dragging tab on Linux because drag-and-drop is not supported properly
			if (CarinaStudio.Platform.IsLinux)
				return;

			// get session
			var session = (e.Item as TabItem)?.DataContext as Session;
			if (session == null)
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
				var tabIndex = activatedSession != null ? this.FindMainTabItemIndex(activatedSession) : -1;
				if (tabIndex < 0)
				{
					if (activatedSession != null)
						this.mainTabControl.SelectedIndex = 0;
				}
				else if (this.mainTabControl.SelectedIndex != tabIndex)
					this.mainTabControl.SelectedIndex = tabIndex;
				if (activatedSession != null)
					this.AttachToActivatedSession(activatedSession);
				else
					this.DetachFromActivatedSession();
			}
        }


		// Reset title of current session.
		void ResetCurrentSessionTitle() =>
			(this.mainTabControl.SelectedItem as TabItem)?.Let(it => this.ResetSessionTitle(it));


		// Reset title of session.
		void ResetSessionTitle(TabItem tabItem)
        {
			if (tabItem.DataContext is Session session)
				session.CustomTitle = null;
		}


		// Set custom title of current session.
		void SetCurrentCustomSessionTitle() =>
			(this.mainTabControl.SelectedItem as TabItem)?.Let(it => this.SetCustomSessionTitle(it));


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
				Message = this.Application.GetString("MainWindow.SetCustomSessionTitle.Message"),
			}.ShowDialog(this);
			if (string.IsNullOrWhiteSpace(customTitle))
				return;

			// set title
			session.CustomTitle = customTitle;
		}


		/// <summary>
		/// Show application info.
		/// </summary>
		public async void ShowAppInfo()
        {
			using var appInfo = new AppInfo();
			await new ApplicationInfoDialog(appInfo).ShowDialog(this);
        }


		/// <summary>
		/// Show application options.
		/// </summary>
		public void ShowAppOptions() => this.ShowAppOptions(ApplicationOptionsDialogSection.First);


		/// <summary>
		/// Show application options.
		/// </summary>
		/// <param name="initSection">Initial section to show.</param>
		public async void ShowAppOptions(ApplicationOptionsDialogSection initSection)
		{
			this.VerifyAccess();
			switch (await new ApplicationOptionsDialog() { InitialFocusedSection = initSection }.ShowDialog<ApplicationOptionsDialogResult>(this))
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
		}


		/// <summary>
		/// Show editor of application configuration.
		/// </summary>
		public void ShowConfigurationEditor()
		{
			var keys = new List<SettingKey>();
			keys.AddRange(SettingKey.GetDefinedKeys<CarinaStudio.AppSuite.ConfigurationKeys>());
			keys.AddRange(SettingKey.GetDefinedKeys<ConfigurationKeys>());
			_ = new SettingsEditorDialog()
			{
				SettingKeys = keys,
				Settings = this.Configuration,
			}.ShowDialog(this);
		}
	}
}
