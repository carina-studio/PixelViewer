using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using CarinaStudio.Configuration;
using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.ViewModels
{
	/// <summary>
	/// View-model for application options.
	/// </summary>
	class AppOptions : CarinaStudio.AppSuite.ViewModels.ApplicationOptions
	{
		/// <summary>
		/// Initialize new <see cref="AppOptions"/> instance.
		/// </summary>
		public AppOptions()
		{ }


		/// <summary>
		/// List of available values for <see cref="DefaultImageDimensionsEvaluationAspectRatio"/>.
		/// </summary>
		public IEnumerable<AspectRatio> AspectRatios { get; } = (AspectRatio[])Enum.GetValues(typeof(AspectRatio));


		/// <summary>
		/// Brightness transformation function.
		/// </summary>
		public Media.ImageFilters.BrightnessTransformationFunction BrightnessTransformationFunction
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.BrightnessTransformationFunction);
			set => this.Settings.SetValue<Media.ImageFilters.BrightnessTransformationFunction>(SettingKeys.BrightnessTransformationFunction, value);
		}


		/// <summary>
		/// Contrast transformation function.
		/// </summary>
		public Media.ImageFilters.ContrastTransformationFunction ContrastTransformationFunction
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.ContrastTransformationFunction);
			set => this.Settings.SetValue<Media.ImageFilters.ContrastTransformationFunction>(SettingKeys.ContrastTransformationFunction, value);
		}


		/// <summary>
		/// Whether new session is should be created for drag-and-drop file or not.
		/// </summary>
		public bool CreateNewSessionForDragDropFile
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.CreateNewSessionForDragDropFile);
			set => this.Settings.SetValue<bool>(SettingKeys.CreateNewSessionForDragDropFile, value);
		}


		/// <summary>
		/// Default byte ordering.
		/// </summary>
		public ByteOrdering DefaultByteOrdering
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.DefaultByteOrdering);
			set => this.Settings.SetValue<ByteOrdering>(SettingKeys.DefaultByteOrdering, value);
		}


		/// <summary>
		/// Default color space for image rendering.
		/// </summary>
		public ColorSpace DefaultColorSpace
		{
			get 
			{
				ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.DefaultColorSpaceName), out var colorSpace);
				return colorSpace;
			}
			set => this.Settings.SetValue<string>(SettingKeys.DefaultColorSpaceName, value.Name);
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
		/// Default YUV to RGB conversion.
		/// </summary>
		public YuvToBgraConverter DefaultYuvToBgraConverter
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.DefaultYuvToBgraConversion).Let(name =>
			{
				YuvToBgraConverter.TryGetByName(name, out var converter);
				return converter;
			});
			set => this.Settings.SetValue<string>(SettingKeys.DefaultYuvToBgraConversion, value.Name);
		}


		/// <summary>
		/// Enable color space management on rendered image.
		/// </summary>
		public bool EnableColorSpaceManagement
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.EnableColorSpaceManagement);
			set => this.Settings.SetValue<bool>(SettingKeys.EnableColorSpaceManagement, value);
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
		/// Evaluate image renderer by file name of image.
		/// </summary>
		public bool EvaluateImageRendererByFileName
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.EvaluateImageRendererByFileName);
			set => this.Settings.SetValue<bool>(SettingKeys.EvaluateImageRendererByFileName, value);
		}


		/// <summary>
        /// Check whether system defined screen color space is supported or not. 
        /// </summary>
        public bool IsSystemScreenColorSpaceSupported => ColorSpace.IsSystemScreenColorSpaceSupported;


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
			if (key == SettingKeys.BrightnessTransformationFunction)
				this.OnPropertyChanged(nameof(this.BrightnessTransformationFunction));
			else if (key == SettingKeys.ContrastTransformationFunction)
				this.OnPropertyChanged(nameof(this.ContrastTransformationFunction));
			else if (key == SettingKeys.CreateNewSessionForDragDropFile)
				this.OnPropertyChanged(nameof(this.CreateNewSessionForDragDropFile));
			else if (key == SettingKeys.DefaultByteOrdering)
				this.OnPropertyChanged(nameof(this.DefaultByteOrdering));
			else if (key == SettingKeys.DefaultColorSpaceName)
				this.OnPropertyChanged(nameof(this.DefaultColorSpace));
			else if (key == SettingKeys.DefaultImageDimensionsEvaluationAspectRatio)
				this.OnPropertyChanged(nameof(this.DefaultImageDimensionsEvaluationAspectRatio));
			else if (key == SettingKeys.DefaultImageRendererFormatName)
				this.OnPropertyChanged(nameof(this.DefaultImageRenderer));
			else if (key == SettingKeys.DefaultYuvToBgraConversion)
				this.OnPropertyChanged(nameof(this.DefaultYuvToBgraConverter));
			else if (key == SettingKeys.EnableColorSpaceManagement)
				this.OnPropertyChanged(nameof(this.EnableColorSpaceManagement));
			else if (key == SettingKeys.EvaluateImageDimensionsAfterChangingRenderer)
				this.OnPropertyChanged(nameof(this.EvaluateImageDimensionsAfterChangingRenderer));
			else if (key == SettingKeys.EvaluateImageDimensionsAfterOpeningSourceFile)
				this.OnPropertyChanged(nameof(this.EvaluateImageDimensionsAfterOpeningSourceFile));
			else if (key == SettingKeys.EvaluateImageRendererByFileName)
				this.OnPropertyChanged(nameof(this.EvaluateImageRendererByFileName));
			else if (key == SettingKeys.MaxRenderedImagesMemoryUsageMB)
				this.OnPropertyChanged(nameof(this.MaxRenderedImagesMemoryUsageMB));
			else if (key == SettingKeys.Render32BitColorsOnly)
				this.OnPropertyChanged(nameof(this.Render32BitColorsOnly));
			else if (key == SettingKeys.ResetFilterParamsAfterOpeningSourceFile)
				this.OnPropertyChanged(nameof(this.ResetFilterParamsAfterOpeningSourceFile));
			else if (key == SettingKeys.ResetImagePlaneOptionsAfterChangingImageDimensions)
				this.OnPropertyChanged(nameof(this.ResetImagePlaneOptionsAfterChangingImageDimensions));
			else if (key == SettingKeys.SaveRenderedImageWithOrientation)
				this.OnPropertyChanged(nameof(this.SaveRenderedImageWithOrientation));
			else if (key == SettingKeys.ScreenColorSpaceName)
				this.OnPropertyChanged(nameof(this.ScreenColorSpace));
			else if (key == SettingKeys.ShowProcessInfo)
				this.OnPropertyChanged(nameof(this.ShowProcessInfo));
			else if (key == SettingKeys.ShowSelectedRenderedImagePixelArgbColor)
				this.OnPropertyChanged(nameof(this.ShowSelectedRenderedImagePixelArgbColor));
			else if (key == SettingKeys.ShowSelectedRenderedImagePixelLabColor)
				this.OnPropertyChanged(nameof(this.ShowSelectedRenderedImagePixelLabColor));
			else if (key == SettingKeys.ShowSelectedRenderedImagePixelXyzColor)
				this.OnPropertyChanged(nameof(this.ShowSelectedRenderedImagePixelXyzColor));
			else if (key == SettingKeys.UseDefaultImageRendererAfterOpeningSourceFile)
				this.OnPropertyChanged(nameof(this.UseDefaultImageRendererAfterOpeningSourceFile));
			else if (key == SettingKeys.UseSystemScreenColorSpace)
				this.OnPropertyChanged(nameof(this.UseSystemScreenColorSpace));
		}
		
		
		/// <summary>
		/// Whether using 32-bit colors to render images only or not.
		/// </summary>
		public bool Render32BitColorsOnly
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.Render32BitColorsOnly);
			set => this.Settings.SetValue<bool>(SettingKeys.Render32BitColorsOnly, value);
		}


		/// <summary>
		/// Reset image filter parameters after opening image source file.
		/// </summary>
		public bool ResetFilterParamsAfterOpeningSourceFile
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.ResetFilterParamsAfterOpeningSourceFile);
			set => this.Settings.SetValue<bool>(SettingKeys.ResetFilterParamsAfterOpeningSourceFile, value);
		}


		/// <summary>
		/// Reset image plane options after changing image dimensions.
		/// </summary>
		public bool ResetImagePlaneOptionsAfterChangingImageDimensions
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.ResetImagePlaneOptionsAfterChangingImageDimensions);
			set => this.Settings.SetValue<bool>(SettingKeys.ResetImagePlaneOptionsAfterChangingImageDimensions, value);
		}


		/// <summary>
		/// Apply orientation on saved rendered image.
		/// </summary>
		public bool SaveRenderedImageWithOrientation
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.SaveRenderedImageWithOrientation);
			set => this.Settings.SetValue<bool>(SettingKeys.SaveRenderedImageWithOrientation, value);
		}


		/// <summary>
		/// Color space of screen.
		/// </summary>
		public ColorSpace ScreenColorSpace
		{
			get 
			{
				ColorSpace.TryGetColorSpace(this.Settings.GetValueOrDefault(SettingKeys.ScreenColorSpaceName), out var colorSpace);
				return colorSpace;
			}
			set => this.Settings.SetValue<string>(SettingKeys.ScreenColorSpaceName, value.Name);
		}


		/// <summary>
		/// Show process information on UI.
		/// </summary>
		public bool ShowProcessInfo
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.ShowProcessInfo);
			set => this.Settings.SetValue<bool>(SettingKeys.ShowProcessInfo, value);
		}


		/// <summary>
		/// Show ARGB color of selected pixel of rendered image or not.
		/// </summary>
		public bool ShowSelectedRenderedImagePixelArgbColor
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelArgbColor);
			set => this.Settings.SetValue<bool>(SettingKeys.ShowSelectedRenderedImagePixelArgbColor, value);
		}


		/// <summary>
		/// Show L*a*b* color of selected pixel of rendered image or not.
		/// </summary>
		public bool ShowSelectedRenderedImagePixelLabColor
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelLabColor);
			set => this.Settings.SetValue<bool>(SettingKeys.ShowSelectedRenderedImagePixelLabColor, value);
		}


		/// <summary>
		/// Show XYZ color of selected pixel of rendered image or not.
		/// </summary>
		public bool ShowSelectedRenderedImagePixelXyzColor
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.ShowSelectedRenderedImagePixelXyzColor);
			set => this.Settings.SetValue<bool>(SettingKeys.ShowSelectedRenderedImagePixelXyzColor, value);
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
		/// Use screen color space defined by system.
		/// </summary>
		public bool UseSystemScreenColorSpace
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.UseSystemScreenColorSpace);
			set => this.Settings.SetValue<bool>(SettingKeys.UseSystemScreenColorSpace, IsSystemScreenColorSpaceSupported && value);
		}
	}
}
