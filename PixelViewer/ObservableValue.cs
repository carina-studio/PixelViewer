using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Dynamic value which implements <see cref="IObservable{T}"/>.
	/// </summary>
	abstract class ObservableValue<T> : IObservable<T>
	{
		// Holder of observer.
		class ObserverHolder : IDisposable
		{
			// Fields.
			public bool IsDisposed;
			public readonly IObserver<T> Observer;
			public readonly ObservableValue<T> Owner;

			// Constructor.
			public ObserverHolder(ObservableValue<T> owner, IObserver<T> observer)
			{
				this.Owner = owner;
				this.Observer = observer;
			}

			// Dispose.
			void IDisposable.Dispose()
			{
				this.IsDisposed = true;
				this.Owner.Unsubscribe(this);
			}
		}


		// Fields.
		bool isObserverHoldersChanged;
		readonly List<ObserverHolder> observerHolders = new List<ObserverHolder>();
		int observerNotifyingCounter;
		[AllowNull] T value;


		/// <summary>
		/// Initialize new <see cref="ObservableValue{T}"/> instance.
		/// </summary>
		/// <param name="initValue">Initial value.</param>
		protected ObservableValue([AllowNull] T initValue = default)
		{
			this.value = initValue;
		}


		// Notify observers.
		void NotifyObservers(T value)
		{
			++this.observerNotifyingCounter;
			try
			{
				for (var i = this.observerHolders.Count - 1; i >= 0; --i)
				{
					this.observerHolders[i].Also((observerHolder) =>
					{
						if (!observerHolder.IsDisposed)
							observerHolder.Observer.OnNext(value);
					});
				}
			}
			finally
			{
				--this.observerNotifyingCounter;
				if (this.observerNotifyingCounter == 0 && this.isObserverHoldersChanged)
				{
					this.isObserverHoldersChanged = false;
					for (var i = this.observerHolders.Count - 1; i >= 0; --i)
					{
						if (this.observerHolders[i].IsDisposed)
							this.observerHolders.RemoveAt(i);
					}
				}
			}
		}


		// Subscribe observer.
		public IDisposable Subscribe(IObserver<T> observer)
		{
			return new ObserverHolder(this, observer).Also((it) =>
			{
				this.observerHolders.Add(it);
				if (this.observerNotifyingCounter == 0)
					observer.OnNext(this.value);
			});
		}


		// To readable string.
		public override string? ToString() => this.value?.ToString();


		// Unsubscribe observer.
		void Unsubscribe(ObserverHolder observerHolder)
		{
			if (this.observerNotifyingCounter <= 0)
				this.observerHolders.Remove(observerHolder);
			else
				this.isObserverHoldersChanged = true;
		}


		/// <summary>
		/// Get or set value.
		/// </summary>
		public T Value
		{
			get => this.value;
			protected set
			{
				if (value == null)
				{
					if (this.value == null)
						return;
				}
				else if (this.value != null)
				{
					if (value is IEquatable<T> equatable)
					{
						if (equatable.Equals(this.value))
							return;
					}
					else if (value.Equals(this.value))
						return;
				}
				this.value = value;
				this.NotifyObservers(value);
			}
		}
	}
}
