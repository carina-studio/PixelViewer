using Carina.PixelViewer.Threading;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.Threading
{
	/// <summary>
	/// Tests of <see cref="SingleThreadSynchronizationContext"/>.
	/// </summary>
	[TestFixture]
	class SingleThreadSynchronizationContextTests : BaseTests
	{
		/// <summary>
		/// Test for creating instance and disposing.
		/// </summary>
		[Test]
		public async Task TestCreationAndDisposing()
		{
			// create
			var syncContext = new SingleThreadSynchronizationContext();
			Assert.IsTrue(syncContext.IsExecutionThreadAlive, "Execution thread should be alive.");

			// delay
			await Task.Delay(1000);
			Assert.IsTrue(syncContext.IsExecutionThreadAlive, "Execution thread should be alive.");

			// dispose
			syncContext.Dispose();
			await Task.Delay(1000);
			Assert.IsFalse(syncContext.IsExecutionThreadAlive, "Execution thread should be stopped.");
		}


		/// <summary>
		/// Test for sending/posting call-backs to synchronization context.
		/// </summary>
		/// <returns></returns>
		[Test]
		public void TestSendingAndPosting()
		{
			// create instance
			using var syncContext = new SingleThreadSynchronizationContext();

			// check execution thread and synchronization context in call-back
			SynchronizationContext? syncContextInCallback = null;
			Thread? threadInCallback = null;
			var syncLock = new object();
			lock (syncLock)
			{
				syncContext.Post(() =>
				{
					syncContextInCallback = SynchronizationContext.Current;
					threadInCallback = Thread.CurrentThread;
					lock (syncLock)
					{
						Monitor.Pulse(syncLock);
					}
				});
				Monitor.Wait(syncLock, 1000);
			}
			Assert.AreSame(syncContext, syncContextInCallback, "Synchronization context in call-back is incorrect.");
			Assert.AreSame(syncContext.ExecutionThread, threadInCallback, "Call-back is not executed by execution thread.");

			// send call-back
			threadInCallback = null;
			syncContext.Send((_) =>
			{
				Thread.Sleep(1000);
				threadInCallback = Thread.CurrentThread;
			}, null);
			Assert.AreSame(syncContext.ExecutionThread, threadInCallback, "Call-back is not executed by execution thread.");

			// post call-backs continuously
			var callbackCount = 1234;
			var nextCallbackId = 1;
			var executedCallbackCount = 0;
			var isExecutionOrderingCorrect = true;
			for (var i = 1; i <= callbackCount; ++i)
			{
				var id = i;
				syncContext.Post(() =>
				{
					++executedCallbackCount;
					if (id == nextCallbackId)
						++nextCallbackId;
					else
						isExecutionOrderingCorrect = false;
					if (executedCallbackCount == callbackCount)
					{
						lock (syncLock)
						{
							Monitor.Pulse(syncLock);
						}
					}
				});
			}
			lock (syncLock)
			{
				if (executedCallbackCount < callbackCount)
					Monitor.Wait(syncLock, 10000);
			}
			Assert.AreEqual(callbackCount, executedCallbackCount, "Number of executed call-backs is not same as posted.");
			Assert.IsTrue(isExecutionOrderingCorrect, "Execution ordering is incorrect.");
		}
	}
}
