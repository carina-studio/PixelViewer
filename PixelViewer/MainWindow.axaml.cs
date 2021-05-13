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


		/// <summary>
		/// Initialize new <see cref="MainWindow"/> instance.
		/// </summary>
		public MainWindow()
		{
			// create commands
			this.CloseSessionCommand = ReactiveCommand.Create((TabItem tabItem) => this.CloseSession(tabItem));
			this.NewSessionCommand = ReactiveCommand.Create(this.NewSession);

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

			// create app options view-model
			(this.mainTabItems[0] as TabItem)?.Let((tabItem) =>
			{
				var appOptions = new AppOptions();
				tabItem.DataContext = appOptions;
				(tabItem.Content as IControl)?.Let((it) =>
				{
					it.DataContext = appOptions;
				});
			});

			// create first session
			this.NewSession();

			// update state
			this.isConstructing = false;
		}


		// Close given session.
		void CloseSession(TabItem tabItem)
		{
			// check session
			if (!(tabItem.DataContext is Session session))
				return;

			// remove tab
			var index = this.mainTabItems.IndexOf(tabItem);
			if (index >= 0)
			{
				if (this.mainTabItems.Count == 3 && !this.isClosed)
					this.NewSession();
				else if (this.mainTabControl.SelectedIndex == index)
				{
					if (index > 1)
						this.mainTabControl.SelectedIndex = index - 1;
					else
						this.mainTabControl.SelectedIndex = index + 1;
				}
				this.mainTabItems.RemoveAt(index);
			}

			// remove data context
			tabItem.DataContext = null;
			(tabItem.Content as IControl)?.Let((it) =>
			{
				it.DataContext = null;
			});

			// dispose session
			session.Dispose();
		}


		/// <summary>
		/// Command for closing given session.
		/// </summary>
		public ICommand CloseSessionCommand { get; }


		// Find index of main tab contains dragging point.
		int FindMainTabIndex(DragEventArgs e)
		{
			for (int i = this.mainTabItems.Count - 1; i > 0; --i)
			{
				if (!((this.mainTabItems[i] as TabItem)?.Header is IVisual headerVisual))
					continue;
				if (e.IsContainedBy(headerVisual))
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


		// Create new session.
		Session NewSession()
		{
			// create session
			var session = new Session();

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
			var index = this.mainTabItems.Count - 1;
			this.mainTabItems.Insert(index, tabItem);
			this.mainTabControl.SelectedIndex = index;

			// complete
			return session;
		}


		/// <summary>
		/// Command for creating new session.
		/// </summary>
		public ICommand NewSessionCommand { get; }


		// Called when window closed.
		protected override void OnClosed(EventArgs e)
		{
			// update state
			this.isClosed = true;

			// close all sessions
			foreach (var item in new object[this.mainTabItems.Count].Also((it) => this.mainTabItems.CopyTo(it, 0)))
			{
				if (item is TabItem tabItem)
				{
					if (tabItem.DataContext is AppOptions appOptions)
						appOptions.Dispose();
					else
						this.CloseSession(tabItem);
				}
			}

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
			int mainTabIndex = this.FindMainTabIndex(e);
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
				int mainTabIndex = this.FindMainTabIndex(e);
				if (mainTabIndex < 0)
					return;

				// find session
				Session? session = null;
				if (mainTabIndex < this.mainTabItems.Count - 1)
					session = (this.mainTabItems[mainTabIndex] as TabItem)?.DataContext as Session;
				else
					session = this.NewSession();
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
				this.NewSession();
			else
			{
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
			if (property == Window.HeightProperty || property == Window.WidthProperty)
				this.saveWindowSizeOperation.Schedule(SaveWindowSizeDelay);
			else if (property == Window.WindowStateProperty)
			{
				var state = this.WindowState;
				if (state != WindowState.Minimized)
					this.Settings.MainWindowState = state;
			}
		}


		/// <summary>
		/// Application settings.
		/// </summary>
		public Settings Settings { get; } = App.Current.Settings;
	}
}
