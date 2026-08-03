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
	/// <summary>
	/// Algorithm which keeps the mosaic of image without demosaicing.
	/// </summary>
	public static readonly DemosaicingAlgorithm Bypass = new BypassDemosaicingAlgorithm();


	// Static fields.
	static readonly ObservableList<DemosaicingAlgorithm> all = [ Bypass, Bilinear ];


	/// <summary>
	/// Get all available <see cref="DemosaicingAlgorithm"/>s.
	/// </summary>
	/// <remarks><see cref="Bypass"/> is the first algorithm in the list.</remarks>
	public static IList<DemosaicingAlgorithm> All { get; } = ListExtensions.AsReadOnly(all);


	/// <summary>
	/// Get default <see cref="DemosaicingAlgorithm"/>.
	/// </summary>
	public static DemosaicingAlgorithm Default => Bilinear;


	/// <summary>
	/// Try getting <see cref="DemosaicingAlgorithm"/> with given identifier.
	/// </summary>
	/// <param name="id">Identifier of algorithm.</param>
	/// <param name="algorithm">Algorithm with given identifier, or <see cref="Default"/> if algorithm cannot be found.</param>
	/// <returns>True if algorithm with given identifier has been found.</returns>
	public static bool TryGetById(string? id, out DemosaicingAlgorithm algorithm)
	{
		if (id is not null)
		{
			foreach (var candidate in all)
			{
				if (candidate.Id == id)
				{
					algorithm = candidate;
					return true;
				}
			}
		}
		algorithm = Default;
		return false;
	}
}
