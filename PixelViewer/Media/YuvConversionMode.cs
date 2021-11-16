using System;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Mode of conversion between YUV and RGB.
	/// </summary>
	public enum YuvConversionMode
	{
		/// <summary>
		/// NTSC stabdard.
		/// </summary>
		NTSC,
		/// <summary>
		/// ITU-R.
		/// </summary>
		[Obsolete]
		ITU_R,
		/// <summary>
		/// ITU-R Recommendation BT.601.
		/// </summary>
		BT_601,
		/// <summary>
		/// ITU-R Recommendation BT.709
		/// </summary>
		BT_709,
	}
}
