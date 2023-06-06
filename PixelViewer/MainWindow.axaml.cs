//#define DEMO

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Carina.PixelViewer.Controls;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Collections;
using CarinaStudio.Input;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;

using AsTabControl = CarinaStudio.AppSuite.Controls.TabControl;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Main window of PixelViewer.
	/// </summary>
	class MainWindow : MainWindow<Workspace>
	{
		// Static fields.
		static readonly StyledProperty<bool> HasMultipleSessionsProperty = AvaloniaProperty.Register<MainWindow, bool>("HasMultipleSessions");


		// Constants.
		const string DraggingSessionKey = "DraggingSettion";


		// Fields.
		Session? attachedActivatedSession;
		readonly Border baseBorder;
		bool isPerformingContentRelayout;
		readonly AsTabControl mainTabControl;
		readonly ObservableList<TabItem> mainTabItems = new();
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
			if (CarinaStudio.Platform.IsMacOS)
				NativeMenu.SetMenu(this, (NativeMenu)this.Resources["nativeMenu"].AsNonNull());

			// setup controls
			this.baseBorder = this.FindControl<Border>(nameof(baseBorder)).AsNonNull().Also(it => 
			{
				it.GetObservable(BoundsProperty).Subscribe(new Observer<Rect>(_ =>
				{
					if (this.isPerformingContentRelayout)
					{
						// [Workaround] Trigger layout to make sure that content will be placed correctly after changing size of window by code
						this.isPerformingContentRelayout = false;
						it.Padding = new Thickness();
					}
				}));
			});

			// setup main tab control
			this.mainTabControl = this.Get<AsTabControl>("tabControl").Also((it) =>
			{
				it.SelectionChanged += (s, _) => this.OnMainTabControlSelectionChanged();
			});
			this.mainTabItems.AddRange(this.mainTabControl.Items.Cast<TabItem>());
			this.mainTabControl.Items.Clear();
			this.mainTabControl.ItemsSource = this.mainTabItems;

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
				var session = this.attachedActivatedSession;
				if (session == null)
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
				header?.Let(it => it.ContextMenu = null);

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

			// close session
			(this.DataContext as Workspace)?.DetachAndCloseSession(session);
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
			if (this.attachedActivatedSession == null)
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
			if (emptySession != null)
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
				var tabIndex = workspace.ActivatedSession != null ? this.FindMainTabItemIndex(workspace.ActivatedSession) : -1;
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
			if (CarinaStudio.Platform.IsLinux)
				return;
			
			// check state
			if (e.Handled || e.Item is not TabItem tabItem)
				return;
			
			// clear insertion indicators
			ItemInsertionIndicator.SetInsertingItemAfter(tabItem, false);
			ItemInsertionIndicator.SetInsertingItemBefore(tabItem, false);
			
			// drop files
			Session? session;
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
				var selectedSession = (this.mainTabControl.SelectedItem as Control)?.DataContext as Session;
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


		// Called when property changed.
		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
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
					}
					break;
			}
			this.SetValue(HasMultipleSessionsProperty, (this.DataContext as Workspace)?.Sessions?.Count > 1);
		}


		// Called when tab item dragged.
		void OnTabItemDragged(object? sender, TabItemDraggedEventArgs e)
		{
			// prevent dragging tab on Linux because drag-and-drop is not supported properly
			if (CarinaStudio.Platform.IsLinux)
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


#if WINDOWS10_0_17763_0_OR_GREATER
		/// <inheritdoc/>
		protected override Type? TaskbarManagerType => typeof(Microsoft.WindowsAPICodePack.Taskbar.TaskbarManager);
#endif
	}
}
