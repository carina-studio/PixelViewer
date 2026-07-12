using Carina.PixelViewer.Media;
using NUnit.Framework;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test
{
	/// <summary>
	/// Tests of <see cref="FileSequenceImageDataSource"/> frame ordering.
	/// </summary>
	[TestFixture]
	class FileSequenceImageDataSourceTests
	{
		[Test]
		public void SortsFilesInNaturalNumericOrder()
		{
			var input = new[]
			{
				System.IO.Path.Combine("seq", "frame_10.yuv"),
				System.IO.Path.Combine("seq", "frame_2.yuv"),
				System.IO.Path.Combine("seq", "frame_1.yuv"),
			};
			var sorted = FileSequenceImageDataSource.SortFiles(input);
			Assert.That(sorted, Is.EqualTo(new[]
			{
				System.IO.Path.Combine("seq", "frame_1.yuv"),
				System.IO.Path.Combine("seq", "frame_2.yuv"),
				System.IO.Path.Combine("seq", "frame_10.yuv"),
			}));
		}

		[Test]
		public void SortsPlainNamesAlphabetically()
		{
			var input = new[] { "c.png", "a.png", "b.png" };
			var sorted = FileSequenceImageDataSource.SortFiles(input);
			Assert.That(sorted, Is.EqualTo(new[] { "a.png", "b.png", "c.png" }));
		}

		[Test]
		public void SortsByFileNameIgnoringDirectory()
		{
			var input = new[]
			{
				System.IO.Path.Combine("z", "1.raw"),
				System.IO.Path.Combine("a", "2.raw"),
			};
			var sorted = FileSequenceImageDataSource.SortFiles(input);
			Assert.That(sorted, Is.EqualTo(new[]
			{
				System.IO.Path.Combine("z", "1.raw"),
				System.IO.Path.Combine("a", "2.raw"),
			}));
		}


		// Create a temporary directory with frame_000/001/002 files whose content is {10*(index+1)} repeated (index+1) times.
		static string CreateFrameFiles(out string[] fileNames)
		{
			var dir = Path.Combine(Path.GetTempPath(), "PixelViewerSeqTest_" + Path.GetRandomFileName());
			Directory.CreateDirectory(dir);
			fileNames = new string[3];
			for (var i = 0; i < 3; ++i)
			{
				var path = Path.Combine(dir, $"frame_{i:000}.raw");
				var bytes = new byte[i + 1];
				for (var b = 0; b < bytes.Length; ++b)
					bytes[b] = (byte)(10 * (i + 1));
				File.WriteAllBytes(path, bytes);
				fileNames[i] = path;
			}
			return dir;
		}


		[Test]
		public void SelectFrameServesCorrectFile()
		{
			var dir = CreateFrameFiles(out var files);
			try
			{
				using var src = new FileSequenceImageDataSource(files);
				Assert.That(src.FrameCount, Is.EqualTo(3));

				src.SelectFrame(2);
				Assert.That(src.Size, Is.EqualTo(3), "Size should reflect the selected frame (frame_002 = 3 bytes).");
				Assert.That(src.CurrentFileName, Is.EqualTo(files[2]));

				src.SelectFrame(1);
				Assert.That(src.Size, Is.EqualTo(2));
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		// Regression: the selected frame must survive IShareableDisposable.Share(), because the renderer
		// reads from a shared instance. A per-instance index would make the share always serve frame 0.
		[Test]
		public async Task SelectedFrameSurvivesShare()
		{
			var dir = CreateFrameFiles(out var files);
			try
			{
				using var src = new FileSequenceImageDataSource(files);
				src.SelectFrame(2);

				using var shared = ((IImageDataSource)src).Share();
				Assert.That(shared.Size, Is.EqualTo(3), "Shared instance must serve the frame selected on the original.");

				using var stream = await shared.OpenStreamAsync(CarinaStudio.IO.StreamAccess.Read, CancellationToken.None);
				var buffer = new byte[8];
				var read = stream.Read(buffer, 0, buffer.Length);
				Assert.That(read, Is.EqualTo(3));
				Assert.That(buffer[0], Is.EqualTo((byte)30), "Shared instance must read bytes of frame_002, not frame_000.");
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}
	}
}
