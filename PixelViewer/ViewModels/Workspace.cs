﻿using Carina.PixelViewer.Input;
using Carina.PixelViewer.Threading;
using CarinaStudio;
using CarinaStudio.AppSuite.ViewModels;
using CarinaStudio.Collections;
using CarinaStudio.Threading;
using CarinaStudio.ViewModels;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// Workspace.
	/// </summary>
	class Workspace : MainWindowViewModel
	{
		/// <summary>
		/// Property of <see cref="ActivatedSession"/>.
		/// </summary>
		public static readonly ObservableProperty<Session?> ActivatedSessionProperty = ObservableProperty.Register<Workspace, Session?>(nameof(ActivatedSession));


		// Fields.
		IDisposable? sessionActivationToken;
		readonly ObservableList<Session> sessions = new ObservableList<Session>();


		/// <summary>
		/// Initialize new <see cref="Workspace"/> instance.
		/// </summary>
		/// <param name="savedState">Saved state in JSON format.</param>
		public Workspace(JsonElement? savedState)
		{
			// setup properties
			this.Sessions = this.sessions.AsReadOnly();

			// restore state
			savedState?.Let(savedState =>
			{
				// check saved state
				if (savedState.ValueKind != JsonValueKind.Object)
					return;

				this.Logger.LogWarning("Start restoring state");

				// restore sessions
				if (savedState.TryGetProperty(nameof(Sessions), out var jsonProperty) && jsonProperty.ValueKind == JsonValueKind.Array)
				{
					foreach (var jsonValue in jsonProperty.EnumerateArray())
						this.sessions.Add(new Session(this, jsonValue));
				}

				// restore activated session
				if (savedState.TryGetProperty(nameof(ActivatedSession), out jsonProperty)
					&& jsonProperty.TryGetInt32(out var intValue)
					&& intValue >= 0
					&& intValue < this.sessions.Count)
				{
					this.SetValue(ActivatedSessionProperty, this.sessions[intValue]);
				}

				this.Logger.LogWarning($"State restored, session count: {this.sessions.Count}");
			});
		}


		/// <summary>
		/// Get or set activated session.
		/// </summary>
		public Session? ActivatedSession
		{
			get => this.GetValue(ActivatedSessionProperty);
			set => this.SetValue(ActivatedSessionProperty, value);
		}


		/// <summary>
		/// Close given session.
		/// </summary>
		/// <param name="session">Session to close.</param>
		public async void CloseSession(Session session)
		{
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();
			if (!this.sessions.Contains(session))
				throw new ArgumentException($"Unknown session {session} to close.");

			// deactivate
			if (this.ActivatedSession == session)
				this.ActivatedSession = null;

			// detach
			session.PropertyChanged -= this.OnSessionPropertyChanged;
			this.sessions.Remove(session);
			this.Logger.LogDebug($"Close session {session}, count: {this.sessions.Count}");

			// wait for completion
			await this.WaitForNecessaryTaskAsync(session.WaitForNecessaryTasksAsync());

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
			this.VerifyDisposed();

			// create session
			var session = new Session(this, null);
			session.PropertyChanged += this.OnSessionPropertyChanged;
			this.sessions.Add(session);
			this.Logger.LogDebug($"Create session {session}, count: {this.sessions.Count}");

			// open file
			if (fileName != null)
			{
				this.Logger.LogDebug($"Open '{fileName}' after creating {session}");
				if (!session.OpenSourceFileCommand.TryExecute(fileName))
					this.Logger.LogError($"Unable to open '{fileName}' after creating {session}");
			}

			// complete
			return session;
		}


		// Dispose.
		protected override void Dispose(bool disposing)
		{
			// close all sessions
			foreach (var session in this.sessions)
			{
				this.Logger.LogWarning($"Close session {session} when disposing workspace");
				session.PropertyChanged -= this.OnSessionPropertyChanged;
				session.Dispose();
			}
			this.sessions.Clear();

			// call base
			base.Dispose(disposing);
		}


		// Property changed.
        protected override void OnPropertyChanged(ObservableProperty property, object? oldValue, object? newValue)
        {
            base.OnPropertyChanged(property, oldValue, newValue);
			if (property == ActivatedSessionProperty)
			{
				// deactivate
				this.sessionActivationToken = this.sessionActivationToken.DisposeAndReturnNull();

				// check value
				var newSession = (newValue as Session);
				if (newSession != null && !this.sessions.Contains(newSession))
				{
					this.Logger.LogError($"Invalid session: {newSession}");
					this.SynchronizationContext.Post(() => this.ActivatedSession = null);
					return;
				}

				// activate
				if (newSession != null)
				{
					this.Logger.LogDebug($"Activate session {newSession}");
					this.sessionActivationToken = newSession.Activate();
				}
				this.InvalidateTitle();
			}
        }


        // Called when property of session changed.
        void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (sender is not Session session)
				return;
			switch (e.PropertyName)
			{
				case nameof(Session.Title):
					if (session == this.ActivatedSession)
						this.InvalidateTitle();
					break;
			}
		}


		// Update title.
        protected override string? OnUpdateTitle() => this.ActivatedSession?.Let(it =>
		{
			return $"PixelViewer - {it.Title}";
		}) ?? "PixelViewer";


		/// <inheritdoc/>
        public override void SaveState(Utf8JsonWriter writer)
        {
			// start object
			writer.WriteStartObject();

			// save sessions
			writer.WritePropertyName(nameof(Sessions));
			writer.WriteStartArray();
			foreach (var session in this.sessions)
				session.SaveState(writer);
			writer.WriteEndArray();

			// save activated session
			var index = this.ActivatedSession != null ? this.sessions.IndexOf(this.ActivatedSession) : -1;
			if (index >= 0)
				writer.WriteNumber(nameof(ActivatedSession), index);

			// complete
			writer.WriteEndObject();
		}


        /// <summary>
        /// Get all <see cref="Session"/>s.
        /// </summary>
        public IList<Session> Sessions { get; }
	}
}
