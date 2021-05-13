using Carina.PixelViewer.Threading;
using NUnit.Framework;
using System.Diagnostics;
using System.Threading;

namespace Carina.PixelViewer.Test.Threading
{
	/// <summary>
	/// Tests of <see cref="ScheduledOperation"/>.
	/// </summary>
	[TestFixture]
	class ScheduledOperationTests : BaseTests
	{
		// Fields.
		SingleThreadSynchronizationContext? syncContext;


		/// <summary>
		/// Create <see cref="SynchronizationContext"/> for testing.
		/// </summary>
		[OneTimeSetUp]
		public void CreateSynchronizationContext()
		{
			this.syncContext = new SingleThreadSynchronizationContext();
		}


		/// <summary>
		/// Create <see cref="SynchronizationContext"/> for testing.
		/// </summary>
		[OneTimeTearDown]
		public void DisposeSynchronizationContext()
		{
			this.syncContext?.Dispose();
		}


		/// <summary>
		/// Test for cancelling execution.
		/// </summary>
		[Test]
		public void TestCancellation()
		{
			// prepare operation
			var syncContext = this.syncContext ?? throw new AssertionException("No synchronization context for testing.");
			var executed = false;
			var scheduledOperation = new ScheduledOperation(syncContext, () =>
			{
				executed = true;
			});

			// cancel before execution without delay
			var result = false;
			syncContext.Send((_) =>
			{
				result = scheduledOperation.Schedule();
				Assert.IsTrue(result, "Result of scheduling should be true.");
				Assert.IsTrue(scheduledOperation.IsScheduled, "State of operation is wrong.");
				result = scheduledOperation.Cancel();
				Assert.IsTrue(result, "Result of cancellation should be true.");
				Assert.IsFalse(scheduledOperation.IsScheduled, "State of operation is wrong.");
				result = scheduledOperation.Cancel();
				Assert.IsFalse(result, "Result of cancellation should be false.");
			}, null);
			Thread.Sleep(1000);
			Assert.IsFalse(executed, "Operation should not be executed.");

			// cancel before execution with delay
			result = scheduledOperation.Schedule(100);
			Assert.IsTrue(result, "Result of scheduling should be true.");
			Assert.IsTrue(scheduledOperation.IsScheduled, "State of operation is wrong.");
			Thread.Sleep(50);
			result = scheduledOperation.Cancel();
			Assert.IsTrue(result, "Result of cancellation should be true.");
			Assert.IsFalse(scheduledOperation.IsScheduled, "State of operation is wrong.");
			Thread.Sleep(1000);
			Assert.IsFalse(executed, "Operation should not be executed.");

			// cancel after execution
			result = scheduledOperation.Schedule(100);
			Assert.IsTrue(result, "Result of scheduling should be true.");
			Assert.IsTrue(scheduledOperation.IsScheduled, "State of operation is wrong.");
			Thread.Sleep(200);
			result = scheduledOperation.Cancel();
			Assert.IsFalse(result, "Result of cancellation should be true.");
			Assert.IsFalse(scheduledOperation.IsScheduled, "State of operation is wrong.");
			Assert.IsTrue(executed, "Operation should be executed.");
		}


		/// <summary>
		/// Test for executing immediately.
		/// </summary>
		[Test]
		public void TestExecutingIfScheduled()
		{
			// prepare operation
			var syncContext = this.syncContext ?? throw new AssertionException("No synchronization context for testing.");
			Thread? executionThread = null;
			var scheduledOperation = new ScheduledOperation(syncContext, () =>
			{
				executionThread = Thread.CurrentThread;
			});

			// call before execution
			var result = scheduledOperation.Schedule(200);
			Assert.IsTrue(result, "Result of scheduling should be true.");
			Assert.IsTrue(scheduledOperation.IsScheduled, "State of operation is wrong.");
			Thread.Sleep(50);
			result = scheduledOperation.ExecuteIfScheduled();
			Assert.IsTrue(result, "Result of execution should be true.");
			Assert.IsFalse(scheduledOperation.IsScheduled, "State of operation is wrong.");
			Assert.AreSame(executionThread, Thread.CurrentThread, "Operation was not executed by calling thread.");
			executionThread = null;
			Thread.Sleep(500);
			Assert.IsNull(executionThread, "Operation should not be executed");

			// call after execution
			result = scheduledOperation.Schedule(100);
			Assert.IsTrue(result, "Result of scheduling should be true.");
			Assert.IsTrue(scheduledOperation.IsScheduled, "State of operation is wrong.");
			Thread.Sleep(200);
			Assert.AreSame(executionThread, syncContext.ExecutionThread, "Operation was not executed by given synchronization context.");
			result = scheduledOperation.ExecuteIfScheduled();
			Assert.IsFalse(result, "Result of execution should be true.");
		}


		/// <summary>
		/// Test for re-scheduling execution.
		/// </summary>
		[Test]
		public void TestRescheduling()
		{
			// prepare operation
			var stopWatch = new Stopwatch();
			var syncLock = new object();
			var syncContext = this.syncContext ?? throw new AssertionException("No synchronization context for testing.");
			var scheduledOperation = new ScheduledOperation(syncContext, () =>
			{
				stopWatch.Stop();
				lock (syncLock)
				{
					Monitor.Pulse(syncLock);
				}
			});

			// rescheduling
			for (var i = 10; i > 0; --i)
			{
				var delayedMillis = this.Random.Next(100, 500);
				stopWatch.Restart();
				lock (syncLock)
				{
					var result = scheduledOperation.Schedule(delayedMillis * 2);
					Assert.IsTrue(result, "Result of scheduling should be true.");
					Assert.IsTrue(scheduledOperation.IsScheduled, "State of operation is wrong.");
					scheduledOperation.Reschedule(delayedMillis);
					Monitor.Wait(syncLock, delayedMillis + 1000);
				}
				Assert.IsFalse(stopWatch.IsRunning, "Operation was not executed.");
				Assert.IsFalse(scheduledOperation.IsScheduled, "State of operation is wrong.");
				Assert.LessOrEqual(stopWatch.ElapsedMilliseconds, delayedMillis + 100, "Operation was executed too late.");
				Assert.GreaterOrEqual(stopWatch.ElapsedMilliseconds, delayedMillis, "Operation was executed too early.");
			}
		}


		/// <summary>
		/// Test for scheduling execution.
		/// </summary>
		[Test]
		public void TestScheduling()
		{
			// prepare operation
			var stopWatch = new Stopwatch();
			var syncLock = new object();
			var syncContext = this.syncContext ?? throw new AssertionException("No synchronization context for testing.");
			Thread? executionThread = null;
			var scheduledOperation = new ScheduledOperation(syncContext, () =>
			{
				stopWatch.Stop();
				executionThread = Thread.CurrentThread;
				lock (syncLock)
				{
					Monitor.Pulse(syncLock);
				}
			});

			// schedule without delay
			for (var i = 10; i > 0; --i)
			{
				stopWatch.Restart();
				lock (syncLock)
				{
					var result = scheduledOperation.Schedule();
					Assert.IsTrue(result, "Result of scheduling should be true.");
					Monitor.Wait(syncLock, 1000);
				}
				Assert.IsFalse(stopWatch.IsRunning, "Operation was not executed.");
				Assert.IsFalse(scheduledOperation.IsScheduled, "State of operation is wrong.");
				Assert.LessOrEqual(stopWatch.ElapsedMilliseconds, 100, "Operation was executed too late.");
				Assert.AreSame(executionThread, syncContext.ExecutionThread, "Operation was not executed by given synchronization context.");
			}

			// schedule with delay
			for (var i = 10; i > 0; --i)
			{
				var delayedMillis = this.Random.Next(100, 500);
				stopWatch.Restart();
				lock (syncLock)
				{
					var result = scheduledOperation.Schedule(delayedMillis);
					Assert.IsTrue(result, "Result of scheduling should be true.");
					Assert.IsTrue(scheduledOperation.IsScheduled, "State of operation is wrong.");
					result = scheduledOperation.Schedule(delayedMillis + 1000);
					Assert.IsFalse(result, "Operation cannot be scheduled again.");
					Monitor.Wait(syncLock, delayedMillis + 1000);
				}
				Assert.IsFalse(stopWatch.IsRunning, "Operation was not executed.");
				Assert.IsFalse(scheduledOperation.IsScheduled, "State of operation is wrong.");
				Assert.LessOrEqual(stopWatch.ElapsedMilliseconds, delayedMillis + 100, "Operation was executed too late.");
				Assert.GreaterOrEqual(stopWatch.ElapsedMilliseconds, delayedMillis, "Operation was executed too early.");
				Assert.AreSame(executionThread, syncContext.ExecutionThread, "Operation was not executed by given synchronization context.");
			}
		}
	}
}
