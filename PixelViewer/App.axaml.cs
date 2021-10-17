using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Carina.PixelViewer.Controls;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.ViewModels;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Application.
	/// </summary>
	class App : AppSuiteApplication
	{
		// Constants.
		const string FilePathKey = "FilePath";


		// Static fields.
		static readonly Uri PreviewPackageManifestUri = new Uri("https://raw.githubusercontent.com/carina-studio/PixelViewer/master/PackageManifest-Preview.json");
		static readonly Uri StablePackageManifestUri = new Uri("https://raw.githubusercontent.com/carina-studio/PixelViewer/master/PackageManifest.json");


		// Constructor.
		public App()
        {
			this.Name = "PixelViewer";
        }


		// Avalonia configuration, don't remove; also used by visual designer.
		static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();


		// Initialize.
		public override void Initialize() => AvaloniaXamlLoader.Load(this);


		// Application entry point.
		[STAThread]
		public static void Main(string[] args)
		{
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}


		// Create main window.
		protected override CarinaStudio.AppSuite.Controls.Window OnCreateMainWindow(object? param) => new MainWindow();


		// Create view-model for main window.
		protected override ViewModel OnCreateMainWindowViewModel(object? param) => new Workspace().Also(it =>
		{
			this.LaunchOptions.TryGetValue(FilePathKey, out var filePath);
			it.ActivatedSession = it.CreateSession(filePath as string);
		});


		// Load default strings.
        protected override IResourceProvider? OnLoadDefaultStringResource()
        {
			var resources = (IResourceProvider)new ResourceInclude()
			{
				Source = new Uri("avares://PixelViewer/Strings/Default.xaml")
			};
			if (CarinaStudio.Platform.IsLinux)
			{
				resources = new ResourceDictionary().Also(it =>
				{
					it.MergedDictionaries.Add(resources);
					it.MergedDictionaries.Add(new ResourceInclude()
					{
						Source = new Uri("avares://PixelViewer/Strings/Default-Linux.xaml")
					});
				});
			}
			return resources;
		}


		// Load strings.
        protected override IResourceProvider? OnLoadStringResource(CultureInfo cultureInfo)
        {
			var resources = (IResourceProvider?)null;
			try
			{
				resources = new ResourceInclude().Also(it =>
				{
					it.Source = new Uri($"avares://PixelViewer/Strings/{cultureInfo}.xaml");
					_ = it.Loaded;
				});
			}
			catch
			{
				this.Logger.LogWarning($"No string resources for {cultureInfo}");
				return null;
			}
			try
			{
				if (CarinaStudio.Platform.IsLinux)
				{
					var platformResources = new ResourceInclude().Also(it =>
					{
						it.Source = new Uri($"avares://PixelViewer/Strings/{cultureInfo}-Linux.xaml");
						_ = it.Loaded;
					});
					resources = new ResourceDictionary().Also(it =>
					{
						it.MergedDictionaries.Add(resources);
						it.MergedDictionaries.Add(platformResources);
					});
				}
			}
			catch
			{
				this.Logger.LogWarning($"No platform-specific string resources for {cultureInfo}");
			}
			return resources;
		}


		// Load theme.
        protected override IStyle? OnLoadTheme(ThemeMode themeMode)
        {
			var uri = themeMode switch
			{
				ThemeMode.Light => new Uri($"avares://PixelViewer/Styles/Light.xaml"),
				_ => new Uri($"avares://PixelViewer/Styles/Dark.xaml"),
			};
			return new StyleInclude(new Uri("avares://PixelViewer/")).Also(it =>
			{
				it.Source = uri;
				_ = it.Loaded;
			});
		}


		// New instance launched.
		protected override void OnNewInstanceLaunched(IDictionary<string, object> launchOptions)
		{
			// call base
			base.OnNewInstanceLaunched(launchOptions);

			// open image in current workspace
			if (this.MainWindows.IsNotEmpty() && this.MainWindows[0].DataContext is Workspace workspace)
			{
				if (launchOptions.TryGetValue(FilePathKey, out var value) && value is string filePath)
				{
					var emptySession = workspace.Sessions.FirstOrDefault(it => !it.IsSourceFileOpened);
					if (emptySession == null || !emptySession.OpenSourceFileCommand.TryExecute(filePath))
						workspace.CreateSession(filePath);
				}
				this.MainWindows[0].ActivateAndBringToFront();
			}
			else
				this.Logger.LogError("No main window or worksapce to handle new instance");
		}


		// Parse argument.
        protected override int OnParseArguments(string[] args, int index, IDictionary<string, object> launchOptions)
        {
			var arg = args[index];
			if (arg.Length > 0 && arg[0] != '-')
			{
				launchOptions[FilePathKey] = arg;
				return ++index;
			}
            return base.OnParseArguments(args, index, launchOptions);
        }


		// Prepare shutting down.
        protected override async Task OnPrepareShuttingDownAsync()
        {
			// wait for I/O of image rendering profiles
			await Media.Profiles.ImageRenderingProfiles.WaitForIOTasksAsync();

			// call base
            await base.OnPrepareShuttingDownAsync();
        }


        // Prepare starting.
        protected override async Task OnPrepareStartingAsync()
        {
            // call base
            try
            {
				await base.OnPrepareStartingAsync();
			}
			catch(Exception ex)
            {
				this.Logger.LogError(ex, "Unhandled error when launching");
				this.Shutdown();
				return;
            }

			// initialize file formats
			Media.FileFormats.Initialize(this);

			// initialize image rendering profiles
			this.UpdateSplashWindowMessage(this.GetStringNonNull("App.InitializingImageRenderingProfiles"));
			await Media.Profiles.ImageRenderingProfiles.InitializeAsync(this);

			// show main window
			this.ShowMainWindow();
        }


#pragma warning disable CS0612
		// Upgrade settings.
		protected override void OnUpgradeSettings(ISettings settings, int oldVersion, int newVersion)
        {
			// call base
            base.OnUpgradeSettings(settings, oldVersion, newVersion);

			// upgrade culture
			if (oldVersion <= 1)
			{
                settings.GetValueOrDefault(SettingKeys.AutoSelectLanguage).Let(it =>
                {
					settings.ResetValue(SettingKeys.AutoSelectLanguage);
					if (!it)
						settings.SetValue<ApplicationCulture>(CarinaStudio.AppSuite.SettingKeys.Culture, ApplicationCulture.EN_US);
				});
			}

			// upgrade theme mode
			if (oldVersion <= 1)
			{
				settings.GetValueOrDefault(SettingKeys.DarkMode).Let(it =>
				{
					settings.ResetValue(SettingKeys.DarkMode);
					if (!this.IsSystemThemeModeSupported)
						settings.SetValue<ThemeMode>(CarinaStudio.AppSuite.SettingKeys.ThemeMode, it ? ThemeMode.Dark : ThemeMode.Light);
				});
			}
		}
#pragma warning restore CS0612


		// URI of package manifest.
        public override Uri? PackageManifestUri
        {
			get => this.Settings.GetValueOrDefault(CarinaStudio.AppSuite.SettingKeys.AcceptNonStableApplicationUpdate)
				? PreviewPackageManifestUri
				: StablePackageManifestUri;
        }


        // Releasing type.
        public override ApplicationReleasingType ReleasingType => ApplicationReleasingType.Preview;
    }
}
