using Carina.PixelViewer.Media;
using NUnit.Framework;

namespace Carina.PixelViewer.Test.Media
{
	/// <summary>
	/// Tests of <see cref="BitmapBuffer"/>.
	/// </summary>
	[TestFixture]
	class BitmapBufferTests : BaseBitmapBufferTests<BitmapBuffer>
	{
		// Create instance.
		protected override BitmapBuffer CreateInstance(BitmapFormat format, int width, int height) => new BitmapBuffer(format, width, height);
	}
}
