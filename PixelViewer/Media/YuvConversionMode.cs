using System;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Mode of conversion between YUV and RGB.
	/// </summary>
	public enum YuvConversionMode
	{
		/// <summary>
		/// ITU-R Recommendation BT.709
		/// </summary>
		BT_709,
		/// <summary>
		/// ITU-R Recommendation BT.601.
		/// </summary>
		BT_601,
		/// <summary>
		/// ITU-R Recommendation BT.656 (NTSC).
		/// </summary>
		BT_656,
		/// <summary>
		/// ITU-R Recommendation BT.2020
		/// </summary>
		BT_2020,
		/// <summary>
		/// NTSC stabdard.
		/// </summary>
		[Obsolete]
		NTSC,
		/// <summary>
		/// ITU-R.
		/// </summary>
		[Obsolete]
		ITU_R,
	}
}
