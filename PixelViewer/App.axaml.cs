using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.ReactiveUI;
using NLog;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Application.
	/// </summary>
	class App : Application
	{
		// Static fields.
		static readonly ILogger Logger = LogManager.GetCurrentClassLogger();


		// Fields.
		ResourceInclude? stringResources;
		ResourceInclude? stringResourcesLinux;


		// Avalonia configuration, don't remove; also used by visual designer.
		static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.UseReactiveUI()
			.LogToTrace();


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

			// load XAML
			AvaloniaXamlLoader.Load(this);

			// attach to settings
			this.Settings.PropertyChanged += (_, e) => this.OnSettingsChanged(e.PropertyName);

			// load strings
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				this.Resources.MergedDictionaries.Add(new ResourceInclude()
				{
					Source = new Uri($"avares://PixelViewer/Strings/Default-Linux.xaml")
				});
			}
			this.UpdateStringResources();

			// load styles
			var style = new StyleInclude(new Uri("avares://PixelViewer/")).Also((it) =>
			{
				it.Source = this.Settings.DarkMode switch
				{
					true => new Uri("avares://PixelViewer/Styles/Dark.xaml"),
					_ => new Uri("avares://PixelViewer/Styles/Light.xaml"),
				};
			});
			this.Styles.Add(style);
		}


		// Application entry point.
		[STAThread]
		public static void Main(string[] args)
		{
			Logger.Info("Start");
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
			App.Current.Settings.Save();
		}


		// Called when framework initialization completed.
		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
				desktop.MainWindow = new MainWindow();
			base.OnFrameworkInitializationCompleted();
		}


		// Called when settings changed.
		void OnSettingsChanged(string propertyName)
		{
			switch (propertyName)
			{
				case nameof(Settings.AutoSelectLanguage):
					this.UpdateStringResources();
					break;
			}
		}


		/// <summary>
		/// Get application settings.
		/// </summary>
		public Settings Settings { get; } = Settings.Default;


		// Update string resource according to settings.
		void UpdateStringResources()
		{
			if (this.Settings.AutoSelectLanguage)
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
	}
}
