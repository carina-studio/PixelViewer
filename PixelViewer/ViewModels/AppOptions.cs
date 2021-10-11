using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using CarinaStudio.Configuration;
using CarinaStudio.ViewModels;
using CarinaStudio.Windows.Input;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Input;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// View-model for application options.
	/// </summary>
	class AppOptions : ViewModel
	{
		// Fields.
		bool isOriginallyDarkMode;


		/// <summary>
		/// Initialize new <see cref="AppOptions"/> instance.
		/// </summary>
		public AppOptions(Workspace workspace) : base(workspace)
		{
			// create command
			this.RestartMainWindowCommand = new Command(() => App.Current.RestartMainWindows());

			// get initial settings state
			this.isOriginallyDarkMode = this.Settings.GetValueOrDefault(SettingKeys.DarkMode);

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
			get => this.Settings.GetValueOrDefault(SettingKeys.AutoSelectLanguage);
			set => this.Settings.SetValue<bool>(SettingKeys.AutoSelectLanguage, value);
		}


		/// <summary>
		/// Use dark interface mode.
		/// </summary>
		public bool DarkMode
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.DarkMode);
			set => this.Settings.SetValue<bool>(SettingKeys.DarkMode, value);
		}


		/// <summary>
		/// Default aspect ratio for image dimensions evaluation.
		/// </summary>
		public AspectRatio DefaultImageDimensionsEvaluationAspectRatio
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.DefaultImageDimensionsEvaluationAspectRatio);
			set => this.Settings.SetValue<AspectRatio>(SettingKeys.DefaultImageDimensionsEvaluationAspectRatio, value);
		}


		/// <summary>
		/// Default <see cref="IImageRenderer"/>.
		/// </summary>
		public IImageRenderer DefaultImageRenderer
		{
			get
			{
				if (ImageRenderers.TryFindByFormatName(this.Settings.GetValueOrDefault(SettingKeys.DefaultImageRendererFormatName), out var renderer))
					return renderer.AsNonNull();
				return ImageRenderers.All[0];
			}
			set => this.Settings.SetValue<string>(SettingKeys.DefaultImageRendererFormatName, value.Format.Name);
		}


		/// <summary>
		/// Evaluate image dimensions after changing image renderer.
		/// </summary>
		public bool EvaluateImageDimensionsAfterChangingRenderer
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterChangingRenderer);
			set => this.Settings.SetValue<bool>(SettingKeys.EvaluateImageDimensionsAfterChangingRenderer, value);
		}


		/// <summary>
		/// Evaluate image dimensions after opening file.
		/// </summary>
		public bool EvaluateImageDimensionsAfterOpeningSourceFile
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageDimensionsAfterOpeningSourceFile);
			set => this.Settings.SetValue<bool>(SettingKeys.EvaluateImageDimensionsAfterOpeningSourceFile, value);
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
			get => this.Settings.GetValueOrDefault(SettingKeys.MaxRenderedImagesMemoryUsageMB);
			set => this.Settings.SetValue<long>(SettingKeys.MaxRenderedImagesMemoryUsageMB, value);
		}


        // Called when setting changed.
		protected override void OnSettingChanged(SettingChangedEventArgs e)
		{
			base.OnSettingChanged(e);
			var key = e.Key;
			if (key == SettingKeys.AutoSelectLanguage)
			{
				this.OnPropertyChanged(nameof(this.AutoSelectLanguage));
				this.UpdateVersionString();
			}
			else if (key == SettingKeys.DarkMode)
			{
				this.OnPropertyChanged(nameof(this.DarkMode));
				this.IsRestartingMainWindowNeededToApplyDarkMode = (this.Settings.GetValueOrDefault(SettingKeys.DarkMode) != this.isOriginallyDarkMode);
				this.OnPropertyChanged(nameof(this.IsRestartingMainWindowNeededToApplyDarkMode));
			}
			else if (key == SettingKeys.DefaultImageDimensionsEvaluationAspectRatio)
				this.OnPropertyChanged(nameof(this.DefaultImageDimensionsEvaluationAspectRatio));
			else if (key == SettingKeys.DefaultImageRendererFormatName)
				this.OnPropertyChanged(nameof(this.DefaultImageRenderer));
			else if (key == SettingKeys.EvaluateImageDimensionsAfterChangingRenderer)
				this.OnPropertyChanged(nameof(this.EvaluateImageDimensionsAfterChangingRenderer));
			else if (key == SettingKeys.EvaluateImageDimensionsAfterOpeningSourceFile)
				this.OnPropertyChanged(nameof(this.EvaluateImageDimensionsAfterOpeningSourceFile));
			else if (key == SettingKeys.MaxRenderedImagesMemoryUsageMB)
				this.OnPropertyChanged(nameof(this.MaxRenderedImagesMemoryUsageMB));
			else if (key == SettingKeys.YuvConversionMode)
				this.OnPropertyChanged(nameof(this.YuvConversionMode));
		}


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
			get => this.Settings.GetValueOrDefault(SettingKeys.UseDefaultImageRendererAfterOpeningSourceFile);
			set => this.Settings.SetValue<bool>(SettingKeys.UseDefaultImageRendererAfterOpeningSourceFile, value);
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
			get => this.Settings.GetValueOrDefault(SettingKeys.YuvConversionMode);
			set => this.Settings.SetValue<YuvConversionMode>(SettingKeys.YuvConversionMode, value);
		}


		/// <summary>
		/// List of available values for <see cref="YuvConversionMode"/>.
		/// </summary>
		public IEnumerable<YuvConversionMode> YuvConversionModes { get; } = (YuvConversionMode[])Enum.GetValues(typeof(YuvConversionMode));
	}
}
