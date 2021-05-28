using System;
using System.Collections.Generic;
using System.Text;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Class to hold all available <see cref="IImageRenderer"/>s.
	/// </summary>
	static class ImageRenderers
	{
		/// <summary>
		/// Get all available <see cref="IImageRenderer"/>s.
		/// </summary>
		public static IList<IImageRenderer> All { get; } = new List<IImageRenderer>(new IImageRenderer[]
		{
			new L8ImageRenderer(),
			new L16BEImageRenderer(),
			new L16LEImageRenderer(),
			new NV12ImageRenderer(),
			new NV21ImageRenderer(),
			new I420ImageRenderer(),
			new YV12ImageRenderer(),
			new Yuv422pImageRenderer(),
			new UyvyImageRenderer(),
			new YuvyImageRenderer(),
			new Yuv444pImageRenderer(),
			new AndroidYuv420ImageRenderer(),
			new Rgb565BEImageRenderer(),
			new Rgb565LEImageRenderer(),
			new Bgr888ImageRenderer(),
			new Rgb888ImageRenderer(),
			new Abgr8888ImageRenderer(),
			new Argb8888ImageRenderer(),
			new Bgra8888ImageRenderer(),
			new Rgba8888ImageRenderer(),
			new Bgrx8888ImageRenderer(),
			new Rgbx8888ImageRenderer(),
			new Xbgr8888ImageRenderer(),
			new Xrgb8888ImageRenderer(),
			new Bggr16BEImageRenderer(),
			new Bggr16LEImageRenderer(),
			new Gbrg16BEImageRenderer(),
			new Gbrg16LEImageRenderer(),
			new Grbg16BEImageRenderer(),
			new Grbg16LEImageRenderer(),
			new Rggb16BEImageRenderer(),
			new Rggb16LEImageRenderer(),
		}).AsReadOnly();


		/// <summary>
		/// Try finding specific <see cref="IImageRenderer"/> by the format supported by it.
		/// </summary>
		/// <param name="format">Format supported by <see cref="IImageRenderer"/>.</param>
		/// <param name="renderer">Found <see cref="IImageRenderer"/>, or null if no <see cref="IImageRenderer"/> supports given format.</param>
		/// <returns>True if <see cref="IImageRenderer"/> found.</returns>
		public static bool TryFindByFormat(ImageFormat format, out IImageRenderer? renderer)
		{
			foreach (var candidate in All)
			{
				if (candidate.Format.Equals(format))
				{
					renderer = candidate;
					return true;
				}
			}
			renderer = null;
			return false;
		}


		/// <summary>
		/// Try finding specific <see cref="IImageRenderer"/> by the name of format supported by it.
		/// </summary>
		/// <param name="formatName">Name of format supported by <see cref="IImageRenderer"/>.</param>
		/// <param name="renderer">Found <see cref="IImageRenderer"/>, or null if no <see cref="IImageRenderer"/> supports given format.</param>
		/// <returns>True if <see cref="IImageRenderer"/> found.</returns>
		public static bool TryFindByFormatName(string formatName, out IImageRenderer? renderer)
		{
			foreach (var candidate in All)
			{
				if (candidate.Format.Name == formatName)
				{
					renderer = candidate;
					return true;
				}
			}
			renderer = null;
			return false;
		}
	}
}
