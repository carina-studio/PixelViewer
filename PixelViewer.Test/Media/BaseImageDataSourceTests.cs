using Carina.PixelViewer.Media;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.IO;

namespace Carina.PixelViewer.Test.Media
{
	/// <summary>
	/// Base class for tests of <see cref="IImageDataSource"/>.
	/// </summary>
	/// <typeparam name="T">Type of <see cref="IImageDataSource"/>.</typeparam>
	abstract class BaseImageDataSourceTests<T> : BaseShareableDisposableTests<T> where T : class, IImageDataSource, IShareableDisposable<T>
	{
		// Create instance with random data.
		protected override T CreateInstance() => this.CreateInstance(this.GenerateRandomData(1 << 10, 1 << 20));


		/// <summary>
		/// Create <see cref="IImageDataSource"/> instance with given data.
		/// </summary>
		/// <param name="data">Given data should be provided by <see cref="IImageDataSource"/>.</param>
		/// <returns><see cref="IImageDataSource"/> instance.</returns>
		protected abstract T CreateInstance(byte[] data);


		/// <summary>
		/// Generate random data.
		/// </summary>
		/// <param name="minSize">Inclusive lower bound of data size.</param>
		/// <param name="maxSize">Inclusive upper bound of data size.</param>
		/// <returns>Random data.</returns>
		protected byte[] GenerateRandomData(int minSize, int maxSize)
		{
			var size = this.Random.Next(minSize, maxSize + 1);
			return new byte[size].Also((it) => this.GenerateRandomData(it));
		}


		/// <summary>
		/// Generate random data and fill into given array.
		/// </summary>
		/// <param name="data">Byte array to receive random data.</param>
		protected void GenerateRandomData(byte[] data)
		{
			for (var i = data.Length - 1; i >= 0; --i)
				data[i] = (byte)this.Random.Next(0, 256);
		}


		/// <summary>
		/// Test for instance creation.
		/// </summary>
		[Test]
		public void TestInstanceCreation()
		{
			for (var i = 10; i > 0; --i)
			{
				// create instance
				var data = this.GenerateRandomData(1 << 10, 1 << 20);
				using var source = this.CreateInstance(data);

				// check size
				var size = data.Length;
				Assert.AreEqual(size, source.Size, "Size of created source is not same as expected.");

				// check data
				using var stream = source.Open();
				var readData = new byte[size];
				var readDataSize = 0;
				var readCount = stream.Read(readData, 0, size);
				while (readCount > 0 && readDataSize < size)
				{
					readDataSize += readCount;
					stream.Read(readData, readDataSize, size - readDataSize);
				}
				Assert.AreEqual(size, readDataSize, "Size of accessible data should not be less than expected.");
				for (var j = size - 1; j >= 0; --j)
					Assert.AreEqual(data[j], readData[j], $"Data[{j}] read from source is different from expected.");
			}
		}


		// Validate instance.
		protected override void ValidateInstance(T instance)
		{
			// check size
			var size = instance.Size;
			Assert.GreaterOrEqual(size, 0, "Size of data should not be negative.");

			// access data
			using var stream = instance.Open();
			var buffer = new byte[1024];
			if (stream.CanSeek && size > buffer.Length)
			{
				// read head of data
				var readCount = stream.Read(buffer, 0, buffer.Length);
				Assert.AreEqual(readCount, buffer.Length, "Cannot access head of data.");

				// read tail of data
				var position = (size - buffer.Length);
				Assert.AreEqual(position, stream.Seek(position, SeekOrigin.Begin), "Cannot seek in opened stream.");
				readCount = stream.Read(buffer, 0, buffer.Length);
				Assert.AreEqual(readCount, buffer.Length, "Cannot access tail of data.");

				// read middle of data
				position = (size / 2) - (buffer.Length / 2) - 1;
				Assert.AreEqual(position, stream.Seek(position, SeekOrigin.Begin), "Cannot seek in opened stream.");
				readCount = stream.Read(buffer, 0, buffer.Length);
				Assert.AreEqual(readCount, buffer.Length, "Cannot access middle of data.");
			}
			else
			{
				var readDataSize = 0;
				var readCount = stream.Read(buffer, 0, buffer.Length);
				while (readCount > 0 && readDataSize < size)
				{
					readDataSize += readCount;
					readCount = stream.Read(buffer, 0, buffer.Length);
				}
				Assert.GreaterOrEqual(readDataSize, size, "Size of accessible data should not be less than reported size.");
			}
		}
	}
}
