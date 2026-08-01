using Carina.PixelViewer.Media.ImageRenderers;
using CarinaStudio;
using CarinaStudio.Threading;
using System;
using System.Threading;

namespace Carina.PixelViewer.Media.Demosaicing;

/// <summary>
/// <see cref="DemosaicingAlgorithm"/> which interpolates each missing color component of pixel by averaging the same component of its neighbors.
/// </summary>
class BilinearDemosaicingAlgorithm() : DemosaicingAlgorithm("Bilinear")
{
	/// <inheritdoc/>
	[CalledOnBackgroundThread]
	public override void Demosaic(IBitmapBuffer bitmapBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		// widen the sub block to 5x5 because a 3x3 one cannot cover every color component of a color block which is wider than 2 pixels
		if (bayerPattern.BlockWidth > 2)
			this.Demosaic5x5(bitmapBuffer, colorComponentSelector, renderingOptions, cancellationToken);
		else
			this.Demosaic3x3(bitmapBuffer, colorComponentSelector, renderingOptions, cancellationToken);
	}


	// Demosaicing by 3x3 sub block.
	unsafe void Demosaic3x3(IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
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
							var centerComponent = (int)colorComponentSelector(x, y);

							// collect colors around current pixel
							var isNotLastPixelInRow = x < lastColumnIndex;
							int neighborComponent;
							if (isNotTopRow)
							{
								if (x > 0)
								{
									neighborComponent = (int)colorComponentSelector(x - 1, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (topBitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								neighborComponent = (int)colorComponentSelector(x, y - 1);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += topBitmapPixelPtr[neighborComponent];
									++colorCounts[neighborComponent];
								}
								if (isNotLastPixelInRow)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (topBitmapPixelPtr + 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
							}
							if (x > 0)
							{
								neighborComponent = (int)colorComponentSelector(x - 1, y);
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
								neighborComponent = (int)colorComponentSelector(x + 1, y);
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
									neighborComponent = (int)colorComponentSelector(x - 1, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottomBitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								neighborComponent = (int)colorComponentSelector(x, y + 1);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += bottomBitmapPixelPtr[neighborComponent];
									++colorCounts[neighborComponent];
								}
								if (isNotLastPixelInRow)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y + 1);
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
							var centerComponent = (int)colorComponentSelector(x, y);

							// collect colors around current pixel
							var isNotLastPixelInRow = x < lastColumnIndex;
							int neighborComponent;
							if (isNotTopRow)
							{
								if (x > 0)
								{
									neighborComponent = (int)colorComponentSelector(x - 1, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (topBitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								neighborComponent = (int)colorComponentSelector(x, y - 1);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += topBitmapPixelPtr[neighborComponent];
									++colorCounts[neighborComponent];
								}
								if (isNotLastPixelInRow)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (topBitmapPixelPtr + 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
							}
							if (x > 0)
							{
								neighborComponent = (int)colorComponentSelector(x - 1, y);
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
								neighborComponent = (int)colorComponentSelector(x + 1, y);
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
									neighborComponent = (int)colorComponentSelector(x - 1, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottomBitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								neighborComponent = (int)colorComponentSelector(x, y + 1);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += bottomBitmapPixelPtr[neighborComponent];
									++colorCounts[neighborComponent];
								}
								if (isNotLastPixelInRow)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y + 1);
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
	unsafe void Demosaic5x5(IBitmapBuffer bitmapBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
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
							var centerComponent = (int)colorComponentSelector(x, y);

							// collect colors in 3x3 sub block first
							int neighborComponent;
							if (isNotTop1Row)
							{
								if (x > 0)
								{
									neighborComponent = (int)colorComponentSelector(x - 1, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1BitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								neighborComponent = (int)colorComponentSelector(x, y - 1);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += (top1BitmapPixelPtr)[neighborComponent];
									++colorCounts[neighborComponent];
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1BitmapPixelPtr + 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
							}
							if (x > 0)
							{
								neighborComponent = (int)colorComponentSelector(x - 1, y);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += (bitmapPixelPtr - 4)[neighborComponent];
									++colorCounts[neighborComponent];
								}
							}
							if (x < last1ColumnIndex)
							{
								neighborComponent = (int)colorComponentSelector(x + 1, y);
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
									neighborComponent = (int)colorComponentSelector(x - 1, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottom1BitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								neighborComponent = (int)colorComponentSelector(x, y + 1);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += (bottom1BitmapPixelPtr)[neighborComponent];
									++colorCounts[neighborComponent];
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y + 1);
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
										neighborComponent = (int)colorComponentSelector(x - 2, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2BitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x > 0)
									{
										neighborComponent = (int)colorComponentSelector(x - 1, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2BitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y - 2);
									if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
									{
										accumColors[neighborComponent] += (top2BitmapPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2BitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y - 2);
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
										neighborComponent = (int)colorComponentSelector(x - 2, y - 1);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top1BitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y - 1);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top1BitmapPixelPtr + 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 1)
								{
									neighborComponent = (int)colorComponentSelector(x - 2, y);
									if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
									{
										accumColors[neighborComponent] += (bitmapPixelPtr - 8)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (x < last2ColumnIndex)
								{
									neighborComponent = (int)colorComponentSelector(x + 2, y);
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
										neighborComponent = (int)colorComponentSelector(x - 2, y + 1);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom1BitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y + 1);
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
										neighborComponent = (int)colorComponentSelector(x - 2, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2BitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x > 0)
									{
										neighborComponent = (int)colorComponentSelector(x - 1, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2BitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y + 2);
									if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
									{
										accumColors[neighborComponent] += (bottom2BitmapPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2BitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y + 2);
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
							var centerComponent = (int)colorComponentSelector(x, y);

							// collect colors in 3x3 sub block first
							int neighborComponent;
							if (isNotTop1Row)
							{
								if (x > 0)
								{
									neighborComponent = (int)colorComponentSelector(x - 1, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1BitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								neighborComponent = (int)colorComponentSelector(x, y - 1);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += (top1BitmapPixelPtr)[neighborComponent];
									++colorCounts[neighborComponent];
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1BitmapPixelPtr + 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
							}
							if (x > 0)
							{
								neighborComponent = (int)colorComponentSelector(x - 1, y);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += (bitmapPixelPtr - 4)[neighborComponent];
									++colorCounts[neighborComponent];
								}
							}
							if (x < last1ColumnIndex)
							{
								neighborComponent = (int)colorComponentSelector(x + 1, y);
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
									neighborComponent = (int)colorComponentSelector(x - 1, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottom1BitmapPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								neighborComponent = (int)colorComponentSelector(x, y + 1);
								if (neighborComponent != centerComponent)
								{
									accumColors[neighborComponent] += (bottom1BitmapPixelPtr)[neighborComponent];
									++colorCounts[neighborComponent];
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y + 1);
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
										neighborComponent = (int)colorComponentSelector(x - 2, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2BitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x > 0)
									{
										neighborComponent = (int)colorComponentSelector(x - 1, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2BitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y - 2);
									if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
									{
										accumColors[neighborComponent] += (top2BitmapPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2BitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y - 2);
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
										neighborComponent = (int)colorComponentSelector(x - 2, y - 1);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top1BitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y - 1);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top1BitmapPixelPtr + 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 1)
								{
									neighborComponent = (int)colorComponentSelector(x - 2, y);
									if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
									{
										accumColors[neighborComponent] += (bitmapPixelPtr - 8)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (x < last2ColumnIndex)
								{
									neighborComponent = (int)colorComponentSelector(x + 2, y);
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
										neighborComponent = (int)colorComponentSelector(x - 2, y + 1);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom1BitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y + 1);
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
										neighborComponent = (int)colorComponentSelector(x - 2, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2BitmapPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x > 0)
									{
										neighborComponent = (int)colorComponentSelector(x - 1, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2BitmapPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y + 2);
									if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
									{
										accumColors[neighborComponent] += (bottom2BitmapPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < width - 1)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2BitmapPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y + 2);
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
}
