using Carina.PixelViewer.Media;
using Carina.PixelViewer.Media.ImageEncoders;
using Carina.PixelViewer.Media.ImageRenderers;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
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
		/// Test for passing the black and white levels carried by a profile of 16-bit RGB to the renderer.
		/// </summary>
		[Test]
		public void TestApplyingLevelsOfRgbProfile()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// open file, its format is unidentifiable so the default profile is kept
				var filePath = this.GenerateSourceFile();
				session.OpenSourceFileCommand.Execute(filePath);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open source file.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete first rendering.");

				// apply the profile which carries valid levels
				ImageRenderers.TryFindByFormatName("RGB_161616", out var renderer);
				Assert.That(renderer, Is.Not.Null);
				ImageRenderingProfile CreateProfile(uint blackLevel, uint whiteLevel) => new ImageRenderingProfile("Levels", renderer!).Setup(it =>
				{
					it.Width = 8;
					it.Height = 8;
					it.EffectiveBits = [ 14, 0, 0, 0 ];
					it.BlackLevels = [ blackLevel, 0, 0, 0 ];
					it.WhiteLevels = [ whiteLevel, 0, 0, 0 ];
					it.PixelStrides = [ 6, 0, 0, 0 ];
					it.RowStrides = [ 8 * 6, 0, 0, 0 ];
				});
				using var profile = CreateProfile(1024, 15000);
				session.Profile = profile;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering with levels.");
				Assert.That(session.HasRenderingError, Is.False, "Rendering with valid levels failed.");
				Assert.That(session.AreAdjustableBlackWhiteLevels1, Is.True, "Levels of 16-bit RGB should be adjustable.");
				Assert.That((session.BlackLevel1, session.WhiteLevel1), Is.EqualTo((1024u, 15000u)), "Levels carried by the profile should be applied.");

				// apply the profile which carries invalid levels, which fails only if the levels reach the renderer
				using var invalidProfile = CreateProfile(15000, 1024);
				session.Profile = invalidProfile;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering with invalid levels.");
				Assert.That(session.HasRenderingError, Is.True, "Invalid levels should be passed to the renderer and refused by it.");

				// close file
				session.Profile = ImageRenderingProfile.Default;
				session.ClearSourceCommand.Execute(null);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000), Is.True, "Cannot close source file.");
				await DeleteFileAsync(filePath);
			});
		}


		/// <summary>
		/// Test for applying base exposure and RGB gains of current profile after selecting a renderer which does not apply them.
		/// </summary>
		[Test]
		public void TestApplyingProfileValuesToRendererWithoutSupport()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// prepare the actions to select renderer and to set and check the values
				void SelectRenderer(string formatName)
				{
					ImageRenderers.TryFindByFormatName(formatName, out var renderer);
					Assert.That(renderer, Is.Not.Null, $"Renderer of {formatName} should be found.");
					session.ImageRenderer = renderer;
				}
				void SetValues(double baseExposure, double gain)
				{
					session.BaseExposure = baseExposure;
					session.RedColorGain = gain;
					session.GreenColorGain = gain;
					session.BlueColorGain = gain;
				}
				void CheckValues(double baseExposure, double gain, string message)
				{
					Assert.That(session.BaseExposure, Is.EqualTo(baseExposure).Within(ImageRenderingOptions.MinBaseExposureDifference), message);
					Assert.That((session.RedColorGain, session.GreenColorGain, session.BlueColorGain), Is.EqualTo((gain, gain, gain)), message);
				}

				// open file, its format is unidentifiable so the default profile is kept
				var filePath = this.GenerateSourceFile();
				session.OpenSourceFileCommand.Execute(filePath);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open source file.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete first rendering.");
				try
				{
					// the values of the default profile are applied after selecting the renderer which does not apply them, base exposure is applied by the renderer of luminance only if it is supported at all
					SelectRenderer("Bayer_Pattern_16");
					SetValues(1.5, 2.0);
					SelectRenderer("L8");
					CheckValues(session.IsBaseExposureSupported ? 1.5 : 0, 1.0, "RGB gains of default profile should be applied after selecting the renderer which does not apply them.");
					SelectRenderer("P010");
					CheckValues(0, 1.0, "Values of default profile should be applied after selecting the renderer which does not apply them.");
					SelectRenderer("Bayer_Pattern_16");
					CheckValues(0, 1.0, "Values should not come back after selecting the renderer which applies them.");

					// the values of the user-defined profile are applied after selecting the renderer which does not apply them
					ImageRenderers.TryFindByFormatName("Bayer_Pattern_16", out var renderer);
					using var profile = new ImageRenderingProfile("Base Exposure and RGB Gains", renderer.AsNonNull()).Setup(it =>
					{
						it.BaseExposure = 0.75;
						it.RedColorGain = 3.0;
						it.GreenColorGain = 3.0;
						it.BlueColorGain = 3.0;
					});
					session.Profile = profile;
					SetValues(2.5, 1.25);
					SelectRenderer("P010");
					CheckValues(0.75, 3.0, "Values of user-defined profile should be applied after selecting the renderer which does not apply them.");
					SelectRenderer("Bayer_Pattern_16");
					CheckValues(0.75, 3.0, "Values of user-defined profile should be kept after selecting the renderer which applies them.");
					Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering.");
				}
				finally
				{
					// close file
					session.Profile = ImageRenderingProfile.Default;
					session.ClearSourceCommand.Execute(null);
					await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000);
					await DeleteFileAsync(filePath);
				}
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
		/// Test for reporting the lens shading defined by current profile, and for keeping whether it is applied or not in the saved state.
		/// </summary>
		[Test]
		public void TestLensShadingOfProfile()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// save the state of session as a JSON string
				static string SaveState(Session session)
				{
					using var stream = new MemoryStream();
					using (var writer = new Utf8JsonWriter(stream))
						session.SaveState(writer);
					return Encoding.UTF8.GetString(stream.ToArray());
				}

				// open file, its format is unidentifiable so the default profile is kept
				var filePath = this.GenerateSourceFile();
				session.OpenSourceFileCommand.Execute(filePath);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open source file.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete first rendering.");
				try
				{
					// the default profile defines no lens shading
					Assert.That(session.HasLensShading, Is.False, "Default profile should define no lens shading.");

					// the lens shading of the profile is reported only while the renderer renders a mosaic
					ImageRenderers.TryFindByFormatName("Bayer_Pattern_16", out var renderer);
					ImageRenderers.TryFindByFormatName("L8", out var luminanceRenderer);
					Assert.That(renderer, Is.Not.Null);
					Assert.That(luminanceRenderer, Is.Not.Null);
					var gains = new float[2 * 2 * 2 * 2];
					Array.Fill(gains, 2f);
					using var profile = new ImageRenderingProfile("Lens Shading", renderer.AsNonNull()).Setup(it =>
					{
						it.LensShading = new LensShadingMap(0, 0, 16, 16, 2, 2, 2, 2, 0, 0, 1, 1, gains);
					});
					session.Profile = profile;
					Assert.That(session.HasLensShading, Is.True, "Lens shading of the profile should be reported.");
					session.ImageRenderer = luminanceRenderer;
					Assert.That(session.HasLensShading, Is.False, "Lens shading should not be reported for the renderer which does not render a mosaic.");
					session.ImageRenderer = renderer;
					Assert.That(session.HasLensShading, Is.True, "Lens shading should be reported again for the renderer which renders a mosaic.");
					session.Profile = ImageRenderingProfile.Default;
					Assert.That(session.HasLensShading, Is.False, "Lens shading should not be reported after selecting the profile which defines none.");

					// whether lens shading is applied or not is kept in the saved state, it is written only when it is not applied
					Assert.That(session.ApplyLensShading, Is.True, "Lens shading should be applied by default.");
					Assert.That(SaveState(session), Does.Not.Contain($"\"{nameof(Session.ApplyLensShading)}\""), "Applying lens shading by default should not be written to the saved state.");
					session.ApplyLensShading = false;
					var savedState = SaveState(session);
					Assert.That(savedState, Does.Contain($"\"{nameof(Session.ApplyLensShading)}\""), "Not applying lens shading should be written to the saved state.");
					session.ApplyLensShading = true;
					using (var document = JsonDocument.Parse(savedState))
						await session.RestoreState(document.RootElement);
					Assert.That(session.ApplyLensShading, Is.False, "Not applying lens shading should be restored from the saved state.");
				}
				finally
				{
					// close file
					session.ApplyLensShading = true;
					session.Profile = ImageRenderingProfile.Default;
					session.ClearSourceCommand.Execute(null);
					await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000);
					await DeleteFileAsync(filePath);
				}
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
		/// Test for refusing to render compressed or tiled data which current renderer cannot handle.
		/// </summary>
		[Test]
		public void TestRenderingCompressedOrTiledData()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// open file
				var filePath = this.GenerateSourceFile();
				session.OpenSourceFileCommand.Execute(filePath);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open source file.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete first rendering.");

				// render by a renderer and dimensions which the data is sufficient for, the session is shared with other tests so its renderer is unknown
				ImageRenderers.TryFindByFormatName("L8", out var renderer);
				Assert.That(renderer, Is.Not.Null);
				session.ImageRenderer = renderer.AsNonNull();
				session.ImageWidth = 16;
				session.ImageHeight = 16;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering by the renderer selected for testing.");
				Assert.That(session.HasRenderingError, Is.False, "Rendering data which is not compressed should succeed.");

				// select compression which current renderer cannot decompress
				Assert.That(session.IsCompressionSupported, Is.False, "The renderer selected for testing should not support compression.");
				session.Compression = ImageDataCompression.LosslessJpeg;
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.HasRenderingError), true, 5000), Is.True, "Rendering compressed data which cannot be decompressed should fail.");

				// select no compression again
				session.Compression = ImageDataCompression.None;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering without compression.");
				Assert.That(session.HasRenderingError, Is.False, "Rendering should succeed again without compression.");

				// select a renderer which cannot handle tiles
				ImageRenderers.TryFindByFormatName("YUYV", out var yuvRenderer);
				Assert.That(yuvRenderer, Is.Not.Null);
				session.ImageRenderer = yuvRenderer.AsNonNull();
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering by the renderer which cannot handle tiles.");
				Assert.That(session.HasRenderingError, Is.False, "Rendering data which is not tiled should succeed.");

				// select tiles which current renderer cannot handle, one non-zero dimension is enough to make data tiled
				Assert.That(session.IsTileLayoutSupported, Is.False, "The renderer selected for testing should not support tiles.");
				session.TileWidth = 8;
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.HasRenderingError), true, 5000), Is.True, "Rendering tiled data which cannot be handled should fail.");

				// select no tiles again
				session.TileWidth = 0;
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete rendering without tiles.");
				Assert.That(session.HasRenderingError, Is.False, "Rendering should succeed again without tiles.");

				// close file
				session.ClearSourceCommand.Execute(null);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000), Is.True, "Cannot close source file.");
				await DeleteFileAsync(filePath);
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


		/// <summary>
		/// Test for resetting base exposure and RGB gains after changing source, according to where they came from.
		/// </summary>
		[Test]
		public void TestResettingBaseExposureAndRgbGainsAfterChangingSource()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// prepare the actions to open file after closing the current one, and to set and check the values
				var filePaths = new List<string>();
				async Task OpenAsync(string filePath)
				{
					filePaths.Add(filePath);
					if (session.IsSourceOpened)
					{
						session.ClearSourceCommand.Execute(null);
						Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000), Is.True, "Cannot close source file.");
					}
					session.OpenSourceFileCommand.Execute(filePath);
					Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open source file.");
					Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete first rendering.");
				}
				void SetValues(double baseExposure, double gain)
				{
					session.BaseExposure = baseExposure;
					session.RedColorGain = gain;
					session.GreenColorGain = gain;
					session.BlueColorGain = gain;
				}
				void CheckValues(double baseExposure, double gain, string message)
				{
					Assert.That(session.BaseExposure, Is.EqualTo(baseExposure).Within(ImageRenderingOptions.MinBaseExposureDifference), message);
					Assert.That((session.RedColorGain, session.GreenColorGain, session.BlueColorGain), Is.EqualTo((gain, gain, gain)), message);
				}
				try
				{
					// the values of a source opened with its profile of file format are not applied to the next source
					await OpenAsync(await this.GenerateImageFileAsync("reset_params_1.png", FileFormats.Png, 16, 16));
					Assert.That(session.Profile.IsFileFormat, Is.True, "Profile of file format should be applied.");
					SetValues(1.5, 2.0);
					await OpenAsync(this.GenerateSourceFile());
					CheckValues(0, 1.0, "Values of previous source opened with its profile of file format should be reset.");

					// the values set by user for a source without profile are kept for the next source
					SetValues(-1.25, 1.5);
					await OpenAsync(this.GenerateSourceFile());
					CheckValues(-1.25, 1.5, "Values set by user should be kept for the next source.");

					// the values of the user-defined profile kept selected are applied again to the next source, base exposure is applied only by the renderer which supports it
					ImageRenderers.TryFindByFormatName("Bayer_Pattern_16", out var renderer);
					Assert.That(renderer, Is.Not.Null);
					using var profile = new ImageRenderingProfile("Base Exposure and RGB Gains", renderer!).Setup(it =>
					{
						it.BaseExposure = 0.75;
						it.RedColorGain = 3.0;
						it.GreenColorGain = 3.0;
						it.BlueColorGain = 3.0;
					});
					session.Profile = profile;
					CheckValues(0.75, 3.0, "Values of user-defined profile should be applied.");
					SetValues(2.5, 1.25);
					await OpenAsync(this.GenerateSourceFile());
					Assert.That(session.Profile, Is.SameAs(profile), "User-defined profile should be kept selected.");
					CheckValues(session.IsBaseExposureSupported ? 0.75 : 2.5, 3.0, "Values of user-defined profile should be applied again to the next source.");
				}
				finally
				{
					// close file and delete generated files
					session.Profile = ImageRenderingProfile.Default;
					session.ClearSourceCommand.Execute(null);
					await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000);
					foreach (var filePath in filePaths)
						await DeleteFileAsync(filePath);
				}
			});
		}


		/// <summary>
		/// Test for showing and analyzing only the region of source image left by its insets, without rendering the image again.
		/// </summary>
		[Test]
		public void TestSourceInsets()
		{
			var session = this.session ?? throw new AssertionException("No instance for testing.");
			this.TestOnApplicationThread(async () =>
			{
				// open file and render it by a format of single plane
				var filePath = this.GenerateSourceFile();
				session.OpenSourceFileCommand.Execute(filePath);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), true, 1000), Is.True, "Cannot open source file.");
				Assert.That(await this.WaitForRenderingAsync(session), Is.True, "Unable to complete first rendering.");
				ImageRenderers.TryFindByFormatName("L8", out var l8Renderer);
				session.ImageRenderer = l8Renderer.AsNonNull();
				session.ImageWidth = 64;
				session.ImageHeight = 48;
				Assert.That(await WaitForRenderedImageSizeAsync(session, 64, 48), Is.True, "Whole image should be shown without insets.");

				// take in the edges, only the region left is shown and analyzed
				session.SourceInsets = new PixelInsets(4, 3, 10, 5);
				Assert.That(await WaitForRenderedImageSizeAsync(session, 50, 40), Is.True, "Only the region left by insets should be shown.");
				Assert.That(await WaitForHistogramsPixelCountAsync(session, 50 * 40), Is.True, "Only the region left by insets should be analyzed.");
				Assert.That((session.SourceInsetsLeft, session.SourceInsetsTop, session.SourceInsetsRight, session.SourceInsetsBottom), Is.EqualTo((4, 3, 10, 5)), "Edges of insets should be reported.");

				// change an edge of insets
				session.SourceInsetsLeft = 6;
				Assert.That(session.SourceInsets, Is.EqualTo(new PixelInsets(6, 3, 10, 5)), "Changing an edge should change the insets.");
				Assert.That(await WaitForRenderedImageSizeAsync(session, 48, 40), Is.True, "Region left by the changed insets should be shown.");

				// insets which leave nothing or extend the image fail rendering, which recovers after the insets become valid again
				foreach (var invalidInsets in new[] { new PixelInsets(64, 0, 0, 0), new PixelInsets(0, 0, 0, 48), new PixelInsets(-1, 0, 0, 0) })
				{
					session.SourceInsets = invalidInsets;
					Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.HasRenderingError), true, 5000), Is.True, $"Rendering should fail with insets {invalidInsets}.");
					session.SourceInsets = PixelInsets.Empty;
					Assert.That(await WaitForRenderedImageSizeAsync(session, 64, 48), Is.True, $"Rendering should recover after insets {invalidInsets}.");
					Assert.That(session.HasRenderingError, Is.False);
				}

				// insets are applied to the image of multiple planes in the same way
				ImageRenderers.TryFindByFormatName("NV12", out var nv12Renderer);
				session.ImageRenderer = nv12Renderer.AsNonNull();
				session.SourceInsets = new PixelInsets(4, 2, 4, 2);
				Assert.That(await WaitForRenderedImageSizeAsync(session, 56, 44), Is.True, "Only the region left by insets should be shown for format of multiple planes.");

				// insets are reset after closing the file
				session.ClearSourceCommand.Execute(null);
				Assert.That(await this.WaitForPropertyAsync(session, nameof(Session.IsSourceOpened), false, 1000), Is.True, "Cannot close source file.");
				Assert.That(session.SourceInsets.IsEmpty, Is.True, "Insets should be reset after closing source file.");
				await DeleteFileAsync(filePath);
			});

			// wait for the histograms of the given number of pixels
			static async Task<bool> WaitForHistogramsPixelCountAsync(Session session, int pixelCount)
			{
				for (var i = 0; i < 200; ++i)
				{
					if (session.Histograms?.EffectivePixelCount == pixelCount)
						return true;
					await Task.Delay(50);
				}
				return false;
			}

			// wait for the rendered image of the given size to be shown
			static async Task<bool> WaitForRenderedImageSizeAsync(Session session, int width, int height)
			{
				for (var i = 0; i < 200; ++i)
				{
					if (session.RenderedImage is { } image && image.PixelSize.Width == width && image.PixelSize.Height == height && !session.IsRenderingImage)
						return true;
					await Task.Delay(50);
				}
				return false;
			}
		}
	}
}
