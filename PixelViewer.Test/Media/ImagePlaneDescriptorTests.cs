using Carina.PixelViewer.Media;
using NUnit.Framework;
using System;

namespace Carina.PixelViewer.Test.Media;

/// <summary>
/// Tests of <see cref="ImagePlaneDescriptor"/>.
/// </summary>
[TestFixture]
class ImagePlaneDescriptorTests
{
	/// <summary>
	/// Test for comparing descriptors which are different in subsampling.
	/// </summary>
	[Test]
	public void ComparingSubsamplingTest()
	{
		// check that descriptors with the same subsampling are equal
		var descriptor = new ImagePlaneDescriptor(2, 8, 16, false, 2, 2);
		Assert.That(descriptor, Is.EqualTo(new ImagePlaneDescriptor(2, 8, 16, false, 2, 2)));
		Assert.That(descriptor.GetHashCode(), Is.EqualTo(new ImagePlaneDescriptor(2, 8, 16, false, 2, 2).GetHashCode()));

		// check that a difference in either direction of subsampling is not equal
		Assert.That(descriptor, Is.Not.EqualTo(new ImagePlaneDescriptor(2, 8, 16, false, 1, 2)));
		Assert.That(descriptor, Is.Not.EqualTo(new ImagePlaneDescriptor(2, 8, 16, false, 2, 1)));

		// check that the subsampling is taken into account by the hash code, so that a descriptor is not mistaken for another one
		Assert.That(descriptor.GetHashCode(), Is.Not.EqualTo(new ImagePlaneDescriptor(2, 8, 16, false, 1, 2).GetHashCode()));
		Assert.That(descriptor.GetHashCode(), Is.Not.EqualTo(new ImagePlaneDescriptor(2, 8, 16, false, 2, 1).GetHashCode()));
	}


	/// <summary>
	/// Test for the default subsampling of descriptor.
	/// </summary>
	[Test]
	public void DefaultSubsamplingTest()
	{
		// check that a plane is not subsampled unless the subsampling is specified
		Assert.That(new ImagePlaneDescriptor(1).HorizontalSubsampling, Is.EqualTo(1));
		Assert.That(new ImagePlaneDescriptor(1).VerticalSubsampling, Is.EqualTo(1));
		Assert.That(new ImagePlaneDescriptor(2, 8, 16, true).HorizontalSubsampling, Is.EqualTo(1));
		Assert.That(new ImagePlaneDescriptor(2, 8, 16, true).VerticalSubsampling, Is.EqualTo(1));
	}


	/// <summary>
	/// Test for rejecting an invalid subsampling.
	/// </summary>
	[Test]
	public void InvalidSubsamplingTest()
	{
		// check that a subsampling which is less than 1 is rejected, one sample is shared by at least one pixel
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ImagePlaneDescriptor(1, 8, 8, false, 0, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ImagePlaneDescriptor(1, 8, 8, false, 1, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ImagePlaneDescriptor(1, 8, 8, false, -1, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ImagePlaneDescriptor(1, 8, 8, false, 1, -1));
	}


	/// <summary>
	/// Test for keeping the subsampling given to the descriptor.
	/// </summary>
	[Test]
	public void SubsamplingTest()
	{
		// check that the subsampling of a plane which is subsampled in both directions is kept, as the chroma plane of a YUV 4:2:0 format
		var descriptor = new ImagePlaneDescriptor(2, 8, 8, false, 2, 2);
		Assert.That(descriptor.HorizontalSubsampling, Is.EqualTo(2));
		Assert.That(descriptor.VerticalSubsampling, Is.EqualTo(2));

		// check that the subsampling of a plane which is subsampled horizontally only is kept, as the chroma plane of a YUV 4:2:2 format
		descriptor = new ImagePlaneDescriptor(1, 8, 8, false, 2, 1);
		Assert.That(descriptor.HorizontalSubsampling, Is.EqualTo(2));
		Assert.That(descriptor.VerticalSubsampling, Is.EqualTo(1));
	}
}
