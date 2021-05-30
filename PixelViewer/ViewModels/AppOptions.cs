using Carina.PixelViewer.Configuration;
using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using CarinaStudio.Configuration;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// View-model for application options.
	/// </summary>
	class AppOptions : BaseViewModel
	{
		// Fields.
		bool isOriginallyDarkMode;


		/// <summary>
		/// Initialize new <see cref="AppOptions"/> instance.
		/// </summary>
		public AppOptions()
		{
			// create command
			this.OpenLinkCommand = ReactiveCommand.Create<string>(this.OpenLink);
			this.RestartMainWindowCommand = ReactiveCommand.Create(() =>
			{
				App.Current.RestartMainWindow();
				this.isOriginallyDarkMode = this.Settings.GetValueOrDefault(Settings.DarkMode);
				this.IsRestartingMainWindowNeededToApplyDarkMode = false;
				this.OnPropertyChanged(nameof(this.IsRestartingMainWindowNeededToApplyDarkMode));
			});

			// get initial settings state
			this.isOriginallyDarkMode = this.Settings.GetValueOrDefault(Settings.DarkMode);

			// get version name
			this.UpdateVersionString();
		}


		/// <summary>
		/// List of available values for <see cref="DefaultImageDimensionsEvaluationAspectRatio"/>.
		/// </summary>
		public IEnumerable<AspectRatio> AspectRatios { get; } = (AspectRatio[])Enum.GetValues(typeof(AspectRatio));


		/// <summary>
		/// Select interface language automatically.
		/// </summary>
		public bool AutoSelectLanguage
		{
			get => this.Settings.GetValueOrDefault(Settings.AutoSelectLanguage);
			set => this.Settings.SetValue(Settings.AutoSelectLanguage, value);
		}


		/// <summary>
		/// Use dark interface mode.
		/// </summary>
		public bool DarkMode
		{
			get => this.Settings.GetValueOrDefault(Settings.DarkMode);
			set => this.Settings.SetValue(Settings.DarkMode, value);
		}


		/// <summary>
		/// Default aspect ratio for image dimensions evaluation.
		/// </summary>
		public AspectRatio DefaultImageDimensionsEvaluationAspectRatio
		{
			get => this.Settings.GetValueOrDefault(Settings.DefaultImageDimensionsEvaluationAspectRatio);
			set => this.Settings.SetValue(Settings.DefaultImageDimensionsEvaluationAspectRatio, value);
		}


		/// <summary>
		/// Default <see cref="IImageRenderer"/>.
		/// </summary>
		public IImageRenderer DefaultImageRenderer
		{
			get
			{
				if (ImageRenderers.TryFindByFormatName(this.Settings.GetValueOrDefault(Settings.DefaultImageRendererFormatName), out var renderer))
					return renderer.AsNonNull();
				return ImageRenderers.All[0];
			}
			set => this.Settings.SetValue(Settings.DefaultImageRendererFormatName, value.Format.Name);
		}


		/// <summary>
		/// Evaluate image dimensions after changing image renderer.
		/// </summary>
		public bool EvaluateImageDimensionsAfterChangingRenderer
		{
			get => this.Settings.GetValueOrDefault(Settings.EvaluateImageDimensionsAfterChangingRenderer);
			set => this.Settings.SetValue(Settings.EvaluateImageDimensionsAfterChangingRenderer, value);
		}


		/// <summary>
		/// Evaluate image dimensions after opening file.
		/// </summary>
		public bool EvaluateImageDimensionsAfterOpeningSourceFile
		{
			get => this.Settings.GetValueOrDefault(Settings.EvaluateImageDimensionsAfterOpeningSourceFile);
			set => this.Settings.SetValue(Settings.EvaluateImageDimensionsAfterOpeningSourceFile, value);
		}


		/// <summary>
		/// Check whether restarting main window is needed to apply dark mode change or not.
		/// </summary>
		public bool IsRestartingMainWindowNeededToApplyDarkMode { get; private set; } = false;


		/// <summary>
		/// Maximum memory usage for rendering images.
		/// </summary>
		public long MaxRenderedImagesMemoryUsageMB
		{
			get => this.Settings.GetValueOrDefault(Settings.MaxRenderedImagesMemoryUsageMB);
			set => this.Settings.SetValue(Settings.MaxRenderedImagesMemoryUsageMB, value);
		}


		// Called when setting changed.
		protected override void OnSettingChanged(SettingKey key)
		{
			base.OnSettingChanged(key);
			if (key == Settings.AutoSelectLanguage)
			{
				this.OnPropertyChanged(nameof(this.AutoSelectLanguage));
				this.UpdateVersionString();
			}
			else if (key == Settings.DarkMode)
			{
				this.OnPropertyChanged(nameof(this.DarkMode));
				this.IsRestartingMainWindowNeededToApplyDarkMode = (this.Settings.GetValueOrDefault(Settings.DarkMode) != this.isOriginallyDarkMode);
				this.OnPropertyChanged(nameof(this.IsRestartingMainWindowNeededToApplyDarkMode));
			}
			else if (key == Settings.DefaultImageDimensionsEvaluationAspectRatio)
				this.OnPropertyChanged(nameof(this.DefaultImageDimensionsEvaluationAspectRatio));
			else if (key == Settings.DefaultImageRendererFormatName)
				this.OnPropertyChanged(nameof(this.DefaultImageRenderer));
			else if (key == Settings.EvaluateImageDimensionsAfterChangingRenderer)
				this.OnPropertyChanged(nameof(this.EvaluateImageDimensionsAfterChangingRenderer));
			else if (key == Settings.EvaluateImageDimensionsAfterOpeningSourceFile)
				this.OnPropertyChanged(nameof(this.EvaluateImageDimensionsAfterOpeningSourceFile));
			else if (key == Settings.MaxRenderedImagesMemoryUsageMB)
				this.OnPropertyChanged(nameof(this.MaxRenderedImagesMemoryUsageMB));
			else if (key == Settings.YuvConversionMode)
				this.OnPropertyChanged(nameof(this.YuvConversionMode));
		}


		// Open given URI in browser.
		void OpenLink(string uri)
		{
			try
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					Process.Start(new ProcessStartInfo("cmd", $"/c start {uri}")
					{
						CreateNoWindow = true
					});
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
					Process.Start("xdg-open", uri);
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
					Process.Start("open", uri);
			}
			catch(Exception ex)
			{
				this.Logger.Error(ex, $"Unable to open '{uri}'");
			}
		}


		/// <summary>
		/// Command to open given URI in browser.
		/// </summary>
		public ICommand OpenLinkCommand { get; }


		/// <summary>
		/// Command to restart main window.
		/// </summary>
		public ICommand RestartMainWindowCommand { get; }


		// Update version string.
		void UpdateVersionString()
		{
			var version = Assembly.GetExecutingAssembly().GetName().Version;
			this.VersionString = string.Format(App.Current.GetStringNonNull("AppOptionsControl.VersionName"), version);
			this.OnPropertyChanged(nameof(this.VersionString));
		}


		/// <summary>
		/// Reset to default image renderer after opening file.
		/// </summary>
		public bool UseDefaultImageRendererAfterOpeningSourceFile
		{
			get => this.Settings.GetValueOrDefault(Settings.UseDefaultImageRendererAfterOpeningSourceFile);
			set => this.Settings.SetValue(Settings.UseDefaultImageRendererAfterOpeningSourceFile, value);
		}


		/// <summary>
		/// Get application version name.
		/// </summary>
		public string VersionString { get; private set; } = "";


		/// <summary>
		/// YUV to RGB conversion mode.
		/// </summary>
		public YuvConversionMode YuvConversionMode
		{
			get => this.Settings.GetValueOrDefault(Settings.YuvConversionMode);
			set => this.Settings.SetValue(Settings.YuvConversionMode, value);
		}


		/// <summary>
		/// List of available values for <see cref="YuvConversionMode"/>.
		/// </summary>
		public IEnumerable<YuvConversionMode> YuvConversionModes { get; } = (YuvConversionMode[])Enum.GetValues(typeof(YuvConversionMode));
	}
}
