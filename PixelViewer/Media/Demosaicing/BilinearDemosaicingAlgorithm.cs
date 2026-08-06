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
	public override void Demosaic(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		// widen the sub block to 5x5 because a 3x3 one cannot cover every color component of a color block which is wider than 2 pixels
		if (bayerPattern.BlockWidth > 2)
			this.Demosaic5x5(srcBuffer, destBuffer, colorComponentSelector, renderingOptions, cancellationToken);
		else
			this.Demosaic3x3(srcBuffer, destBuffer, colorComponentSelector, renderingOptions, cancellationToken);
	}


	// Demosaicing by 3x3 sub block. Only the color component provided by each pixel is read from the source buffer, so the source and the destination can be the same buffer.
	unsafe void Demosaic3x3(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		var width = srcBuffer.Width;
		var height = srcBuffer.Height;
		var srcRowStride = srcBuffer.RowBytes;
		var destRowStride = destBuffer.RowBytes;
		var lastColumnIndex = width - 1;
		srcBuffer.Memory.Pin(srcBaseAddress =>
		{
			destBuffer.Memory.Pin(destBaseAddress =>
			{
				switch (srcBuffer.Format)
				{
					case BitmapFormat.Bgra32:
						ImageProcessing.ParallelFor(0, height, y =>
						{
							if (cancellationToken.IsCancellationRequested)
								return;
							var accumColors = stackalloc int[3];
							var colorCounts = stackalloc int[3];
							var srcPixelPtr = (byte*)srcBaseAddress + srcRowStride * y;
							var destPixelPtr = (byte*)destBaseAddress + destRowStride * y;
							var leftSrcPixelPtr = (byte*)null;
							var rightSrcPixelPtr = srcPixelPtr + 4;
							var topSrcPixelPtr = srcPixelPtr - srcRowStride;
							var bottomSrcPixelPtr = srcPixelPtr + srcRowStride;
							var isNotTopRow = (y > 0);
							var isNotBottomRow = (y < height - 1);
							for (var x = 0; x < width; ++x, leftSrcPixelPtr = srcPixelPtr, srcPixelPtr = rightSrcPixelPtr, rightSrcPixelPtr += 4, topSrcPixelPtr += 4, bottomSrcPixelPtr += 4, destPixelPtr += 4)
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
											accumColors[neighborComponent] += (topSrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += topSrcPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topSrcPixelPtr + 4)[neighborComponent];
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
										accumColors[neighborComponent] += leftSrcPixelPtr[neighborComponent];
#pragma warning restore CS8602
										++colorCounts[neighborComponent];
									}
								}
								if (isNotLastPixelInRow)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += rightSrcPixelPtr[neighborComponent];
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
											accumColors[neighborComponent] += (bottomSrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += bottomSrcPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottomSrcPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}

								// combine to full BGRA color, the component provided by the pixel itself and the alpha are copied from the source
								destPixelPtr[0] = colorCounts[0] > 0 ? (byte)(accumColors[0] / colorCounts[0]) : srcPixelPtr[0];
								destPixelPtr[1] = colorCounts[1] > 0 ? (byte)(accumColors[1] / colorCounts[1]) : srcPixelPtr[1];
								destPixelPtr[2] = colorCounts[2] > 0 ? (byte)(accumColors[2] / colorCounts[2]) : srcPixelPtr[2];
								destPixelPtr[3] = srcPixelPtr[3];
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
							var srcPixelPtr = (ushort*)((byte*)srcBaseAddress + srcRowStride * y);
							var destPixelPtr = (ushort*)((byte*)destBaseAddress + destRowStride * y);
							var leftSrcPixelPtr = (ushort*)null;
							var rightSrcPixelPtr = srcPixelPtr + 4;
							var topSrcPixelPtr = (ushort*)((byte*)srcPixelPtr - srcRowStride);
							var bottomSrcPixelPtr = (ushort*)((byte*)srcPixelPtr + srcRowStride);
							var isNotTopRow = (y > 0);
							var isNotBottomRow = (y < height - 1);
							for (var x = 0; x < width; ++x, leftSrcPixelPtr = srcPixelPtr, srcPixelPtr = rightSrcPixelPtr, rightSrcPixelPtr += 4, topSrcPixelPtr += 4, bottomSrcPixelPtr += 4, destPixelPtr += 4)
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
											accumColors[neighborComponent] += (topSrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += topSrcPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topSrcPixelPtr + 4)[neighborComponent];
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
										accumColors[neighborComponent] += leftSrcPixelPtr[neighborComponent];
#pragma warning restore CS8602
										++colorCounts[neighborComponent];
									}
								}
								if (isNotLastPixelInRow)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += rightSrcPixelPtr[neighborComponent];
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
											accumColors[neighborComponent] += (bottomSrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += bottomSrcPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottomSrcPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}

								// combine to full BGRA color, the component provided by the pixel itself and the alpha are copied from the source
								destPixelPtr[0] = colorCounts[0] > 0 ? (ushort)(accumColors[0] / colorCounts[0]) : srcPixelPtr[0];
								destPixelPtr[1] = colorCounts[1] > 0 ? (ushort)(accumColors[1] / colorCounts[1]) : srcPixelPtr[1];
								destPixelPtr[2] = colorCounts[2] > 0 ? (ushort)(accumColors[2] / colorCounts[2]) : srcPixelPtr[2];
								destPixelPtr[3] = srcPixelPtr[3];
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
		});
	}


	// Demosaicing by 5x5 sub block. Only the color component provided by each pixel is read from the source buffer, so the source and the destination can be the same buffer.
	unsafe void Demosaic5x5(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		var width = srcBuffer.Width;
		var height = srcBuffer.Height;
		var srcRowStride = srcBuffer.RowBytes;
		var destRowStride = destBuffer.RowBytes;
		var last1ColumnIndex = width - 1;
		var last2ColumnIndex = width - 2;
		srcBuffer.Memory.Pin(srcBaseAddress =>
		{
			destBuffer.Memory.Pin(destBaseAddress =>
			{
				switch (srcBuffer.Format)
				{
					case BitmapFormat.Bgra32:
						ImageProcessing.ParallelFor(0, height, y =>
						{
							if (cancellationToken.IsCancellationRequested)
								return;
							var accumColors = stackalloc int[3];
							var colorCounts = stackalloc int[3];
							var use5x5BlockColors = stackalloc bool[3];
							var srcPixelPtr = (byte*)srcBaseAddress + srcRowStride * y;
							var destPixelPtr = (byte*)destBaseAddress + destRowStride * y;
							var top1SrcPixelPtr = srcPixelPtr - srcRowStride;
							var top2SrcPixelPtr = srcPixelPtr - srcRowStride - srcRowStride;
							var bottom1SrcPixelPtr = srcPixelPtr + srcRowStride;
							var bottom2SrcPixelPtr = srcPixelPtr + srcRowStride + srcRowStride;
							var isNotTop1Row = (y > 0);
							var isNotTop2Row = (y > 1);
							var isNotBottom1Row = (y < height - 1);
							var isNotBottom2Row = (y < height - 2);
							for (var x = 0; x < width; ++x, srcPixelPtr += 4, destPixelPtr += 4, top1SrcPixelPtr += 4, top2SrcPixelPtr += 4, bottom1SrcPixelPtr += 4, bottom2SrcPixelPtr += 4)
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
											accumColors[neighborComponent] += (top1SrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1SrcPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1SrcPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = (int)colorComponentSelector(x - 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (srcPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (srcPixelPtr + 4)[neighborComponent];
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
											accumColors[neighborComponent] += (bottom1SrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottom1SrcPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottom1SrcPixelPtr + 4)[neighborComponent];
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
												accumColors[neighborComponent] += (top2SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = (int)colorComponentSelector(x - 1, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = (int)colorComponentSelector(x, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2SrcPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 1, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 2, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr + 8)[neighborComponent];
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
												accumColors[neighborComponent] += (top1SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 2, y - 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1SrcPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (x > 1)
									{
										neighborComponent = (int)colorComponentSelector(x - 2, y);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (srcPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (srcPixelPtr + 8)[neighborComponent];
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
												accumColors[neighborComponent] += (bottom1SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 2, y + 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom1SrcPixelPtr + 8)[neighborComponent];
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
												accumColors[neighborComponent] += (bottom2SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = (int)colorComponentSelector(x - 1, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = (int)colorComponentSelector(x, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2SrcPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 1, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 2, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
								}

								// combine to full BGRA color, the component provided by the pixel itself and the alpha are copied from the source
								for (var i = 2; i >= 0; --i)
								{
									destPixelPtr[i] = colorCounts[i] > 0 ? (byte)(accumColors[i] / colorCounts[i]) : srcPixelPtr[i];
									accumColors[i] = 0;
									colorCounts[i] = 0;
									use5x5BlockColors[i] = false;
								}
								destPixelPtr[3] = srcPixelPtr[3];
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
							var use5x5BlockColors = stackalloc bool[3];
							var srcPixelPtr = (ushort*)((byte*)srcBaseAddress + srcRowStride * y);
							var destPixelPtr = (ushort*)((byte*)destBaseAddress + destRowStride * y);
							var top1SrcPixelPtr = (ushort*)((byte*)srcPixelPtr - srcRowStride);
							var top2SrcPixelPtr = (ushort*)((byte*)srcPixelPtr - srcRowStride - srcRowStride);
							var bottom1SrcPixelPtr = (ushort*)((byte*)srcPixelPtr + srcRowStride);
							var bottom2SrcPixelPtr = (ushort*)((byte*)srcPixelPtr + srcRowStride + srcRowStride);
							var isNotTop1Row = (y > 0);
							var isNotTop2Row = (y > 1);
							var isNotBottom1Row = (y < height - 1);
							var isNotBottom2Row = (y < height - 2);
							for (var x = 0; x < width; ++x, srcPixelPtr += 4, destPixelPtr += 4, top1SrcPixelPtr += 4, top2SrcPixelPtr += 4, bottom1SrcPixelPtr += 4, bottom2SrcPixelPtr += 4)
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
											accumColors[neighborComponent] += (top1SrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y - 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1SrcPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y - 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1SrcPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = (int)colorComponentSelector(x - 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (srcPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = (int)colorComponentSelector(x + 1, y);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (srcPixelPtr + 4)[neighborComponent];
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
											accumColors[neighborComponent] += (bottom1SrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = (int)colorComponentSelector(x, y + 1);
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottom1SrcPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 1, y + 1);
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottom1SrcPixelPtr + 4)[neighborComponent];
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
												accumColors[neighborComponent] += (top2SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = (int)colorComponentSelector(x - 1, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = (int)colorComponentSelector(x, y - 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2SrcPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 1, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 2, y - 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr + 8)[neighborComponent];
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
												accumColors[neighborComponent] += (top1SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 2, y - 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1SrcPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (x > 1)
									{
										neighborComponent = (int)colorComponentSelector(x - 2, y);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (srcPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = (int)colorComponentSelector(x + 2, y);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (srcPixelPtr + 8)[neighborComponent];
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
												accumColors[neighborComponent] += (bottom1SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 2, y + 1);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom1SrcPixelPtr + 8)[neighborComponent];
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
												accumColors[neighborComponent] += (bottom2SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = (int)colorComponentSelector(x - 1, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = (int)colorComponentSelector(x, y + 2);
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2SrcPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 1, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = (int)colorComponentSelector(x + 2, y + 2);
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
								}

								// combine to full BGRA color, the component provided by the pixel itself and the alpha are copied from the source
								for (var i = 2; i >= 0; --i)
								{
									destPixelPtr[i] = colorCounts[i] > 0 ? (ushort)(accumColors[i] / colorCounts[i]) : srcPixelPtr[i];
									accumColors[i] = 0;
									colorCounts[i] = 0;
									use5x5BlockColors[i] = false;
								}
								destPixelPtr[3] = srcPixelPtr[3];
							}
						});
						break;
				}
			});
		});
	}


	/// <inheritdoc/>
	public override bool IsInPlaceDemosaicingSupported(BayerPattern pattern) => true;
}
