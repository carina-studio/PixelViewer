using System;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Base implementation of <see cref="ISharableDisposable{T}"/>.
	/// </summary>
	abstract class BaseSharableDisposable<T> : ISharableDisposable<T> where T : BaseSharableDisposable<T>
	{
		/// <summary>
		/// Holder of resources inside <see cref="BaseSharableDisposable{T}"/>.
		/// </summary>
		protected abstract class BaseHolder
		{
			/// <summary>
			/// Number of <see cref="BaseSharableDisposable{T}"/> refer to this holder.
			/// </summary>
			public volatile uint ReferenceCount = 1;

			/// <summary>
			/// Dispose the holder.
			/// </summary>
			/// <param name="disposing">True to release managed resources.</param>
			public virtual void Dispose(bool disposing)
			{ }
		}


		// Fields.
		readonly BaseHolder holder;
		volatile bool isDisposed;


		/// <summary>
		/// Initialize new <see cref="BaseSharableDisposable{T}"/> instance.
		/// </summary>
		/// <param name="holder">Holder of resources inside the instance.</param>
		protected BaseSharableDisposable(BaseHolder holder)
		{
			this.holder = holder;
		}


		// Finalizer.
		~BaseSharableDisposable()
		{
			if (this.holder != null)
				this.Dispose(false);
		}


		// Dispose.
		public void Dispose()
		{
			// check state
			lock (this)
			{
				if (this.isDisposed)
					return;
				this.isDisposed = true;
			}

			// dispose
			GC.SuppressFinalize(this);
			this.Dispose(true);
		}


		/// <summary>
		/// Dispose the instance.
		/// </summary>
		/// <param name="disposing">True to release managed resources.</param>
		protected virtual void Dispose(bool disposing)
		{
			// dispose holder
			lock (this.holder)
			{
				--this.holder.ReferenceCount;
				if (this.holder.ReferenceCount != 0)
					return;
			}
			this.holder.Dispose(disposing);
		}


		/// <summary>
		/// Get holder of resources inside the instance.
		/// </summary>
		protected BaseHolder Holder { get => this.holder; }


		/// <summary>
		/// Check whether instance has been disposed or not.
		/// </summary>
		protected bool IsDisposed { get => this.isDisposed; }


		/// <summary>
		/// Called to share the ownership.
		/// </summary>
		/// <param name="holder">Holder of resources for new instance.</param>
		/// <returns>New instance with shared ownership.</returns>
		protected abstract T OnShare(BaseHolder holder);


		// Share the ownership.
		public T Share()
		{
			// update holder
			lock (this)
			{
				if (this.isDisposed)
					throw new ObjectDisposedException(this.GetType().Name);
				lock (this.holder)
				{
					++this.holder.ReferenceCount;
				}
			}

			// share
			try
			{
				return this.OnShare(this.holder).Also((it) =>
				{
					if (object.ReferenceEquals(this, it))
						throw new InvalidOperationException();
				});
			}
			catch
			{
				lock (this.holder)
				{
					--this.holder.ReferenceCount;
					if (this.holder.ReferenceCount != 0)
						throw;
				}
				this.holder.Dispose(true);
				throw;
			}
		}
	}
}
