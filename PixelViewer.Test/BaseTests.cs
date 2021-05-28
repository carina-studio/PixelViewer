using CarinaStudio;
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
	abstract class BaseTests
	{
		// Fields.
		volatile string? baseDirectory;
		volatile string? cacheDirectory;


		/// <summary>
		/// Initialize new <see cref="BaseTests"/> instance.
		/// </summary>
		protected BaseTests()
		{ }


		/// <summary>
		/// Get base directory for testing purpose.
		/// </summary>
		protected string BaseDirectory
		{
			get => this.baseDirectory ?? Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)?.Also((it) =>
			{
				this.baseDirectory = it;
			}) ?? throw new Exception("Unable to get base directory.");
		}


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
						this.cacheDirectory = Path.Combine(this.BaseDirectory, this.GetType().Name);
					Directory.CreateDirectory(this.cacheDirectory);
				}
			}

			// generate file
			while(true)
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


		/// <summary>
		/// Wait for value of given property to be specific one.
		/// </summary>
		/// <param name="source">Source of property.</param>
		/// <param name="propertyName">Name of property.</param>
		/// <param name="targetValue">Specific value to wait for.</param>
		/// <param name="timeoutMillis">Timeout in milliseconds.</param>
		/// <returns>True if property has been changed to specific value in given timeout.</returns>
		protected async Task<bool> WaitForProperty<TSource, TValue>(TSource source, string propertyName, TValue targetValue, int timeoutMillis) where TSource : INotifyPropertyChanged
		{
			// find property
			var sourceType = source.GetType();
			var propertyGetter = sourceType.GetProperty(propertyName)?.GetGetMethod()?.CreateDelegate(typeof(Func<TSource, TValue>)) as Func<TSource, TValue> ?? throw new ArgumentException($"Cannot find property '{propertyName}' in {sourceType.Name}.");

			// check current value
			var checkValueFunc = new Func<TValue, TValue, bool>((x, y) =>
			{
				if (x != null)
					return y != null && x.Equals(y);
				return y == null;
			});
			var value = propertyGetter(source);
			if (checkValueFunc(value, targetValue))
				return true;

			// check timeout
			if (timeoutMillis == 0)
				return false;

			// wait for property change
			var cancellationTokenSource = new CancellationTokenSource();
			var eventHandler = new PropertyChangedEventHandler((_, e) =>
			{
				if (e.PropertyName == propertyName && checkValueFunc(propertyGetter(source), targetValue))
					cancellationTokenSource.Cancel();
			});
			source.PropertyChanged += eventHandler;
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
				source.PropertyChanged -= eventHandler;
			}

			// check final value
			return checkValueFunc(propertyGetter(source), targetValue);
		}
	}
}
