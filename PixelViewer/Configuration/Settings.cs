using Carina.PixelViewer.Media;
using CarinaStudio.Configuration;
using System;
namespace Carina.PixelViewer.Configuration
{
	/// <summary>
	/// Application settings.
	/// </summary>
	class Settings : BaseSettings
	{
		/// <summary>
		/// Select language automatically.
		/// </summary>
		public static readonly SettingKey<bool> AutoSelectLanguage = new SettingKey<bool>(nameof(AutoSelectLanguage), true);
		/// <summary>
		/// Use dark interface mode.
		/// </summary>
		public static readonly SettingKey<bool> DarkMode = new SettingKey<bool>(nameof(DarkMode), true);
		/// <summary>
		/// Default aspect ratio for image dimensions evaluation.
		/// </summary>
		public static readonly SettingKey<AspectRatio> DefaultImageDimensionsEvaluationAspectRatio = new SettingKey<AspectRatio>(nameof(DefaultImageDimensionsEvaluationAspectRatio), AspectRatio.Unknown);
		/// <summary>
		/// Name of format of default image renderer.
		/// </summary>
		public static readonly SettingKey<string> DefaultImageRendererFormatName = new SettingKey<string>(nameof(DefaultImageRendererFormatName), "L8");
		/// <summary>
		/// Evaluate image dimensions after changing image renderer.
		/// </summary>
		public static readonly SettingKey<bool> EvaluateImageDimensionsAfterChangingRenderer = new SettingKey<bool>(nameof(EvaluateImageDimensionsAfterChangingRenderer), false);
		/// <summary>
		/// Evaluate image dimensions after opening file.
		/// </summary>
		public static readonly SettingKey<bool> EvaluateImageDimensionsAfterOpeningSourceFile = new SettingKey<bool>(nameof(EvaluateImageDimensionsAfterOpeningSourceFile), true);
		/// <summary>
		/// Height of main window.
		/// </summary>
		public static readonly SettingKey<int> MainWindowHeight = new SettingKey<int>(nameof(MainWindowHeight), 0);
		/// <summary>
		/// State of main window.
		/// </summary>
		public static readonly SettingKey<Avalonia.Controls.WindowState> MainWindowState = new SettingKey<Avalonia.Controls.WindowState>(nameof(MainWindowState), Avalonia.Controls.WindowState.Maximized);
		/// <summary>
		/// Width of main window.
		/// </summary>
		public static readonly SettingKey<int> MainWindowWidth = new SettingKey<int>(nameof(MainWindowWidth), 0);
		/// <summary>
		/// Maximum memory usage for image rendering.
		/// </summary>
		public static readonly SettingKey<long> MaxRenderedImagesMemoryUsageMB = new SettingKey<long>(nameof(MaxRenderedImagesMemoryUsageMB), 1024);
		/// <summary>
		/// Change to default image renderer after opening file.
		/// </summary>
		public static readonly SettingKey<bool> UseDefaultImageRendererAfterOpeningSourceFile = new SettingKey<bool>(nameof(UseDefaultImageRendererAfterOpeningSourceFile), false);
		/// <summary>
		/// YUV to RGB conversion mode.
		/// </summary>
		public static readonly SettingKey<YuvConversionMode> YuvConversionMode = new SettingKey<YuvConversionMode>(nameof(YuvConversionMode), Media.YuvConversionMode.NTSC);


		/// <summary>
		/// Initialize new <see cref="Settings"/> instance.
		/// </summary>
		public Settings() : base(JsonSettingsSerializer.Default)
		{ }


		// Upgrade settings.
		protected override void OnUpgrade(int oldVersion)
		{ }


		// Version.
		protected override int Version => 1;
	}
}
