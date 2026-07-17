using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.ViewModels;
using CarinaStudio.Tests;
using NUnit.Framework;
using System.IO;

namespace Carina.PixelViewer.Test.ViewModels
{
	/// <summary>
	/// Tests of <see cref="Session"/>.
	/// </summary>
	[TestFixture]
	[Ignore("Requires a real Application.Current, which MockAppSuiteApplication does not provide. Re-enable once AppSuite exposes IAppSuiteApplication.FallbackCurrent for the mock (planned in AppSuiteBase).")]
	class SessionTests : BaseTests
	{
		// Fields.
		Session? session;


		/// <summary>
		/// Create <see cref="Session"/> instance for testing.
		/// </summary>
		[OneTimeSetUp]
		public void CreateSession()
		{
			this.TestOnApplicationThread(async () =>
			{
				// initialize the sub-systems required to construct and run a session
				FileFormats.Initialize(this.Application);
				Carina.PixelViewer.Media.FileFormatParsers.FileFormatParsers.Initialize(this.Application);
				await ColorSpace.InitializeAsync(this.Application);
				await ImageRenderingProfiles.InitializeAsync(this.Application);

				// create session for testing
				this.session = new Session(this.Application, null);
			});
		}


		/// <summary>
		/// Dispose created <see cref="Session"/> instance for testing.
		/// </summary>
		[OneTimeTearDown]
		public void DisposeSession()
		{
			this.TestOnApplicationThread(() =>
			{
				this.session?.Dispose();
			});
		}


		// Generate source image file with random data.
		string GenerateSourceFile()
		{
			var data = new byte[Random.Next(1 << 10, 1 << 20 + 1)];
			for (var i = data.Length - 1; i >= 0; --i)
				data[i] = (byte)Random.Next(0, 256);
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
			this.TestOnApplicationThread(async () =>
			{
				// open file
				var filePath = this.GenerateSourceFile();
				session.OpenSourceFileCommand.Execute(filePath);
				Assert.That(await this.WaitForPropertyAsync(session,nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open source file.");

				// wait for first rendering
				Assert.That(await this.WaitForPropertyAsync(session,nameof(Session.IsRenderingImage), false, 10000), Is.True, "Unable to complete first rendering.");
				Assert.That(session.RenderedImage, Is.Not.Null, "No rendered image for first rendering.");

				// change renderers
				foreach (var imageRenderer in ImageRenderers.All)
				{
					session.ImageRenderer = imageRenderer;
					var planeDescriptors = imageRenderer.Format.PlaneDescriptors;
					Assert.That(await this.WaitForPropertyAsync(session,nameof(Session.IsRenderingImage), false, 10000), Is.True, $"Unable to complete rendering by {imageRenderer}.");
					Assert.That(session.RenderedImage, Is.Not.Null, $"No rendered image for rendering by {imageRenderer}.");
					Assert.That(session.ImagePlaneCount, Is.EqualTo(planeDescriptors.Count), "Reported image plane count is incorrect.");
					Assert.That(session.HasImagePlane1, Is.EqualTo(planeDescriptors.Count >= 1), $"{nameof(Session.HasImagePlane1)} is incorrect.");
					Assert.That(session.HasImagePlane2, Is.EqualTo(planeDescriptors.Count >= 2), $"{nameof(Session.HasImagePlane2)} is incorrect.");
					Assert.That(session.HasImagePlane3, Is.EqualTo(planeDescriptors.Count >= 3), $"{nameof(Session.HasImagePlane3)} is incorrect.");
				}

				// close file
				session.ClearSourceCommand.Execute(null);
				Assert.That(await this.WaitForPropertyAsync(session,nameof(Session.IsSourceOpened), false, 1000), Is.True, "Cannot close source file.");
				Assert.That(session.RenderedImage, Is.Null, "Rendered image is still there after closing source file.");
				File.Delete(filePath);
			});
		}


		/// <summary>
		/// Test for opening and closing source image file.
		/// </summary>
		[Test]
		public void TestOnApplicationThread()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			var openCommand = session.OpenSourceFileCommand;
			var closeCommand = session.ClearSourceCommand;
			this.TestOnApplicationThread(async () =>
			{
				// open file 1
				var filePath1 = this.GenerateSourceFile();
				Assert.That(openCommand.CanExecute(filePath1), Is.True, "Source file opening should be able to be executed.");
				Assert.That(session.IsSourceOpened, Is.False, $"{nameof(Session.IsSourceOpened)} should false.");
				openCommand.Execute(filePath1);
				Assert.That(openCommand.CanExecute(filePath1), Is.False, "Source file opening should not be able to be executed.");
				Assert.That(session.IsSourceOpened, Is.False, $"{nameof(Session.IsSourceOpened)} should false.");

				// wait for opening
				var waitingResult = await this.WaitForPropertyAsync(session,nameof(Session.IsSourceOpened), true, 1000);
				Assert.That(waitingResult, Is.True, $"{nameof(Session.IsSourceOpened)} should be true.");
				Assert.That(session.SourceFileName, Is.EqualTo(filePath1), "Source file name is different from set one.");
				Assert.That(openCommand.CanExecute(null), Is.True, "Source file opening should be able to be executed.");
				Assert.That(closeCommand.CanExecute(null), Is.True, "Source file closing should be able to be executed.");

				// open file 2
				var filePath2 = this.GenerateSourceFile();
				Assert.That(openCommand.CanExecute(filePath2), Is.True, "Source file opening should be able to be executed.");
				openCommand.Execute(filePath2);
				Assert.That(openCommand.CanExecute(filePath2), Is.False, "Source file opening should not be able to be executed.");
				Assert.That(session.IsSourceOpened, Is.False, $"{nameof(Session.IsSourceOpened)} should false.");

				// wait for opening
				waitingResult = await this.WaitForPropertyAsync(session,nameof(Session.IsSourceOpened), true, 1000);
				Assert.That(waitingResult, Is.True, $"{nameof(Session.IsSourceOpened)} should be true.");
				Assert.That(session.SourceFileName, Is.EqualTo(filePath2), "Source file name is different from set one.");
				Assert.That(openCommand.CanExecute(null), Is.True, "Source file opening should be able to be executed.");
				Assert.That(closeCommand.CanExecute(null), Is.True, "Source file closing should be able to be executed.");

				// delete file 1 to make sure that file has been unlocked
				File.Delete(filePath1);

				// close file 2
				closeCommand.Execute(null);

				// wait for closing
				waitingResult = await this.WaitForCommandState(closeCommand, false, null, 1000);
				Assert.That(waitingResult, Is.True, "Source file closing should be able to be executed.");
				waitingResult = await this.WaitForPropertyAsync(session,nameof(Session.IsSourceOpened), false, 1000);
				Assert.That(waitingResult, Is.True, $"{nameof(Session.IsSourceOpened)} should be false.");
				Assert.That(openCommand.CanExecute(null), Is.True, "Source file opening should be able to be executed.");

				// delete file 2 to make sure that file has been unlocked
				File.Delete(filePath2);
			});
		}
	}
}
