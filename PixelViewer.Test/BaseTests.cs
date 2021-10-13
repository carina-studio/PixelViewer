using CarinaStudio;
using NLog;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Carina.PixelViewer.Test
{
	/// <summary>
	/// Base class for test fixture.
	/// </summary>
	abstract class BaseTests : CarinaStudio.AppSuite.ApplicationBasedTests
	{
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
		public void ClearCacheDirectory()
		{
			this.cacheDirectory?.Let((it) => Directory.Delete(it, true));
		}


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
			var eventHandler = new EventHandler((_, e) =>
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
				await Task.Delay(1); // delay to make sure that other properties changed by source are completed
				return true;
			}
			finally
			{
				command.CanExecuteChanged -= eventHandler;
			}

			// check final value
			return command.CanExecute(parameter) == canExecute;
		}
	}
}
