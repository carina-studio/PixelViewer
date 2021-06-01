using Carina.PixelViewer.Media;
using Carina.PixelViewer.Platform;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.Test.Media
{
	/// <summary>
	/// Base class for tests of <see cref="IBitmapBuffer"/>.
	/// </summary>
	/// <typeparam name="T">Type of <see cref="IBitmapBuffer"/>.</typeparam>
	abstract class BaseBitmapBufferTests<T> : BaseShareableDisposableTests<T> where T : class, IBitmapBuffer, IShareableDisposable<T>
	{
		// Create instance with random size.
		protected override T CreateInstance()
		{
			var format = this.Formats[this.Random.Next(0, this.Formats.Count)];
			return this.CreateInstance(format, this.GenerateRandomDimension(), this.GenerateRandomDimension());
		}


		/// <summary>
		/// Create <see cref="IBitmapBuffer"/> instance.
		/// </summary>
		/// <param name="format">Format of bitmap.</param>
		/// <param name="width">Width of bitmap.</param>
		/// <param name="height">Height of bitmap.</param>
		/// <returns><see cref="IBitmapBuffer"/> instance.</returns>
		protected abstract T CreateInstance(BitmapFormat format, int width, int height);


		/// <summary>
		/// Default <see cref="BitmapFormat"/>s for tests.
		/// </summary>
		protected static readonly IList<BitmapFormat> DefaultFormats = new List<BitmapFormat>((BitmapFormat[])Enum.GetValues(typeof(BitmapFormat))).AsReadOnly();


		/// <summary>
		/// All supported <see cref="BitmapFormat"/>s for tests.
		/// </summary>
		protected virtual IList<BitmapFormat> Formats { get; } = DefaultFormats;


		/// <summary>
		/// Generate random dimension.
		/// </summary>
		/// <param name="evenOnly">True to generate even number dimonsion only.</param>
		/// <returns>Generated dimension.</returns>
		protected int GenerateRandomDimension(bool evenOnly = true) => this.GenerateRandomDimension(64, 256, evenOnly);


		/// <summary>
		/// Generate random dimension.
		/// </summary>
		/// <param name="min">Inclusive lower bound.</param>
		/// <param name="max">Inclusive upper bound.</param>
		/// <param name="evenOnly">True to generate even number dimonsion only.</param>
		/// <returns>Generated dimension.</returns>
		protected int GenerateRandomDimension(int min, int max, bool evenOnly = true)
		{
			var dimension = this.Random.Next(min, max + 1);
			if (evenOnly && (dimension & 1) == 1)
			{
				if (dimension < max)
					--dimension;
				else if (dimension > min)
					++dimension;
				else
					throw new ArgumentOutOfRangeException();
			}
			return dimension;
		}


		/// <summary>
		/// Test for instance creaation.
		/// </summary>
		[Test]
		public void TestInstanceCreation()
		{
			foreach (var format in this.Formats)
			{
				for (var i = 10; i > 0; --i)
				{
					using var buffer = this.CreateInstance(format, this.GenerateRandomDimension(), this.GenerateRandomDimension());
					this.ValidateInstance(buffer, true);
				}
			}
		}


		// Validate instance.
		protected override unsafe void ValidateInstance(T instance) => this.ValidateInstance(instance, false);


		/// <summary>
		/// Called to validate whether given instance is valid or not.
		/// </summary>
		/// <param name="instance">Instance to be checked.</param>
		/// <param name="fullAccess">True to try accessing all bytes in buffer.</param>
		protected unsafe void ValidateInstance(T instance, bool fullAccess)
		{
			// check row stride
			var bpp = instance.Format.GetByteSize();
			var width = instance.Width;
			var height = instance.Height;
			var minRowStride = bpp * width;
			Assert.GreaterOrEqual(instance.RowBytes, minRowStride, $"Insufficient row-stride: {instance.RowBytes}, size: {width}x{height}, bpp: {bpp}.");

			// access data
			instance.Memory.Pin((address) =>
			{
				if (fullAccess)
				{
					var b = (byte)0;
					var rowStride = instance.RowBytes;
					var rowPtr = (byte*)address;
					for (var y = height; y > 0; --y, rowPtr += rowStride)
					{
						var pixelPtr = rowPtr;
						for (var i = minRowStride; i > 0; --i, ++pixelPtr)
						{
							b = *pixelPtr;
							*pixelPtr = (byte)this.Random.Next(0, 256);
							*pixelPtr = b;
						}
					}
				}
				else
				{
					// access first pixel
					var pixel = new byte[bpp];
					var pixelPtr = (byte*)address;
					for (var i = bpp - 1; i >= 0; --i)
						pixel[i] = pixelPtr[i];

					// access last pixel
					pixelPtr += ((height - 1) * instance.RowBytes) + ((width - 1) * bpp);
					for (var i = bpp - 1; i >= 0; --i)
						pixel[i] = pixelPtr[i];
				}
			});
		}
	}
}
