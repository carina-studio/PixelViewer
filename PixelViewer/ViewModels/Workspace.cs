using Carina.PixelViewer.Input;
using Carina.PixelViewer.Threading;
using CarinaStudio;
using CarinaStudio.Threading;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// Workspace.
	/// </summary>
	class Workspace : BaseViewModel
	{
		// Fields.
		readonly ObservableCollection<Session> activatedSessions = new ObservableCollection<Session>();
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
			// create read-only collections
			this.readOnlyActivatedSessions = new ReadOnlyObservableCollection<Session>(this.activatedSessions);
			this.readOnlySessions = new ReadOnlyObservableCollection<Session>(this.sessions);

			// create scheduled operations
			this.updateTitleOperation = new ScheduledAction(this.UpdateTitle);

			// observe property values
			this.ObservePropertyValue(this.title, nameof(this.Title));

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

			// call base
			base.Dispose(disposing);
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
