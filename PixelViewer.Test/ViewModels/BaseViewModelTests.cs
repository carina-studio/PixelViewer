using Carina.PixelViewer.Threading;
using Carina.PixelViewer.ViewModels;
using NUnit.Framework;
using System;
using System.Threading;

namespace Carina.PixelViewer.Test.ViewModels
{
	/// <summary>
	/// Base implementation of tests of <see cref="BaseViewModel"/>.
	/// </summary>
	class BaseViewModelTests<T> : BaseTests where T : BaseViewModel
	{
		// Fields.
		SingleThreadSynchronizationContext? testSyncContext;


		/// <summary>
		/// Dispose <see cref="TestSynchronizationContext"/> instance.
		/// </summary>
		[OneTimeTearDown]
		public void DisposeTestSynchronizationContext()
		{
			this.testSyncContext?.PostDelayed(() =>
			{
				this.testSyncContext?.Dispose();
			}, 1000);
		}


		/// <summary>
		/// Setup Avalonia application environment before testing.
		/// </summary>
		[OneTimeSetUp]
		public void SetupAvaloniaApp()
		{
			AvaloniaApp.Setup();
		}


		/// <summary>
		/// Perform testing by using <see cref="TestSynchronizationContext"/>.
		/// </summary>
		/// <param name="testAction">Test action.</param>
		/// <param name="timeoutMillis">Timeout of waiting for testing to be completed in milliseconds.</param>
		protected void TestByTestSynchronizationContext(Action testAction, int timeoutMillis)
		{
			var syncLock = new object();
			Exception? exception = null;
			lock (syncLock)
			{
				this.TestSynchronizationContext.Post((_) =>
				{
					try
					{
						testAction();
					}
					catch (Exception ex)
					{
						exception = ex;
					}
					finally
					{
						lock (syncLock)
						{
							Monitor.Pulse(syncLock);
						}
					}
				}, null);
				Assert.IsTrue(Monitor.Wait(syncLock, timeoutMillis), "Cannot complete testing.");
				if (exception != null)
					throw new AssertionException(exception.Message, exception);
			}
		}


		/// <summary>
		/// <see cref="SynchronizationContext"/> for testing <see cref="BaseViewModel"/> instance.
		/// </summary>
		protected SynchronizationContext TestSynchronizationContext
		{
			get
			{
				if(this.testSyncContext == null)
				{
					lock (this)
					{
						if (this.testSyncContext == null)
							this.testSyncContext = new SingleThreadSynchronizationContext();
					}
				}
				return this.testSyncContext.EnsureNonNull();
			}
		}
	}
}
