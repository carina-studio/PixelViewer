using Avalonia.Controls;
using Avalonia.VisualTree;
using CarinaStudio;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

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

			// [workaround] move to center of owner for Linux
			if (this.WindowStartupLocation == WindowStartupLocation.CenterOwner && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				(this.Owner as Window)?.Let((owner) =>
				{
					this.WindowStartupLocation = WindowStartupLocation.Manual;
					this.Position = owner.Position.Let((position) =>
					{
						var screenScale = owner.Screens.Primary.PixelDensity;
						var offsetX = (int)((owner.Width - this.Width) / 2 * screenScale);
						var offsetY = (int)((owner.Height - this.Height) / 2 * screenScale);
						return new Avalonia.PixelPoint(position.X + offsetX, position.Y + offsetY);
					});
				});
			}

			// animate content
			if (!(this.Content is IVisual visual))
				return;
			visual.Opacity = 1;
		}
	}
}
