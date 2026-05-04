using Avalonia.Media;
using System;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Color with 16-bit per channel (ARGB), packed into 64 bits.
/// </summary>
public readonly struct Color64 : IEquatable<Color64>
{
	/// <summary>
	/// Initialize a new <see cref="Color64"/> from 16-bit ARGB channels.
	/// </summary>
	/// <param name="a">Alpha channel (0-65535).</param>
	/// <param name="r">Red channel (0-65535).</param>
	/// <param name="g">Green channel (0-65535).</param>
	/// <param name="b">Blue channel (0-65535).</param>
	public Color64(ushort a, ushort r, ushort g, ushort b)
	{
		this.A = a;
		this.R = r;
		this.G = g;
		this.B = b;
	}


	/// <summary>
	/// Initialize a new <see cref="Color64"/> from 8-bit ARGB channels. Each 8-bit value is replicated into the high and low byte of the corresponding 16-bit channel so that 0x00 maps to 0x0000 and 0xFF maps to 0xFFFF.
	/// </summary>
	/// <param name="a">Alpha channel (0-255).</param>
	/// <param name="r">Red channel (0-255).</param>
	/// <param name="g">Green channel (0-255).</param>
	/// <param name="b">Blue channel (0-255).</param>
	public Color64(byte a, byte r, byte g, byte b)
	{
		this.A = (ushort)((a << 8) | a);
		this.R = (ushort)((r << 8) | r);
		this.G = (ushort)((g << 8) | g);
		this.B = (ushort)((b << 8) | b);
	}


	/// <summary>
	/// Initialize a new <see cref="Color64"/> from an 8-bit Avalonia <see cref="Color"/>. Each 8-bit channel is replicated into the high and low byte of the corresponding 16-bit channel.
	/// </summary>
	/// <param name="color">Source 8-bit color.</param>
	public Color64(Color color) : this(color.A, color.R, color.G, color.B)
	{ }


	/// <summary>
	/// Get alpha channel (0-65535).
	/// </summary>
	public ushort A { get; }


	/// <summary>
	/// Get blue channel (0-65535).
	/// </summary>
	public ushort B { get; }


	/// <summary>
	/// Get equivalent 8-bit Avalonia <see cref="Color"/>. Each 16-bit channel is truncated by taking its high byte (bits 8-15).
	/// </summary>
	public Color Color =>
		new((byte)(this.A >> 8), (byte)(this.R >> 8), (byte)(this.G >> 8), (byte)(this.B >> 8));


	/// <inheritdoc/>
	public bool Equals(Color64 other) =>
		this.A == other.A && this.R == other.R && this.G == other.G && this.B == other.B;


	/// <inheritdoc/>
	public override bool Equals(object? obj) =>
		obj is Color64 other && this.Equals(other);


	/// <summary>
	/// Get green channel (0-65535).
	/// </summary>
	public ushort G { get; }


	/// <inheritdoc/>
	public override int GetHashCode() =>
		HashCode.Combine(this.A, this.R, this.G, this.B);


	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator ==(Color64 left, Color64 right) =>
		left.Equals(right);


	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator !=(Color64 left, Color64 right) =>
		!left.Equals(right);


	/// <summary>
	/// Get red channel (0-65535).
	/// </summary>
	public ushort R { get; }


	/// <inheritdoc/>
	public override string ToString() =>
		$"#{this.A:X4}{this.R:X4}{this.G:X4}{this.B:X4}";
}
