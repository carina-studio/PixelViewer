using CarinaStudio.Collections;
using System.Collections.Generic;

namespace Carina.PixelViewer.Media.Demosaicing;

/// <summary>
/// Class to hold all available <see cref="DemosaicingAlgorithm"/>s.
/// </summary>
static class DemosaicingAlgorithms
{
	/// <summary>
	/// Algorithm which interpolates each missing color component of pixel by averaging the same component of its neighbors.
	/// </summary>
	public static readonly DemosaicingAlgorithm Bilinear = new BilinearDemosaicingAlgorithm();


	// Static fields.
	static readonly ObservableList<DemosaicingAlgorithm> all = [ Bilinear ];


	/// <summary>
	/// Get all available <see cref="DemosaicingAlgorithm"/>s.
	/// </summary>
	public static IList<DemosaicingAlgorithm> All { get; } = ListExtensions.AsReadOnly(all);


	/// <summary>
	/// Get default <see cref="DemosaicingAlgorithm"/>.
	/// </summary>
	public static DemosaicingAlgorithm Default => Bilinear;
}
