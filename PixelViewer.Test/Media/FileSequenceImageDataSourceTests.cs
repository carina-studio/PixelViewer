using Carina.PixelViewer.Media;
using CarinaStudio;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="FileSequenceImageDataSource"/>.
/// </summary>
[TestFixture]
class FileSequenceImageDataSourceTests : BaseShareableDisposableTests<FileSequenceImageDataSource>
{
	// Create a temporary directory with frame_000/001/002 files whose content is {10*(index+1)} repeated (index+1) times.
	static string CreateFrameFiles(out string[] fileNames)
	{
		// create directory
		var directory = Path.Combine(Path.GetTempPath(), $"PixelViewerSeqTest_{Path.GetRandomFileName()}");
		Directory.CreateDirectory(directory);

		// create file of each frame
		fileNames = new string[3];
		for (var i = 0; i < 3; ++i)
		{
			var fileName = Path.Combine(directory, $"frame_{i:000}.raw");
			var data = new byte[i + 1];
			for (var j = data.Length - 1; j >= 0; --j)
				data[j] = (byte)(10 * (i + 1));
			File.WriteAllBytes(fileName, data);
			fileNames[i] = fileName;
		}
		return directory;
	}


	// Create instance with 3 frames, order of frames is irrelevant because file names are generated.
	protected override FileSequenceImageDataSource CreateInstance()
	{
		var fileNames = new string[3];
		for (var i = 0; i < fileNames.Length; ++i)
		{
			var data = new byte[i + 1];
			for (var j = data.Length - 1; j >= 0; --j)
				data[j] = (byte)(10 * (i + 1));
			fileNames[i] = this.CreateCacheFile().Use(stream =>
			{
				stream.Write(data);
				return stream.Name;
			});
		}
		return new FileSequenceImageDataSource(this.Application, fileNames);
	}


	/// <summary>
	/// Test for providing no data by the sequence itself.
	/// </summary>
	[Test]
	public void DataAccessTest() => this.TestOnApplicationThread(async () =>
	{
		// create files of frames
		string directory = CreateFrameFiles(out string[] fileNames);
		try
		{
			// check that data can only be accessed through source of frame
			using var source = new FileSequenceImageDataSource(this.Application, fileNames);
			Assert.That(source.CheckStreamAccess(CarinaStudio.IO.StreamAccess.Read), Is.False);
			Assert.Throws<InvalidOperationException>(() =>
				_ = source.OpenStreamAsync(CarinaStudio.IO.StreamAccess.Read, CancellationToken.None));
		}
		finally
		{
			await DeleteDirectoryAsync(directory);
		}
	});


	/// <summary>
	/// Test for sorting files by file name only, no matter which directory each file is in.
	/// </summary>
	[Test]
	public void DirectoryIgnoringSortingTest()
	{
		string[] fileNames = [ Path.Combine("z", "1.raw"), Path.Combine("a", "2.raw") ];
		string[] expectedFileNames = [ Path.Combine("z", "1.raw"), Path.Combine("a", "2.raw") ];
		string[] sortedFileNames = FileSequenceImageDataSource.SortFiles(fileNames);
		Assert.That(sortedFileNames, Is.EqualTo(expectedFileNames));
	}


	/// <summary>
	/// Test for rejecting a frame index which is out of range.
	/// </summary>
	[Test]
	public void FrameIndexRangeTest() => this.TestOnApplicationThread(async () =>
	{
		// create files of frames
		string directory = CreateFrameFiles(out string[] fileNames);
		try
		{
			// check that an out-of-range index is rejected
			using var source = new FileSequenceImageDataSource(this.Application, fileNames);
			foreach (var frameIndex in (int[])[ -1, 3 ])
			{
				var isFrameIndexRejected = false;
				try
				{
					using var frameSource = await source.GetFrameAsync(frameIndex, CancellationToken.None);
				}
				catch (ArgumentOutOfRangeException)
				{
					isFrameIndexRejected = true;
				}
				Assert.That(isFrameIndexRejected, $"Frame index {frameIndex} should be rejected.");
			}
		}
		finally
		{
			await DeleteDirectoryAsync(directory);
		}
	});


	/// <summary>
	/// Test for keeping source of frame in cache and sharing it to each caller.
	/// </summary>
	[Test]
	public void FrameSourceCacheTest() => this.TestOnApplicationThread(async () =>
	{
		// create files of frames
		string directory = CreateFrameFiles(out string[] fileNames);
		try
		{
			// get source of the same frame twice
			using var source = new FileSequenceImageDataSource(this.Application, fileNames);
			var firstFrameSource = await source.GetFrameAsync(0, CancellationToken.None);
			using var secondFrameSource = await source.GetFrameAsync(0, CancellationToken.None);
			Assert.That(secondFrameSource, Is.Not.SameAs(firstFrameSource), "Each caller should get its own shared instance.");

			// check that disposing one of them keeps the other one usable
			firstFrameSource.Dispose();
			await using var stream = await secondFrameSource.OpenStreamAsync(CarinaStudio.IO.StreamAccess.Read, CancellationToken.None);
			Assert.That(stream.ReadByte(), Is.EqualTo(10));
		}
		finally
		{
			await DeleteDirectoryAsync(directory);
		}
	});


	/// <summary>
	/// Test for providing data of each frame by its own source.
	/// </summary>
	[Test]
	public void FrameSourceTest() => this.TestOnApplicationThread(async () =>
	{
		// create files of frames
		string directory = CreateFrameFiles(out string[] fileNames);
		try
		{
			// check state of source
			using var source = new FileSequenceImageDataSource(this.Application, fileNames);
			Assert.That(source.FrameCount, Is.EqualTo(3));
			Assert.That(source.FileNames, Is.EqualTo(fileNames));
			Assert.That(source.FileNames.IsReadOnly, "File names must not be exposed as a mutable list.");
			Assert.That(source.Size, Is.EqualTo(6), "Size should be the total size of all frames.");

			// check data provided by source of the last frame
			using var frameSource = await source.GetFrameAsync(2, CancellationToken.None);
			Assert.That(frameSource.Size, Is.EqualTo(3), "Size of frame should be size of its own file (frame_002 = 3 bytes).");
			await using var stream = await frameSource.OpenStreamAsync(CarinaStudio.IO.StreamAccess.Read, CancellationToken.None);
			Assert.That(stream.ReadByte(), Is.EqualTo(30), "Source of frame must provide data of frame_002.");
		}
		finally
		{
			await DeleteDirectoryAsync(directory);
		}
	});


	/// <summary>
	/// Test for keeping an inaccessible file in the sequence instead of failing the whole sequence.
	/// </summary>
	[Test]
	public void InaccessibleFileTest() => this.TestOnApplicationThread(async () =>
	{
		// create files of frames and remove the file of the 2nd frame
		string directory = CreateFrameFiles(out string[] fileNames);
		try
		{
			await DeleteFileAsync(fileNames[1]);
			using var source = new FileSequenceImageDataSource(this.Application, fileNames);
			Assert.That(source.FrameCount, Is.EqualTo(3), "Inaccessible file must be kept in the sequence.");
			Assert.That(source.Size, Is.EqualTo(4), "Size of inaccessible file should be treated as zero.");

			// check that only the frame of the removed file is unavailable
			var isGettingFrameSourceFailed = false;
			try
			{
				using var frameSourceOfRemovedFile = await source.GetFrameAsync(1, CancellationToken.None);
			}
			catch (FileNotFoundException)
			{
				isGettingFrameSourceFailed = true;
			}
			Assert.That(isGettingFrameSourceFailed, "Getting source of frame of inaccessible file should fail.");

			// check that the other frames are still readable
			using var frameSource = await source.GetFrameAsync(2, CancellationToken.None);
			await using var stream = await frameSource.OpenStreamAsync(CarinaStudio.IO.StreamAccess.Read, CancellationToken.None);
			Assert.That(stream.ReadByte(), Is.EqualTo(30));
		}
		finally
		{
			await DeleteDirectoryAsync(directory);
		}
	});


	/// <summary>
	/// Test for sorting files in natural (numeric-aware) order.
	/// </summary>
	[Test]
	public void NaturalNumericSortingTest()
	{
		string[] fileNames =
		[
			Path.Combine("seq", "frame_10.yuv"),
			Path.Combine("seq", "frame_2.yuv"),
			Path.Combine("seq", "frame_1.yuv"),
		];
		string[] expectedFileNames =
		[
			Path.Combine("seq", "frame_1.yuv"),
			Path.Combine("seq", "frame_2.yuv"),
			Path.Combine("seq", "frame_10.yuv"),
		];
		string[] sortedFileNames = FileSequenceImageDataSource.SortFiles(fileNames);
		Assert.That(sortedFileNames, Is.EqualTo(expectedFileNames));
	}


	/// <summary>
	/// Test for sorting files which have no number in their file names.
	/// </summary>
	[Test]
	public void PlainNameSortingTest()
	{
		string[] fileNames = [ "c.png", "a.png", "b.png" ];
		string[] expectedFileNames = [ "a.png", "b.png", "c.png" ];
		string[] sortedFileNames = FileSequenceImageDataSource.SortFiles(fileNames);
		Assert.That(sortedFileNames, Is.EqualTo(expectedFileNames));
	}


	/// <summary>
	/// Test for sorting files which have the same file name in different directories.
	/// </summary>
	[Test]
	public void SameFileNameSortingTest()
	{
		string[] fileNames = [ Path.Combine("b", "1.raw"), Path.Combine("a", "1.raw") ];
		string[] expectedFileNames = [ Path.Combine("a", "1.raw"), Path.Combine("b", "1.raw") ];
		string[] sortedFileNames = FileSequenceImageDataSource.SortFiles(fileNames);
		Assert.That(sortedFileNames, Is.EqualTo(expectedFileNames), "Files with the same file name must be ordered by their paths.");
	}


	// Validate instance.
	protected override async Task ValidateInstanceAsync(FileSequenceImageDataSource instance)
	{
		// check state
		Assert.That(instance.FrameCount, Is.EqualTo(instance.FileNames.Count));
		Assert.That(instance.CheckStreamAccess(CarinaStudio.IO.StreamAccess.Read), Is.False, "Sequence should provide no data by itself.");

		// check data provided by source of each frame
		var totalSize = 0L;
		for (var i = 0; i < instance.FrameCount; ++i)
		{
			using var frameSource = await instance.GetFrameAsync(i, CancellationToken.None);
			await using var stream = await frameSource.OpenStreamAsync(CarinaStudio.IO.StreamAccess.Read, CancellationToken.None);
			Assert.That(stream.Length, Is.EqualTo(frameSource.Size), $"Size of data of frame {i} is different from expected.");
			totalSize += frameSource.Size;
		}
		Assert.That(instance.Size, Is.EqualTo(totalSize), "Size of sequence should be total size of all frames.");
	}
}
