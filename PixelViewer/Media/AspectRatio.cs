using System;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Aspect ratio.
	/// </summary>
	enum AspectRatio
	{
		/// <summary>
		/// Unknown.
		/// </summary>
		Unknown,
		/// <summary>
		/// 4:3.
		/// </summary>
		Ratio_4x3,
		/// <summary>
		/// 3:4.
		/// </summary>
		Ratio_3x4,
		/// <summary>
		/// 16:9.
		/// </summary>
		Ratio_16x9,
		/// <summary>
		/// 9:16.
		/// </summary>
		Ratio_9x16,
		/// <summary>
		/// 3:2.
		/// </summary>
		Ratio_3x2,
		/// <summary>
		/// 3:2.
		/// </summary>
		Ratio_2x3,
		/// <summary>
		/// 1:1.
		/// </summary>
		Ratio_1x1,
	}


	/// <summary>
	/// Extensions for <see cref="AspectRatio"/>.
	/// </summary>
	static class AspectRatioExtensions
	{
		/// <summary>
		/// Calculate ratio (width / height) of <see cref="AspectRatio"/>.
		/// </summary>
		/// <param name="aspectRatio"><see cref="AspectRatio"/>.</param>
		/// <returns>Landscape ratio, or <see cref="double.NaN"/> if ratio is undefined.</returns>
		public static double CalculateRatio(this AspectRatio aspectRatio) => aspectRatio switch
		{
			AspectRatio.Ratio_4x3 => 4 / 3.0,
			AspectRatio.Ratio_3x4 => 3 / 4.0,
			AspectRatio.Ratio_16x9 => 16 / 9.0,
			AspectRatio.Ratio_9x16 => 9 / 16.0,
			AspectRatio.Ratio_3x2 => 3 / 2.0,
			AspectRatio.Ratio_2x3 => 2 / 3.0,
			AspectRatio.Ratio_1x1 => 1,
			_ => double.NaN,
		};
	}
}
