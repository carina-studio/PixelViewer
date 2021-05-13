using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.ViewModels;
using NUnit.Framework;
using System.IO;

namespace Carina.PixelViewer.Test.ViewModels
{
	/// <summary>
	/// Tests of <see cref="Session"/>.
	/// </summary>
	[TestFixture]
	class SessionTests : BaseViewModelTests<Session>
	{
		// Fields.
		Session? session = null;


		/// <summary>
		/// Create <see cref="Session"/> instance for testing.
		/// </summary>
		[OneTimeSetUp]
		public void CreateSession()
		{
			this.TestSynchronizationContext.Send((_) =>
			{
				this.session = new Session();
			}, null);
		}


		/// <summary>
		/// Dispose created <see cref="Session"/> instance for testing.
		/// </summary>
		[OneTimeTearDown]
		public void DisposeSession()
		{
			this.TestSynchronizationContext.Send((_) =>
			{
				this.session?.Dispose();
			}, null);
		}


		// Generate source image file with random data.
		string GenerateSourceFile()
		{
			var data = new byte[this.Random.Next(1 << 10, 1 << 20 + 1)];
			for (var i = data.Length - 1; i >= 0; --i)
				data[i] = (byte)this.Random.Next(0, 256);
			using var stream = this.CreateCacheFile();
			stream.Write(data);
			return stream.Name;
		}


		/// <summary>
		/// Test for changing image renderer.
		/// </summary>
		/// <returns></returns>
		[Test]
		public void TestChangingImageRenderer()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestByTestSynchronizationContext(async () =>
			{
				// open file
				var filePath = this.GenerateSourceFile();
				session.OpenSourceFileCommand.Execute(filePath);
				Assert.IsTrue(await this.WaitForProperty(session, nameof(Session.IsSourceFileOpened), true, 1000), "Cannot open source file.");

				// wait for first rendering
				Assert.IsTrue(await this.WaitForProperty(session, nameof(Session.IsRenderingImage), false, 10000), "Unable to complete first rendering.");
				Assert.IsNotNull(session.RenderedImage, "No rendered image for first rendering.");

				// change renderers
				foreach (var imageRenderer in ImageRenderers.All)
				{
					session.ImageRenderer = imageRenderer;
					var planeDescriptors = imageRenderer.Format.PlaneDescriptors;
					Assert.IsTrue(await this.WaitForProperty(session, nameof(Session.IsRenderingImage), false, 10000), $"Unable to complete rendering by {imageRenderer}.");
					Assert.IsNotNull(session.RenderedImage, $"No rendered image for rendering by {imageRenderer}.");
					Assert.AreEqual(planeDescriptors.Count, session.ImagePlaneCount, "Reported image plane count is incorrect.");
					Assert.AreEqual(planeDescriptors.Count >= 1, session.HasImagePlane1, $"{nameof(Session.HasImagePlane1)} is incorrect.");
					Assert.AreEqual(planeDescriptors.Count >= 2, session.HasImagePlane2, $"{nameof(Session.HasImagePlane2)} is incorrect.");
					Assert.AreEqual(planeDescriptors.Count >= 3, session.HasImagePlane3, $"{nameof(Session.HasImagePlane3)} is incorrect.");
				}

				// close file
				session.CloseSourceFileCommand.Execute(null);
				Assert.IsTrue(await this.WaitForProperty(session, nameof(Session.IsSourceFileOpened), false, 1000), "Cannot close source file.");
				Assert.IsNull(session.RenderedImage, "Rendered image is still there after closing source file.");
				File.Delete(filePath);
			}, 30000);
		}


		/// <summary>
		/// Test for opening and closing source image file.
		/// </summary>
		[Test]
		public void TestOpeningClosingSourceFile()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			var openCommand = session.OpenSourceFileCommand;
			var closeCommand = session.CloseSourceFileCommand;
			this.TestByTestSynchronizationContext(async () =>
			{
				// open file 1
				var filePath1 = this.GenerateSourceFile();
				Assert.IsTrue(openCommand.CanExecute(filePath1), "Source file opening should be able to be executed.");
				Assert.IsFalse(session.IsSourceFileOpened, $"{nameof(Session.IsSourceFileOpened)} should false.");
				openCommand.Execute(filePath1);
				Assert.IsFalse(openCommand.CanExecute(filePath1), "Source file opening should not be able to be executed.");
				Assert.IsFalse(session.IsSourceFileOpened, $"{nameof(Session.IsSourceFileOpened)} should false.");

				// wait for opening
				var waitingResult = await this.WaitForProperty(session, nameof(Session.IsSourceFileOpened), true, 1000);
				Assert.IsTrue(waitingResult, $"{nameof(Session.IsSourceFileOpened)} should be true.");
				Assert.AreEqual(filePath1, session.SourceFileName, "Source file name is different from set one.");
				Assert.IsTrue(openCommand.CanExecute(null), "Source file opening should be able to be executed.");
				Assert.IsTrue(closeCommand.CanExecute(null), "Source file closing should be able to be executed.");

				// open file 2
				var filePath2 = this.GenerateSourceFile();
				Assert.IsTrue(openCommand.CanExecute(filePath2), "Source file opening should be able to be executed.");
				openCommand.Execute(filePath2);
				Assert.IsFalse(openCommand.CanExecute(filePath2), "Source file opening should not be able to be executed.");
				Assert.IsFalse(session.IsSourceFileOpened, $"{nameof(Session.IsSourceFileOpened)} should false.");

				// wait for opening
				waitingResult = await this.WaitForProperty(session, nameof(Session.IsSourceFileOpened), true, 1000);
				Assert.IsTrue(waitingResult, $"{nameof(Session.IsSourceFileOpened)} should be true.");
				Assert.AreEqual(filePath2, session.SourceFileName, "Source file name is different from set one.");
				Assert.IsTrue(openCommand.CanExecute(null), "Source file opening should be able to be executed.");
				Assert.IsTrue(closeCommand.CanExecute(null), "Source file closing should be able to be executed.");

				// delete file 1 to make sure that file has been unlocked
				File.Delete(filePath1);

				// close file 2
				closeCommand.Execute(null);

				// wait for closing
				waitingResult = await this.WaitForCommandState(closeCommand, false, null, 1000);
				Assert.IsTrue(waitingResult, "Source file closing should be able to be executed.");
				waitingResult = await this.WaitForProperty(session, nameof(Session.IsSourceFileOpened), false, 1000);
				Assert.IsTrue(waitingResult, $"{nameof(Session.IsSourceFileOpened)} should be false.");
				Assert.IsTrue(openCommand.CanExecute(null), "Source file opening should be able to be executed.");

				// delete file 2 to make sure that file has been unlocked
				File.Delete(filePath2);
			}, 10000);
		}
	}
}
