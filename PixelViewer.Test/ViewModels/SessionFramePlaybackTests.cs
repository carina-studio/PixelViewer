using Carina.PixelViewer.ViewModels;
using NUnit.Framework;

namespace Carina.PixelViewer.Test.ViewModels
{
	/// <summary>
	/// Tests of frame sequence playback logic in <see cref="Session"/>.
	/// </summary>
	[TestFixture]
	class SessionFramePlaybackTests
	{
		[Test]
		public void AdvancesToNextFrameWithinSequence()
		{
			Assert.That(Session.GetNextFrameNumber(1, 5, looping: false), Is.EqualTo(2L));
			Assert.That(Session.GetNextFrameNumber(4, 5, looping: false), Is.EqualTo(5L));
			Assert.That(Session.GetNextFrameNumber(1, 5, looping: true), Is.EqualTo(2L));
		}

		[Test]
		public void StopsAtLastFrameWhenNotLooping()
		{
			Assert.That(Session.GetNextFrameNumber(5, 5, looping: false), Is.Null);
		}

		[Test]
		public void LoopsToFirstFrameWhenLooping()
		{
			Assert.That(Session.GetNextFrameNumber(5, 5, looping: true), Is.EqualTo(1L));
		}

		[Test]
		public void ReturnsNullWhenSingleOrNoFrame()
		{
			Assert.That(Session.GetNextFrameNumber(1, 1, looping: true), Is.Null);
			Assert.That(Session.GetNextFrameNumber(1, 1, looping: false), Is.Null);
			Assert.That(Session.GetNextFrameNumber(1, 0, looping: true), Is.Null);
		}

		[Test]
		public void ClampsCurrentBelowRange()
		{
			Assert.That(Session.GetNextFrameNumber(0, 5, looping: false), Is.EqualTo(1L));
		}
	}
}
