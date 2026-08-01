using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Extensions for <see cref="BayerPattern"/>.
/// </summary>
static class BayerPatternExtensions
{
	extension(BayerPattern pattern)
	{
		/// <summary>
		/// Get height of color block of the pattern in pixels.
		/// </summary>
		public int BlockHeight => colorPatterns[pattern].Length;


		/// <summary>
		/// Get width of color block of the pattern in pixels.
		/// </summary>
		public int BlockWidth => colorPatterns[pattern][0].Length;


		/// <summary>
		/// Get color component of each pixel in color block of the pattern.
		/// </summary>
		/// <remarks>The 1st dimension is the position in vertical direction, the 2nd dimension is the position in horizontal direction.</remarks>
		public BayerPatternColorComponent[][] ColorPattern => colorPatterns[pattern];


		/// <summary>
		/// Create function to select color component of pixel at given position.
		/// </summary>
		/// <returns>Function which accepts horizontal and vertical position of pixel, and returns the color component of the pixel.</returns>
		public Func<int, int, BayerPatternColorComponent> CreateColorComponentSelector()
		{
			// get color pattern
			var colorPattern = colorPatterns[pattern];
			var colorPatternWidth = colorPattern[0].Length;
			var colorPatternHeight = colorPattern.Length;

			// select position by masking if the size of color block is power of 2
			var xMask = colorPatternWidth switch
			{
				2 => 0x1,
				4 => 0x3,
				8 => 0x7,
				_ => 0,
			};
			var yMask = colorPatternHeight switch
			{
				2 => 0x1,
				4 => 0x3,
				8 => 0x7,
				_ => 0,
			};
			if (xMask != 0)
			{
				if (yMask != 0)
					return (x, y) => colorPattern[y & yMask][x & xMask];
				return (x, y) => colorPattern[y % colorPatternHeight][x & xMask];
			}
			if (yMask != 0)
				return (x, y) => colorPattern[y & yMask][x % colorPatternWidth];
			return (x, y) => colorPattern[y % colorPatternHeight][x % colorPatternWidth];
		}
	}


	// Constants.
	const BayerPatternColorComponent BlueColorComponent = BayerPatternColorComponent.Blue;
	const BayerPatternColorComponent GreenColorComponent = BayerPatternColorComponent.Green;
	const BayerPatternColorComponent RedColorComponent = BayerPatternColorComponent.Red;


	// Static fields.
	static readonly Dictionary<BayerPattern, BayerPatternColorComponent[][]> colorPatterns = new()
	{
		{
			BayerPattern.BGGR_2x2,
			[
				[ BlueColorComponent, GreenColorComponent ],
				[ GreenColorComponent, RedColorComponent ],
			]
		},
		{
			BayerPattern.GBRG_2x2,
			[
				[ GreenColorComponent, BlueColorComponent ],
				[ RedColorComponent, GreenColorComponent ],
			]
		},
		{
			BayerPattern.GRBG_2x2,
			[
				[ GreenColorComponent, RedColorComponent ],
				[ BlueColorComponent, GreenColorComponent ],
			]
		},
		{
			BayerPattern.RGGB_2x2,
			[
				[ RedColorComponent, GreenColorComponent ],
				[ GreenColorComponent, BlueColorComponent ],
			]
		},
		{
			BayerPattern.BGGR_4x4,
			[
				[ BlueColorComponent, BlueColorComponent, GreenColorComponent, GreenColorComponent ],
				[ BlueColorComponent, BlueColorComponent, GreenColorComponent, GreenColorComponent ],
				[ GreenColorComponent, GreenColorComponent, RedColorComponent, RedColorComponent ],
				[ GreenColorComponent, GreenColorComponent, RedColorComponent, RedColorComponent ],
			]
		},
		{
			BayerPattern.GBRG_4x4,
			[
				[ GreenColorComponent, GreenColorComponent, BlueColorComponent, BlueColorComponent ],
				[ GreenColorComponent, GreenColorComponent, BlueColorComponent, BlueColorComponent ],
				[ RedColorComponent, RedColorComponent, GreenColorComponent, GreenColorComponent ],
				[ RedColorComponent, RedColorComponent, GreenColorComponent, GreenColorComponent ],
			]
		},
		{
			BayerPattern.GRBG_4x4,
			[
				[ GreenColorComponent, GreenColorComponent, RedColorComponent, RedColorComponent ],
				[ GreenColorComponent, GreenColorComponent, RedColorComponent, RedColorComponent ],
				[ BlueColorComponent, BlueColorComponent, GreenColorComponent, GreenColorComponent ],
				[ BlueColorComponent, BlueColorComponent, GreenColorComponent, GreenColorComponent ],
			]
		},
		{
			BayerPattern.RGGB_4x4,
			[
				[ RedColorComponent, RedColorComponent, GreenColorComponent, GreenColorComponent ],
				[ RedColorComponent, RedColorComponent, GreenColorComponent, GreenColorComponent ],
				[ GreenColorComponent, GreenColorComponent, BlueColorComponent, BlueColorComponent ],
				[ GreenColorComponent, GreenColorComponent, BlueColorComponent, BlueColorComponent ],
			]
		},
	};
}
