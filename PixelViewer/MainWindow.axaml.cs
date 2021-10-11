using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Carina.PixelViewer.Controls;
using Carina.PixelViewer.Input;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Windows.Input;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Main window of PixelViewer.
	/// </summary>
	class MainWindow : CarinaStudio.AppSuite.Controls.MainWindow<Workspace>
	{
		// Fields.
		readonly TabControl mainTabControl;
		readonly IList mainTabItems;


		/// <summary>
		/// Initialize new <see cref="MainWindow"/> instance.
		/// </summary>
		public MainWindow()
		{
			// create commands
			this.CloseMainTabItemCommand = new Command<TabItem>(this.CloseMainTabItem);

			// initialize Avalonia resources
			InitializeComponent();

			// setup main tab control
			this.mainTabControl = this.FindControl<TabControl>("tabControl").AsNonNull().Also((it) =>
			{
				it.SelectionChanged += (s, e) => this.OnMainTabControlSelectionChanged();
			});
			this.mainTabItems = (IList)this.mainTabControl.Items;
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
			(this.DataContext as Workspace)?.CloseSession(session);
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


		// Initialize Avalonia component.
		private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


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
				this.mainTabControl.SelectedIndex = 0;
		}


        // Called when window closed.
        protected override void OnClosed(EventArgs e)
		{
			// disable drag-drop
			this.RemoveHandler(DragDrop.DragEnterEvent, this.OnDragEnter);
			this.RemoveHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.RemoveHandler(DragDrop.DropEvent, this.OnDrop);

			// call super
			base.OnClosed(e);
		}


		// Detach from view-model.
        protected override void OnDetachFromViewModel(Workspace workspace)
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
			(workspace.Sessions as INotifyCollectionChanged)?.Let((it) => it.CollectionChanged -= this.OnSessionsChanged);

			// call base
			base.OnDetachFromViewModel(workspace);
        }


        // Called when drag enter.
        void OnDragEnter(object? sender, DragEventArgs e)
		{
			this.ActivateAndBringToFront();
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

				// find session and open file
				if (mainTabIndex < this.mainTabItems.Count - 1)
				{
					((this.mainTabItems[mainTabIndex] as TabItem)?.DataContext as Session)?.Let((session) =>
					{
						this.Logger.LogDebug($"Open source '{fileName}' by drag-drop to {session}");
						if (!session.OpenSourceFileCommand.TryExecute(fileName))
							this.Logger.LogError($"Cannot open source '{fileName}' by drag-drop to {session}");
					});
				}
				else
					(this.DataContext as Workspace)?.CreateSession(fileName);
			});
		}


		// Called when selection of main tab control changed.
		void OnMainTabControlSelectionChanged()
		{
			if (this.mainTabControl.SelectedIndex >= this.mainTabItems.Count - 1 && !this.IsClosed)
				(this.DataContext as Workspace)?.CreateSession();
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


		// Called when opened.
		protected override void OnOpened(EventArgs e)
		{
			// call base
			base.OnOpened(e);

			// enable drag-drop
			this.AddHandler(DragDrop.DragEnterEvent, this.OnDragEnter);
			this.AddHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.AddHandler(DragDrop.DropEvent, this.OnDrop);
		}


		// Called when collection of sessions has been changed.
		void OnSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					{
						var tabIndex = e.NewStartingIndex + 1;
						foreach (Session? session in e.NewItems.AsNonNull())
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
						foreach (Session? session in e.OldItems.AsNonNull())
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
						(this.DataContext as Workspace)?.Let((it) =>
						{
							if (it.Sessions.IsEmpty() && !this.IsClosed)
								it.CreateSession();
						});
						break;
					}
			}
		}


		// Property of view-model changed.
        protected override void OnViewModelPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnViewModelPropertyChanged(e);
			if (this.DataContext is not Workspace workspace)
				return;
			if (e.PropertyName == nameof(Workspace.ActivatedSession))
			{
				var tabIndex = workspace.ActivatedSession != null ? this.FindMainTabItemIndex(workspace.ActivatedSession) : -1;
				if (tabIndex < 0)
					this.mainTabControl.SelectedIndex = 0;
				else if (this.mainTabControl.SelectedIndex != tabIndex)
					this.mainTabControl.SelectedIndex = tabIndex;
			}
        }
    }
}
