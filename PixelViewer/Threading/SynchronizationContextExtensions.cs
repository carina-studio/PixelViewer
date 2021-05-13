using System;
using System.Threading;

namespace Carina.PixelViewer.Threading
{
	/// <summary>
	/// Extensions for <see cref="SynchronizationContext"/>.
	/// </summary>
	static class SynchronizationContextExtensions
	{
		// Token of posted delayed call-back.
		class DelayedCallbackToken
		{
			// Fields.
			public readonly SendOrPostCallback Callback;
			public volatile bool IsCancelled;
			public readonly object? State;
			public readonly SynchronizationContext SynchronizationContext;
			public readonly Timer Timer;

			// Constructor.
			public DelayedCallbackToken(SynchronizationContext syncContext, SendOrPostCallback callback, object? state, int delayedMillis)
			{
				this.SynchronizationContext = syncContext;
				this.Callback = callback;
				this.State = state;
				lock (this)
				{
					this.Timer = new Timer((_) =>
					{
						lock (this)
						{
							this.Timer?.Dispose();
							if (!this.IsCancelled)
								this.SynchronizationContext.Post(callback, state);
						}
					}, null, delayedMillis, Timeout.Infinite);
				}
			}
		}


		// Invalid token of delayed call-back.
		class InvalidDelayedCallbackToken
		{ }


		/// <summary>
		/// Cancel delayed call-back.
		/// </summary>
		/// <param name="synchronizationContext"><see cref="SynchronizationContext"/>.</param>
		/// <param name="token">Token of delayed call-back returned by <see cref="PostDelayed(SynchronizationContext, SendOrPostCallback, object?, int)"/>.</param>
		public static void CancelDelayed(this SynchronizationContext synchronizationContext, object token)
		{
			if (token is InvalidDelayedCallbackToken)
				return;
			if (!(token is DelayedCallbackToken delayedCallbackToken) || delayedCallbackToken.SynchronizationContext != synchronizationContext)
				throw new ArgumentException("Incorrect token.");
			delayedCallbackToken.IsCancelled = true;
			delayedCallbackToken.Timer.Dispose();
		}


		/// <summary>
		/// Call given method asynchronously.
		/// </summary>
		/// <param name="synchronizationContext"><see cref="SynchronizationContext"/>.</param>
		/// <param name="method">Method to call.</param>
		public static void Post(this SynchronizationContext synchronizationContext, Action method) => synchronizationContext.Post((state) => method(), null);


		/// <summary>
		/// Call given method asynchronously.
		/// </summary>
		/// <param name="synchronizationContext"><see cref="SynchronizationContext"/>.</param>
		/// <param name="method">Method to call.</param>
		public static void Post<TRet>(this SynchronizationContext synchronizationContext, Func<TRet> method) => synchronizationContext.Post((state) => method(), null);


		/// <summary>
		/// Post delayed call-back.
		/// </summary>
		/// <param name="synchronizationContext"><see cref="SynchronizationContext"/>.</param>
		/// <param name="callback">Call-back.</param>
		/// <param name="state">Custom state passed to call-back.</param>
		/// <param name="delayedMillis">Delayed time in milliseconds.</param>
		/// <returns>Token of delayed call-back. You can cancel it before execution by calling <see cref="CancelDelayed(SynchronizationContext, object)"/>.</returns>
		public static object PostDelayed(this SynchronizationContext synchronizationContext, SendOrPostCallback callback, object? state, int delayedMillis)
		{
			if (delayedMillis < 0)
				return new InvalidDelayedCallbackToken();
			if (delayedMillis > 0)
				return new DelayedCallbackToken(synchronizationContext, callback, state, delayedMillis);
			synchronizationContext.Post(callback, state);
			return new InvalidDelayedCallbackToken();
		}


		/// <summary>
		/// Post delayed call-back.
		/// </summary>
		/// <param name="synchronizationContext"><see cref="SynchronizationContext"/>.</param>
		/// <param name="callback">Call-back.</param>
		/// <param name="delayedMillis">Delayed time in milliseconds.</param>
		/// <returns>Token of delayed call-back. You can cancel it before execution by calling <see cref="CancelDelayed(SynchronizationContext, object)"/>.</returns>
		public static object PostDelayed(this SynchronizationContext synchronizationContext, Action callback, int delayedMillis) => PostDelayed(synchronizationContext, (_) => callback(), null, delayedMillis);


		/// <summary>
		/// Post delayed call-back.
		/// </summary>
		/// <param name="synchronizationContext"><see cref="SynchronizationContext"/>.</param>
		/// <param name="callback">Call-back.</param>
		/// <param name="delayedMillis">Delayed time in milliseconds.</param>
		/// <returns>Token of delayed call-back. You can cancel it before execution by calling <see cref="CancelDelayed(SynchronizationContext, object)"/>.</returns>
		public static object PostDelayed<TRet>(this SynchronizationContext synchronizationContext, Func<TRet> callback, int delayedMillis) => PostDelayed(synchronizationContext, (_) => callback(), null, delayedMillis);
	}
}
