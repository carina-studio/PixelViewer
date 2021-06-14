using Carina.PixelViewer.Input;
using Carina.PixelViewer.Threading;
using CarinaStudio;
using CarinaStudio.Threading;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// Workspace.
	/// </summary>
	class Workspace : BaseViewModel
	{
		// Fields.
		readonly ObservableCollection<Session> activatedSessions = new ObservableCollection<Session>();
		readonly MutableObservableBoolean isAppUpdateAvailable = new MutableObservableBoolean();
		readonly ReadOnlyObservableCollection<Session> readOnlyActivatedSessions;
		readonly ReadOnlyObservableCollection<Session> readOnlySessions;
		readonly ObservableCollection<Session> sessions = new ObservableCollection<Session>();
		readonly ScheduledAction updateTitleOperation;
		readonly MutableObservableString title = new MutableObservableString();


		/// <summary>
		/// Initialize new <see cref="Workspace"/> instance.
		/// </summary>
		public Workspace()
		{
			// create commands
			this.IgnoreAppUpdateCommand = ReactiveCommand.Create(this.IgnoreAppUpdate, this.isAppUpdateAvailable);
			this.UpdateAppCommand = ReactiveCommand.Create(this.UpdateApp, this.isAppUpdateAvailable);

			// create read-only collections
			this.readOnlyActivatedSessions = new ReadOnlyObservableCollection<Session>(this.activatedSessions);
			this.readOnlySessions = new ReadOnlyObservableCollection<Session>(this.sessions);

			// create scheduled operations
			this.updateTitleOperation = new ScheduledAction(this.UpdateTitle);

			// observe property values
			this.ObservePropertyValue(this.isAppUpdateAvailable, nameof(this.IsAppUpdateAvailable));
			this.ObservePropertyValue(this.title, nameof(this.Title));

			// attach to App
			App.Current.Let((app) =>
			{
				app.UpdateInfoChanged += this.OnAppUpdateInfoChanged;
				this.OnAppUpdateInfoChanged(app, EventArgs.Empty);
			});

			// setup initial title
			this.UpdateTitle();
		}


		/// <summary>
		/// Get all activated <see cref="Session"/>s.
		/// </summary>
		public ReadOnlyObservableCollection<Session> ActivatedSessions { get => this.readOnlyActivatedSessions; }


		/// <summary>
		/// Activate given session.
		/// </summary>
		/// <param name="session">Session to activate.</param>
		public void ActivateSession(Session session)
		{
			// check state
			this.VerifyAccess();
			this.ThrowIfDisposed();
			if (!this.sessions.Contains(session))
				throw new ArgumentException($"Invalid session: {session}.");
			if (this.activatedSessions.Contains(session))
				return;

			// activate
			this.activatedSessions.Add(session);
			this.Logger.Debug($"Activate session {session}, count: {this.activatedSessions.Count}");
			this.updateTitleOperation.Schedule();
		}


		/// <summary>
		/// Get <see cref="AppOptions"/> view-model.
		/// </summary>
		public AppOptions AppOptions { get; } = new AppOptions();


		/// <summary>
		/// Close given session.
		/// </summary>
		/// <param name="session">Session to close.</param>
		public void CloseSession(Session session)
		{
			// check state
			this.VerifyAccess();
			this.ThrowIfDisposed();
			if (!this.sessions.Contains(session))
				throw new ArgumentException($"Unknown session {session} to close.");

			// deactivate
			this.DeactivateSession(session);

			// close session
			session.PropertyChanged -= this.OnSessionPropertyChanged;
			this.sessions.Remove(session);
			this.Logger.Debug($"Close session {session}, count: {this.sessions.Count}");

			// dispose session
			session.Dispose();
		}


		/// <summary>
		/// Create new session.
		/// </summary>
		/// <param name="fileName">Name of source image file.</param>
		/// <returns>Created <see cref="Session"/>.</returns>
		public Session CreateSession(string? fileName = null)
		{
			// check state
			this.VerifyAccess();
			this.ThrowIfDisposed();

			// create session
			var session = new Session();
			session.PropertyChanged += this.OnSessionPropertyChanged;
			this.sessions.Add(session);
			this.Logger.Debug($"Create session {session}, count: {this.sessions.Count}");

			// open file
			if (fileName != null)
			{
				this.Logger.Debug($"Open '{fileName}' after creating {session}");
				if (!session.OpenSourceFileCommand.TryExecute(fileName))
					this.Logger.Error($"Unable to open '{fileName}' after creating {session}");
			}

			// complete
			return session;
		}


		/// <summary>
		/// Deactivate given session.
		/// </summary>
		/// <param name="session"><see cref="Session"/> to deactivate.</param>
		public void DeactivateSession(Session session)
		{
			this.VerifyAccess();
			if (!this.activatedSessions.Remove(session))
				return;
			this.Logger.Debug($"Deactivate session {session}, count: {this.activatedSessions.Count}");
			this.updateTitleOperation.Schedule();
		}


		// Dispose.
		protected override void Dispose(bool disposing)
		{
			// close all sessions
			this.activatedSessions.Clear();
			foreach (var session in this.sessions)
			{
				this.Logger.Warn($"Close session {session} when disposing workspace");
				session.PropertyChanged -= this.OnSessionPropertyChanged;
				session.Dispose();
			}
			this.sessions.Clear();

			// dispose app options
			this.AppOptions.Dispose();

			// detach from App
			App.Current.UpdateInfoChanged -= this.OnAppUpdateInfoChanged;

			// call base
			base.Dispose(disposing);
		}


		// Ignore application update.
		void IgnoreAppUpdate()
		{
			if (!this.isAppUpdateAvailable.Value)
				return;
			this.Logger.Warn("Ignore application update");
			this.isAppUpdateAvailable.Update(false);
		}


		/// <summary>
		/// Command to ignore application update.
		/// </summary>
		public ICommand IgnoreAppUpdateCommand { get; }


		/// <summary>
		/// Check whether application update is available or not.
		/// </summary>
		public bool IsAppUpdateAvailable { get => this.isAppUpdateAvailable.Value; }


		// Called when app update info changed.
		void OnAppUpdateInfoChanged(object? sender, EventArgs e)
		{
			this.isAppUpdateAvailable.Update(App.Current.UpdateInfo != null);
		}


		// Called when property of session changed.
		void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (sender is not Session session)
				return;
			switch (e.PropertyName)
			{
				case nameof(Session.Title):
					{
						if (this.activatedSessions.Contains(session))
							this.updateTitleOperation.Schedule();
						break;
					}
			}
		}


		/// <summary>
		/// Get all <see cref="Session"/>s.
		/// </summary>
		public ReadOnlyObservableCollection<Session> Sessions { get => this.readOnlySessions; }


		// Update application.
		void UpdateApp()
		{
			// check state
			if (!this.isAppUpdateAvailable.Value)
				return;
			var updateInfo = App.Current.UpdateInfo;
			if (updateInfo == null)
			{
				this.Logger.Error("No application update info");
				return;
			}

			// update state
			this.isAppUpdateAvailable.Update(false);

			// open download link
			this.Logger.Info("Open application update page");
			try
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					Process.Start(new ProcessStartInfo("cmd", $"/c start {updateInfo.ReleasePageUri}")
					{
						CreateNoWindow = true
					});
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
					Process.Start("xdg-open", updateInfo.ReleasePageUri.ToString());
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
					Process.Start("open", updateInfo.ReleasePageUri.ToString());
			}
			catch (Exception ex)
			{
				this.Logger.Error(ex, $"Unable to open '{updateInfo.ReleasePageUri}' to update application");
			}
		}


		/// <summary>
		/// Command to update application.
		/// </summary>
		public ICommand UpdateAppCommand { get; }


		// Update title.
		void UpdateTitle()
		{
			if (this.activatedSessions.Count == 1)
				this.title.Update($"PixelViewer - {this.activatedSessions[0].Title}");
			else
				this.title.Update("PixelViewer");
		}


		/// <summary>
		/// Get title of workspace.
		/// </summary>
		public string? Title { get => this.title.Value; }
	}
}
