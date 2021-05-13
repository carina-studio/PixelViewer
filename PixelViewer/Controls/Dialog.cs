using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.ComponentModel;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// Base class for dialog.
	/// </summary>
	abstract class Dialog : Window
	{
		// Called when window closing.
		protected override void OnClosing(CancelEventArgs e)
		{
			base.OnClosing(e);
			if(!e.Cancel)
				(this.Owner as MainWindow)?.OnDialogClosing(this);
		}


		// Called when window opened.
		protected override void OnOpened(EventArgs e)
		{
			// call base
			base.OnOpened(e);

			// notify owner
			(this.Owner as MainWindow)?.OnDialogOpened(this);

			// animate content
			if (!(this.Content is IVisual visual))
				return;
			visual.Opacity = 1;
		}
	}
}
