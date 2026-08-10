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
	// Constants.
	const int ComponentTableOffset3x3 = 1;
	const int ComponentTableOffset5x5 = 2;


	/// <inheritdoc/>
	public override OutputBufferRequirement CheckOutputBufferRequirement(ImageRenderingOptions renderingOptions, int width, int height) => OutputBufferRequirement.NotRequired;


	/// <inheritdoc/>
	[CalledOnBackgroundThread]
	public override void Demosaic(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, Memory<byte> workingBuffer, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		// widen the sub block to 5x5 because a 3x3 one cannot cover every color component of a color block which is wider than 2 pixels
		if (bayerPattern.BlockWidth > 2)
			this.Demosaic5x5(srcBuffer, destBuffer, bayerPattern, colorComponentSelector, renderingOptions, cancellationToken);
		else
			this.Demosaic3x3(srcBuffer, destBuffer, bayerPattern, colorComponentSelector, renderingOptions, cancellationToken);
	}


	// Demosaicing by 3x3 sub block. Only the color component provided by each pixel is read from the source buffer, so the source and the destination can be the same buffer.
	unsafe void Demosaic3x3(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		var width = srcBuffer.Width;
		var height = srcBuffer.Height;
		var srcRowStride = srcBuffer.RowBytes;
		var destRowStride = destBuffer.RowBytes;
		var lastColumnIndex = width - 1;
		var blockWidth = bayerPattern.BlockWidth;
		var lastPhase = blockWidth - 1;
		var componentCount = blockWidth + ComponentTableOffset3x3 * 2;
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
							var topComponents = stackalloc int[componentCount];
							var centerComponents = stackalloc int[componentCount];
							var bottomComponents = stackalloc int[componentCount];
							var phase = 0;
							SelectRowColorComponents(colorComponentSelector, y, blockWidth, ComponentTableOffset3x3, centerComponents);
							if (isNotTopRow)
								SelectRowColorComponents(colorComponentSelector, y - 1, blockWidth, ComponentTableOffset3x3, topComponents);
							if (isNotBottomRow)
								SelectRowColorComponents(colorComponentSelector, y + 1, blockWidth, ComponentTableOffset3x3, bottomComponents);
							for (var x = 0; x < width; ++x, leftSrcPixelPtr = srcPixelPtr, srcPixelPtr = rightSrcPixelPtr, rightSrcPixelPtr += 4, topSrcPixelPtr += 4, bottomSrcPixelPtr += 4, destPixelPtr += 4, phase = phase < lastPhase ? phase + 1 : 0)
							{
								// get component at current pixel
								var centerComponent = centerComponents[phase + 1];

								// collect colors around current pixel
								var isNotLastPixelInRow = x < lastColumnIndex;
								int neighborComponent;
								if (isNotTopRow)
								{
									if (x > 0)
									{
										neighborComponent = topComponents[phase];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topSrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = topComponents[phase + 1];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += topSrcPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = topComponents[phase + 2];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topSrcPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = centerComponents[phase];
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
									neighborComponent = centerComponents[phase + 2];
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
										neighborComponent = bottomComponents[phase];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottomSrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = bottomComponents[phase + 1];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += bottomSrcPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = bottomComponents[phase + 2];
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
							var topComponents = stackalloc int[componentCount];
							var centerComponents = stackalloc int[componentCount];
							var bottomComponents = stackalloc int[componentCount];
							var phase = 0;
							SelectRowColorComponents(colorComponentSelector, y, blockWidth, ComponentTableOffset3x3, centerComponents);
							if (isNotTopRow)
								SelectRowColorComponents(colorComponentSelector, y - 1, blockWidth, ComponentTableOffset3x3, topComponents);
							if (isNotBottomRow)
								SelectRowColorComponents(colorComponentSelector, y + 1, blockWidth, ComponentTableOffset3x3, bottomComponents);
							for (var x = 0; x < width; ++x, leftSrcPixelPtr = srcPixelPtr, srcPixelPtr = rightSrcPixelPtr, rightSrcPixelPtr += 4, topSrcPixelPtr += 4, bottomSrcPixelPtr += 4, destPixelPtr += 4, phase = phase < lastPhase ? phase + 1 : 0)
							{
								// get component at current pixel
								var centerComponent = centerComponents[phase + 1];

								// collect colors around current pixel
								var isNotLastPixelInRow = x < lastColumnIndex;
								int neighborComponent;
								if (isNotTopRow)
								{
									if (x > 0)
									{
										neighborComponent = topComponents[phase];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topSrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = topComponents[phase + 1];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += topSrcPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = topComponents[phase + 2];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (topSrcPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = centerComponents[phase];
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
									neighborComponent = centerComponents[phase + 2];
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
										neighborComponent = bottomComponents[phase];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottomSrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = bottomComponents[phase + 1];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += bottomSrcPixelPtr[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (isNotLastPixelInRow)
									{
										neighborComponent = bottomComponents[phase + 2];
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
	unsafe void Demosaic5x5(IBitmapBuffer srcBuffer, IBitmapBuffer destBuffer, BayerPattern bayerPattern, Func<int, int, BayerPatternColorComponent> colorComponentSelector, ImageRenderingOptions renderingOptions, CancellationToken cancellationToken)
	{
		var width = srcBuffer.Width;
		var height = srcBuffer.Height;
		var srcRowStride = srcBuffer.RowBytes;
		var destRowStride = destBuffer.RowBytes;
		var last1ColumnIndex = width - 1;
		var last2ColumnIndex = width - 2;
		var blockWidth = bayerPattern.BlockWidth;
		var lastPhase = blockWidth - 1;
		var componentCount = blockWidth + ComponentTableOffset5x5 * 2;
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
							var top2Components = stackalloc int[componentCount];
							var top1Components = stackalloc int[componentCount];
							var centerComponents = stackalloc int[componentCount];
							var bottom1Components = stackalloc int[componentCount];
							var bottom2Components = stackalloc int[componentCount];
							var phase = 0;
							SelectRowColorComponents(colorComponentSelector, y, blockWidth, ComponentTableOffset5x5, centerComponents);
							if (isNotTop1Row)
								SelectRowColorComponents(colorComponentSelector, y - 1, blockWidth, ComponentTableOffset5x5, top1Components);
							if (isNotTop2Row)
								SelectRowColorComponents(colorComponentSelector, y - 2, blockWidth, ComponentTableOffset5x5, top2Components);
							if (isNotBottom1Row)
								SelectRowColorComponents(colorComponentSelector, y + 1, blockWidth, ComponentTableOffset5x5, bottom1Components);
							if (isNotBottom2Row)
								SelectRowColorComponents(colorComponentSelector, y + 2, blockWidth, ComponentTableOffset5x5, bottom2Components);
							for (var x = 0; x < width; ++x, srcPixelPtr += 4, destPixelPtr += 4, top1SrcPixelPtr += 4, top2SrcPixelPtr += 4, bottom1SrcPixelPtr += 4, bottom2SrcPixelPtr += 4, phase = phase < lastPhase ? phase + 1 : 0)
							{
								// get component at current pixel
								var centerComponent = centerComponents[phase + 2];

								// collect colors in 3x3 sub block first
								int neighborComponent;
								if (isNotTop1Row)
								{
									if (x > 0)
									{
										neighborComponent = top1Components[phase + 1];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1SrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = top1Components[phase + 2];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1SrcPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = top1Components[phase + 3];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1SrcPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = centerComponents[phase + 1];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (srcPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = centerComponents[phase + 3];
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
										neighborComponent = bottom1Components[phase + 1];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottom1SrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = bottom1Components[phase + 2];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottom1SrcPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = bottom1Components[phase + 3];
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
											neighborComponent = top2Components[phase];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = top2Components[phase + 1];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = top2Components[phase + 2];
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2SrcPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = top2Components[phase + 3];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = top2Components[phase + 4];
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
											neighborComponent = top1Components[phase];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = top1Components[phase + 4];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1SrcPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (x > 1)
									{
										neighborComponent = centerComponents[phase];
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (srcPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = centerComponents[phase + 4];
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
											neighborComponent = bottom1Components[phase];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom1SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = bottom1Components[phase + 4];
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
											neighborComponent = bottom2Components[phase];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = bottom2Components[phase + 1];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = bottom2Components[phase + 2];
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2SrcPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = bottom2Components[phase + 3];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = bottom2Components[phase + 4];
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
							var top2Components = stackalloc int[componentCount];
							var top1Components = stackalloc int[componentCount];
							var centerComponents = stackalloc int[componentCount];
							var bottom1Components = stackalloc int[componentCount];
							var bottom2Components = stackalloc int[componentCount];
							var phase = 0;
							SelectRowColorComponents(colorComponentSelector, y, blockWidth, ComponentTableOffset5x5, centerComponents);
							if (isNotTop1Row)
								SelectRowColorComponents(colorComponentSelector, y - 1, blockWidth, ComponentTableOffset5x5, top1Components);
							if (isNotTop2Row)
								SelectRowColorComponents(colorComponentSelector, y - 2, blockWidth, ComponentTableOffset5x5, top2Components);
							if (isNotBottom1Row)
								SelectRowColorComponents(colorComponentSelector, y + 1, blockWidth, ComponentTableOffset5x5, bottom1Components);
							if (isNotBottom2Row)
								SelectRowColorComponents(colorComponentSelector, y + 2, blockWidth, ComponentTableOffset5x5, bottom2Components);
							for (var x = 0; x < width; ++x, srcPixelPtr += 4, destPixelPtr += 4, top1SrcPixelPtr += 4, top2SrcPixelPtr += 4, bottom1SrcPixelPtr += 4, bottom2SrcPixelPtr += 4, phase = phase < lastPhase ? phase + 1 : 0)
							{
								// get component at current pixel
								var centerComponent = centerComponents[phase + 2];

								// collect colors in 3x3 sub block first
								int neighborComponent;
								if (isNotTop1Row)
								{
									if (x > 0)
									{
										neighborComponent = top1Components[phase + 1];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1SrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = top1Components[phase + 2];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (top1SrcPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = top1Components[phase + 3];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (top1SrcPixelPtr + 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
								}
								if (x > 0)
								{
									neighborComponent = centerComponents[phase + 1];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (srcPixelPtr - 4)[neighborComponent];
										++colorCounts[neighborComponent];
									}
								}
								if (x < last1ColumnIndex)
								{
									neighborComponent = centerComponents[phase + 3];
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
										neighborComponent = bottom1Components[phase + 1];
										if (neighborComponent != centerComponent)
										{
											accumColors[neighborComponent] += (bottom1SrcPixelPtr - 4)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									neighborComponent = bottom1Components[phase + 2];
									if (neighborComponent != centerComponent)
									{
										accumColors[neighborComponent] += (bottom1SrcPixelPtr)[neighborComponent];
										++colorCounts[neighborComponent];
									}
									if (x < last1ColumnIndex)
									{
										neighborComponent = bottom1Components[phase + 3];
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
											neighborComponent = top2Components[phase];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = top2Components[phase + 1];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = top2Components[phase + 2];
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (top2SrcPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = top2Components[phase + 3];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top2SrcPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = top2Components[phase + 4];
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
											neighborComponent = top1Components[phase];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = top1Components[phase + 4];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (top1SrcPixelPtr + 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
									}
									if (x > 1)
									{
										neighborComponent = centerComponents[phase];
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (srcPixelPtr - 8)[neighborComponent];
											++colorCounts[neighborComponent];
										}
									}
									if (x < last2ColumnIndex)
									{
										neighborComponent = centerComponents[phase + 4];
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
											neighborComponent = bottom1Components[phase];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom1SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = bottom1Components[phase + 4];
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
											neighborComponent = bottom2Components[phase];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr - 8)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x > 0)
										{
											neighborComponent = bottom2Components[phase + 1];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr - 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										neighborComponent = bottom2Components[phase + 2];
										if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
										{
											accumColors[neighborComponent] += (bottom2SrcPixelPtr)[neighborComponent];
											++colorCounts[neighborComponent];
										}
										if (x < last1ColumnIndex)
										{
											neighborComponent = bottom2Components[phase + 3];
											if (neighborComponent != centerComponent && use5x5BlockColors[neighborComponent])
											{
												accumColors[neighborComponent] += (bottom2SrcPixelPtr + 4)[neighborComponent];
												++colorCounts[neighborComponent];
											}
										}
										if (x < last2ColumnIndex)
										{
											neighborComponent = bottom2Components[phase + 4];
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


	// Resolve the color components which the pixels of the given row provide, one for each phase of pixel in the block of pattern. The table is padded by the given number of phases at both of its ends so that the neighbors of the first and the last phase are read without wrapping the index, and the caller is expected to fill it only for a row which is inside the image because the selector is not defined outside of it.
	static unsafe void SelectRowColorComponents(Func<int, int, BayerPatternColorComponent> colorComponentSelector, int y, int blockWidth, int offset, int* components)
	{
		for (var i = blockWidth + offset * 2 - 1; i >= 0; --i)
		{
			var phase = (i - offset) % blockWidth;
			if (phase < 0)
				phase += blockWidth;
			components[i] = (int)colorComponentSelector(phase, y);
		}
	}
}
