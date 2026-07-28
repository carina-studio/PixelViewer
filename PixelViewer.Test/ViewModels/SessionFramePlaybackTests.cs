using Carina.PixelViewer.ViewModels;
using NUnit.Framework;

namespace Carina.PixelViewer.Test.ViewModels;

/// <summary>
/// Tests of frame sequence playback logic in <see cref="Session"/>.
/// </summary>
[TestFixture]
class SessionFramePlaybackTests
{
	// Interval between frames of playback at 30 FPS.
	const double Interval30Fps = 1000.0 / 30;


	/// <summary>
	/// Test for dropping frames when rendering is unable to catch up with the frame rate.
	/// </summary>
	[Test]
	public void DroppingFramesTest()
	{
		// check that rendering which overruns 3 intervals advances 4 frames instead of 1
		var nextFrame = Session.SelectNextFrameForPlayback(1, 0, Interval30Fps * 3, Interval30Fps, 100, false);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(5L));
		Assert.That(nextFrame?.Delay, Is.EqualTo((int)Interval30Fps), "Frame should be presented at its own time on the timeline.");

		// check that only the remaining time of the current interval is waited when rendering overruns partially
		nextFrame = Session.SelectNextFrameForPlayback(1, 0, Interval30Fps * 3.5, Interval30Fps, 100, false);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(5L));
		Assert.That(nextFrame?.Delay, Is.EqualTo((int)(Interval30Fps * 0.5)));

		// check that rendering which overruns a single interval advances 2 frames
		nextFrame = Session.SelectNextFrameForPlayback(1, 0, Interval30Fps * 1.5, Interval30Fps, 100, false);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(3L));
	}


	/// <summary>
	/// Test for keeping the playback timeline anchored instead of drifting.
	/// </summary>
	[Test]
	public void FrameTimelineTest()
	{
		// check that the next frame is presented one interval after the anchored frame
		var nextFrame = Session.SelectNextFrameForPlayback(1, 0, 0, Interval30Fps, 100, false);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(2L));
		Assert.That(nextFrame?.Delay, Is.EqualTo((int)Interval30Fps));

		// check that rendering which took less than one interval only consumes part of the delay
		nextFrame = Session.SelectNextFrameForPlayback(1, 0, 10, Interval30Fps, 100, false);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(2L));
		Assert.That(nextFrame?.Delay, Is.EqualTo((int)(Interval30Fps - 10)), "Delay should be measured from the anchored time, not from completion of rendering.");

		// check that the timeline is kept when the anchor is not at the beginning of playback
		nextFrame = Session.SelectNextFrameForPlayback(10, 1000, 1000, Interval30Fps, 100, false);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(11L));
		Assert.That(nextFrame?.Delay, Is.EqualTo((int)Interval30Fps));
	}


	/// <summary>
	/// Test for looping back to the first frame after the last one.
	/// </summary>
	[Test]
	public void LoopingTest()
	{
		// check that playback loops back to the first frame
		var nextFrame = Session.SelectNextFrameForPlayback(5, 0, 0, Interval30Fps, 5, true);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(1L));

		// check that dropped frames are wrapped as well
		nextFrame = Session.SelectNextFrameForPlayback(5, 0, Interval30Fps * 2, Interval30Fps, 5, true);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(3L));

		// check that playback stops after the last frame when looping is disabled
		Assert.That(Session.SelectNextFrameForPlayback(5, 0, 0, Interval30Fps, 5, false), Is.Null);
	}


	/// <summary>
	/// Test for starting from the first frame when the anchored frame is out of range.
	/// </summary>
	[Test]
	public void UnanchoredFrameTest() =>
		Assert.That(Session.SelectNextFrameForPlayback(0, 0, 0, Interval30Fps, 5, false)?.FrameNumber, Is.EqualTo(2L));


	/// <summary>
	/// Test for moving forward round by round, as playback does by anchoring on the selected frame.
	/// </summary>
	[Test]
	public void SuccessiveSelectionTest()
	{
		// check that frames keep moving forward when playing as fast as possible, each round takes 500 ms
		var frameNumber = 1L;
		var baseTime = 0.0;
		var currentTime = 0.0;
		for (var i = 1; i <= 3; ++i)
		{
			var nextFrame = Session.SelectNextFrameForPlayback(frameNumber, baseTime, currentTime, 0, 100, false);
			Assert.That(nextFrame?.FrameNumber, Is.EqualTo(frameNumber + 1), "Playing as fast as possible should move to the next frame in every round.");
			frameNumber = nextFrame.GetValueOrDefault().FrameNumber;
			baseTime = nextFrame.GetValueOrDefault().PresentTime;
			currentTime += 500;
		}
		Assert.That(frameNumber, Is.EqualTo(4L));

		// check that frames keep moving forward at a fixed frame rate when rendering is fast enough
		frameNumber = 1;
		baseTime = 0;
		currentTime = 0;
		for (var i = 1; i <= 3; ++i)
		{
			var nextFrame = Session.SelectNextFrameForPlayback(frameNumber, baseTime, currentTime, Interval30Fps, 100, false);
			Assert.That(nextFrame?.FrameNumber, Is.EqualTo(frameNumber + 1));
			frameNumber = nextFrame.GetValueOrDefault().FrameNumber;
			baseTime = nextFrame.GetValueOrDefault().PresentTime;
			currentTime = baseTime + 1; // rendering of the frame completes 1 ms after it was presented
		}
		Assert.That(frameNumber, Is.EqualTo(4L));
		Assert.That(baseTime, Is.EqualTo(Interval30Fps * 3).Within(0.001), "Timeline should not drift when it is anchored on the presenting time.");
	}


	/// <summary>
	/// Test for playing frames as fast as possible.
	/// </summary>
	[Test]
	public void UnlimitedFrameRateTest()
	{
		// check that no frame is dropped and no delay is applied no matter how long rendering took
		var nextFrame = Session.SelectNextFrameForPlayback(1, 0, 10000, 0, 100, false);
		Assert.That(nextFrame?.FrameNumber, Is.EqualTo(2L), "No frame should be dropped when playing as fast as possible.");
		Assert.That(nextFrame?.Delay, Is.EqualTo(0));
		Assert.That(nextFrame?.PresentTime, Is.EqualTo(10000), "Frame should be presented immediately when playing as fast as possible.");

		// check that looping is still applied
		Assert.That(Session.SelectNextFrameForPlayback(5, 0, 10000, 0, 5, true)?.FrameNumber, Is.EqualTo(1L));
		Assert.That(Session.SelectNextFrameForPlayback(5, 0, 10000, 0, 5, false), Is.Null);
	}


	/// <summary>
	/// Test for stopping playback when there are not enough frames to play.
	/// </summary>
	[Test]
	public void UnplayableFrameCountTest()
	{
		Assert.That(Session.SelectNextFrameForPlayback(1, 0, 0, Interval30Fps, 1, true), Is.Null);
		Assert.That(Session.SelectNextFrameForPlayback(1, 0, 0, Interval30Fps, 0, true), Is.Null);
	}
}
