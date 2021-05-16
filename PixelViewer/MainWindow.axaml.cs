using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Carina.PixelViewer.Collections;
using Carina.PixelViewer.Controls;
using Carina.PixelViewer.Data.Converters;
using Carina.PixelViewer.Input;
using Carina.PixelViewer.Threading;
using Carina.PixelViewer.ViewModels;
using NLog;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Windows.Input;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Main window of PixelViewer.
	/// </summary>
	class MainWindow : Window
	{
		/// <summary>
		/// Property of <see cref="HasDialog"/>.
		/// </summary>
		public static readonly AvaloniaProperty<bool> HasDialogProperty = AvaloniaProperty.Register<MainWindow, bool>(nameof(HasDialog), false);
		/// <summary>
		/// <see cref="IValueConverter"/> to convert <see cref="HasDialog"/> to opacity of control.
		/// </summary>
		public static readonly IValueConverter HasDialogToControlOpacityConverter = new BooleanToDoubleConverter(0.3, 1.0);


		// Constants.
		const int SaveWindowSizeDelay = 300;
		

		// Static fields.
		static readonly ILogger Logger = LogManager.GetCurrentClassLogger();


		// Fields.
		readonly List<Dialog> dialogs = new List<Dialog>();
		bool isClosed;
		bool isConstructing = true;
		readonly TabControl mainTabControl;
		readonly IList mainTabItems;
		readonly ScheduledOperation saveWindowSizeOperation;
		Workspace? workspace;


		/// <summary>
		/// Initialize new <see cref="MainWindow"/> instance.
		/// </summary>
		public MainWindow()
		{
			// create commands
			this.CloseMainTabItemCommand = ReactiveCommand.Create((TabItem tabItem) => this.CloseMainTabItem(tabItem));

			// create scheduled operations
			this.saveWindowSizeOperation = new ScheduledOperation(() =>
			{
				if (this.WindowState == WindowState.Normal)
				{
					this.Settings.MainWindowWidth = (int)(this.Width + 0.5);
					this.Settings.MainWindowHeight = (int)(this.Height + 0.5);
				}
			});

			// initialize Avalonia resources
			InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

			// setup window state and size
			this.WindowState = this.Settings.MainWindowState;
			var windowWidth = this.Settings.MainWindowWidth;
			var windowHeight = this.Settings.MainWindowHeight;
			if (windowWidth > 0 && windowHeight > 0)
			{
				this.Width = windowWidth;
				this.Height = windowHeight;
			}

			// setup main tab control
			this.mainTabControl = this.FindControl<TabControl>("tabControl").EnsureNonNull().Also((it) =>
			{
				it.SelectionChanged += (s, e) => this.OnMainTabControlSelectionChanged();
			});
			this.mainTabItems = (IList)this.mainTabControl.Items;

			// update state
			this.isConstructing = false;
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

			// create tab item
			var tabItem = new TabItem()
			{
				Content = sessionControl,
				DataContext = session,
				Header = header,
			};
			return tabItem;
		}


		// Close given tab item.
		void CloseMainTabItem(TabItem tabItem)
		{
			// check session
			if (tabItem.DataContext is not Session session)
				return;

			// close session
			this.workspace?.CloseSession(session);
		}


		/// <summary>
		/// Command for closing given tab item.
		/// </summary>
		public ICommand CloseMainTabItemCommand { get; }


		// Detach tab item from session.
		void DetachTabItemFromSession(TabItem tabItem)
		{
			(tabItem.Header as IControl)?.Let((it) => it.DataContext = null);
			(tabItem.Content as IControl)?.Let((it) => it.DataContext = null);
			tabItem.DataContext = null;
		}


		// Find index of main tab item contains dragging point.
		int FindMainTabItemIndex(DragEventArgs e)
		{
			for (var i = this.mainTabItems.Count - 1; i > 0; --i)
			{
				if (!((this.mainTabItems[i] as TabItem)?.Header is IVisual headerVisual))
					continue;
				if (e.IsContainedBy(headerVisual))
					return i;
			}
			return -1;
		}


		// Find index of main tab item attached to given session.
		int FindMainTabItemIndex(Session session)
		{
			for (var i = this.mainTabItems.Count - 1; i > 0; --i)
			{
				if ((this.mainTabItems[i] as TabItem)?.DataContext == session)
					return i;
			}
			return -1;
		}


		/// <summary>
		/// Check whether one or more dialog hasn been show or not.
		/// </summary>
		public bool HasDialog { get => this.GetValue<bool>(HasDialogProperty); }


		// Initialize Avalonia component.
		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}


		// Called when window closed.
		protected override void OnClosed(EventArgs e)
		{
			// update state
			this.isClosed = true;

			// disable drag-drop
			this.RemoveHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.RemoveHandler(DragDrop.DropEvent, this.OnDrop);

			// call super
			base.OnClosed(e);
		}


		/// <summary>
		/// Called when dialog owned by this window is closing.
		/// </summary>
		/// <param name="dialog">Closed dialog.</param>
		public void OnDialogClosing(Dialog dialog)
		{
			if (!this.dialogs.Remove(dialog) || this.dialogs.IsNotEmpty())
				return;
			this.SetValue<bool>(HasDialogProperty, false);
		}


		/// <summary>
		/// Called when dialog owned by this window has been opened.
		/// </summary>
		/// <param name="dialog">Opened dialog.</param>
		public void OnDialogOpened(Dialog dialog)
		{
			this.dialogs.Add(dialog);
			if (this.dialogs.Count == 1)
				this.SetValue<bool>(HasDialogProperty, true);
		}


		// Called when drag over.
		void OnDragOver(object? sender, DragEventArgs e)
		{
			if (e.Handled)
				return;
			if (!e.Data.HasSingleFileName())
			{
				e.DragEffects = DragDropEffects.None;
				return;
			}
			int mainTabIndex = this.FindMainTabItemIndex(e);
			if (mainTabIndex < 0)
			{
				e.DragEffects = DragDropEffects.None;
				return;
			}
			if (mainTabIndex < this.mainTabItems.Count - 1)
				this.mainTabControl.SelectedIndex = mainTabIndex;
			e.DragEffects = DragDropEffects.Copy;
		}


		// Called when drop.
		void OnDrop(object? sender, DragEventArgs e)
		{
			if (e.Handled)
				return;
			e.Data.GetSingleFileName()?.Let((fileName) =>
			{
				// find tab
				int mainTabIndex = this.FindMainTabItemIndex(e);
				if (mainTabIndex < 0)
					return;

				// find session
				Session? session = null;
				if (mainTabIndex < this.mainTabItems.Count - 1)
					session = (this.mainTabItems[mainTabIndex] as TabItem)?.DataContext as Session;
				else
					session = this.workspace?.CreateSession();
				if (session == null)
					return;

				// open source file
				session.OpenSourceFileCommand?.Let((command) =>
				{
					if (!command.CanExecute(fileName))
					{
						Logger.Error($"Cannot open source '{fileName}' by drag-drop in current state");
						return;
					}
					Logger.Info($"Open source '{fileName}' by drag-drop");
					command.Execute(fileName);
				});
			});
		}


		// Called when selection of main tab control changed.
		void OnMainTabControlSelectionChanged()
		{
			if (this.mainTabControl.SelectedIndex >= this.mainTabItems.Count - 1 && !this.isClosed)
				this.workspace?.CreateSession();
			else
			{
				// update activated session
				var selectedSession = (this.mainTabControl.SelectedItem as IControl)?.DataContext as Session;
				this.workspace?.Let((workspace) =>
				{
					for (var i = workspace.ActivatedSessions.Count - 1; i >= 0; --i)
					{
						if (workspace.ActivatedSessions[i] != selectedSession)
							workspace.DeactivateSession(workspace.ActivatedSessions[i]);
					}
					if (selectedSession != null)
						workspace.ActivateSession(selectedSession);
				});

				// focus on content later to make sure that view has been attached to visual tree
				SynchronizationContext.Current?.Post(() =>
				{
					((this.mainTabControl.SelectedItem as TabItem)?.Content as IInputElement)?.Focus();
				});
			}
		}


		// Called when opened.
		protected override void OnOpened(EventArgs e)
		{
			// call base
			base.OnOpened(e);

			// enable drag-drop
			this.AddHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.AddHandler(DragDrop.DropEvent, this.OnDrop);
		}


		// Called when property changed.
		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
		{
			base.OnPropertyChanged(change);
			if (this.isConstructing)
				return;
			var property = change.Property;
			if (property == Window.DataContextProperty)
			{
				if (change.OldValue.Value is Workspace prevWorkspace && prevWorkspace == this.workspace)
				{
					// clear tab items
					this.mainTabControl.SelectedIndex = 0;
					for (var i = this.mainTabItems.Count - 1; i > 0; --i)
					{
						if (this.mainTabItems[i] is not TabItem tabItem)
							continue;
						this.DetachTabItemFromSession(tabItem);
						this.mainTabItems.RemoveAt(i);
					}

					// detach from workspace
					(prevWorkspace.Sessions as INotifyCollectionChanged)?.Let((it) => it.CollectionChanged -= this.OnSessionsChanged);
				}
				if (change.NewValue.Value is Workspace newWorkspace)
				{
					// keep reference
					this.workspace = newWorkspace;

					// create tab items
					foreach (var session in newWorkspace.Sessions)
					{
						var tabItem = this.AttachTabItemToSession(session);
						this.mainTabItems.Insert(this.mainTabItems.Count - 1, tabItem);
					}

					// attach to workspace
					(workspace.Sessions as INotifyCollectionChanged)?.Let((it) => it.CollectionChanged += this.OnSessionsChanged);

					// make sure that at most 1 session has been activated
					for (var i = newWorkspace.ActivatedSessions.Count - 1; i > 1; --i)
						newWorkspace.DeactivateSession(newWorkspace.ActivatedSessions[i]);

					// select tab item according to activated session
					if (newWorkspace.ActivatedSessions.IsNotEmpty())
					{
						var tabIndex = this.FindMainTabItemIndex(newWorkspace.ActivatedSessions[0]);
						if (tabIndex > 0)
							this.mainTabControl.SelectedIndex = tabIndex;
						else
							this.mainTabControl.SelectedIndex = 0;
					}
					else
						this.mainTabControl.SelectedIndex = 0;
				}
				else
				{
					this.workspace = null;
					this.mainTabControl.SelectedIndex = 0;
				}
			}
			if (property == Window.HeightProperty || property == Window.WidthProperty)
				this.saveWindowSizeOperation.Schedule(SaveWindowSizeDelay);
			else if (property == Window.WindowStateProperty)
			{
				var state = this.WindowState;
				if (state != WindowState.Minimized)
					this.Settings.MainWindowState = state;
			}
		}


		// Called when collection of sessions has been changed.
		void OnSessionsChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					{
						var tabIndex = e.NewStartingIndex + 1;
						foreach (Session? session in e.NewItems)
						{
							if (session == null)
								continue;
							var tabItem = this.AttachTabItemToSession(session);
							this.mainTabItems.Insert(tabIndex, tabItem);
							this.mainTabControl.SelectedIndex = tabIndex++;
						}
						break;
					}
				case NotifyCollectionChangedAction.Remove:
					{
						foreach (Session? session in e.OldItems)
						{
							if (session == null)
								continue;
							var tabIndex = this.FindMainTabItemIndex(session);
							if (tabIndex < 0 || this.mainTabItems[tabIndex] is not TabItem tabItem)
								continue;
							if (tabIndex > 1)
								this.mainTabControl.SelectedIndex = (tabIndex - 1);
							else if (tabIndex < this.mainTabItems.Count - 2)
								this.mainTabControl.SelectedIndex = (tabIndex + 1);
							else
								this.mainTabControl.SelectedIndex = 0;
							this.mainTabItems.RemoveAt(tabIndex);
						}
						this.workspace?.Let((it) =>
						{
							if (it.Sessions.IsEmpty() && !this.isClosed)
								it.CreateSession();
						});
						break;
					}
			}
		}


		/// <summary>
		/// Application settings.
		/// </summary>
		public Settings Settings { get; } = App.Current.Settings;
	}
}
