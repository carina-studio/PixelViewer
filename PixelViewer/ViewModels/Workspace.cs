using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.Threading;
using CarinaStudio;
using CarinaStudio.AppSuite.ViewModels;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
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
		/// <summary>
		/// Property of <see cref="EffectiveScreenColorSpace"/>.
		/// </summary>
		public static readonly ObservableProperty<ColorSpace> EffectiveScreenColorSpaceProperty = ObservableProperty.Register<Workspace, ColorSpace>(nameof(EffectiveScreenColorSpace), ColorSpace.Default);
		/// <summary>
		/// Property of <see cref="Window"/>.
		/// </summary>
		public static readonly ObservableProperty<Window?> WindowProperty = ObservableProperty.Register<Workspace, Window?>(nameof(Window), null);


		// Constants.
		const int UpdateEffectiveScreenColorSpaceInterval = 1000;


		// Fields.
		Screen? currentScreen;
		IDisposable? sessionActivationToken;
		readonly ObservableList<Session> sessions = new ObservableList<Session>();
		readonly ScheduledAction updateEffectiveScreenColorSpaceAction;
		readonly Observer<Rect> windowBoundsObserver;
		IDisposable? windowBoundsObserverToken;
		readonly Observer<bool> windowIsActiveObserver;
		IDisposable? windowIsActiveObserverToken;


		/// <summary>
		/// Initialize new <see cref="Workspace"/> instance.
		/// </summary>
		/// <param name="savedState">Saved state in JSON format.</param>
		public Workspace(JsonElement? savedState)
		{
			// setup properties
			this.Sessions = this.sessions.AsReadOnly();

			// setup actions
			this.updateEffectiveScreenColorSpaceAction = new(async () =>
			{
				// check state
				if (this.IsDisposed)
					return;
				var window = this.GetValue(WindowProperty);
				if (window == null || !window.IsActive)
					return;
				var screen = window.Screens.ScreenFromVisual(window);
				if (screen == null || screen.Bounds == this.currentScreen?.Bounds)
					return;
				
				// get screen color space
				this.currentScreen = screen;
				var screenColorSpace = ColorSpace.Default;
				if (!ColorSpace.IsSystemScreenColorSpaceSupported || !this.Settings.GetValueOrDefault(SettingKeys.UseSystemScreenColorSpace))
					ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.ScreenColorSpaceName), out screenColorSpace);
				else
				{
					try
					{
						screenColorSpace = await ColorSpace.GetSystemScreenColorSpaceAsync(window);
					}
					catch (Exception ex)
					{
						this.Logger.LogError(ex, "Unable to get system screen color space, fall-back to color space in settings");
						ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.ScreenColorSpaceName), out screenColorSpace);
					}
				}
				this.Logger.LogDebug($"Screen color space is '{screenColorSpace}'");

				// update state
				if (!this.IsDisposed)
					this.SetValue(EffectiveScreenColorSpaceProperty, screenColorSpace);
			});
			this.windowBoundsObserver = new(_ => 
				this.updateEffectiveScreenColorSpaceAction.Schedule(UpdateEffectiveScreenColorSpaceInterval));
			this.windowIsActiveObserver = new(isActive => 
			{
				if (isActive)
				{
					this.currentScreen = null;
					this.updateEffectiveScreenColorSpaceAction.Reschedule();
				}
			});
			
			// attach to settings
			this.Settings.SettingChanged += this.OnSettingChanged;

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
						this.sessions.Add(new Session(this.Application, jsonValue) { Owner = this });
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
		/// Attach given <see cref="Session"/> to this instance.
		/// </summary>
		/// <param name="index">Index of session to be placed in <see cref="Sessions"/>.</param>
		/// <param name="session"><see cref="Session"/> to attach.</param>
		public void AttachSession(int index, Session session)
        {
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();
			if (session.Owner == this)
				return;

			// check parameter
			if (index < 0 || index > this.sessions.Count)
				throw new ArgumentOutOfRangeException();

			// detach from current workspace
			(session.Owner as Workspace)?.DetachSession(session);

			// attach
			session.Owner = this;
			session.PropertyChanged += this.OnSessionPropertyChanged;
			this.sessions.Insert(index, session);
			this.Logger.LogDebug($"Attach session {session} at {index}, count: {this.sessions.Count}");
		}


		/// <summary>
		/// Create new session.
		/// </summary>
		/// <param name="index">Index of created session to be put in <see cref="Sessions"/>.</param>
		/// <returns>Created <see cref="Session"/>.</returns>
		public Session CreateAndAttachSession(int index)
		{
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();

			// create session
			var session = new Session(this.Application, null);
			this.Logger.LogDebug($"Create session {session}");

			// attach
			this.AttachSession(index, session);

			// complete
			return session;
		}


		/// <summary>
		/// Create new session.
		/// </summary>
		/// <param name="fileName">Name of source image file.</param>
		/// <returns>Created <see cref="Session"/>.</returns>
		public Session CreateAndAttachSession(string? fileName = null) =>
			this.CreateAndAttachSession(this.Sessions.Count, fileName);


		/// <summary>
		/// Create new session.
		/// </summary>
		/// <param name="index">Index of created session to be put in <see cref="Sessions"/>.</param>
		/// <param name="fileName">Name of source image file.</param>
		/// <returns>Created <see cref="Session"/>.</returns>
		public Session CreateAndAttachSession(int index, string? fileName = null)
		{
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();

			// create session
			var session = this.CreateAndAttachSession(index);

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


		/// <summary>
		/// Create new session.
		/// </summary>
		/// <param name="index">Index of created session to be put in <see cref="Sessions"/>.</param>
		/// <param name="fileName">Name of source image file.</param>
		/// <param name="profile">Initial profile.</param>
		/// <returns>Created <see cref="Session"/>.</returns>
		public Session CreateAndAttachSession(int index, string fileName, ImageRenderingProfile profile) => this.CreateAndAttachSession(index, fileName).Also(it =>
		{
			it.Profile = profile;
		});


		/// <summary>
		/// Detach given <see cref="Session"/> and close it.
		/// </summary>
		/// <param name="session">Session to close.</param>
		public async void DetachAndCloseSession(Session session)
		{
			// check state
			this.VerifyAccess();
			this.VerifyDisposed();
			if (session.Owner != this)
				return;

			// detach
			this.DetachSession(session);

			// wait for completion
			await this.WaitForNecessaryTaskAsync(session.WaitForNecessaryTasksAsync());

			// dispose session
			session.Dispose();
		}


		/// <summary>
		/// Detach given <see cref="Session"/> from this instance.
		/// </summary>
		/// <param name="session"><see cref="Session"/> to detach.</param>
		public void DetachSession(Session session)
        {
			// check state
			this.VerifyAccess();
			if (session.Owner != this)
				return;

			// deactivate
			if (this.ActivatedSession == session && !this.IsDisposed)
				this.ActivatedSession = null;

			// detach
			session.PropertyChanged -= this.OnSessionPropertyChanged;
			session.Owner = null;
			this.sessions.Remove(session);
			this.Logger.LogDebug($"Detach session {session}, count: {this.sessions.Count}");
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

			// detach from settings
			this.Settings.SettingChanged -= this.OnSettingChanged;

			// call base
			base.Dispose(disposing);
		}


		/// <summary>
		/// Get effective screen color space.
		/// </summary>
		public ColorSpace EffectiveScreenColorSpace { get => this.GetValue(EffectiveScreenColorSpaceProperty); }


		/// <summary>
		/// Move given session in <see cref="Sessions"/>.
		/// </summary>
		/// <param name="index">Index of session to be moved.</param>
		/// <param name="newIndex">Index of new position in <see cref="Sessions"/> before moving.</param>
		public void MoveSession(int index, int newIndex)
		{
			this.VerifyAccess();
			if (index < 0 || index >= this.sessions.Count)
				throw new ArgumentOutOfRangeException();
			if (newIndex < 0 || newIndex >= this.sessions.Count)
				throw new ArgumentOutOfRangeException();
			if (index == newIndex)
				return;
			this.sessions.Move(index, newIndex);
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
			else if (property == WindowProperty)
			{
				this.windowBoundsObserverToken = this.windowBoundsObserverToken.DisposeAndReturnNull();
				this.windowIsActiveObserverToken = this.windowIsActiveObserverToken.DisposeAndReturnNull();
				(oldValue as Window)?.Let(it =>
					it.PositionChanged -= this.OnWindowPositionChanged);
				(newValue as Window)?.Let(it =>
				{
					this.windowBoundsObserverToken = it.GetObservable(Window.BoundsProperty).Subscribe(this.windowBoundsObserver);
					this.windowIsActiveObserverToken = it.GetObservable(Window.IsActiveProperty).Subscribe(this.windowIsActiveObserver);
					it.PositionChanged += this.OnWindowPositionChanged;
				});
				this.updateEffectiveScreenColorSpaceAction.Reschedule();
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


		// Called when application setting changed.
		void OnSettingChanged(object? sender, SettingChangedEventArgs e)
		{
			if (e.Key == SettingKeys.ScreenColorSpaceName)
			{
				if (!ColorSpace.IsSystemScreenColorSpaceSupported || !this.Settings.GetValueOrDefault(SettingKeys.UseSystemScreenColorSpace))
					this.updateEffectiveScreenColorSpaceAction.Reschedule();
			}
			else if (e.Key == SettingKeys.UseSystemScreenColorSpace)
			{
				if ((bool)e.Value && ColorSpace.IsSystemScreenColorSpaceSupported)
					this.updateEffectiveScreenColorSpaceAction.Reschedule();
			}
		}


		// Update title.
        protected override string? OnUpdateTitle() => this.ActivatedSession?.Let(it =>
		{
			return $"PixelViewer - {it.Title}";
		}) ?? "PixelViewer";


		// Called when window position changed.
		void OnWindowPositionChanged(object? sender, EventArgs e) =>
			this.updateEffectiveScreenColorSpaceAction.Schedule(UpdateEffectiveScreenColorSpaceInterval);


		/// <inheritdoc/>
        public override bool SaveState(Utf8JsonWriter writer)
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
			return true;
		}


        /// <summary>
        /// Get all <see cref="Session"/>s.
        /// </summary>
        public IList<Session> Sessions { get; }


		/// <summary>
		/// Get or set window which contains the workspace.
		/// </summary>
		public Window? Window
		{
			get => this.GetValue(WindowProperty);
			set => this.SetValue(WindowProperty, value);
		}
	}
}
