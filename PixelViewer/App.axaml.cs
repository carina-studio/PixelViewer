using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.ViewModels;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;
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
		static readonly SettingKey<string> LegacyYuvConversionModeKey = new SettingKey<string>("YuvConversionMode", "");
		static readonly Uri PreviewPackageManifestUri = new Uri("https://raw.githubusercontent.com/carina-studio/PixelViewer/master/PackageManifest-Preview.json");
		static readonly Uri StablePackageManifestUri = new Uri("https://raw.githubusercontent.com/carina-studio/PixelViewer/master/PackageManifest.json");


		// Constructor.
		public App()
        {
			this.Name = "PixelViewer";
        }


		/// <inheritdoc/>
		protected override bool AllowMultipleMainWindows => true;


        /// <inheritdoc/>
        public override int DefaultLogOutputTargetPort => 5570;


        // Initialize.
        public override void Initialize() => AvaloniaXamlLoader.Load(this);


		// Application entry point.
		[STAThread]
		public static void Main(string[] args)
		{
			BuildApplication<App>()
				.StartWithClassicDesktopLifetime(args);
		}


		// Create main window.
		protected override CarinaStudio.AppSuite.Controls.Window OnCreateMainWindow() => new MainWindow();


		// Create view-model for main window.
		protected override ViewModel OnCreateMainWindowViewModel(JsonElement? savedState) => new Workspace(savedState).Also(it =>
		{
			this.LaunchOptions.TryGetValue(FilePathKey, out var filePath);
			if (filePath != null || it.Sessions.IsEmpty())
				it.ActivatedSession = it.CreateAndAttachSession(filePath as string);
		});


		// Load default strings.
        protected override IResourceProvider? OnLoadDefaultStringResource()
        {
			var resources = this.LoadStringResource(new Uri("avares://PixelViewer/Strings/Default.xaml")).AsNonNull();
			if (CarinaStudio.Platform.IsLinux)
			{
				resources = new ResourceDictionary().Also(it =>
				{
					it.MergedDictionaries.Add(resources);
					it.MergedDictionaries.Add(this.LoadStringResource(new Uri("avares://PixelViewer/Strings/Default-Linux.xaml")).AsNonNull());
				});
			}
			else if (CarinaStudio.Platform.IsMacOS)
			{
				resources = new ResourceDictionary().Also(it =>
				{
					it.MergedDictionaries.Add(resources);
					it.MergedDictionaries.Add(this.LoadStringResource(new Uri("avares://PixelViewer/Strings/Default-OSX.xaml")).AsNonNull());
				});
			}
			return resources;
		}


		// Load strings.
		protected override IResourceProvider? OnLoadStringResource(CultureInfo cultureInfo)
		{
			var resources = this.LoadStringResource(new Uri($"avares://PixelViewer/Strings/{cultureInfo}.xaml"));
			if (resources == null)
			{
				this.Logger.LogWarning($"No string resources for {cultureInfo}");
				return null;
			}
			if (CarinaStudio.Platform.IsLinux)
			{
				var platformResources = this.LoadStringResource(new Uri($"avares://PixelViewer/Strings/{cultureInfo}-Linux.xaml"));
				if (platformResources != null)
				{
					resources = new ResourceDictionary().Also(it =>
					{
						it.MergedDictionaries.Add(resources);
						it.MergedDictionaries.Add(platformResources);
					});
				}
				else
					this.Logger.LogWarning($"No platform-specific string resources for {cultureInfo}");
			}
			else if (CarinaStudio.Platform.IsMacOS)
			{
				var platformResources = this.LoadStringResource(new Uri($"avares://PixelViewer/Strings/{cultureInfo}-OSX.xaml"));
				if (platformResources != null)
				{
					resources = new ResourceDictionary().Also(it =>
					{
						it.MergedDictionaries.Add(resources);
						it.MergedDictionaries.Add(platformResources);
					});
				}
				else
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


		// Called when user click the native menu item.
		void OnNativeMenuItemClick(object? sender, EventArgs e)
		{
			switch ((sender as NativeMenuItem)?.CommandParameter as string)
			{
				case "AppInfo":
					(this.LatestActiveMainWindow as MainWindow)?.ShowAppInfo();
					break;
				case "AppOptions":
					(this.LatestActiveMainWindow as MainWindow)?.ShowAppOptions();
					break;
				case "CheckForUpdate":
					(this.LatestActiveMainWindow as MainWindow)?.CheckForAppUpdate();
					break;
				case "Shutdown":
					this.Shutdown();
					break;
			}
		}


		// New instance launched.
		protected override void OnNewInstanceLaunched(IDictionary<string, object> launchOptions)
		{
			// call base
			base.OnNewInstanceLaunched(launchOptions);

			// get file path to open
			var filePath = launchOptions.TryGetValue(FilePathKey, out var value) ? value as string : null;

			// create new main window
			if (filePath == null || this.MainWindows.IsEmpty())
				this.ShowMainWindow();

			// open file
			if (filePath != null)
			{
				if (this.MainWindows[0].DataContext is Workspace workspace)
				{
					var emptySession = workspace.Sessions.FirstOrDefault(it => string.IsNullOrEmpty(it.SourceFileName));
					if (emptySession == null || !emptySession.OpenSourceFileCommand.TryExecute(filePath))
						workspace.CreateAndAttachSession(filePath);
					this.MainWindows[0].ActivateAndBringToFront();
				}
				else
					this.Logger.LogError("No main window or worksapce to handle new instance");
			}
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
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Unhandled error when launching");
				this.Shutdown();
				return;
			}

			// initialize file formats
			Media.FileFormats.Initialize(this);

			// initialize file format parsers
			Media.FileFormatParsers.FileFormatParsers.Initialize(this);

			// initialize image rendering profiles
			this.UpdateSplashWindowMessage(this.GetStringNonNull("App.InitializingImageRenderingProfiles"));
			await Media.Profiles.ImageRenderingProfiles.InitializeAsync(this);

			// check max rendered image memory usage
			this.Settings.GetValueOrDefault(SettingKeys.MaxRenderedImagesMemoryUsageMB).Let(mb =>
			{
				var upperBound = Environment.Is64BitProcess ? 8192 : 1324;
				if (mb > upperBound)
					this.Settings.SetValue<long>(SettingKeys.MaxRenderedImagesMemoryUsageMB, upperBound);
			});

			// show main window
			if (!this.IsRestoringMainWindowsRequested)
				this.ShowMainWindow();
		}


        ///<inheritdoc/>
        protected override bool OnSelectEnteringDebugMode()
        {
#if DEBUG
			return true;
#else
			return base.OnSelectEnteringDebugMode();
#endif
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
			else if (oldVersion <= 3)
			{
				if (CarinaStudio.Platform.IsMacOS && settings.GetValueOrDefault(CarinaStudio.AppSuite.SettingKeys.ThemeMode) == ThemeMode.Light)
					settings.SetValue<ThemeMode>(CarinaStudio.AppSuite.SettingKeys.ThemeMode, ThemeMode.System);
			}

			// upgrade YUV conversion mode
			if (oldVersion <= 2)
			{
				settings.GetValueOrDefault(LegacyYuvConversionModeKey).Let(it =>
				{
					settings.ResetValue(LegacyYuvConversionModeKey);
					settings.SetValue<string>(SettingKeys.DefaultYuvToBgraConversion, it switch
					{
						"ITU_R" => Media.YuvToBgraConverter.BT_601.Name,
						"NTSC" => Media.YuvToBgraConverter.BT_656.Name,
						_ => Media.YuvToBgraConverter.Default.Name,
					});
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


		/// <inheritdoc/>
		public override Version? PrivacyPolicyVersion => new Version(1, 1);


		// Releasing type.
		public override ApplicationReleasingType ReleasingType => ApplicationReleasingType.Preview;


		// Version of settings.
		protected override int SettingsVersion => 4;


		/// <inheritdoc/>
		public override Version? UserAgreementVersion => new Version(1, 0);
    }
}
