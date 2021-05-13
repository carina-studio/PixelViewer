using Avalonia.Input;
using Avalonia.VisualTree;

namespace Carina.PixelViewer.Input
{
	/// <summary>
	/// Extensions for <see cref="DragEventArgs"/>.
	/// </summary>
	static class DragEventArgsExtensions
	{
		/// <summary>
		/// Check whether dragging position is contained by given <see cref="IVisual"/> or not.
		/// </summary>
		/// <param name="e"><see cref="DragEventArgs"/>.</param>
		/// <param name="visual"><see cref="IVisual"/>.</param>
		/// <returns>True if dragging position is contained by given <see cref="IVisual"/>.</returns>
		public static bool IsContainedBy(this DragEventArgs e, IVisual visual)
		{
			var position = e.GetPosition(visual);
			var bounds = visual.Bounds;
			return (position.X >= 0 && position.Y >= 0 && position.X < bounds.Width && position.Y < bounds.Height);
		}
	}
}
