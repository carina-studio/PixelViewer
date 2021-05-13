using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
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
		readonly bool isOriginallyDarkMode;


		/// <summary>
		/// Initialize new <see cref="AppOptions"/> instance.
		/// </summary>
		public AppOptions()
		{
			// create command
			this.OpenLinkCommand = ReactiveCommand.Create<string>(this.OpenLink);

			// get initial settings state
			this.isOriginallyDarkMode = this.Settings.DarkMode;

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
			get => this.Settings.AutoSelectLanguage;
			set => this.Settings.AutoSelectLanguage = value;
		}


		/// <summary>
		/// Use dark interface mode.
		/// </summary>
		public bool DarkMode
		{
			get => this.Settings.DarkMode;
			set => this.Settings.DarkMode = value;
		}


		/// <summary>
		/// Default aspect ratio for image dimensions evaluation.
		/// </summary>
		public AspectRatio DefaultImageDimensionsEvaluationAspectRatio
		{
			get => this.Settings.DefaultImageDimensionsEvaluationAspectRatio;
			set => this.Settings.DefaultImageDimensionsEvaluationAspectRatio = value;
		}


		/// <summary>
		/// Default <see cref="IImageRenderer"/>.
		/// </summary>
		public IImageRenderer DefaultImageRenderer
		{
			get
			{
				if (ImageRenderers.TryFindByFormatName(this.Settings.DefaultImageRendererFormatName, out var renderer))
					return renderer.EnsureNonNull();
				return ImageRenderers.All[0];
			}
			set => this.Settings.DefaultImageRendererFormatName = value.Format.Name;
		}


		/// <summary>
		/// Evaluate image dimensions after changing image renderer.
		/// </summary>
		public bool EvaluateImageDimensionsAfterChangingRenderer
		{
			get => this.Settings.EvaluateImageDimensionsAfterChangingRenderer;
			set => this.Settings.EvaluateImageDimensionsAfterChangingRenderer = value;
		}


		/// <summary>
		/// Evaluate image dimensions after opening file.
		/// </summary>
		public bool EvaluateImageDimensionsAfterOpeningSourceFile
		{
			get => this.Settings.EvaluateImageDimensionsAfterOpeningSourceFile;
			set => this.Settings.EvaluateImageDimensionsAfterOpeningSourceFile = value;
		}


		/// <summary>
		/// Check whether restarting application is needed to apply dark mode change or not.
		/// </summary>
		public bool IsRestartingAppNeededToApplyDarkMode { get; private set; } = false;


		/// <summary>
		/// Maximum memory usage for rendering images.
		/// </summary>
		public long MaxRenderedImagesMemoryUsageMB
		{
			get => this.Settings.MaxRenderedImagesMemoryUsageMB;
			set => this.Settings.MaxRenderedImagesMemoryUsageMB = value;
		}


		// Called when settings changed.
		protected override void OnSettingsChanged(string propertyName)
		{
			base.OnSettingsChanged(propertyName);
			switch (propertyName)
			{
				case nameof(Settings.AutoSelectLanguage):
					this.OnPropertyChanged(nameof(this.AutoSelectLanguage));
					this.UpdateVersionString();
					break;
				case nameof(Settings.DarkMode):
					this.OnPropertyChanged(nameof(this.DarkMode));
					this.IsRestartingAppNeededToApplyDarkMode = (this.Settings.DarkMode != this.isOriginallyDarkMode);
					this.OnPropertyChanged(nameof(this.IsRestartingAppNeededToApplyDarkMode));
					break;
				case nameof(Settings.DefaultImageDimensionsEvaluationAspectRatio):
					this.OnPropertyChanged(nameof(this.DefaultImageDimensionsEvaluationAspectRatio));
					break;
				case nameof(Settings.DefaultImageRendererFormatName):
					this.OnPropertyChanged(nameof(this.DefaultImageRenderer));
					break;
				case nameof(Settings.EvaluateImageDimensionsAfterChangingRenderer):
					this.OnPropertyChanged(nameof(this.EvaluateImageDimensionsAfterChangingRenderer));
					break;
				case nameof(Settings.EvaluateImageDimensionsAfterOpeningSourceFile):
					this.OnPropertyChanged(nameof(this.EvaluateImageDimensionsAfterOpeningSourceFile));
					break;
				case nameof(Settings.MaxRenderedImagesMemoryUsageMB):
					this.OnPropertyChanged(nameof(this.MaxRenderedImagesMemoryUsageMB));
					break;
				case nameof(Settings.YuvConversionMode):
					this.OnPropertyChanged(nameof(this.YuvConversionMode));
					break;
			}
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
			get => this.Settings.UseDefaultImageRendererAfterOpeningSourceFile;
			set => this.Settings.UseDefaultImageRendererAfterOpeningSourceFile = value;
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
			get => this.Settings.YuvConversionMode;
			set => this.Settings.YuvConversionMode = value;
		}


		/// <summary>
		/// List of available values for <see cref="YuvConversionMode"/>.
		/// </summary>
		public IEnumerable<YuvConversionMode> YuvConversionModes { get; } = (YuvConversionMode[])Enum.GetValues(typeof(YuvConversionMode));
	}
}
