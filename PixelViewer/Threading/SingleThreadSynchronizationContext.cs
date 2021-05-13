using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Carina.PixelViewer.Threading
{
	/// <summary>
	/// <see cref="SynchronizationContext"/> which executes posted call-backs on single execution thread. You need to call <see cref="Dispose"/> to stop execution thread.
	/// </summary>
	class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
	{
		// Fields.
		volatile bool isDisposed;
		readonly Queue<Tuple<SendOrPostCallback, object?>> pendingCallbacks = new Queue<Tuple<SendOrPostCallback, object?>>();


		/// <summary>
		/// Initialize new <see cref="SingleThreadSynchronizationContext"/> instance.
		/// </summary>
		public SingleThreadSynchronizationContext()
		{
			this.ExecutionThread = new Thread(this.ExecutionThreadEntry).Also((it) => it.Start());
		}


		// Dispose.
		[MethodImpl(MethodImplOptions.Synchronized)]
		public void Dispose()
		{
			if (this.isDisposed)
				return;
			this.isDisposed = true;
			this.pendingCallbacks.Clear();
			Monitor.PulseAll(this);
		}


		/// <summary>
		/// Get excution thread.
		/// </summary>
		public Thread ExecutionThread { get; }


		// Entry of execution thread.
		void ExecutionThreadEntry()
		{
			SynchronizationContext.SetSynchronizationContext(this);
			while (true)
			{
				Tuple<SendOrPostCallback, object?>? callbackTuple;
				lock (this)
				{
					if (this.isDisposed)
						return;
					if (!this.pendingCallbacks.TryDequeue(out callbackTuple) || callbackTuple == null)
					{
						Monitor.Wait(this);
						continue;
					}
				}
				this.OperationStarted();
				callbackTuple.Item1(callbackTuple.Item2);
				this.OperationCompleted();
			}
		}


		/// <summary>
		/// Check whether execution thread is alive or not.
		/// </summary>
		public bool IsExecutionThreadAlive { get => this.ExecutionThread.IsAlive; }


		// Post call-back.
		[MethodImpl(MethodImplOptions.Synchronized)]
		public override void Post(SendOrPostCallback d, object? state)
		{
			if (this.isDisposed)
				throw new ObjectDisposedException(this.GetType().Name);
			pendingCallbacks.Enqueue(new Tuple<SendOrPostCallback, object?>(d, state));
			Monitor.PulseAll(this);
		}


		// Send call-back.
		public override void Send(SendOrPostCallback d, object? state)
		{
			if (Thread.CurrentThread == this.ExecutionThread)
				d(state);
			else
			{
				var syncLock = new object();
				Exception? exception = null;
				lock (syncLock)
				{
					this.Post((_) =>
					{
						try
						{
							d(state);
						}
						catch (Exception ex)
						{
							exception = ex;
						}
						lock (syncLock)
						{
							Monitor.Pulse(syncLock);
						}
					}, null);
					Monitor.Wait(syncLock);
				}
				if (exception != null)
					throw exception;
			}
		}
	}
}
