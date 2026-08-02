using ASControls = CarinaStudio.AppSuite.Controls;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.AppSuite.Media;
using CarinaStudio.AppSuite.Product;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Controls;
using CarinaStudio.ViewModels;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using CarinaStudio.AppSuite.ViewModels;
using System.IO;
using System.Threading;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Application.
	/// </summary>
	class App : AppSuiteApplication
	{
		// Source of change list.
		class ChangeListSource(App app) : DocumentSource(app)
		{
			public override IList<ApplicationCulture> SupportedCultures =>
			[
				ApplicationCulture.EN_US,
				ApplicationCulture.ZH_CN,
				ApplicationCulture.ZH_TW
			];
			public override Uri Uri => this.Culture switch
			{
				ApplicationCulture.ZH_CN => this.Application.CreateAvaloniaResourceUri("/ChangeList-zh-CN.md"),
				ApplicationCulture.ZH_TW => this.Application.CreateAvaloniaResourceUri("/ChangeList-zh-TW.md"),
				_ => this.Application.CreateAvaloniaResourceUri("/ChangeList.md"),
			};
		}
		
		
		// Source of document of Privacy Policy.
		class PrivacyPolicySource : DocumentSource
		{
			public PrivacyPolicySource(App app) : base(app)
			{ 
				this.SetToCurrentCulture();
			}
			public override IList<ApplicationCulture> SupportedCultures =>
			[
				ApplicationCulture.EN_US,
				ApplicationCulture.ZH_CN,
				ApplicationCulture.ZH_TW,
			];
			public override Uri Uri => this.Culture switch
			{
				ApplicationCulture.ZH_CN => new($"avares://{Assembly.GetExecutingAssembly().GetName().Name}/Resources/PrivacyPolicy-zh-CN.md"),
				ApplicationCulture.ZH_TW => new($"avares://{Assembly.GetExecutingAssembly().GetName().Name}/Resources/PrivacyPolicy-zh-TW.md"),
				_ => new($"avares://{Assembly.GetExecutingAssembly().GetName().Name}/Resources/PrivacyPolicy.md"),
			};
		}


		// Source of document of User Agreement.
		class UserAgreementSource : DocumentSource
		{
			public UserAgreementSource(App app) : base(app)
			{ 
				this.SetToCurrentCulture();
			}
			public override IList<ApplicationCulture> SupportedCultures =>
			[
				ApplicationCulture.EN_US,
				ApplicationCulture.ZH_CN,
				ApplicationCulture.ZH_TW,
			];
			public override Uri Uri => this.Culture switch
			{
				ApplicationCulture.ZH_CN => new($"avares://{Assembly.GetExecutingAssembly().GetName().Name}/Resources/UserAgreement-zh-CN.md"),
				ApplicationCulture.ZH_TW => new($"avares://{Assembly.GetExecutingAssembly().GetName().Name}/Resources/UserAgreement-zh-TW.md"),
				_ => new($"avares://{Assembly.GetExecutingAssembly().GetName().Name}/Resources/UserAgreement.md"),
			};
		}


		// Constants.
		const string FilePathKey = "FilePath";


		// Static fields.
		static readonly SettingKey<bool> LegacySaveRenderedImageWithOrientationKey = new("SaveRenderedImageWithOrientation", true);
		static readonly SettingKey<string> LegacyScreenColorSpaceKey = new("ScreenColorSpace", "");
		static readonly SettingKey<string> LegacyYuvConversionModeKey = new("YuvConversionMode", "");


		// Constructor.
		public App()
        {
			this.Name = "PixelViewer";
        }


		/// <inheritdoc/>
		protected override bool AllowMultipleMainWindows => true;
		
		
		/// <inheritdoc/>
		public override DocumentSource ChangeList => new ChangeListSource(this);


		/// <inheritdoc/>
		public override bool CheckApplicationCultureSupport(ApplicationCulture culture) =>
			culture != ApplicationCulture.JA_JP && base.CheckApplicationCultureSupport(culture);


		/// <inheritdoc/>
        public override ApplicationInfo CreateApplicationInfoViewModel() => new AppInfo();


        /// <inheritdoc/>
        public override ApplicationOptions CreateApplicationOptionsViewModel() => new AppOptions();


        /// <inheritdoc/>
        public override int DefaultLogOutputTargetPort => 5570;


		/// <inheritdoc/>
        public override IEnumerable<ExternalDependency> ExternalDependencies => Array.Empty<ExternalDependency>();


		/// <inheritdoc/>
        public override int ExternalDependenciesVersion => 1;


        // Initialize.
        public override void Initialize() => AvaloniaXamlLoader.Load(this);
        
        
        /// <summary>
        /// Check whether PixelViewer Pro is activated or not.
        /// </summary>
        public bool IsProVersionActivated { get; private set; }


		// Application entry point.
		[STAThread]
		public static void Main(string[] args) =>
			BuildApplicationAndStart<App>(args, argsParser: ParseArguments);


		// Create main window.
		protected override CarinaStudio.AppSuite.Controls.MainWindow OnCreateMainWindow() => new MainWindow();


		// Create view-model for main window.
		protected override ViewModel OnCreateMainWindowViewModel(JsonElement? savedState) => new Workspace(savedState).Also(it =>
		{
			this.LaunchOptions.TryGetValue(FilePathKey, out var filePath);
			if (filePath != null || it.Sessions.IsEmpty())
				it.ActivatedSession = it.CreateAndAttachSession(filePath as string);
		});


		/// <inheritdoc/>
		protected override async Task OnImportApplicationDataAsync(string directory)
		{
			// call base
			await base.OnImportApplicationDataAsync(directory);
			
			// prepare
			if (Platform.IsMacOS)
			{
				if (directory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
				    || directory.EndsWith(".app/", StringComparison.OrdinalIgnoreCase))
				{
					directory = Path.Combine(directory, "Contents", "MacOS");
				}
			}
			Task ImportDirectoryAsync(string directoryName, string usage) => Task.Run(() =>
			{
				var srcDirectory = Path.Combine(directory, directoryName);
				var destDirectory = Path.Combine(this.RootPrivateDirectoryPath, directoryName);
				if (Directory.Exists(srcDirectory))
				{
					// backup current directory
					this.Logger.LogWarning($"Start backing up {usage} directory");
					var backupDestDirectory = Path.Combine(this.RootPrivateDirectoryPath, directoryName + ".Backup");
					if (Directory.Exists(backupDestDirectory))
						Directory.Delete(backupDestDirectory, true);
					if (Directory.Exists(destDirectory))
						Directory.Move(destDirectory, backupDestDirectory);
					this.Logger.LogWarning($"Complete backing up {usage} directory");
					
					// import
					this.Logger.LogWarning($"Start importing {usage}");
					if (!Directory.Exists(destDirectory))
						Directory.CreateDirectory(destDirectory);
					var fileCount = 0;
					foreach (var srcFilePath in Directory.EnumerateFiles(srcDirectory))
					{
						var destFilePath = Path.Combine(destDirectory, Path.GetFileName(srcFilePath));
						File.Copy(srcFilePath, destFilePath, true);
						++fileCount;
					}
					this.Logger.LogWarning($"Complete importing {usage}, {fileCount} file(s) imported");
				}
				else
					this.Logger.LogDebug($"Directory of {usage} not found for importing.");
			});
			
			// import rendering profiles
			await ImportDirectoryAsync("Profiles", "rendering profiles");
			
			// import color spaces
			await ImportDirectoryAsync("ColorSpaces", "color spaces");
		}


		// Load default strings.
        protected override IResourceProvider OnLoadDefaultStringResource()
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
				this.Logger.LogWarning("No string resources for {cultureInfo}", cultureInfo);
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
					this.Logger.LogWarning("No platform-specific string resources for {cultureInfo}", cultureInfo);
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
					this.Logger.LogWarning("No platform-specific string resources for {cultureInfo}", cultureInfo);
			}
			return resources;
		}


		// Load theme.
        protected override IStyle OnLoadTheme(ThemeMode themeMode)
        {
	        var baseUri = $"avares://{this.Assembly.GetName().Name}/";
	        var baseStyles = new StyleInclude(new Uri(baseUri)) { Source = new("/Styles/Base.axaml", UriKind.Relative) };
	        var styles = themeMode switch
			{
				ThemeMode.Light => new StyleInclude(new Uri(baseUri)) { Source = new("/Styles/Light.axaml", UriKind.Relative) },
				_ => new StyleInclude(new Uri(baseUri)) { Source = new("/Styles/Dark.axaml", UriKind.Relative) },
			};
			return new Styles().Also(it =>
			{
				it.Add(baseStyles);
				it.Add(styles);
			});
		}


		// Called when user click the native menu item.
		void OnNativeMenuItemClick(object? sender, EventArgs e)
		{
			switch ((sender as NativeMenuItem)?.CommandParameter as string)
			{
				case "AppInfo":
					this.ShowApplicationInfoDialog();
					break;
				case "AppOptions":
					this.ShowApplicationOptionsDialog();
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
				this.ShowMainWindowAsync();

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
					this.Logger.LogError("No main window or workspace to handle new instance");
			}
		}


		// Prepare shutting down.
        protected override async Task OnPrepareShuttingDownAsync(ApplicationShutdownReason reason)
        {
			// wait for I/O completion
			var colorSpaceTasks = Media.ColorSpace.WaitForIOTasksAsync();
			var renderingProfileTasks = Media.Profiles.ImageRenderingProfiles.WaitForIOTasksAsync();
			if (reason == ApplicationShutdownReason.Critical)
				Task.WaitAll(colorSpaceTasks, renderingProfileTasks);
			else
				await Task.WhenAll(colorSpaceTasks, renderingProfileTasks);

			// call base
            await base.OnPrepareShuttingDownAsync(reason);
        }


		/// <inheritdoc/>
		protected override SplashWindowParams OnPrepareSplashWindow() => base.OnPrepareSplashWindow().Also((ref SplashWindowParams it) =>
		{
			var isDarkMode = this.EffectiveThemeMode == ThemeMode.Dark;
			it.AccentColor = Color.FromArgb(0xff, 0x50, 0xb2, 0x9b);
			it.BackgroundImageOpacity = 0.75;
			it.BackgroundImageUri = isDarkMode
				? new Uri($"avares://{this.Assembly.GetName().Name}/SplashWindowBackground-Dark.png")
				: new Uri($"avares://{this.Assembly.GetName().Name}/SplashWindowBackground-Light.png");
		});


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
			this.UpdateSplashWindowProgress(0.1);

			// setup native menu
			if (Platform.IsMacOS)
			{
				var app = (IAvaloniaApplication)this;
				NativeMenu.GetMenu(this)?.Let(menu =>
				{
					for (var i = menu.Items.Count - 1; i >= 0; --i)
					{
						var item = menu.Items[i];
						if (item is not NativeMenuItem menuItem)
							continue;
						switch (menuItem.CommandParameter as string)
						{
							case "AppInfo":
								menuItem.Icon = app.FindResourceOrDefault<IImage?>("Image/Icon.Information.Outline")?.ToNativeMenuItemIcon();
								break;
							default:
								break;
						}
					}
				});
			}
			
			// attach to product manager
			this.ProductManager.Let(it =>
			{
				if (!it.IsMock)
				{
					it.ProductActivationChanged += this.OnProductActivationChanged;
					if (it.IsProductActivated(Products.Professional))
					{
						this.IsProVersionActivated = true;
						this.OnPropertyChanged(nameof(IsProVersionActivated));
					}
				}
			});

			// initialize file formats
			Media.FileFormats.Initialize(this);

			// initialize file format parsers
			Media.FileFormatParsers.FileFormatParsers.Initialize(this);
			this.UpdateSplashWindowProgress(0.2);

			// initialize color spaces
			this.UpdateSplashWindowMessage(this.GetStringNonNull("App.InitializingColorSpaces"));
			await Media.ColorSpace.InitializeAsync(this);
			this.UpdateSplashWindowProgress(0.6);

			// initialize image renderers, must be done before initializing image rendering profiles which resolve their renderers by name
			await Media.ImageRenderers.ImageRenderers.InitializeAsync(this);

			// initialize image rendering profiles
			this.UpdateSplashWindowMessage(this.GetStringNonNull("App.InitializingImageRenderingProfiles"));
			await Media.Profiles.ImageRenderingProfiles.InitializeAsync(this);
			this.UpdateSplashWindowProgress(0.8);

			// check max rendered image memory usage
			this.Settings.GetValueOrDefault(SettingKeys.MaxRenderedImagesMemoryUsageMB).Let(mb =>
			{
				var upperBound = Environment.Is64BitProcess ? 8192 : 1324;
				if (mb > upperBound)
					this.Settings.SetValue(SettingKeys.MaxRenderedImagesMemoryUsageMB, upperBound);
			});

			// show main window
			if (!this.IsRestoringMainWindowsRequested)
				await this.ShowMainWindowAsync();
		}
		
		
		// Called when product activated/deactivated.
		void OnProductActivationChanged(IProductManager productManager, string productId, bool isActivated)
		{
			if (productId == Products.Professional && this.IsProVersionActivated != isActivated)
			{
				this.IsProVersionActivated = isActivated;
				this.OnPropertyChanged(nameof(IsProVersionActivated));
			}
		}


		///<inheritdoc/>
        protected override async Task<bool> OnRestoreMainWindowsAsync()
        {
            if (await base.OnRestoreMainWindowsAsync())
				return true;
			await this.ShowMainWindowAsync();
			return false;
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


		///<inheritdoc/>
        protected override bool OnTryExitingBackgroundMode()
        {
            if (base.OnTryExitingBackgroundMode())
				return true;
			if (this.MainWindows.IsEmpty())
				_ = this.ShowMainWindowAsync();
			return true;
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
						settings.SetValue(CarinaStudio.AppSuite.SettingKeys.Culture, ApplicationCulture.EN_US);
				});
			}

			// upgrade default color space
			if (oldVersion <= 4)
			{
				var name = settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName).Let(it => it switch
				{
					"Adobe-RGB" => "Adobe-RGB-1998",
					"BT.601" => "BT.601-625-line",
					_ => it,
				});
				settings.SetValue(SettingKeys.DefaultColorSpaceName, name);
			}

			// upgrade screen color space
			if (oldVersion <= 5)
			{
				settings.GetValueOrDefault(LegacyScreenColorSpaceKey).Let(it =>
				{
					switch (it)
					{
						case "DCI_P3":
							settings.SetValue(SettingKeys.ScreenColorSpaceName, Media.ColorSpace.DCI_P3.Name);
							break;
						case "Display_P3":
							settings.SetValue(SettingKeys.ScreenColorSpaceName, Media.ColorSpace.Display_P3.Name);
							break;
						case "Srgb":
							settings.SetValue(SettingKeys.ScreenColorSpaceName, Media.ColorSpace.Srgb.Name);
							break;
					}
				});
			}

			// fall back linear-sRGB to sRGB
			if (oldVersion <= 7)
			{
				if (settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName) == "Linear-sRGB")
					settings.SetValue(SettingKeys.DefaultColorSpaceName, Media.ColorSpace.Srgb.Name);
				if (settings.GetValueOrDefault(SettingKeys.ScreenColorSpaceName) == "Linear-sRGB")
					settings.SetValue(SettingKeys.ScreenColorSpaceName, Media.ColorSpace.Srgb.Name);
			}

			// upgrade theme mode
			if (oldVersion <= 1)
			{
				settings.GetValueOrDefault(SettingKeys.DarkMode).Let(it =>
				{
					settings.ResetValue(SettingKeys.DarkMode);
					if (!this.IsSystemThemeModeSupported)
						settings.SetValue(CarinaStudio.AppSuite.SettingKeys.ThemeMode, it ? ThemeMode.Dark : ThemeMode.Light);
				});
			}
			else if (oldVersion <= 3)
			{
				if (CarinaStudio.Platform.IsMacOS && settings.GetValueOrDefault(CarinaStudio.AppSuite.SettingKeys.ThemeMode) == ThemeMode.Light)
					settings.SetValue(CarinaStudio.AppSuite.SettingKeys.ThemeMode, ThemeMode.System);
			}

			// upgrade YUV conversion mode
			if (oldVersion <= 2)
			{
				settings.GetValueOrDefault(LegacyYuvConversionModeKey).Let(it =>
				{
					settings.ResetValue(LegacyYuvConversionModeKey);
					settings.SetValue(SettingKeys.DefaultYuvToBgraConversion, it switch
					{
						"ITU_R" => Media.YuvToBgraConverter.BT_601.Name,
						"NTSC" => Media.YuvToBgraConverter.BT_656.Name,
						_ => Media.YuvToBgraConverter.Default.Name,
					});
				});
			}

			// migrate save-with-orientation gate to the broader save-with-transformation gate
			if (oldVersion <= 8)
			{
				settings.SetValue(SettingKeys.SaveRenderedImageWithTransformation, settings.GetValueOrDefault(LegacySaveRenderedImageWithOrientationKey));
				settings.ResetValue(LegacySaveRenderedImageWithOrientationKey);
			}
		}
#pragma warning restore CS0612
		
		
		///<inheritdoc/>
		protected override async Task<bool> OnValidateApplicationDataImportAsync(string directory, CancellationToken cancellationToken)
		{
			if (!await base.OnValidateApplicationDataImportAsync(directory, cancellationToken))
				return false;
			if (Platform.IsMacOS)
			{
				if (directory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
				    || directory.EndsWith(".app/", StringComparison.OrdinalIgnoreCase))
				{
					directory = Path.Combine(directory, "Contents", "MacOS");
				}
			}
			return await Task.Run(() => File.Exists(Path.Combine(directory, "PixelViewer.dll")), cancellationToken);
		}


        // Parse argument.
        static int ParseArguments(string[] args, int index, IDictionary<string, object> launchOptions)
        {
	        var arg = args[index];
	        if (arg.Length > 0 && arg[0] != '-')
	        {
		        launchOptions[FilePathKey] = arg;
		        return 1;
	        }
	        return 0;
        }


		/// <inheritdoc/>
		public override DocumentSource PrivacyPolicy => new PrivacyPolicySource(this);


		/// <inheritdoc/>
		public override Version PrivacyPolicyVersion => new(1, 3);


		// Releasing type.
		public override ApplicationReleasingType ReleasingType => ApplicationReleasingType.Development;


		/// <inheritdoc/>
		protected override bool RestoreMainWindowsAfterSystemReboot => true;
		
		
		// Version of settings.
		protected override int SettingsVersion => 9;


		/// <inheritdoc/>
		public override async Task ShowApplicationOptionsDialogAsync(Avalonia.Controls.Window? owner, string? sectionName = null)
		{
			owner?.ActivateAndBringToFront();
			var dialog = new Controls.ApplicationOptionsDialog();
			if (Enum.TryParse<Controls.ApplicationOptionsDialogSection>(sectionName, out var section))
				dialog.InitialFocusedSection = section;
			var result = await (owner != null
				? dialog.ShowDialog<ApplicationOptionsDialogResult>(owner)
				: dialog.ShowDialog<ApplicationOptionsDialogResult>());
			switch (result)
			{
				case ApplicationOptionsDialogResult.RestartApplicationNeeded:
					this.Logger.LogWarning("Restart application");
					this.Restart();
					break;
				case ApplicationOptionsDialogResult.RestartMainWindowsNeeded:
					this.Logger.LogWarning("Restart main windows");
					await this.RestartRootWindowsAsync();
					break;
			}
		}


		/// <summary>
		/// Show editor of application configuration.
		/// </summary>
		public void ShowConfigurationEditor()
		{
			var keys = new List<SettingKey>();
			keys.AddRange(SettingKey.GetDefinedKeys<CarinaStudio.AppSuite.ConfigurationKeys>());
			keys.AddRange(SettingKey.GetDefinedKeys<ConfigurationKeys>());
			new SettingsEditorDialog()
			{
				SettingKeys = keys,
				Settings = this.Configuration,
			}.ShowDialog(this.LatestActiveMainWindow);
		}


		/// <inheritdoc/>
		public override DocumentSource UserAgreement => new UserAgreementSource(this);


		/// <inheritdoc/>
		public override Version UserAgreementVersion => new(1, 8);
	}
}
