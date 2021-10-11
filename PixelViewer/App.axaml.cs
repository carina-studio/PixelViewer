using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.Configuration;
using CarinaStudio.ViewModels;
using Microsoft.Extensions.Logging;
using NLog;
using System;
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


        // Prepare starting.
        protected override async Task OnPrepareStartingAsync()
        {
			// call base
			await base.OnPrepareStartingAsync();

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
				settings.ResetValue(SettingKeys.DarkMode);
		}
#pragma warning restore CS0612
	}
}
