﻿using CarinaStudio;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers
{
    /// <summary>
    /// Base implementation of <see cref="IImageRenderer"/> which renders image with bayer filter pattern.
    /// </summary>
    abstract class BayerPatternImageRenderer : SinglePlaneImageRenderer
    {
        /// <summary>
        /// Index of blue color.
        /// </summary>
        protected const int BlueColorComponent = 0;
		/// <summary>
		/// Index of green color.
		/// </summary>
        protected const int GreenColorComponent = 1;
		/// <summary>
		/// Index of red color.
		/// </summary>
        protected const int RedColorComponent = 2;


        // Static fields.
        static readonly Dictionary<BayerPattern, int[][]> ColorPatternMap = new()
        {
            { 
				BayerPattern.BGGR_2x2, 
				new[]{
					new[]{ BlueColorComponent, GreenColorComponent },
					new[]{ GreenColorComponent, RedColorComponent },
				}
			},
            { 
				BayerPattern.GBRG_2x2, 
				new[]{
					new[]{ GreenColorComponent, BlueColorComponent },
					new[]{ RedColorComponent, GreenColorComponent },
				} 
			},
            { 
				BayerPattern.GRBG_2x2, 
				new[]{
					new[]{ GreenColorComponent, RedColorComponent },
					new[]{ BlueColorComponent, GreenColorComponent },
				}
			},
            { 
				BayerPattern.RGGB_2x2, 
				new[]{
					new[]{ RedColorComponent, GreenColorComponent },
					new[]{ GreenColorComponent, BlueColorComponent },
				} 
			},
			{ 
				BayerPattern.BGGR_4x4, 
				new[]{
					new[]{ BlueColorComponent, BlueColorComponent, GreenColorComponent, GreenColorComponent },
					new[]{ BlueColorComponent, BlueColorComponent, GreenColorComponent, GreenColorComponent },
					new[]{ GreenColorComponent, GreenColorComponent, RedColorComponent, RedColorComponent },
					new[]{ GreenColorComponent, GreenColorComponent, RedColorComponent, RedColorComponent },
				} 
			},
			{
				BayerPattern.GBRG_4x4,
				new[]{
					new[]{ GreenColorComponent, GreenColorComponent, BlueColorComponent, BlueColorComponent },
					new[]{ GreenColorComponent, GreenColorComponent, BlueColorComponent, BlueColorComponent },
					new[]{ RedColorComponent, RedColorComponent, GreenColorComponent, GreenColorComponent },
					new[]{ RedColorComponent, RedColorComponent, GreenColorComponent, GreenColorComponent },
				}
			},
			{
				BayerPattern.GRBG_4x4,
				new[]{
					new[]{ GreenColorComponent, GreenColorComponent, RedColorComponent, RedColorComponent },
					new[]{ GreenColorComponent, GreenColorComponent, RedColorComponent, RedColorComponent },
					new[]{ BlueColorComponent, BlueColorComponent, GreenColorComponent, GreenColorComponent },
					new[]{ BlueColorComponent, BlueColorComponent, GreenColorComponent, GreenColorComponent },
				}
			},
			{
				BayerPattern.RGGB_4x4,
				new[]{
					new[]{ RedColorComponent, RedColorComponent, GreenColorComponent, GreenColorComponent },
					new[]{ RedColorComponent, RedColorComponent, GreenColorComponent, GreenColorComponent },
					new[]{ GreenColorComponent, GreenColorComponent, BlueColorComponent, BlueColorComponent },
					new[]{ GreenColorComponent, GreenColorComponent, BlueColorComponent, BlueColorComponent },
				}
			},
		};


        /// <summary>
        /// Initialize new <see cref="BayerPatternImageRenderer"/> instance.
        /// </summary>
        /// <param name="format">Format.</param>
        protected BayerPatternImageRenderer(ImageFormat format) : base(format)
        { }
        
        
        /// <summary>
        /// Build color transformation table for single color of BGRA32.
        /// </summary>
        /// <param name="table">Pointer to table, the length should be 256.</param>
        /// <param name="gain">Gain for color.</param>
        protected static unsafe void BuildColorTransformationTableUnsafe(byte* table, double gain)
        {
	        table += 255;
	        if (Math.Abs(gain - 1) <= 0.0001)
	        {
		        for (var i = 255; i >= 0; --i, --table)
			        *table = (byte)i;
	        }
	        else
	        {
		        for (var i = 255; i >= 0; --i, --table)
			        *table = ImageProcessing.ClipToByte(i * gain);
	        }
        }


		/// <summary>
		/// Build color transformation table for single color of BGRA64.
		/// </summary>
		/// <param name="table">Pointer to table, the length should be 65536.</param>
		/// <param name="gain">Gain for color.</param>
		protected static unsafe void BuildColorTransformationTableUnsafe(ushort* table, double gain)
		{
			table += 65535;
			if (Math.Abs(gain - 1) <= 0.0001)
			{
				for (var i = 65535; i >= 0; --i, --table)
					*table = (ushort)i;
			}
			else
			{
				for (var i = 65535; i >= 0; --i, --table)
					*table = ImageProcessing.ClipToUInt16(i * gain);
			}
		}


		// Demosaicing by 3x3 sub block.
		unsafe void Demosaic3x3(IBitmapBuffer bitmapBuffer, Func<int, int, int> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
		{
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			var bitmapRowStride = bitmapBuffer.RowBytes;
			var lastColumnIndex = width - 1;
			bitmapBuffer.Memory.Pin(bitmapBaseAddress =>
			{
				switch (bitmapBuffer.Format)
				{
					case BitmapFormat.Bgra32:
						ImageProcessing.ParallelFor(0, height, y =>
						{
							if (cancellationToken.IsCancellationRequested)
								return;
							var accumColors = stackalloc int[3];
							var colorCounts = stackalloc int[3];
							var bitmapPixelPtr = (byte*)bitmapBaseAddress + bitmapRowStride * y;
							var leftBitmapPixelPtr = (byte*)null;
							var rightBitmapPixelPtr = bitmapPixelPtr + 4;
							var topBitmapPixelPtr = bitmapPixelPtr - bitmapRowStride;
							var bottomBitmapPixelPtr = bitmapPixelPtr + bitmapRowStride;
							var isNotTopRow = (y > 0);
							var isNotBottomRow = (y < height - 1);
							for (var x = 0; x < width; ++x, leftBitmapPixelPtr = bitmapPixelPtr, bitmapPixelPtr = rightBitmapPixelPtr, rightBitmapPixelPtr += 4, topBitmapPixelPtr += 4, bottomBitmapPixelPtr += 4)
							{
								// get component at current pixel
								var centerComponent = colorComponentSelector(x, y);

								// collect colors around current pixel
								var isNotLastPixelInRow = x < lastColumnIndex;
								int neighborComponent;
								if (isNotTopRow)
								{
									if (x > 0)
									{
										neighborComponent = colorComponentSelector(x - 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topBitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = colorComponentSelector(x, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += topBitmapPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = colorComponentSelector(x + 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topBitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = colorComponentSelector(x - 1, y);
									if (neighborComponent != centerComponent)
									{
#pragma warning disable CS8602
										accumColors[neighborComponent] += leftBitmapPixelPtr[neighborComponent];
#pragma warning restore CS8602
										++colorCounts[neighborComponent];
									}
								}
								if (isNotLastPixelInRow)
								{
									neighborComponent = colorComponentSelector(x + 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += rightBitmapPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (isNotBottomRow)
								{
									if (x > 0)
									{
										neighborComponent = colorComponentSelector(x - 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottomBitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = colorComponentSelector(x, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += bottomBitmapPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = colorComponentSelector(x + 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottomBitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}

								// combine to full RGB color
								if (colorCounts[0] > 0)
									bitmapPixelPtr[0] = (byte)(accumColors[0] / colorCounts[0]);
								if (colorCounts[1] > 0)
									bitmapPixelPtr[1] = (byte)(accumColors[1] / colorCounts[1]);
								if (colorCounts[2] > 0)
									bitmapPixelPtr[2] = (byte)(accumColors[2] / colorCounts[2]);
								accumColors[0] = 0;
								accumColors[1] = 0;
								accumColors[2] = 0;
								colorCounts[0] = 0;
								colorCounts[1] = 0;
								colorCounts[2] = 0;
							}
						});
						break;

					case BitmapFormat.Bgra64:
						ImageProcessing.ParallelFor(0, height, y =>
						{
							if (cancellationToken.IsCancellationRequested)
								return;
							var accumColors = stackalloc int[3];
							var colorCounts = stackalloc int[3];
							var bitmapPixelPtr = (ushort*)((byte*)bitmapBaseAddress + bitmapRowStride * y);
							var leftBitmapPixelPtr = (ushort*)null;
							var rightBitmapPixelPtr = bitmapPixelPtr + 4;
							var topBitmapPixelPtr = (ushort*)((byte*)bitmapPixelPtr - bitmapRowStride);
							var bottomBitmapPixelPtr = (ushort*)((byte*)bitmapPixelPtr + bitmapRowStride);
							var isNotTopRow = (y > 0);
							var isNotBottomRow = (y < height - 1);
							for (var x = 0; x < width; ++x, leftBitmapPixelPtr = bitmapPixelPtr, bitmapPixelPtr = rightBitmapPixelPtr, rightBitmapPixelPtr += 4, topBitmapPixelPtr += 4, bottomBitmapPixelPtr += 4)
							{
								// get component at current pixel
								var centerComponent = colorComponentSelector(x, y);

								// collect colors around current pixel
								var isNotLastPixelInRow = x < lastColumnIndex;
								int neighborComponent;
								if (isNotTopRow)
								{
									if (x > 0)
									{
										neighborComponent = colorComponentSelector(x - 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topBitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = colorComponentSelector(x, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += topBitmapPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = colorComponentSelector(x + 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topBitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = colorComponentSelector(x - 1, y);
									if (neighborComponent != centerComponent)
									{
#pragma warning disable CS8602
										accumColors[neighborComponent] += leftBitmapPixelPtr[neighborComponent];
#pragma warning restore CS8602
										++colorCounts[neighborComponent];
									}
								}
								if (isNotLastPixelInRow)
								{
									neighborComponent = colorComponentSelector(x + 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += rightBitmapPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (isNotBottomRow)
								{
									if (x > 0)
									{
										neighborComponent = colorComponentSelector(x - 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottomBitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = colorComponentSelector(x, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += bottomBitmapPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = colorComponentSelector(x + 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottomBitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}

								// combine to full RGB color
								if (colorCounts[0] > 0)
									bitmapPixelPtr[0] = (ushort)(accumColors[0] / colorCounts[0]);
								if (colorCounts[1] > 0)
									bitmapPixelPtr[1] = (ushort)(accumColors[1] / colorCounts[1]);
								if (colorCounts[2] > 0)
									bitmapPixelPtr[2] = (ushort)(accumColors[2] / colorCounts[2]);
								accumColors[0] = 0;
								accumColors[1] = 0;
								accumColors[2] = 0;
								colorCounts[0] = 0;
								colorCounts[1] = 0;
								colorCounts[2] = 0;
							}
						});
						break;
				}
			});
		}


		// Demosaicing by 5x5 sub block.
		unsafe void Demosaic5x5(IBitmapBuffer bitmapBuffer, Func<int, int, int> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
		{
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			var bitmapRowStride = bitmapBuffer.RowBytes;
			var last1ColumnIndex = width - 1;
			var last2ColumnIndex = width - 2;
			bitmapBuffer.Memory.Pin(bitmapBaseAddress =>
			{
				switch (bitmapBuffer.Format)
				{
					case BitmapFormat.Bgra32:
						ImageProcessing.ParallelFor(0, height, (y) =>
						{
							if (cancellationToken.IsCancellationRequested)
								return;
							var accumColors = stackalloc int[3];
							var colorCounts = stackalloc int[3];
							var use5x5BlockColors = stackalloc bool[3];
							var bitmapPixelPtr = (byte*)bitmapBaseAddress + bitmapRowStride * y;
							var top1BitmapPixelPtr = bitmapPixelPtr - bitmapRowStride;
							var top2BitmapPixelPtr = bitmapPixelPtr - bitmapRowStride - bitmapRowStride;
							var bottom1BitmapPixelPtr = bitmapPixelPtr + bitmapRowStride;
							var bottom2BitmapPixelPtr = bitmapPixelPtr + bitmapRowStride + bitmapRowStride;
							var isNotTop1Row = (y > 0);
							var isNotTop2Row = (y > 1);
							var isNotBottom1Row = (y < height - 1);
							var isNotBottom2Row = (y < height - 2);
							for (var x = 0; x < width; ++x, bitmapPixelPtr += 4, top1BitmapPixelPtr += 4, top2BitmapPixelPtr += 4, bottom1BitmapPixelPtr += 4, bottom2BitmapPixelPtr += 4)
							{
								// get component at current pixel
								var centerComponent = colorComponentSelector(x, y);

								// collect colors in 3x3 sub block first
								int neighborComponent;
								if (isNotTop1Row)
								{
									if (x > 0)
									{
										neighborComponent = colorComponentSelector(x - 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1BitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = colorComponentSelector(x, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1BitmapPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = colorComponentSelector(x + 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1BitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = colorComponentSelector(x - 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = colorComponentSelector(x + 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bitmapPixelPtr + 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (isNotBottom1Row)
								{
									if (x > 0)
									{
										neighborComponent = colorComponentSelector(x - 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottom1BitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = colorComponentSelector(x, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottom1BitmapPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = colorComponentSelector(x + 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottom1BitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}

								// collect colors in 5x5 sub block if needed
								var is5x5BlockNeeded = false;
								for (var i = 2; i >= 0; --i)
								{
									if (centerComponent != i && colorCounts[i] == 0)
									{
										is5x5BlockNeeded = true;
										use5x5BlockColors[i] = true;
									}
								}
								if (is5x5BlockNeeded)
								{
									if (isNotTop2Row)
									{
										if (x > 1)
										{
											neighborComponent = colorComponentSelector(x - 2, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2BitmapPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = colorComponentSelector(x - 1, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2BitmapPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = colorComponentSelector(x, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2BitmapPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 1, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2BitmapPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 2, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2BitmapPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (isNotTop1Row)
									{
										if (x > 1)
										{
											neighborComponent = colorComponentSelector(x - 2, y - 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1BitmapPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 2, y - 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1BitmapPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (x > 1)
									{
										neighborComponent = colorComponentSelector(x - 2, y);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = colorComponentSelector(x + 2, y);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bitmapPixelPtr + 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (isNotBottom1Row)
									{
										if (x > 1)
										{
											neighborComponent = colorComponentSelector(x - 2, y + 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom1BitmapPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 2, y + 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom1BitmapPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (isNotBottom2Row)
									{
										if (x > 1)
										{
											neighborComponent = colorComponentSelector(x - 2, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2BitmapPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = colorComponentSelector(x - 1, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2BitmapPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = colorComponentSelector(x, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2BitmapPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 1, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2BitmapPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 2, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2BitmapPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
								}

								// combine to full RGB color
								for (var i = 2; i >= 0; --i)
								{
									if (i != centerComponent && colorCounts[i] > 0)
										bitmapPixelPtr[i] = (byte)(accumColors[i] / colorCounts[i]);
									accumColors[i] = 0;
									colorCounts[i] = 0;
									use5x5BlockColors[i] = false;
								}
							}
						});
						break;

					case BitmapFormat.Bgra64:
						ImageProcessing.ParallelFor(0, height, (y) =>
						{
							if (cancellationToken.IsCancellationRequested)
								return;
							var accumColors = stackalloc int[3];
							var colorCounts = stackalloc int[3];
							var use5x5BlockColors = stackalloc bool[3];
							var bitmapPixelPtr = (ushort*)((byte*)bitmapBaseAddress + bitmapRowStride * y);
							var top1BitmapPixelPtr = (ushort*)((byte*)bitmapPixelPtr - bitmapRowStride);
							var top2BitmapPixelPtr = (ushort*)((byte*)bitmapPixelPtr - bitmapRowStride - bitmapRowStride);
							var bottom1BitmapPixelPtr = (ushort*)((byte*)bitmapPixelPtr + bitmapRowStride);
							var bottom2BitmapPixelPtr = (ushort*)((byte*)bitmapPixelPtr + bitmapRowStride + bitmapRowStride);
							var isNotTop1Row = (y > 0);
							var isNotTop2Row = (y > 1);
							var isNotBottom1Row = (y < height - 1);
							var isNotBottom2Row = (y < height - 2);
							for (var x = 0; x < width; ++x, bitmapPixelPtr += 4, top1BitmapPixelPtr += 4, top2BitmapPixelPtr += 4, bottom1BitmapPixelPtr += 4, bottom2BitmapPixelPtr += 4)
							{
								// get component at current pixel
								var centerComponent = colorComponentSelector(x, y);

								// collect colors in 3x3 sub block first
								int neighborComponent;
								if (isNotTop1Row)
								{
									if (x > 0)
									{
										neighborComponent = colorComponentSelector(x - 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1BitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = colorComponentSelector(x, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1BitmapPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = colorComponentSelector(x + 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1BitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = colorComponentSelector(x - 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = colorComponentSelector(x + 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bitmapPixelPtr + 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (isNotBottom1Row)
								{
									if (x > 0)
									{
										neighborComponent = colorComponentSelector(x - 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottom1BitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = colorComponentSelector(x, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottom1BitmapPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = colorComponentSelector(x + 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottom1BitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}

								// collect colors in 5x5 sub block if needed
								var is5x5BlockNeeded = false;
								for (var i = 2; i >= 0; --i)
								{
									if (centerComponent != i && colorCounts[i] == 0)
									{
										is5x5BlockNeeded = true;
										use5x5BlockColors[i] = true;
									}
								}
								if (is5x5BlockNeeded)
								{
									if (isNotTop2Row)
									{
										if (x > 1)
										{
											neighborComponent = colorComponentSelector(x - 2, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2BitmapPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = colorComponentSelector(x - 1, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2BitmapPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = colorComponentSelector(x, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2BitmapPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 1, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2BitmapPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 2, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2BitmapPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (isNotTop1Row)
									{
										if (x > 1)
										{
											neighborComponent = colorComponentSelector(x - 2, y - 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1BitmapPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 2, y - 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1BitmapPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (x > 1)
									{
										neighborComponent = colorComponentSelector(x - 2, y);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = colorComponentSelector(x + 2, y);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bitmapPixelPtr + 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (isNotBottom1Row)
									{
										if (x > 1)
										{
											neighborComponent = colorComponentSelector(x - 2, y + 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom1BitmapPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 2, y + 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom1BitmapPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (isNotBottom2Row)
									{
										if (x > 1)
										{
											neighborComponent = colorComponentSelector(x - 2, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2BitmapPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = colorComponentSelector(x - 1, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2BitmapPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = colorComponentSelector(x, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2BitmapPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < width - 1)
										{
											neighborComponent = colorComponentSelector(x + 1, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2BitmapPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = colorComponentSelector(x + 2, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2BitmapPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
								}

								// combine to full RGB color
								for (var i = 2; i >= 0; --i)
								{
									if (i != centerComponent && colorCounts[i] > 0)
										bitmapPixelPtr[i] = (ushort)(accumColors[i] / colorCounts[i]);
									accumColors[i] = 0;
									colorCounts[i] = 0;
									use5x5BlockColors[i] = false;
								}
							}
						});
						break;
				}
			});
		}


		/// <inheritdoc/>
		protected override ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// get parameters
			var width = bitmapBuffer.Width;
			var height = bitmapBuffer.Height;
			if (width <= 0 || height <= 0)
				throw new ArgumentException($"Invalid size: {width}x{height}.");

			// select color pattern
			var colorPattern = ColorPatternMap[renderingOptions.BayerPattern];
			var colorPatternWidth = colorPattern[0].Length;
			var colorPatternHeight = colorPattern.Length;
			var colorComponentSelector = Global.Run(() =>
			{
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
						return new Func<int, int, int>((x, y) => colorPattern[y & yMask][x & xMask]);
					return (x, y) => colorPattern[y % colorPatternHeight][x & xMask];
				}
				else
				{
					if (yMask != 0)
						return (x, y) => colorPattern[y & yMask][x % colorPatternWidth];
					return (x, y) => colorPattern[y % colorPatternHeight][x % colorPatternWidth];
				}
			});

			// render
			var result = this.OnRender(source, imageStream, bitmapBuffer, colorComponentSelector, renderingOptions, planeOptions, cancellationToken);
			if (cancellationToken.IsCancellationRequested)
				throw new TaskCanceledException();

			// demosaicing
			if (!renderingOptions.Demosaicing)
				return result;
			var stopwatch = new Stopwatch().Also(it => it.Start());
			try
			{
				switch (colorPatternWidth)
				{
					case 4:
						this.Demosaic5x5(bitmapBuffer, colorComponentSelector, renderingOptions, cancellationToken);
						break;
					default:
						this.Demosaic3x3(bitmapBuffer, colorComponentSelector, renderingOptions, cancellationToken);
						break;
				}
				if (!cancellationToken.IsCancellationRequested)
					this.Logger.LogTrace("Demosaicing time: {duration} ms", stopwatch.ElapsedMilliseconds);
			}
			finally
			{
				stopwatch.Stop();
			}

			// complete
			return result;
		}


		/// <summary>
		/// Called to render image.
		/// </summary>
		/// <param name="source">Source of image data.</param>
		/// <param name="imageStream">Stream to read image data.</param>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> to put rendered bayer pattern image.</param>
		/// <param name="colorComponentSelector">Function to select color component for given pixel position.</param>
		/// <param name="renderingOptions">Rendering options.</param>
		/// <param name="planeOptions">Plane options.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Result of rendering.</returns>
		protected abstract ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, Func<int,int,int> colorComponentSelector, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


        /// <inheritdoc/>
        public override Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
	        Task.FromResult(BitmapFormat.Bgra64);
    }
}
