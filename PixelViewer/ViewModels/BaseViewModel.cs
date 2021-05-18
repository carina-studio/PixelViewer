using Carina.PixelViewer.Threading;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// Base class for view-model.
	/// </summary>
	abstract class BaseViewModel : IDisposable, INotifyPropertyChanged, IThreadDependent
	{
		// Observer for property value.
		class PropertyValueObserver<T> : IObserver<T>
		{
			// Fields.
			readonly BaseViewModel owner;
			readonly string propertyName;

			// Constructor.
			public PropertyValueObserver(BaseViewModel owner, string propertyName)
			{
				this.owner = owner;
				this.propertyName = propertyName;
			}

			// Implementations.
			public void OnCompleted()
			{ }
			public void OnError(Exception error)
			{ }
			public void OnNext(T value) => this.owner.OnPropertyChanged(this.propertyName);
		}


		// Static fields.
		static volatile int nextIdIndex = 1;


		// Fields.
		readonly App app = App.Current;
		bool isDisposed;
		readonly List<IDisposable> subscribedPropertyValueObservers = new List<IDisposable>();
		readonly Thread syncThread = Thread.CurrentThread;


		/// <summary>
		/// Initialize new <see cref="BaseViewModel"/> instance.
		/// </summary>
		protected BaseViewModel()
		{
			lock (typeof(BaseViewModel))
			{
				this.Id = $"{this.GetType().Name}({nextIdIndex++:x4})";
			}
			this.Logger = LogManager.GetLogger(this.Id);
			this.Settings.PropertyChanged += this.OnSettingsChanged;
		}


		// Finalizer.
		~BaseViewModel() => this.Dispose(false);


		// Check thread.
		public bool CheckAccess() => this.syncThread == Thread.CurrentThread;


		// Dispose.
		public void Dispose()
		{
			this.VerifyAccess();
			if (this.isDisposed)
				return;
			this.isDisposed = true;
			GC.SuppressFinalize(this);
			this.Dispose(true);
		}


		/// <summary>
		/// Dispose the instance.
		/// </summary>
		/// <param name="disposing">True to dispose managed resources.</param>
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				foreach (var disposable in this.subscribedPropertyValueObservers)
					disposable.Dispose();
				this.subscribedPropertyValueObservers.Clear();
			}
			this.Settings.PropertyChanged -= this.OnSettingsChanged;
		}


		/// <summary>
		/// Get string for current locale.
		/// </summary>
		/// <param name="key">Key of string.</param>
		/// <param name="defaultValue">Default value.</param>
		/// <returns>String or default value.</returns>
		protected string? GetString(string key, string? defaultValue = null) => this.app.GetString(key, defaultValue);


		/// <summary>
		/// Get non-null string for current locale.
		/// </summary>
		/// <param name="key">Key of string.</param>
		/// <param name="defaultValue">Default value.</param>
		/// <returns>String or default value.</returns>
		protected string GetStringNonNull(string key, string defaultValue = "") => this.app.GetStringNonNull(key, defaultValue);


		/// <summary>
		/// Get unique ID of the instance.
		/// </summary>
		public string Id { get; }


		/// <summary>
		/// Check whether instance has been disposed or not.
		/// </summary>
		protected bool IsDisposed { get => this.isDisposed; }


		/// <summary>
		/// Logger.
		/// </summary>
		protected ILogger Logger { get; }


		/// <summary>
		/// Observe the internal value of property, and raise <see cref="PropertyChanged"/> when value changed.
		/// </summary>
		/// <typeparam name="T">Type of value.</typeparam>
		/// <param name="value">Internal property value.</param>
		/// <param name="propertyName">Name of property.</param>
		protected void ObservePropertyValue<T>(IObservable<T> value, string propertyName) => value.Subscribe(new PropertyValueObserver<T>(this, propertyName)).Let((it) =>
		{
			this.subscribedPropertyValueObservers.Add(it);
		});


		/// <summary>
		/// Raise <see cref="PropertyChanged"/> event.
		/// </summary>
		/// <param name="propertyName">Name of changed property.</param>
		protected void OnPropertyChanged(string propertyName)
		{
			this.VerifyAccess();
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}


		/// <summary>
		/// Called when property of <see cref="Settings"/> has been changed.
		/// </summary>
		/// <param name="propertyName">Name of changed property.</param>
		protected virtual void OnSettingsChanged(string propertyName)
		{ }
		void OnSettingsChanged(object sender, PropertyChangedEventArgs e) => this.OnSettingsChanged(e.PropertyName);


		// Raised when property changed.
		public event PropertyChangedEventHandler? PropertyChanged;


		/// <summary>
		/// Get application settings.
		/// </summary>
		protected Settings Settings { get; } = Settings.Default;


		// Synchronization context.
		public SynchronizationContext SynchronizationContext { get; } = SynchronizationContext.Current ?? throw new Exception("No synchronization context on current thread.");


		/// <summary>
		/// Throw <see cref="ObjectDisposedException"/> if instance has been disposed.
		/// </summary>
		protected void ThrowIfDisposed()
		{
			if (this.isDisposed)
				throw new ObjectDisposedException(this.GetType().Name);
		}


		// Get readable string.
		public override string ToString() => this.Id;
	}
}
