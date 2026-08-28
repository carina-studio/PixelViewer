using NLog;
using NUnit.Framework;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Carina.PixelViewer.Test
{
	/// <summary>
	/// Base class for test fixture.
	/// </summary>
	abstract class BaseTests : CarinaStudio.AppSuite.ApplicationBasedTests<CarinaStudio.AppSuite.IAppSuiteApplication>
	{
		// Constants.
		const int FileDeletionRetryInterval = 50;
		const int FileDeletionTimeout = 10000;


		// Fields.
		volatile string? cacheDirectory;


		/// <summary>
		/// Initialize new <see cref="BaseTests"/> instance.
		/// </summary>
		protected BaseTests()
		{ }


		/// <summary>
		/// Clear created cache directory.
		/// </summary>
		[OneTimeTearDown]
		public Task ClearCacheDirectoryAsync() =>
			this.cacheDirectory is not null
				? DeleteDirectoryAsync(this.cacheDirectory)
				: Task.CompletedTask;


		/// <summary>
		/// Create the application for testing, which provides the string resources of the real application.
		/// </summary>
		/// <returns>Application for testing.</returns>
		protected override CarinaStudio.AppSuite.IAppSuiteApplication CreateMockApplication() =>
			CarinaStudio.AppSuite.MockAppSuiteApplication.Initialize(() => new TestApplication());


		/// <summary>
		/// Create file in cache directory and open it.
		/// </summary>
		/// <returns><see cref="Stream"/> of create file.</returns>
		protected FileStream CreateCacheFile()
		{
			// setup directory
			if (this.cacheDirectory == null)
			{
				lock (this)
				{
					if (this.cacheDirectory == null)
						this.cacheDirectory = Path.Combine(this.Application.RootPrivateDirectoryPath, this.GetType().Name);
					Directory.CreateDirectory(this.cacheDirectory);
				}
			}

			// generate file
			while (true)
			{
				var fileName = new char[16];
				for (var i = fileName.Length - 1; i >= 0; --i)
				{
					var n = this.Random.Next(0, 35);
					if (n <= 9)
						fileName[i] = (char)('0' + n);
					else
						fileName[i] = (char)('a' + (n - 10));
				}
				var filePath = Path.Combine(this.cacheDirectory, new string(fileName));
				if (File.Exists(filePath))
					continue;
				return File.Create(filePath);
			}
		}


		// Delete the file or directory at the given path, retrying while it is still held by a source which has not been released yet.
		static async Task DeleteAsync(string path, bool isDirectory)
		{
			// start counting the time spent on waiting for the path to become deletable
			var stopWatch = new Stopwatch();
			stopWatch.Start();

			// delete, the source which held the path may be released asynchronously after the session reported it as closed
			while (true)
			{
				try
				{
					if (isDirectory)
						Directory.Delete(path, true);
					else
						File.Delete(path);
					return;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					if (stopWatch.ElapsedMilliseconds >= FileDeletionTimeout)
						throw;
					await Task.Delay(FileDeletionRetryInterval, CancellationToken.None);
				}
			}
		}


		/// <summary>
		/// Delete the given directory, waiting for the asynchronous release of any source which still holds a file in it.
		/// </summary>
		/// <param name="path">Path of directory to delete.</param>
		/// <returns>Task of deleting the directory.</returns>
		protected static Task DeleteDirectoryAsync(string path) =>
			DeleteAsync(path, true);


		/// <summary>
		/// Delete the given file, waiting for the asynchronous release of the source which held it.
		/// </summary>
		/// <param name="filePath">Path of file to delete.</param>
		/// <returns>Task of deleting the file.</returns>
		/// <remarks>A file which is never released still fails the test, the deletion is only retried for a limited time.</remarks>
		protected static Task DeleteFileAsync(string filePath) =>
			DeleteAsync(filePath, false);


		/// <summary>
		/// Initialize the sub-systems which are shared by tests. Initializing them again does nothing,
		/// so every fixture which needs them can call this method without checking whether another fixture already did.
		/// </summary>
		/// <remarks>The method must be called on the application thread.</remarks>
		protected async Task InitializeSubSystemsAsync()
		{
			Carina.PixelViewer.Media.FileFormats.Initialize(this.Application);
			Carina.PixelViewer.Media.FileFormatParsers.FileFormatParsers.Initialize(this.Application);
			await Carina.PixelViewer.Media.ColorSpace.InitializeAsync(this.Application);
			await Carina.PixelViewer.Media.Profiles.ImageRenderingProfiles.InitializeAsync(this.Application);
		}


		/// <summary>
		/// Logger.
		/// </summary>
		protected ILogger Logger { get; } = LogManager.GetCurrentClassLogger();


		/// <summary>
		/// Get <see cref="Random"/> instance for tests.
		/// </summary>
		protected Random Random { get; } = new Random();


		/// <summary>
		/// Wait for <see cref="ICommand.CanExecute(object)"/> of given command to be specific value.
		/// </summary>
		/// <param name="command">Command.</param>
		/// <param name="canExecute">Specific value of <see cref="ICommand.CanExecute(object)"/>.</param>
		/// <param name="parameter">Command parameter.</param>
		/// <param name="timeoutMillis">Timeout in milliseconds.</param>
		/// <returns>True if <see cref="ICommand.CanExecute(object)"/> of command has been changed to specific value in given timeout.</returns>
		protected async Task<bool> WaitForCommandState(ICommand command, bool canExecute, object? parameter, int timeoutMillis)
		{
			// check current state
			if (command.CanExecute(parameter) == canExecute)
				return true;

			// check timeout
			if (timeoutMillis == 0)
				return false;

			// wait for state change
			var cancellationTokenSource = new CancellationTokenSource();
			var eventHandler = new EventHandler((_, _) =>
			{
				if (command.CanExecute(parameter) == canExecute)
					cancellationTokenSource.Cancel();
			});
			command.CanExecuteChanged += eventHandler;
			try
			{
				await Task.Delay(timeoutMillis, cancellationTokenSource.Token);
			}
			catch (TaskCanceledException)
			{
				await Task.Delay(1, CancellationToken.None); // delay to make sure that other properties changed by source are completed
				return true;
			}
			finally
			{
				command.CanExecuteChanged -= eventHandler;
			}

			// check final value
			return command.CanExecute(parameter) == canExecute;
		}


		/// <summary>
		/// Wait for the given condition to be met.
		/// </summary>
		/// <param name="condition">Condition to check.</param>
		/// <param name="timeoutMillis">Timeout in milliseconds.</param>
		/// <returns>True if the condition has been met before the timeout.</returns>
		/// <remarks>Use this instead of <see cref="WaitForPropertyAsync"/> when the state to wait for is a combination of properties, or when a property may be changed back before it is observed.</remarks>
		protected static async Task<bool> WaitForConditionAsync(Func<bool> condition, int timeoutMillis)
		{
			var stopwatch = Stopwatch.StartNew();
			while (true)
			{
				if (condition())
					return true;
				if (stopwatch.ElapsedMilliseconds >= timeoutMillis)
					return false;
				await Task.Delay(50, CancellationToken.None);
			}
		}


		/// <summary>
		/// Wait for value of given property of an object to become the target value.
		/// </summary>
		/// <param name="obj">Object which owns the property.</param>
		/// <param name="propertyName">Name of property.</param>
		/// <param name="targetValue">Target value.</param>
		/// <param name="timeoutMillis">Timeout in milliseconds.</param>
		/// <returns>True if value of property has become the target value before the timeout.</returns>
		protected Task<bool> WaitForPropertyAsync(INotifyPropertyChanged obj, string propertyName, object? targetValue, int timeoutMillis)
		{
			// CarinaStudio.Tests.NotifyPropertyChangedExtensions.WaitForPropertyAsync is obsolete, but it is the only
			// helper providing the timeout + boolean-result contract these tests rely on; the suggested replacement
			// (WaitForPropertyChangeAsync) offers neither, so keep using it behind this single wrapper.
#pragma warning disable CS0618
			return CarinaStudio.Tests.NotifyPropertyChangedExtensions.WaitForPropertyAsync(obj, propertyName, targetValue, timeoutMillis, CancellationToken.None);
#pragma warning restore CS0618
		}


		/// <summary>
		/// Wait for the given task to complete.
		/// </summary>
		/// <param name="task">Task to wait for.</param>
		/// <param name="timeoutMillis">Timeout in milliseconds.</param>
		/// <returns>True if the task has completed before the timeout.</returns>
		/// <remarks>The result is reported instead of a <see cref="TimeoutException"/> so that the caller can assert on it, blocking asserts such as <see cref="Assert.ThrowsAsync(Type, AsyncTestDelegate)"/> hang when they are used on the application thread.</remarks>
		protected static async Task<bool> WaitForTaskAsync(Task task, int timeoutMillis)
		{
			var completedTask = await Task.WhenAny(task, Task.Delay(timeoutMillis, CancellationToken.None));
			return completedTask == task;
		}
	}
}
