using System;
using System.Threading;

namespace Carina.PixelViewer.Threading
{
	/// <summary>
	/// Timer ticking on given <see cref="SynchronizationContext"/>.
	/// </summary>
	sealed class SynchronizableTimer : IDisposable, ISynchronizable
	{
		// Fields.
		int interval = Timeout.Infinite;
		bool isEnabled;
		volatile bool isDisposed;
		readonly ISynchronizable? synchronizable;
		readonly Thread? syncThread;
		readonly System.Threading.Timer systemTimer;


		/// <summary>
		/// Initialize new <see cref="SynchronizableTimer"/> ticking on current thread.
		/// </summary>
		public SynchronizableTimer()
		{
			this.SynchronizationContext = SynchronizationContext.Current ?? throw new Exception("No synchronization context on current thread.");
			this.syncThread = Thread.CurrentThread;
			this.systemTimer = new System.Threading.Timer(this.OnTimerCallback);
		}


		/// <summary>
		/// Initialize new <see cref="SynchronizableTimer"/>.
		/// </summary>
		/// <param name="synchronizable"><see cref="ISynchronizable"/> for ticking.</param>
		public SynchronizableTimer(ISynchronizable synchronizable)
		{
			this.SynchronizationContext = synchronizable.SynchronizationContext;
			this.synchronizable = synchronizable;
			this.systemTimer = new System.Threading.Timer(this.OnTimerCallback);
		}


		// Finalizer.
		~SynchronizableTimer() => this.Dispose();


		// Dispose.
		public void Dispose()
		{
			lock (this)
			{
				if (this.isDisposed)
					return;
				this.isDisposed = true;
			}
			GC.SuppressFinalize(this);
			this.systemTimer.Dispose();
		}


		/// <summary>
		/// Get or set interval of ticking in milliseconds.
		/// </summary>
		public int Interval
		{
			get => this.interval;
			set
			{
				lock (this)
				{
					if (this.isDisposed)
						throw new ObjectDisposedException(this.GetType().Name);
					if (this.interval == value)
						return;
					this.interval = value;
					if (this.isEnabled)
						this.Update();
				}
			}
		}


		/// <summary>
		/// Get or set whether timer is enabled or not.
		/// </summary>
		public bool IsEnabled
		{
			get => this.isEnabled;
			set
			{
				lock (this)
				{
					if (this.isDisposed)
						throw new ObjectDisposedException(this.GetType().Name);
					if (this.isEnabled == value)
						return;
					this.isEnabled = value;
					this.Update();
				}
			}
		}


		// Timer call-back.
		void OnTimerCallback(object? state)
		{
			this.SynchronizationContext.Post(() =>
			{
				lock (this)
				{
					if (this.isDisposed || !this.isEnabled)
						return;
					this.Tick?.Invoke(this, EventArgs.Empty);
				}
			});
		}


		// Synchronization context.
		public SynchronizationContext SynchronizationContext { get; }


		/// <summary>
		/// Raised when interval reached for every tick.
		/// </summary>
		public event EventHandler? Tick;


		// Update system timer.
		void Update()
		{
			if (this.isEnabled)
				this.systemTimer.Change(0, this.interval);
			else
				this.systemTimer.Change(0, Timeout.Infinite);
		}
	}
}
