using NUnit.Framework;

namespace Carina.PixelViewer.Test
{
	/// <summary>
	/// Base class for testing of <see cref="ISharableDisposable{T}"/>
	/// </summary>
	abstract class BaseSharableDisposableTests<T> : BaseTests where T: class, ISharableDisposable<T>
	{
		/// <summary>
		/// Create instance for testing.
		/// </summary>
		/// <returns>Created instance.</returns>
		protected abstract T CreateInstance();


		/// <summary>
		/// Called to validate whether given instance is valid or not.
		/// </summary>
		/// <param name="instance">Instance to be checked.</param>
		protected abstract void ValidateInstance(T instance);


		/// <summary>
		/// Test for instance sharing.
		/// </summary>
		[Test]
		public virtual void TestInstanceSharing()
		{
			// create base instance
			using var baseInstance = this.CreateInstance();
			this.ValidateInstance(baseInstance);

			// share instance
			using (var sharedInstance = baseInstance.Share())
			{
				Assert.AreNotSame(baseInstance, sharedInstance, "Shared instance should not be same as base one.");
				this.ValidateInstance(sharedInstance);
			}
			this.ValidateInstance(baseInstance);

			// share instance and dispose base one
			using (var sharedInstance = baseInstance.Share())
			{
				// check shared instance
				Assert.AreNotSame(baseInstance, sharedInstance, "Shared instance should not be same as base one.");
				this.ValidateInstance(sharedInstance);

				// dispose base one
				baseInstance.Dispose();
				this.ValidateInstance(sharedInstance);
			}
		}
	}
}
