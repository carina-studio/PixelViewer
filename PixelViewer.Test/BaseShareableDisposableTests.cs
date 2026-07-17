using CarinaStudio;
using NUnit.Framework;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test
{
	/// <summary>
	/// Base class for testing of <see cref="IShareableDisposable{T}"/>
	/// </summary>
	abstract class BaseShareableDisposableTests<T> : BaseTests where T: class, IShareableDisposable<T>
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
		/// <returns>Task of validation.</returns>
		protected abstract Task ValidateInstanceAsync(T instance);


		/// <summary>
		/// Test for instance sharing.
		/// </summary>
		[Test]
		public virtual void TestInstanceSharing() => this.TestOnApplicationThread(async () =>
		{
			// create base instance
			using var baseInstance = this.CreateInstance();
			await this.ValidateInstanceAsync(baseInstance);

			// share instance
			using (var sharedInstance = baseInstance.Share())
			{
				Assert.That(sharedInstance, Is.Not.SameAs(baseInstance), "Shared instance should not be same as base one.");
				await this.ValidateInstanceAsync(sharedInstance);
			}
			await this.ValidateInstanceAsync(baseInstance);

			// share instance and dispose base one
			using (var sharedInstance = baseInstance.Share())
			{
				// check shared instance
				Assert.That(sharedInstance, Is.Not.SameAs(baseInstance), "Shared instance should not be same as base one.");
				await this.ValidateInstanceAsync(sharedInstance);

				// dispose base one
				baseInstance.Dispose();
				await this.ValidateInstanceAsync(sharedInstance);
			}
		});
	}
}
