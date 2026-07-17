using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.IO;
using CarinaStudio.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers;

/// <summary>
/// Base implementation of <see cref="IImageRenderer"/>.
/// </summary>
abstract class BaseImageRenderer : IImageRenderer
{
	// Static fields.
	static readonly TaskFactory RenderingTaskFactory = new(new FixedThreadsTaskScheduler(Math.Min(2, Environment.ProcessorCount)));


	// Fields.
	volatile ImageFormat format;


	/// <summary>
	/// Initialize new <see cref="BaseImageRenderer"/> instance.
	/// </summary>
	/// <param name="format">Format supported by this instance.</param>
	protected BaseImageRenderer(ImageFormat format)
	{
		this.format = format;
		this.Logger = IAppSuiteApplication.Current.LoggerFactory.CreateLogger(this.GetType().Name);
	}


	/// <summary>
	/// Change the format supported by this renderer.
	/// </summary>
	/// <param name="format">New format, which is expected to carry the same identifier as the current one.</param>
	/// <exception cref="InvalidOperationException">The renderer is a built-in renderer, whose format is fixed.</exception>
	/// <remarks><see cref="ImageFormat"/> is immutable, so a renderer which owns a user-defined format calls this method to swap in a new instance after the user edits the format. Keeping the identifier is what allows the references persisted by profiles and sessions to still be resolved. Because the name of a format must be unique, the caller unregisters the current format (<see cref="ImageFormat.Unregister"/>) before constructing <paramref name="format"/> with the same identifier, so this method only swaps the reference and does not touch the registration.</remarks>
	protected void ChangeFormat(ImageFormat format)
	{
		// reject built-in renderer whose format is fixed
		if (this.IsBuiltIn)
			throw new InvalidOperationException("Cannot change the format supported by a built-in renderer.");
		if (ReferenceEquals(this.format, format))
			return;

		// swap the format, the field is volatile because rendering reads it on background thread
		this.format = format;

		// notify
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Format)));
	}


	/// <summary>
	/// Create function to extract from [9, 16]-bit data to 16-bit data.
	/// </summary>
	/// <param name="byteOrdering">Byte ordering.</param>
	/// <param name="effectiveBits">Effective bits, range is [9, 16].</param>
	/// <param name="lsbAligned">True if effective bits are aligned to LSB, False if aligned to MSB.</param>
	/// <returns>Extraction function.</returns>
	protected Func<byte, byte, ushort> Create16BitColorExtraction(ByteOrdering byteOrdering, int effectiveBits, bool lsbAligned = true) =>
		this.Create16BitColorExtraction(byteOrdering, effectiveBits, lsbAligned, 0, (uint)(1 << effectiveBits) - 1);


	/// <summary>
	/// Create function to extract from [9, 16]-bit data to 16-bit data.
	/// </summary>
	/// <param name="byteOrdering">Byte ordering.</param>
	/// <param name="effectiveBits">Effective bits, range is [9, 16].</param>
	/// <param name="blackLevel">Black level.</param>
	/// <param name="whiteLevel">White level.</param>
	/// <returns>Extraction function.</returns>
	protected Func<byte, byte, ushort> Create16BitColorExtraction(ByteOrdering byteOrdering, int effectiveBits, uint blackLevel, uint whiteLevel) =>
		this.Create16BitColorExtraction(byteOrdering, effectiveBits, true, blackLevel, whiteLevel);


	/// <summary>
	/// Create function to extract from [9, 16]-bit data to 16-bit data.
	/// </summary>
	/// <param name="byteOrdering">Byte ordering.</param>
	/// <param name="effectiveBits">Effective bits, range is [9, 16].</param>
	/// <param name="lsbAligned">True if effective bits are aligned to LSB, False if aligned to MSB.</param>
	/// <param name="blackLevel">Black level.</param>
	/// <param name="whiteLevel">White level.</param>
	/// <returns>Extraction function.</returns>
	protected Func<byte, byte, ushort> Create16BitColorExtraction(ByteOrdering byteOrdering, int effectiveBits, bool lsbAligned, uint blackLevel, uint whiteLevel)
	{
		// check parameters
		if (effectiveBits < 9 || effectiveBits > 16)
			throw new ArgumentOutOfRangeException(nameof(effectiveBits));
		if (blackLevel >= whiteLevel)
			throw new ArgumentOutOfRangeException(nameof(blackLevel));
		if (whiteLevel > (1 << effectiveBits) - 1)
			throw new ArgumentOutOfRangeException(nameof(whiteLevel));
		
		// conversion with full bits
		ushort[] correctedColors;
		if (effectiveBits == 16)
		{
			if (blackLevel == 0 && whiteLevel == 65535)
			{
				return byteOrdering == ByteOrdering.LittleEndian
					? (b1, b2) => (ushort)((b2 << 8) | b1)
					: (b1, b2) => (ushort)((b1 << 8) | b2);
			}
			correctedColors = new ushort[65536].Also(it =>
			{
				var scale = 65535.0 / (whiteLevel - blackLevel);
				for (var i = whiteLevel; i > blackLevel; --i)
					it[i] = (ushort)((i - blackLevel) * scale + 0.5);
				for (var i = it.Length - 1; i > whiteLevel; --i)
					it[i] = 65535;
			});
			return byteOrdering == ByteOrdering.LittleEndian
				? (b1, b2) => correctedColors[(ushort)((b2 << 8) | b1)]
				: (b1, b2) => correctedColors[(ushort)((b1 << 8) | b2)];
		}

		// conversion with partial bits
		var effectiveBitsShiftCount = lsbAligned 
			? 16 - effectiveBits 
			: 0;
		var effectiveBitsMask = lsbAligned 
			? 0xffff >> effectiveBitsShiftCount 
			: (0xffff << (16 - effectiveBits)) & 0xffff;
		var paddingBitsShiftCount = lsbAligned 
			? (effectiveBits << 1) - 16 
			: 16 - effectiveBits;
		var paddingBitsMask = 0xffff >> effectiveBits;
		if (blackLevel == 0 && whiteLevel == (1 << effectiveBits) - 1)
		{
			return byteOrdering == ByteOrdering.LittleEndian
				? (b1, b2) =>
				{
					var value = (b2 << 8) | b1;
					return (ushort)(((value & effectiveBitsMask) << effectiveBitsShiftCount) | ((value >> paddingBitsShiftCount) & paddingBitsMask));
				}
				: (b1, b2) =>
				{
					var value = (b1 << 8) | b2;
					return (ushort)(((value & effectiveBitsMask) << effectiveBitsShiftCount) | ((value >> paddingBitsShiftCount) & paddingBitsMask));
				};
		}
		correctedColors = new ushort[1 << effectiveBits].Also(it =>
		{
			var maxColor = (ushort)(it.Length - 1);
			var scale = (double)maxColor / (whiteLevel - blackLevel);
			for (var i = whiteLevel; i > blackLevel; --i)
				it[i] = (ushort)((i - blackLevel) * scale + 0.5);
			for (var i = it.Length - 1; i > whiteLevel; --i)
				it[i] = maxColor;
		});
		if (lsbAligned)
		{
			return byteOrdering == ByteOrdering.LittleEndian
				? (b1, b2) =>
				{
					var value = (b2 << 8) | b1;
					value = correctedColors[(ushort)(value & effectiveBitsMask)];
					return (ushort)((value << effectiveBitsShiftCount) | ((value >> paddingBitsShiftCount) & paddingBitsMask));
				}
				: (b1, b2) =>
				{
					var value = (b1 << 8) | b2;
					value = correctedColors[(ushort)(value & effectiveBitsMask)];
					return (ushort)((value << effectiveBitsShiftCount) | ((value >> paddingBitsShiftCount) & paddingBitsMask));
				};
		}
		return byteOrdering == ByteOrdering.LittleEndian
			? (b1, b2) =>
			{
				var value = (b2 << 8) | b1;
				value = correctedColors[(ushort)((value & effectiveBitsMask) >> paddingBitsShiftCount)];
				return (ushort)((value << effectiveBitsShiftCount) | ((value >> paddingBitsShiftCount) & paddingBitsMask));
			}
			: (b1, b2) =>
			{
				var value = (b1 << 8) | b2;
				value = correctedColors[(ushort)((value & effectiveBitsMask) >> paddingBitsShiftCount)];
				return (ushort)((value << effectiveBitsShiftCount) | ((value >> paddingBitsShiftCount) & paddingBitsMask));
			};
	}


	/// <summary>
	/// Create conversion function to convert from [9, 16]-bits data to 8-bits data.
	/// </summary>
	/// <param name="byteOrdering">Byte ordering.</param>
	/// <param name="effectiveBits">Effective bits, range is [9, 16].</param>
	/// <returns>Conversion function.</returns>
	protected Func<byte, byte, byte> Create16BitsTo8BitsConversion(ByteOrdering byteOrdering, int effectiveBits)
	{
		if (effectiveBits < 9 || effectiveBits > 16)
			throw new ArgumentOutOfRangeException(nameof(effectiveBits));
		if (effectiveBits == 16)
		{
			return byteOrdering == ByteOrdering.LittleEndian
				? (_, b2) => b2
				: (b1, _) => b1;
		}
		var effectiveBitsShiftCount = (effectiveBits - 8);
		var effectiveBitsMask = (0xff << effectiveBitsShiftCount);
		return byteOrdering == ByteOrdering.LittleEndian
			? (b1, b2) => (byte)((((b2 << 8) | b1) & effectiveBitsMask) >> effectiveBitsShiftCount)
			: (b1, b2) => (byte)((((b1 << 8) | b2) & effectiveBitsMask) >> effectiveBitsShiftCount);
	}


	/// <summary>
	/// Create function to extract from [1, 8]-bit data to 8-bit data.
	/// </summary>
	/// <param name="effectiveBits">Effective bits, range is [1, 8].</param>
	/// <returns>Extraction function.</returns>
	protected Func<byte, byte> Create8BitColorExtraction(int effectiveBits) =>
		Create8BitColorExtraction(effectiveBits, 0, (uint)(1 << effectiveBits) - 1);


	/// <summary>
	/// Create function to extract from [1, 8]-bit data to 8-bit data.
	/// </summary>
	/// <param name="effectiveBits">Effective bits, range is [1, 8].</param>
	/// <param name="blackLevel">Black level.</param>
	/// <param name="whiteLevel">White level.</param>
	/// <returns>Extraction function.</returns>
	protected Func<byte, byte> Create8BitColorExtraction(int effectiveBits, uint blackLevel, uint whiteLevel)
	{
		// check parameters
		if (effectiveBits < 1 || effectiveBits > 8)
			throw new ArgumentOutOfRangeException(nameof(effectiveBits));
		if (blackLevel >= whiteLevel)
			throw new ArgumentOutOfRangeException(nameof(blackLevel));
		if (whiteLevel > (1 << effectiveBits) - 1)
			throw new ArgumentOutOfRangeException(nameof(whiteLevel));
		
		// conversion with full bits
		byte[] correctedColors;
		if (effectiveBits == 8)
		{
			if (blackLevel == 0 && whiteLevel == 255)
				return b => b;
			correctedColors = new byte[256].Also(it =>
			{
				var scale = 255.0 / (whiteLevel - blackLevel);
				for (var i = whiteLevel; i > blackLevel; --i)
					it[i] = (byte)((i - blackLevel) * scale + 0.5);
				for (var i = it.Length - 1; i > whiteLevel; --i)
					it[i] = 255;
			});
			return b => correctedColors[b];
		}
		
		// conversion with partial bits
		var effectiveBitsShiftCount = (8 - effectiveBits);
		var effectiveBitsMask = (0xff >> effectiveBitsShiftCount);
		var paddingBitsShiftCount = ((effectiveBits << 1) - 8);
		var paddingBitsMask = (0xff >> effectiveBits);
		if (blackLevel == 0 && whiteLevel == (1 << effectiveBits) - 1)
			return b => (byte)(((b & effectiveBitsMask) << effectiveBitsShiftCount) | ((b >> paddingBitsShiftCount) & paddingBitsMask));
		correctedColors = new byte[1 << effectiveBits].Also(it =>
		{
			var maxColor = (byte)(it.Length - 1);
			var scale = (double)maxColor / (whiteLevel - blackLevel);
			for (var i = whiteLevel; i > blackLevel; --i)
				it[i] = (byte)((i - blackLevel) * scale + 0.5);
			for (var i = it.Length - 1; i > whiteLevel; --i)
				it[i] = maxColor;
		});
		return b =>
		{
			b = (byte)(((b & effectiveBitsMask) << effectiveBitsShiftCount) | ((b >> paddingBitsShiftCount) & paddingBitsMask));
			return correctedColors[b];
		};
	}


	/// <summary>
	/// Logger.
	/// </summary>
	protected ILogger Logger { get; }


	/// <summary>
	/// Called to perform image rendering. The method will be called in background thread.
	/// </summary>
	/// <param name="source">Source of raw image data.</param>
	/// <param name="imageStream"><see cref="Stream"/> to read raw image data.</param>
	/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> to receive rendered image.</param>
	/// <param name="renderingOptions">Rendering options.</param>
	/// <param name="planeOptions">List of <see cref="ImagePlaneOptions"/> for rendering.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Result of rendering.</returns>
	protected abstract ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


	/// <inheritdoc/>
	public async Task<ImageRenderingResult> RenderAsync(IImageDataSource source, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
	{
		// share resources
		using var sharedSource = source.Share();
		using var sharedBitmapBuffer = bitmapBuffer.Share();

		// open stream
		var stream = await sharedSource.OpenStreamAsync(StreamAccess.Read);
		if (cancellationToken.IsCancellationRequested)
		{
			Global.RunWithoutErrorAsync(stream.Dispose);
			throw new TaskCanceledException();
		}

		// render
		try
        {
			return await RenderingTaskFactory.StartNew(() =>
			{
				if (renderingOptions.DataOffset > 0)
					stream.Seek(renderingOptions.DataOffset, SeekOrigin.Begin);
				var stopWatch = IAppSuiteApplication.CurrentOrNull?.IsDebugMode == true ? new Stopwatch() : null;
				stopWatch?.Start();
				var result = this.OnRender(sharedSource, stream, sharedBitmapBuffer, renderingOptions, planeOptions, cancellationToken);
				stopWatch?.Let(it => this.Logger.LogTrace("Rendering time: {duration} ms", it.ElapsedMilliseconds));
				return result;
			}, cancellationToken);
		}
		finally
        {
			Global.RunWithoutErrorAsync(stream.Dispose);
		}
	}


	/// <inheritdoc/>
	public virtual Task<BitmapFormat> SelectRenderedFormatAsync(IImageDataSource source, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken = default) =>
		Task.FromResult(BitmapFormat.Bgra32);


	// Implementations.
	public abstract IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height);
	public abstract int EvaluatePixelCount(IImageDataSource source);
	public abstract long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions);
	public ImageFormat Format => this.format;
	public bool IsBuiltIn => this.format.Category != ImageFormatCategory.UserDefined;
	public event PropertyChangedEventHandler? PropertyChanged;
}
