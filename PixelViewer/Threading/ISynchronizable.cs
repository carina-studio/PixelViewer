using System;
using System.Threading;

namespace Carina.PixelViewer.Threading
{
	/// <summary>
	/// Object which relates to <see cref="SynchronizationContext"/>.
	/// </summary>
	interface ISynchronizable
	{
		/// <summary>
		/// Related <see cref="SynchronizationContext"/>.
		/// </summary>
		SynchronizationContext SynchronizationContext { get; }
	}
}
