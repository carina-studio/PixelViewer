using CarinaStudio;
using CarinaStudio.IO;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Source of raw image data.
/// </summary>
interface IImageDataSource : IShareableDisposable<IImageDataSource>, IStreamProvider
{
	/// <summary>
	/// Callbacks to be called when the resource shared by all instances of an <see cref="IImageDataSource"/> is released.
	/// </summary>
	/// <param name="logger">Logger to log errors of callbacks.</param>
	/// <remarks>It is for implementing <see cref="RegisterSourceReleasedCallback"/>. The instance should be owned by the shared resource, and be invoked after the resource has been released.</remarks>
	sealed class SourceReleasedCallbacks(ILogger logger)
	{
		// Registration of callback.
		sealed class Registration(SourceReleasedCallbacks owner, Action callback) : IDisposable
		{
			// Fields.
			public readonly Action Callback = callback;

			/// <inheritdoc/>
			public void Dispose() => owner.Unregister(this);
		}

		// Fields.
		bool isInvoked;
		readonly List<Registration> registrations = [];
		readonly Lock syncLock = new();

		/// <summary>
		/// Invoke and drop all registered callbacks. It takes effect only once.
		/// </summary>
		public void Invoke()
		{
			// take registered callbacks
			Registration[] registrations;
			lock (this.syncLock)
			{
				if (this.isInvoked)
					return;
				this.isInvoked = true;
				registrations = this.registrations.ToArray();
				this.registrations.Clear();
			}

			// invoke callbacks
			foreach (var registration in registrations)
			{
				try
				{
					registration.Callback();
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error occurred while calling callback of releasing source");
				}
			}
		}

		/// <summary>
		/// Register a callback.
		/// </summary>
		/// <param name="callback">Callback.</param>
		/// <returns><see cref="IDisposable"/> to unregister the callback.</returns>
		/// <exception cref="ObjectDisposedException">The callbacks have been invoked.</exception>
		public IDisposable Register(Action callback)
		{
			var registration = new Registration(this, callback);
			lock (this.syncLock)
			{
				if (this.isInvoked)
					throw new ObjectDisposedException(nameof(IImageDataSource));
				this.registrations.Add(registration);
			}
			return registration;
		}

		// Unregister a callback.
		void Unregister(Registration registration)
		{
			lock (this.syncLock)
				this.registrations.Remove(registration);
		}
	}


	/// <summary>
	/// Check whether the resource of the source is shared with given source or not.
	/// </summary>
	/// <param name="source"><see cref="IImageDataSource"/> to check.</param>
	/// <returns>True if the resource is shared with <paramref name="source"/>.</returns>
	/// <remarks>True is also returned if <paramref name="source"/> is the same instance. None of the instances should be disposed before calling the method.</remarks>
	bool IsSourceSharedWith(IImageDataSource source);


	/// <summary>
	/// Register a callback to be called when the resource shared by all instances is released.
	/// </summary>
	/// <param name="callback">Callback.</param>
	/// <returns><see cref="IDisposable"/> to unregister the callback. Disposing it more than once, or after the resource has been released, takes no effect.</returns>
	/// <exception cref="ObjectDisposedException">The instance has been disposed.</exception>
	/// <remarks>The callback is called once after the resource has been released, on the thread which disposes the last instance, or on the finalizer thread. It is called while the lock of the resource is held, so it should be thread-safe and quick, and must not wait for a lock under which an instance sharing the same resource is disposed. An exception thrown by the callback is logged and does not affect other callbacks.</remarks>
	IDisposable RegisterSourceReleasedCallback(Action callback);


	/// <summary>
	/// Size of data in bytes.
	/// </summary>
	long Size { get; }
}