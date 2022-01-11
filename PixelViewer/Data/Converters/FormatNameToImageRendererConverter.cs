using Avalonia.Data.Converters;
using Carina.PixelViewer.Media.ImageRenderers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Carina.PixelViewer.Data.Converters
{
	/// <summary>
	/// <see cref="IValueConverter"/> to convert from name of <see cref="IImageFormat"/> to <see cref="IImageRenderer"/>.
	/// </summary>
	class FormatNameToImageRendererConverter : IValueConverter
	{
		/// <summary>
		/// Default instance.
		/// </summary>
		public static readonly FormatNameToImageRendererConverter Default = new FormatNameToImageRendererConverter(ImageRenderers.All);


		// Fields.
		readonly IEnumerable<IImageRenderer> imageRenderers;


		/// <summary>
		/// Initialize new <see cref="FormatNameToImageRendererConverter"/> instance.
		/// </summary>
		/// <param name="imageRenderers">Set of <see cref="IImageRenderer"/>.</param>
		public FormatNameToImageRendererConverter(IEnumerable<IImageRenderer> imageRenderers)
		{
			this.imageRenderers = imageRenderers;
		}


		// Convert.
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if(value is string name)
			{
				foreach (var imageRender in this.imageRenderers)
				{
					if (imageRender.Format.Name == name)
						return imageRender;
				}
			}
			return null;
		}


		// Convert back.
		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is IImageRenderer imageRenderer)
				return imageRenderer.Format.Name;
			return null;
		}
	}
}
