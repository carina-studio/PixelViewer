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
	/// <remarks>The algorithm is the one which every build can always run, so it is what an unknown identifier resolves to and what a session falls back to when the algorithm it would use cannot be used.</remarks>
	public static DemosaicingAlgorithm Default => Bilinear;


	/// <summary>
	/// Get the <see cref="DemosaicingAlgorithm"/> which a new session starts with unless the settings name another one.
	/// </summary>
	/// <remarks>The algorithm is allowed to be one which the current build or the current state of the application cannot run, in which case the session falls back to <see cref="Default"/> instead of failing to render.</remarks>
	public static DemosaicingAlgorithm Preferred => Bilinear;


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
