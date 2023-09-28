using Carina.PixelViewer.Media;
using CarinaStudio.Configuration;
using System;

namespace Carina.PixelViewer;

/// <summary>
/// Keys of setting.
/// </summary>
static class SettingKeys
{
	/// <summary>
	/// Select language automatically.
	/// </summary>
	[Obsolete]
	public static readonly SettingKey<bool> AutoSelectLanguage = new(nameof(AutoSelectLanguage), true);
	/// <summary>
	/// Brightness transformation function.
	/// </summary>
	public static readonly SettingKey<Media.ImageFilters.BrightnessTransformationFunction> BrightnessTransformationFunction = new(nameof(BrightnessTransformationFunction), Media.ImageFilters.BrightnessTransformationFunction.Arctan);
	/// <summary>
	/// Timing to perform color space conversion.
	/// </summary>
	public static readonly SettingKey<ColorSpaceConversionTiming> ColorSpaceConversionTiming = new(nameof(ColorSpaceConversionTiming), Media.ColorSpaceConversionTiming.BeforeRenderingToDisplay);
	/// <summary>
	/// Contrast transformation function.
	/// </summary>
	public static readonly SettingKey<Media.ImageFilters.ContrastTransformationFunction> ContrastTransformationFunction = new(nameof(ContrastTransformationFunction), Media.ImageFilters.ContrastTransformationFunction.Arctan);
	/// <summary>
	/// Whether new session is should be created for drag-and-drop file or not.
	/// </summary>
	public static readonly SettingKey<bool> CreateNewSessionForDragDropFile = new(nameof(CreateNewSessionForDragDropFile), true);
	/// <summary>
	/// Use dark interface mode.
	/// </summary>
	[Obsolete]
	public static readonly SettingKey<bool> DarkMode = new(nameof(DarkMode), true);
	/// <summary>
	/// Default byte ordering.
	/// </summary>
	public static readonly SettingKey<ByteOrdering> DefaultByteOrdering = new(nameof(ByteOrdering), ByteOrdering.LittleEndian);
	/// <summary>
	/// Name of default color space.
	/// </summary>
	public static readonly SettingKey<string> DefaultColorSpaceName = new(nameof(DefaultColorSpaceName), "sRGB");
	/// <summary>
	/// Default aspect ratio for image dimensions evaluation.
	/// </summary>
	public static readonly SettingKey<AspectRatio> DefaultImageDimensionsEvaluationAspectRatio = new(nameof(DefaultImageDimensionsEvaluationAspectRatio), AspectRatio.Unknown);
	/// <summary>
	/// Name of format of default image renderer.
	/// </summary>
	public static readonly SettingKey<string> DefaultImageRendererFormatName = new(nameof(DefaultImageRendererFormatName), "L8");
	/// <summary>
	/// Default YUV to RGB conversion.
	/// </summary>
	public static readonly SettingKey<string> DefaultYuvToBgraConversion = new(nameof(DefaultYuvToBgraConversion), YuvToBgraConverter.Default.Name);
	/// <summary>
	/// Enable color space management on image rendering.
	/// </summary>
	public static readonly SettingKey<bool> EnableColorSpaceManagement = new(nameof(EnableColorSpaceManagement), true);
	/// <summary>
	/// Evaluate image dimensions after changing image renderer.
	/// </summary>
	public static readonly SettingKey<bool> EvaluateImageDimensionsAfterChangingRenderer = new(nameof(EvaluateImageDimensionsAfterChangingRenderer), false);
	/// <summary>
	/// Evaluate image dimensions after opening file.
	/// </summary>
	public static readonly SettingKey<bool> EvaluateImageDimensionsAfterOpeningSourceFile = new(nameof(EvaluateImageDimensionsAfterOpeningSourceFile), true);
	/// <summary>
	/// Evaluate image renderer by file name of image.
	/// </summary>
	public static readonly SettingKey<bool> EvaluateImageRendererByFileName = new(nameof(EvaluateImageRendererByFileName), true);
	/// <summary>
	/// Hide scroll bars of image viewer automatically.
	/// </summary>
	public static readonly SettingKey<bool> HideImageViewerScrollBarsAutomatically = new(nameof(HideImageViewerScrollBarsAutomatically), true);
	/// <summary>
	/// Maximum memory usage for image rendering.
	/// </summary>
	public static readonly SettingKey<long> MaxRenderedImagesMemoryUsageMB = new(nameof(MaxRenderedImagesMemoryUsageMB), Environment.Is64BitProcess ? 2048 : 1024);
	/// <summary>
	/// Whether using 32-bit colors to render images only or not.
	/// </summary>
	public static readonly SettingKey<bool> Render32BitColorsOnly = new(nameof(Render32BitColorsOnly), false);
	/// <summary>
	/// Reset image filter parameters after opening image source file.
	/// </summary>
	public static readonly SettingKey<bool> ResetFilterParamsAfterOpeningSourceFile = new(nameof(ResetFilterParamsAfterOpeningSourceFile), true);
	/// <summary>
	/// Reset image plane options after changing image dimensions.
	/// </summary>
	public static readonly SettingKey<bool> ResetImagePlaneOptionsAfterChangingImageDimensions = new(nameof(ResetImagePlaneOptionsAfterChangingImageDimensions), true);
	/// <summary>
	/// Apply orientation on saved rendered image.
	/// </summary>
	public static readonly SettingKey<bool> SaveRenderedImageWithOrientation = new(nameof(SaveRenderedImageWithOrientation), true);
	/// <summary>
	/// Name of color space of screen.
	/// </summary>
	public static readonly SettingKey<string> ScreenColorSpaceName = new(nameof(ScreenColorSpaceName), CarinaStudio.Platform.IsMacOS ? ColorSpace.Display_P3.Name : ColorSpace.Srgb.Name);
	/// <summary>
	/// Show process info on UI or not.
	/// </summary>
	public static readonly SettingKey<bool> ShowProcessInfo = new(nameof(ShowProcessInfo), false);
	/// <summary>
	/// Show ARGB color of selected pixel of rendered image or not.
	/// </summary>
	public static readonly SettingKey<bool> ShowSelectedRenderedImagePixelArgbColor = new(nameof(ShowSelectedRenderedImagePixelArgbColor), true);
	/// <summary>
	/// Show L*a*b* color of selected pixel of rendered image or not.
	/// </summary>
	public static readonly SettingKey<bool> ShowSelectedRenderedImagePixelLabColor = new(nameof(ShowSelectedRenderedImagePixelLabColor), true);
	/// <summary>
	/// Show XYZ color of selected pixel of rendered image or not.
	/// </summary>
	public static readonly SettingKey<bool> ShowSelectedRenderedImagePixelXyzColor = new(nameof(ShowSelectedRenderedImagePixelXyzColor), true);
	/// <summary>
	/// Change to default image renderer after opening file.
	/// </summary>
	public static readonly SettingKey<bool> UseDefaultImageRendererAfterOpeningSourceFile = new(nameof(UseDefaultImageRendererAfterOpeningSourceFile), false);
	/// <summary>
	/// Use screen color space defined by system.
	/// </summary>
	public static readonly SettingKey<bool> UseSystemScreenColorSpace = new (nameof(UseSystemScreenColorSpace), true);
}
