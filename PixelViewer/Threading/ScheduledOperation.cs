using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Carina.PixelViewer.Threading
{
	/// <summary>
	/// Scheduled operation on specific <see cref="SynchronizationContext"/>.
	/// </summary>
	class ScheduledOperation : ISynchronizable
	{
		// Fields.
		readonly Action operation;
		volatile object? postingToken;


		/// <summary>
		/// Initialize new <see cref="ScheduledOperation"/> instance by <see cref="SynchronizationContext"/> on current thread.
		/// </summary>
		/// <param name="operation">Operation to be executed.</param>
		public ScheduledOperation(Action operation) : this(SynchronizationContext.Current ?? throw new ArgumentException("No synchronization context on current thread."), operation)
		{ }


		/// <summary>
		/// Initialize new <see cref="ScheduledOperation"/> instance.
		/// </summary>
		/// <param name="synchronizationContext"><see cref="SynchronizationContext"/> to execute operation.</param>
		/// <param name="operation">Operation to be executed.</param>
		public ScheduledOperation(SynchronizationContext synchronizationContext, Action operation)
		{
			this.operation = operation;
			this.SynchronizationContext = synchronizationContext;
		}


		/// <summary>
		/// Initialize new <see cref="ScheduledOperation"/> instance.
		/// </summary>
		/// <param name="synchronizable"><see cref="ISynchronizable"/> to provide <see cref="SynchronizationContext"/> to execute operation.</param>
		/// <param name="operation">Operation to be executed.</param>
		public ScheduledOperation(ISynchronizable synchronizable, Action operation)
		{
			this.operation = operation;
			this.SynchronizationContext = synchronizable.SynchronizationContext;
		}


		/// <summary>
		/// Cancel scheduled execution.
		/// </summary>
		/// <returns>True if execution has been cancelled.</returns>
		[MethodImpl(MethodImplOptions.Synchronized)]
		public bool Cancel()
		{
			if (this.postingToken == null)
				return false;
			this.SynchronizationContext.CancelDelayed(this.postingToken);
			this.postingToken = null;
			return true;
		}


		/// <summary>
		/// Execute operation immediately and cancel scheduled execution.
		/// </summary>
		public void Execute()
		{
			this.Cancel();
			this.operation();
		}


		/// <summary>
		/// Execute operation immediately and cancel scheduled execution if it was scheduled.
		/// </summary>
		/// <returns>True if operation has been executed immediately.</returns>
		public bool ExecuteIfScheduled()
		{
			if (!this.Cancel())
				return false;
			this.operation();
			return true;
		}


		/// <summary>
		/// Check whether operation has been scheduled or not.
		/// </summary>
		public bool IsScheduled { get => this.postingToken != null; }


		/// <summary>
		/// Schedule operation execution no matter it was already shceduled or not.
		/// </summary>
		/// <param name="delayMillis">Delay time to execute operation.</param>
		[MethodImpl(MethodImplOptions.Synchronized)]
		public void Reschedule(int delayMillis = 0)
		{
			this.Cancel();
			object? localToken = null;
			localToken = this.SynchronizationContext.PostDelayed((_) =>
			{
				lock (this)
				{
					if (localToken != this.postingToken)
						return;
					this.postingToken = null;
				}
				this.operation();
			}, null, delayMillis);
			this.postingToken = localToken;
		}


		/// <summary>
		/// Schedule operation execution. Execution won't be schedule again if it was already shceduled.
		/// </summary>
		/// <param name="delayMillis">Delay time to execute operation.</param>
		/// <returns>True if execution has been scheduled with given delayed time.</returns>
		[MethodImpl(MethodImplOptions.Synchronized)]
		public bool Schedule(int delayMillis = 0)
		{
			if (this.postingToken != null)
				return false;
			object? localToken = null;
			localToken = this.SynchronizationContext.PostDelayed((_) =>
			{
				lock(this)
				{
					if (localToken != this.postingToken)
						return;
					this.postingToken = null;
				}
				this.operation();
			}, null, delayMillis);
			this.postingToken = localToken;
			return true;
		}


		// Synchronization context.
		public SynchronizationContext SynchronizationContext { get; }
	}
}
