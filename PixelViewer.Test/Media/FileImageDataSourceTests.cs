using Carina.PixelViewer.Media;
using CarinaStudio;
using NUnit.Framework;

namespace Carina.PixelViewer.Test.Media
{
	/// <summary>
	/// Tests of <see cref="FileImageDataSource"/>.
	/// </summary>
	[TestFixture]
	class FileImageDataSourceTests : BaseImageDataSourceTests<FileImageDataSource>
	{
		// Create instance.
		protected override FileImageDataSource CreateInstance(byte[] data)
		{
			return new FileImageDataSource(this.CreateCacheFile().Use((stream) =>
			{
				stream.Write(data);
				return stream.Name;
			}));
		}
	}
}
