using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageEncoders;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.ViewModels
{
	/// <summary>
	/// Tests of <see cref="Session"/>.
	/// </summary>
	[TestFixture]
	class SessionTests : BaseTests
	{
		// Fields.
		Session? session;
		IDisposable? sessionActivationToken;


		/// <summary>
		/// Close the source opened by the completed test. The <see cref="Session"/> instance is shared by all tests in the
		/// fixture, so a test which fails before closing its source must not leave the source opened for the next test.
		/// </summary>
		[TearDown]
		public void CloseSource()
		{
			this.TestOnApplicationThread(async () =>
			{
				var session = this.session;
				if (session is null || !session.IsSourceOpened)
					return;
				session.ClearSourceCommand.Execute(null);
				await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000);
			});
		}


		// Create a color table which maps each 8-bit value of a color channel to a 14-bit color.
		static ColorTable CreateColorTable()
		{
			var colorTable = new ColorTable(256, 14);
			var colors = colorTable.Memory.Span;
			for (var i = colors.Length - 1; i >= 0; --i)
				colors[i] = (uint)(i * 64);
			return colorTable;
		}


		/// <summary>
		/// Create <see cref="Session"/> instance for testing.
		/// </summary>
		[OneTimeSetUp]
		public void CreateSession()
		{
			this.TestOnApplicationThread(async () =>
			{
				// initialize the sub-systems required to construct and run a session
				await this.InitializeSubSystemsAsync();

				// create session for testing and activate it so image rendering is performed
				this.session = new Session(this.Application, null);
				this.sessionActivationToken = this.session.Activate();
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
				this.sessionActivationToken?.Dispose();
				this.session?.Dispose();
			});
		}


		// Generate image file with the given name, file format and dimensions in cache directory.
		async Task<string> GenerateImageFileAsync(string fileName, FileFormat fileFormat, int width, int height)
		{
			// get directory to place the file, name of file needs to be controlled to keep the order of frames stable
			string directoryPath;
			using (var stream = this.CreateCacheFile())
				directoryPath = Path.GetDirectoryName(stream.Name).AsNonNull();

			// encode image with the given dimensions
			ImageEncoders.TryGetEncoderByFormat(fileFormat, out var encoder);
			using var bitmapBuffer = new BitmapBuffer(BitmapFormat.Bgra32, ColorSpace.Default, width, height);
			var filePath = Path.Combine(directoryPath, fileName);
			await encoder.AsNonNull().EncodeAsync(bitmapBuffer, new CarinaStudio.IO.FileStreamProvider(filePath), new ImageEncodingOptions { QualityLevel = 90 }, CancellationToken.None);
			return filePath;
		}


		// Generate file with the given name filled with random data in cache directory, its file format is unidentifiable.
		string GenerateRawDataFile(string fileName)
		{
			// get directory to place the file, name of file needs to be controlled to keep the order of frames stable
			string directoryPath;
			using (var stream = this.CreateCacheFile())
				directoryPath = Path.GetDirectoryName(stream.Name).AsNonNull();

			// fill file with random data
			var data = new byte[Random.Next(1 << 10, 1 << 16)];
			for (var i = data.Length - 1; i >= 0; --i)
				data[i] = (byte)Random.Next(0, 256);
			var filePath = Path.Combine(directoryPath, fileName);
			File.WriteAllBytes(filePath, data);
			return filePath;
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


		// Wait for the rendering triggered by opening a file or changing the renderer to complete.
		// Rendering is scheduled with a delay, so wait for it to start before waiting for it to finish.
		async Task<bool> WaitForRenderingAsync(Session session)
		{
			await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), true, 5000);
			return await this.WaitForPropertyAsync(session, nameof(Session.IsRenderingImage), false, 10000);
		}


		/// <summary>
		/// Test for applying the color tables carried by a profile to the rendering.
		/// </summary>
		[Test]
		public void TestApplyingColorTables()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// open file, its format is unidentifiable so the default profile is kept
				var filePath = this.GenerateSourceFile();
				session.OpenSourceFileCommand.Execute(filePath);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open source file.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete first rendering.");
				Assert.That(session.HasColorTables, Is.False, "No color table should be applied before applying the profile.");

				// prepare a profile which carries 14-bit color tables and a white level which is meaningful in the color domain of the tables only
				ImageRenderers.TryFindByFormatName("Bayer_Pattern_8", out var renderer);
				Assert.That(renderer, Is.Not.Null);
				Assert.That(renderer!.IsColorTableSupported, Is.True, "The renderer selected for testing should support color tables.");
				using var redColorTable = CreateColorTable();
				using var greenColorTable = CreateColorTable();
				using var blueColorTable = CreateColorTable();
				using var profile = new ImageRenderingProfile("Color Tables", renderer).Setup(it =>
				{
					it.Width = 16;
					it.Height = 16;
					it.EffectiveBits = [ 8, 0, 0, 0 ];
					it.BlackLevels = [ 0, 0, 0, 0 ];
					it.WhiteLevels = [ 12000, 0, 0, 0 ];
					it.PixelStrides = [ 1, 0, 0, 0 ];
					it.RowStrides = [ 16, 0, 0, 0 ];
					it.RedColorTable = redColorTable;
					it.GreenColorTable = greenColorTable;
					it.BlueColorTable = blueColorTable;
				});

				// apply the profile and check that the tables decide the effective bits without wiping the white level carried by the profile
				session.Profile = profile;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering with color tables.");
				Assert.That(session.HasRenderingError, Is.False, "Rendering with color tables failed.");
				Assert.That(session.HasColorTables, Is.True, $"{nameof(Session.HasColorTables)} should be set once the profile carrying the tables is applied.");
				Assert.That(session.EffectiveBits1, Is.EqualTo(14), "Effective bits should be coerced to the color depth of the tables.");
				Assert.That(session.SourceImageEffectiveBits, Is.EqualTo(14), $"{nameof(Session.SourceImageEffectiveBits)} should follow the color depth of the tables.");
				Assert.That(session.WhiteLevel1, Is.EqualTo(12000u), "White level carried by the profile should survive the coercion of effective bits.");

				// the effective bits set by user are ignored while the tables are applied
				session.EffectiveBits1 = 8;
				Assert.That(session.EffectiveBits1, Is.EqualTo(14), "Effective bits should not be changeable while the color tables are applied.");

				// the tables are dropped when the renderer which applies them is replaced
				ImageRenderers.TryFindByFormatName("L8", out var luminanceRenderer);
				Assert.That(luminanceRenderer, Is.Not.Null);
				Assert.That(luminanceRenderer!.IsColorTableSupported, Is.False, "The renderer selected for testing should not support color tables.");
				session.ImageRenderer = luminanceRenderer;
				Assert.That(session.HasColorTables, Is.False, $"{nameof(Session.HasColorTables)} should be cleared when the renderer does not apply the tables.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering without color tables.");
				session.EffectiveBits1 = 6;
				Assert.That(session.EffectiveBits1, Is.EqualTo(6), "Effective bits should be changeable again once the tables are not applied.");

				// close file
				session.Profile = ImageRenderingProfile.Default;
				session.ClearSourceCommand.Execute(null);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000), Is.True, "Cannot close source file.");
				await DeleteFileAsync(filePath);
			});
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
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete first rendering.");
				Assert.That(session.RenderedImage, Is.Not.Null, "No rendered image for first rendering.");

				// change renderers
				foreach (var imageRenderer in ImageRenderers.All)
				{
					// skip renderers of compressed formats, which cannot render arbitrary raw data
					if (imageRenderer.Format.Category == ImageFormatCategory.Compressed)
						continue;

					session.ImageRenderer = imageRenderer;
					var planeDescriptors = imageRenderer.Format.PlaneDescriptors;
					Assert.That(await this.WaitForRenderingAsync(session), Is.True, $"Unable to complete rendering by {imageRenderer}.");
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
				await DeleteFileAsync(filePath);
			});
		}


		/// <summary>
		/// Test for moving to another frame of frame sequence when the current frame cannot be rendered.
		/// </summary>
		[Test]
		public void TestMovingToUnrenderableFrameOfSequence()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// generate frames, file format of the 2nd frame is unidentifiable so it cannot be rendered by renderer of PNG
				var frameFilePaths = new string[]
				{
					await this.GenerateImageFileAsync("unrenderable_1.png", FileFormats.Png, 64, 48),
					this.GenerateRawDataFile("unrenderable_2.dat"),
					await this.GenerateImageFileAsync("unrenderable_3.png", FileFormats.Png, 64, 48),
				};

				// open files as frame sequence
				session.OpenSourceFilesCommand.Execute(frameFilePaths);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open files as frame sequence.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering of the 1st frame.");
				Assert.That(session.FrameCount, Is.EqualTo(3L), "Number of frames should be number of files.");
				Assert.That(session.HasRenderingError, Is.False, "Unable to render the 1st frame.");

				// move to the frame which cannot be rendered
				session.FrameNumber = 2;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering of the 2nd frame.");
				Assert.That(session.HasRenderingError, Is.True, "The frame with unidentifiable file format should not be rendered.");
				Assert.That(session.MoveToNextFrameCommand.CanExecute(null), Is.True, "Should be able to move to the next frame even if the current frame cannot be rendered.");
				Assert.That(session.MoveToPreviousFrameCommand.CanExecute(null), Is.True, "Should be able to move to the previous frame even if the current frame cannot be rendered.");

				// move to the next frame and check that it can still be rendered
				session.MoveToNextFrameCommand.Execute(null);
				Assert.That(session.FrameNumber, Is.EqualTo(3L), "Should have moved to the 3rd frame.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering of the 3rd frame.");
				Assert.That(session.HasRenderingError, Is.False, "Unable to render the frame after the frame which cannot be rendered.");

				// close files
				session.ClearSourceCommand.Execute(null);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000), Is.True, "Cannot close frame sequence.");
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
				await DeleteFileAsync(filePath1);

				// close file 2
				closeCommand.Execute(null);

				// wait for closing
				waitingResult = await this.WaitForCommandState(closeCommand, false, null, 1000);
				Assert.That(waitingResult, Is.True, "Source file closing should be able to be executed.");
				waitingResult = await this.WaitForPropertyAsync(session,nameof(Session.IsSourceOpened), false, 1000);
				Assert.That(waitingResult, Is.True, $"{nameof(Session.IsSourceOpened)} should be false.");
				Assert.That(openCommand.CanExecute(null), Is.True, "Source file opening should be able to be executed.");

				// delete file 2 to make sure that file has been unlocked
				await DeleteFileAsync(filePath2);
			});
		}


		/// <summary>
		/// Test for rendering frames of frame sequence which consists of files with different file formats.
		/// </summary>
		[Test]
		public void TestRenderingMixedFormatFrameSequence()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// generate frames with different file formats and dimensions
				var frameFilePaths = new string[]
				{
					await this.GenerateImageFileAsync("mixed_1.png", FileFormats.Png, 64, 48),
					await this.GenerateImageFileAsync("mixed_2.jpg", FileFormats.Jpeg, 32, 24),
				};

				// open files as frame sequence, renderer and dimensions are selected by file format of the 1st frame
				session.OpenSourceFilesCommand.Execute(frameFilePaths);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open files as frame sequence.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering of the 1st frame.");
				Assert.That(session.FrameCount, Is.EqualTo(2L), "Number of frames should be number of files.");
				Assert.That(session.HasRenderingError, Is.False, "Unable to render the 1st frame.");
				Assert.That(session.ImageWidth, Is.EqualTo(64), "Width should be selected by file format of the 1st frame.");
				Assert.That(session.ImageHeight, Is.EqualTo(48), "Height should be selected by file format of the 1st frame.");
				var frame1ImageRenderer = session.ImageRenderer;

				// move to the frame with another file format
				session.FrameNumber = 2;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering of the 2nd frame.");
				Assert.That(session.HasRenderingError, Is.False, "Unable to render the frame with another file format.");
				Assert.That(session.ImageRenderer, Is.Not.SameAs(frame1ImageRenderer), "Renderer should be selected by file format of the 2nd frame.");
				Assert.That(session.ImageWidth, Is.EqualTo(32), "Width should be selected by file format of the 2nd frame.");
				Assert.That(session.ImageHeight, Is.EqualTo(24), "Height should be selected by file format of the 2nd frame.");

				// move back to the 1st frame
				session.FrameNumber = 1;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering after moving back to the 1st frame.");
				Assert.That(session.HasRenderingError, Is.False, "Unable to render the 1st frame again.");
				Assert.That(session.ImageRenderer, Is.SameAs(frame1ImageRenderer), "Renderer should be selected by file format of the 1st frame again.");
				Assert.That(session.ImageWidth, Is.EqualTo(64), "Width should be selected by file format of the 1st frame again.");
				Assert.That(session.ImageHeight, Is.EqualTo(48), "Height should be selected by file format of the 1st frame again.");

				// close files
				session.ClearSourceCommand.Execute(null);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000), Is.True, "Cannot close frame sequence.");
			});
		}
	}
}
