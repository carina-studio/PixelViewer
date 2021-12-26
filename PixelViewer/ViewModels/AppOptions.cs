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
		/// Whether new session is should be created for drag-and-drop file or not.
		/// </summary>
		public bool CreateNewSessionForDragDropFile
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.CreateNewSessionForDragDropFile);
			set => this.Settings.SetValue<bool>(SettingKeys.CreateNewSessionForDragDropFile, value);
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
			if (key == SettingKeys.CreateNewSessionForDragDropFile)
				this.OnPropertyChanged(nameof(this.CreateNewSessionForDragDropFile));
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
			else if (key == SettingKeys.MaxRenderedImagesMemoryUsageMB)
				this.OnPropertyChanged(nameof(this.MaxRenderedImagesMemoryUsageMB));
			else if (key == SettingKeys.ResetImagePlaneOptionsAfterChangingImageDimensions)
				this.OnPropertyChanged(nameof(this.ResetImagePlaneOptionsAfterChangingImageDimensions));
			else if (key == SettingKeys.ScreenColorSpace)
				this.OnPropertyChanged(nameof(this.ScreenColorSpace));
			else if (key == SettingKeys.ShowProcessInfo)
				this.OnPropertyChanged(nameof(this.ShowProcessInfo));
			else if (key == SettingKeys.UseDefaultImageRendererAfterOpeningSourceFile)
				this.OnPropertyChanged(nameof(this.UseDefaultImageRendererAfterOpeningSourceFile));
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
		/// Color space of screen.
		/// </summary>
		public ScreenColorSpace ScreenColorSpace
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.ScreenColorSpace);
			set => this.Settings.SetValue<ScreenColorSpace>(SettingKeys.ScreenColorSpace, value);
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
		/// Reset to default image renderer after opening file.
		/// </summary>
		public bool UseDefaultImageRendererAfterOpeningSourceFile
		{
			get => this.Settings.GetValueOrDefault(SettingKeys.UseDefaultImageRendererAfterOpeningSourceFile);
			set => this.Settings.SetValue<bool>(SettingKeys.UseDefaultImageRendererAfterOpeningSourceFile, value);
		}
	}
}
