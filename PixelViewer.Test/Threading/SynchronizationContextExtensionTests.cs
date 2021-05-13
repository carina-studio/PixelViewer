using Carina.PixelViewer.Threading;
using NUnit.Framework;
using System.Diagnostics;
using System.Threading;

namespace Carina.PixelViewer.Test.Threading
{
	/// <summary>
	/// Tests of <see cref="SynchronizationContextExtensions"/>.
	/// </summary>
	[TestFixture]
	class SynchronizationContextExtensionTests : BaseTests
	{
		/// <summary>
		/// Test for posting call-back with delayed time.
		/// </summary>
		[Test]
		public void TestPostingDelayed()
		{
			// create synchronization context
			using var syncContext = new SingleThreadSynchronizationContext();

			// post without delay
			var syncLock = new object();
			var stopWatch = new Stopwatch();
			stopWatch.Start();
			lock (syncLock)
			{
				syncContext.PostDelayed(() =>
				{
					stopWatch.Stop();
					lock (syncLock)
					{
						Monitor.Pulse(syncLock);
					}
				}, 0);
				Monitor.Wait(syncLock, 1000);
			}
			Assert.IsFalse(stopWatch.IsRunning, "Call-back was not executed.");
			Assert.LessOrEqual(stopWatch.ElapsedMilliseconds, 10, "Call-back was executed too late.");

			// post with delay
			for (var i = 10; i > 0; --i)
			{
				var delayedMillis = this.Random.Next(100, 500);
				stopWatch.Restart();
				lock (syncLock)
				{
					syncContext.PostDelayed(() =>
					{
						stopWatch.Stop();
						lock (syncLock)
						{
							Monitor.Pulse(syncLock);
						}
					}, delayedMillis);
					Monitor.Wait(syncLock, delayedMillis + 1000);
				}
				Assert.IsFalse(stopWatch.IsRunning, "Call-back was not executed.");
				Assert.LessOrEqual(stopWatch.ElapsedMilliseconds, delayedMillis + 100, "Call-back was executed too late.");
				Assert.GreaterOrEqual(stopWatch.ElapsedMilliseconds, delayedMillis, "Call-back was executed too early.");
			}

			// cancel before executing
			stopWatch.Restart();
			lock (syncLock)
			{
				var token = syncContext.PostDelayed(() =>
				{
					stopWatch.Stop();
					lock (syncLock)
					{
						Monitor.Pulse(syncLock);
					}
				}, 500);
				Thread.Sleep(200);
				syncContext.CancelDelayed(token);
				Monitor.Wait(syncLock, 1000);
			}
			Assert.IsTrue(stopWatch.IsRunning, "Call-back should not be executed.");

			// cancel after executing
			stopWatch.Restart();
			lock (syncLock)
			{
				var token = syncContext.PostDelayed(() =>
				{
					stopWatch.Stop();
				}, 500);
				Thread.Sleep(600);
				syncContext.CancelDelayed(token);
			}
			Assert.IsFalse(stopWatch.IsRunning, "Call-back was not executed.");
		}
	}
}
