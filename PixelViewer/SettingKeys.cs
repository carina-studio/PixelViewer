using Carina.PixelViewer.Media;
using CarinaStudio.Configuration;
using System;

namespace Carina.PixelViewer
{
    /// <summary>
    /// Keys of setting.
    /// </summary>
    static class SettingKeys
    {
		/// <summary>
		/// Select language automatically.
		/// </summary>
		[Obsolete]
		public static readonly SettingKey<bool> AutoSelectLanguage = new SettingKey<bool>(nameof(AutoSelectLanguage), true);
		/// <summary>
		/// Whether new session is should be created for drag-and-drop file or not.
		/// </summary>
		public static readonly SettingKey<bool> CreateNewSessionForDragDropFile = new SettingKey<bool>(nameof(CreateNewSessionForDragDropFile), true);
		/// <summary>
		/// Use dark interface mode.
		/// </summary>
		[Obsolete]
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
		/// Default YUV to RGB conversion.
		/// </summary>
		public static readonly SettingKey<string> DefaultYuvToBgraConversion = new SettingKey<string>(nameof(DefaultYuvToBgraConversion), YuvToBgraConverter.Default.Name);
		/// <summary>
		/// Enable color space management on image rendering.
		/// </summary>
		public static readonly SettingKey<bool> EnableColorSpaceManagement = new SettingKey<bool>(nameof(EnableColorSpaceManagement), false);
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
		public static readonly SettingKey<long> MaxRenderedImagesMemoryUsageMB = new SettingKey<long>(nameof(MaxRenderedImagesMemoryUsageMB), Environment.Is64BitProcess ? 2048 : 1024);
		/// <summary>
		/// Reset image plane options after changing image dimensions.
		/// </summary>
		public static readonly SettingKey<bool> ResetImagePlaneOptionsAfterChangingImageDimensions = new SettingKey<bool>(nameof(ResetImagePlaneOptionsAfterChangingImageDimensions), true);
		/// <summary>
		/// Apply orientation on saved rendered image.
		/// </summary>
		public static readonly SettingKey<bool> SaveRenderedImageWithOrientation = new SettingKey<bool>(nameof(SaveRenderedImageWithOrientation), true);
		/// <summary>
		/// Color space of screen.
		/// </summary>
		public static readonly SettingKey<ScreenColorSpace> ScreenColorSpace = new SettingKey<ScreenColorSpace>(nameof(ScreenColorSpace), CarinaStudio.Platform.IsMacOS ? Media.ScreenColorSpace.Display_P3 : Media.ScreenColorSpace.Srgb);
		/// <summary>
		/// Show process info on UI or not.
		/// </summary>
		public static readonly SettingKey<bool> ShowProcessInfo = new SettingKey<bool>(nameof(ShowProcessInfo), false);
		/// <summary>
		/// Show ARGB color of selected pixel of rendered image or not.
		/// </summary>
		public static readonly SettingKey<bool> ShowSelectedRenderedImagePixelArgbColor = new SettingKey<bool>(nameof(ShowSelectedRenderedImagePixelArgbColor), true);
		/// <summary>
		/// Show L*a*b* color of selected pixel of rendered image or not.
		/// </summary>
		public static readonly SettingKey<bool> ShowSelectedRenderedImagePixelLabColor = new SettingKey<bool>(nameof(ShowSelectedRenderedImagePixelLabColor), true);
		/// <summary>
		/// Show XYZ color of selected pixel of rendered image or not.
		/// </summary>
		public static readonly SettingKey<bool> ShowSelectedRenderedImagePixelXyzColor = new SettingKey<bool>(nameof(ShowSelectedRenderedImagePixelXyzColor), true);
		/// <summary>
		/// Change to default image renderer after opening file.
		/// </summary>
		public static readonly SettingKey<bool> UseDefaultImageRendererAfterOpeningSourceFile = new SettingKey<bool>(nameof(UseDefaultImageRendererAfterOpeningSourceFile), false);
	}
}
