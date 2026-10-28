using Carina.PixelViewer.Media;
using CarinaStudio;
using CarinaStudio.IO;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

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
		public void TestInstanceCreation() => this.TestOnApplicationThread(async () =>
		{
			for (var i = 10; i > 0; --i)
			{
				// create instance
				var data = this.GenerateRandomData(1 << 10, 1 << 20);
				using var source = this.CreateInstance(data);

				// check size
				var size = data.Length;
				Assert.That(source.Size, Is.EqualTo(size), "Size of created source is not same as expected.");

				// check data
				using var stream = await source.OpenStreamAsync(StreamAccess.Read);
				var readData = new byte[size];
				stream.ReadExactly(readData, 0, size);
				for (var j = size - 1; j >= 0; --j)
					Assert.That(readData[j], Is.EqualTo(data[j]), $"Data[{j}] read from source is different from expected.");
			}
		});


		/// <summary>
		/// Test for calling callback once after the last instance which shares the resource is disposed.
		/// </summary>
		[Test]
		public void TestSourceReleasedCallback() => this.TestOnApplicationThread(() =>
		{
			// register callback
			var source = this.CreateInstance();
			var sharedSource1 = ((IShareableDisposable<T>)source).Share();
			var sharedSource2 = ((IShareableDisposable<T>)source).Share();
			var callCount = 0;
			using var registration = sharedSource1.RegisterSourceReleasedCallback(() => { ++callCount; });

			// dispose instances other than the last one
			source.Dispose();
			sharedSource1.Dispose();
			Assert.That(callCount, Is.Zero, "Callback should not be called before the last instance is disposed.");

			// dispose the last instance
			sharedSource2.Dispose();
			Assert.That(callCount, Is.EqualTo(1), "Callback should be called after the last instance is disposed.");

			// dispose the last instance again
			sharedSource2.Dispose();
			Assert.That(callCount, Is.EqualTo(1), "Callback should be called only once.");
		});


		/// <summary>
		/// Test for registering callback through a disposed instance.
		/// </summary>
		[Test]
		public void TestSourceReleasedCallbackOnDisposedSource() => this.TestOnApplicationThread(() =>
		{
			var source = this.CreateInstance();
			using var sharedSource = ((IShareableDisposable<T>)source).Share();
			source.Dispose();
			Assert.Throws<ObjectDisposedException>(() => source.RegisterSourceReleasedCallback(() => { }), "Registering through a disposed instance should be rejected.");
		});


		/// <summary>
		/// Test for unregistering callback.
		/// </summary>
		[Test]
		public void TestSourceReleasedCallbackUnregistration() => this.TestOnApplicationThread(() =>
		{
			// register the same callback twice
			var source = this.CreateInstance();
			var callCount = 0;
			Action callback = () => { ++callCount; };
			var registration1 = source.RegisterSourceReleasedCallback(callback);
			var registration2 = source.RegisterSourceReleasedCallback(callback);

			// unregister one of them more than once
			registration1.Dispose();
			registration1.Dispose();

			// release resource
			source.Dispose();
			Assert.That(callCount, Is.EqualTo(1), "Only the callback which is still registered should be called.");

			// unregister after releasing resource
			registration2.Dispose();
			Assert.That(callCount, Is.EqualTo(1), "Unregistering after releasing resource should take no effect.");
		});


		/// <summary>
		/// Test for checking whether resource is shared between instances or not.
		/// </summary>
		[Test]
		public void TestSourceSharing() => this.TestOnApplicationThread(() =>
		{
			using var source = this.CreateInstance();
			using var sharedSource = ((IShareableDisposable<T>)source).Share();
			using var otherSource = this.CreateInstance();
			Assert.That(source.IsSourceSharedWith(source), "Instance should share resource with itself.");
			Assert.That(source.IsSourceSharedWith(sharedSource), "Instance should share resource with its shared instance.");
			Assert.That(sharedSource.IsSourceSharedWith(source), "Shared instance should share resource with its base instance.");
			Assert.That(source.IsSourceSharedWith(otherSource), Is.False, "Separate instances should not share resource.");
		});


		/// <summary>
		/// Test for calling other callbacks when one of callbacks throws exception.
		/// </summary>
		[Test]
		public void TestThrowingSourceReleasedCallback() => this.TestOnApplicationThread(() =>
		{
			// register callbacks
			var source = this.CreateInstance();
			var isCallbackCalled = false;
			using var registration1 = source.RegisterSourceReleasedCallback(() => throw new InvalidOperationException());
			using var registration2 = source.RegisterSourceReleasedCallback(() => { isCallbackCalled = true; });

			// release resource
			Assert.DoesNotThrow(source.Dispose, "Exception thrown by callback should not break disposing.");
			Assert.That(isCallbackCalled, "Callback should be called even if other callback throws exception.");
		});


		// Validate instance.
		protected override async Task ValidateInstanceAsync(T instance)
		{
			// check size
			var size = instance.Size;
			Assert.That(size, Is.GreaterThanOrEqualTo(0), "Size of data should not be negative.");

			// access data
			using var stream = await instance.OpenStreamAsync(StreamAccess.Read);
			var buffer = new byte[1024];
			if (stream.CanSeek && size > buffer.Length)
			{
				// read head of data
				var readCount = stream.Read(buffer, 0, buffer.Length);
				Assert.That(readCount, Is.EqualTo(buffer.Length), "Cannot access head of data.");

				// read tail of data
				var position = (size - buffer.Length);
				Assert.That(stream.Seek(position, SeekOrigin.Begin), Is.EqualTo(position), "Cannot seek in opened stream.");
				readCount = stream.Read(buffer, 0, buffer.Length);
				Assert.That(readCount, Is.EqualTo(buffer.Length), "Cannot access tail of data.");

				// read middle of data
				position = (size / 2) - (buffer.Length / 2) - 1;
				Assert.That(stream.Seek(position, SeekOrigin.Begin), Is.EqualTo(position), "Cannot seek in opened stream.");
				readCount = stream.Read(buffer, 0, buffer.Length);
				Assert.That(readCount, Is.EqualTo(buffer.Length), "Cannot access middle of data.");
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
				Assert.That(readDataSize, Is.GreaterThanOrEqualTo(size), "Size of accessible data should not be less than reported size.");
			}
		}
	}
}
