using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.ReactiveUI;
using Carina.PixelViewer.Configuration;
using Carina.PixelViewer.Controls;
using Carina.PixelViewer.Input;
using Carina.PixelViewer.Threading;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Threading;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Application.
	/// </summary>
	class App : Application, IThreadDependent
	{
		// Parsed options of launching application.
		struct LaunchOptions
		{
			// Fields.
			public string? FileName;

			// Try parsing options from arguments.
			public static bool TryParse(IList<string> args, out LaunchOptions options)
			{
				options = new LaunchOptions();
				if (args.IsEmpty())
					return true;
				if (args.Count == 1)
				{
					options.FileName = args[0];
					return true;
				}
				return false;
			}
		}


		// Constants.
		const string PIPE_NAME = "PixelViewer-Pipe";


		// Static fields.
		static string[] Arguments = new string[0];
		static readonly ILogger Logger = LogManager.GetCurrentClassLogger();


		// Fields.
		bool isRestartMainWindowRequested;
		bool isServer;
		LaunchOptions launchOptions;
		MainWindow? mainWindow;
		readonly CancellationTokenSource serverCancellationTokenSource = new CancellationTokenSource();
		NamedPipeServerStream? serverStream;
		volatile Settings? settings;
		string settingsFilePath = "";
		ResourceInclude? stringResources;
		ResourceInclude? stringResourcesLinux;
		StyleInclude? stylesDark;
		StyleInclude? stylesLight;
		volatile SynchronizationContext? syncContext;
		Workspace? workspace;


		// Avalonia configuration, don't remove; also used by visual designer.
		static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.UseReactiveUI()
			.LogToTrace();


		// Check application update.
		void CheckApplicationUpdate()
		{
#if MSSTORE

#else

#endif
		}


		/// <summary>
		/// Get current <see cref="CultureInfo"/>.
		/// </summary>
		public CultureInfo CultureInfo { get; private set; } = CultureInfo.CurrentCulture;


		/// <summary>
		/// Get <see cref="App"/> instance for current process.
		/// </summary>
		public static new App Current
		{
			get => (App)Application.Current;
		}


		// Create server pipe stream.
		bool CreateServerStream(bool printErrorLog = true)
		{
			if (this.serverStream != null)
				return true;
			try
			{
				this.serverStream = new NamedPipeServerStream(PIPE_NAME, PipeDirection.In, 1);
				Logger.Warn("Server stream created");
				return true;
			}
			catch (Exception ex)
			{
				if (printErrorLog)
					Logger.Error(ex, "Unable to create server stream");
				return false;
			}
		}


		/// <summary>
		/// Path of directory of application.
		/// </summary>
		public string Directory { get; } = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) ?? throw new Exception("Unable to get application directory.");


		/// <summary>
		/// Get string for current locale.
		/// </summary>
		/// <param name="key">Key of string.</param>
		/// <param name="defaultValue">Default value.</param>
		/// <returns>String or default value.</returns>
		public string? GetString(string key, string? defaultValue = null)
		{
			if (this.Resources.TryGetResource($"String.{key}", out var value) && value is string str)
				return str;
			return defaultValue;
		}


		/// <summary>
		/// Get non-null string for current locale.
		/// </summary>
		/// <param name="key">Key of string.</param>
		/// <param name="defaultValue">Default value.</param>
		/// <returns>String or default value.</returns>
		public string GetStringNonNull(string key, string defaultValue = "") => this.GetString(key) ?? defaultValue;


		// Handle client connection from pipe.
		async void HandleClientConnection()
		{
			if (this.serverStream == null)
			{
				Logger.Error("No server stream");
				return;
			}
			try
			{
				// wait for connection
				Logger.Warn("Start waiting for client connection");
				await this.serverStream.WaitForConnectionAsync(this.serverCancellationTokenSource.Token);

				// read arguments and parse as options from client
				Logger.Warn("Start reading arguments from client");
				var launchOptions = await Task.Run(() =>
				{
					using var reader = new BinaryReader(this.serverStream, Encoding.UTF8);
					var argCount = Math.Max(0, reader.ReadInt32());
					var argList = new List<string>(argCount);
					for (var i = argCount; i > 0; --i)
						argList.Add(reader.ReadString());
					if (!LaunchOptions.TryParse(argList, out var launchOptions))
						Logger.Error($"Invalid arguments passing from client: {argList.ContentToString()}");
					return launchOptions;
				});

				// activate main window
				this.mainWindow?.Let((mainWindow) =>
				{
					if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
						mainWindow.WindowState = this.Settings.GetValueOrDefault(Settings.MainWindowState);
					mainWindow.ActivateAndBringToFront();
				});

				// open file or activate existent session
				if (launchOptions.FileName != null && this.workspace != null)
				{
					var existentSession = this.workspace.Sessions.First((it) => it.SourceFileName == launchOptions.FileName);
					if (existentSession != null)
						this.workspace.ActivateSession(existentSession);
					else
					{
						var emptySession = this.workspace.Sessions.First((it) => !it.IsSourceFileOpened && it.SourceFileName == null);
						if (emptySession == null)
							this.workspace.CreateSession(launchOptions.FileName);
						else if (emptySession.OpenSourceFileCommand.TryExecute(launchOptions.FileName))
							this.workspace.ActivateSession(emptySession);
						else
						{
							Logger.Error($"Unable to open '{launchOptions.FileName}' by {emptySession}");
							this.workspace.CreateSession(launchOptions.FileName);
						}
					}
				}
			}
			catch (Exception ex)
			{
				if (!this.serverCancellationTokenSource.IsCancellationRequested)
					Logger.Error(ex, "Error occurred while waiting for client connection");
			}
			finally
			{
				// close server stream
				this.serverStream.Close();
				this.serverStream = null;

				// handle next connection
				if (!this.serverCancellationTokenSource.IsCancellationRequested)
				{
					this.SynchronizationContext.Post(() =>
					{
						if (this.CreateServerStream())
							this.HandleClientConnection();
					});
				}
			}
		}


		// Initialize.
		public override void Initialize()
		{
			// setup global exception handler
			AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			{
				var exceptionObj = e.ExceptionObject;
				if (exceptionObj is Exception exception)
					Logger.Fatal(exception, "***** Unhandled application exception *****");
				else
					Logger.Fatal($"***** Unhandled application exception ***** {exceptionObj}");
			};

			// start server or send arguments to server
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				// [workaround] treat process as client first becase limitation of max server instance seems not working on Linux
				if (this.SendArgumentsToServer())
					return;
			}
			if (this.CreateServerStream(false))
			{
				this.isServer = true;
				this.HandleClientConnection();
			}
			else
			{
				this.SendArgumentsToServer();
				return;
			}

			// load XAML
			AvaloniaXamlLoader.Load(this);
		}


		// Application entry point.
		[STAThread]
		public static void Main(string[] args)
		{
			Logger.Info("Start");

			// start application
			Arguments = args;
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

			// release app resources
			App.Current.Let((app) =>
			{
				// dispose workspace
				app.workspace?.Dispose();

				// close pipe server
				app.serverCancellationTokenSource.Cancel();
				try
				{
					app.serverStream?.Close();
				}
				catch
				{ }
				app.serverStream = null;
			});

			Logger.Info("Stop");
		}


		// Called when framework initialization completed.
		public override async void OnFrameworkInitializationCompleted()
		{
			// call base
			base.OnFrameworkInitializationCompleted();

			// get synchronization context
			this.syncContext = SynchronizationContext.Current;

			// load settings
			Logger.Warn("Start loading settings");
			this.settingsFilePath = Path.Combine(this.Directory, "Settings.json");
			this.settings = new Settings();
			try
			{
				await this.settings.LoadAsync(this.settingsFilePath);
				Logger.Warn("Settings loaded");
			}
			catch(Exception ex)
			{
				Logger.Error(ex, "Unable to load settings");
			}

			// attach to settings
			this.Settings.SettingChanged += (_, e) => this.OnSettingChanged(e.Key);

			// load strings
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				this.Resources.MergedDictionaries.Add(new ResourceInclude()
				{
					Source = new Uri($"avares://PixelViewer/Strings/Default-Linux.xaml")
				});
			}
			this.UpdateStringResources();

			// show main window
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				// setup shutdown mode
				desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

				// parse launch options
				if (!LaunchOptions.TryParse(desktop.Args, out this.launchOptions))
					Logger.Error($"Invalid arguments: {desktop.Args.ContentToString()}");

				// show main window
				if (this.isServer)
				{
					this.workspace = new Workspace().Also((it) =>
					{
						// create first session
						it.ActivateSession(it.CreateSession(this.launchOptions.FileName));
					});
					this.SynchronizationContext.Post(this.ShowMainWindow);
				}
				else
				{
					Logger.Warn("Process is not a server, shutdown now");
					this.SynchronizationContext.Post(() => desktop.Shutdown());
				}
			}
		}


		// Called when main window closed.
		async void OnMainWindowClosed()
		{
			Logger.Warn("Main window closed");

			// detach from main window
			this.mainWindow = this.mainWindow?.Let((it) =>
			{
				it.DataContext = null;
				return (MainWindow?)null;
			});

			// save settings
			Logger.Warn("Start saving settings");
			try
			{
				await this.Settings.SaveAsync(this.settingsFilePath);
				Logger.Warn("Settings saved");
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Unable to save settings");
			}

			// restart main window
			if (this.isRestartMainWindowRequested)
			{
				Logger.Warn("Restart main window");
				this.isRestartMainWindowRequested = false;
				this.SynchronizationContext.Post(this.ShowMainWindow);
				return;
			}

			// shutdown application
			if (App.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
			{
				Logger.Warn("Shutdown");
				desktopLifetime.Shutdown();
			}
		}


		// Called when setting changed.
		void OnSettingChanged(SettingKey key)
		{
			if (key == Settings.AutoSelectLanguage)
				this.UpdateStringResources();
		}


		/// <summary>
		/// Restart main window.
		/// </summary>
		public void RestartMainWindow()
		{
			this.VerifyAccess();
			if (this.isRestartMainWindowRequested)
				return;
			if (this.mainWindow != null)
			{
				Logger.Warn("Request restarting main window");
				this.isRestartMainWindowRequested = true;
				this.mainWindow.Close();
			}
			else
			{
				Logger.Warn("No main window to restart, show directly");
				this.ShowMainWindow();
			}
		}


		// Try sending arguments to server.
		bool SendArgumentsToServer()
		{
			try
			{
				// connect
				Logger.Warn("Try connect to server");
				using var clientStream = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out);
				clientStream.Connect(500);

				// send arguments
				Logger.Warn("Send application arguments to pipe server");
				using var writer = new BinaryWriter(clientStream);
				writer.Write(Arguments.Length);
				foreach (var arg in Arguments)
					writer.Write(arg);
				return true;
			}
			catch (TimeoutException)
			{
				Logger.Warn("Unable to connect to server");
				return false;
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Unable to send application arguments to pipe server");
				return false;
			}
		}


		/// <summary>
		/// Get application settings.
		/// </summary>
		public Settings Settings { get => this.settings ?? throw new InvalidOperationException("Application is not ready yet."); }


		// Create and show main window.
		void ShowMainWindow()
		{
			// check state
			if (this.mainWindow != null)
			{
				Logger.Error("Already shown main window");
				return;
			}

			// update styles
			this.UpdateStyles();

			// show main window
			this.mainWindow = new MainWindow().Also((it) =>
			{
				it.DataContext = this.workspace;
				it.Closed += (_, e) => this.OnMainWindowClosed();
			});
			Logger.Warn("Show main window");
			this.mainWindow.Show();
		}


		/// <summary>
		/// Synchronization context.
		/// </summary>
		public SynchronizationContext SynchronizationContext { get => this.syncContext ?? throw new InvalidOperationException("Application is not ready yet."); }


		// Update string resource according to settings.
		void UpdateStringResources()
		{
			if (this.Settings.GetValueOrDefault(Settings.AutoSelectLanguage))
			{
				// base resources
				var localeName = this.CultureInfo.Name;
				if (this.stringResources == null)
				{
					try
					{
						this.stringResources = new ResourceInclude()
						{
							Source = new Uri($"avares://PixelViewer/Strings/{localeName}.xaml")
						};
						_ = this.stringResources.Loaded; // trigger error if resource not found
						Logger.Info($"Load strings for {localeName}.");
					}
					catch
					{
						this.stringResources = null;
						Logger.Warn($"No strings for {localeName}.");
						return;
					}
					this.Resources.MergedDictionaries.Add(this.stringResources);
				}
				else if (!this.Resources.MergedDictionaries.Contains(this.stringResources))
					this.Resources.MergedDictionaries.Add(this.stringResources);

				// resources for specific OS
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					if (this.stringResourcesLinux == null)
					{
						try
						{
							this.stringResourcesLinux = new ResourceInclude()
							{
								Source = new Uri($"avares://PixelViewer/Strings/{localeName}-Linux.xaml")
							};
							_ = this.stringResourcesLinux.Loaded; // trigger error if resource not found
							Logger.Info($"Load strings (Linux) for {localeName}.");
						}
						catch
						{
							this.stringResourcesLinux = null;
							Logger.Warn($"No strings (Linux) for {localeName}.");
							return;
						}
						this.Resources.MergedDictionaries.Add(this.stringResourcesLinux);
					}
					else if (!this.Resources.MergedDictionaries.Contains(this.stringResourcesLinux))
						this.Resources.MergedDictionaries.Add(this.stringResourcesLinux);
				}
			}
			else
			{
				if (this.stringResources != null)
					this.Resources.MergedDictionaries.Remove(this.stringResources);
				if (this.stringResourcesLinux != null)
					this.Resources.MergedDictionaries.Remove(this.stringResourcesLinux);
			}
		}


		// Update styles according to settings.
		void UpdateStyles()
		{
			// select style
			var darkMode = this.Settings.GetValueOrDefault(Settings.DarkMode);
			var addingStyle = darkMode switch
			{
				true => this.stylesDark ?? new StyleInclude(new Uri("avares://PixelViewer/")).Also((it) =>
				{
					it.Source = new Uri("avares://PixelViewer/Styles/Dark.xaml");
					this.stylesDark = it;
				}),
				_ => this.stylesLight ?? new StyleInclude(new Uri("avares://PixelViewer/")).Also((it) =>
				{
					it.Source = new Uri("avares://PixelViewer/Styles/Light.xaml");
					this.stylesLight = it;
				}),
			};
			var removingStyle = darkMode switch
			{
				true => this.stylesLight,
				_ => this.stylesDark,
			};

			// update style
			if (removingStyle != null)
				this.Styles.Remove(removingStyle);
			if (!this.Styles.Contains(addingStyle))
				this.Styles.Add(addingStyle);
		}
	}
}
