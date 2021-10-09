using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using System;
using System.Reflection;
using System.Windows.Input;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// Control to show application options.
	/// </summary>
	class AppOptionsControl : UserControl
	{
		/// <summary>
		/// Initialize new <see cref="AppOptionsControl"/> instance.
		/// </summary>
		public AppOptionsControl()
		{
			// initialize
			InitializeComponent();
		}


		// Initialize Avalonia component.
		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}


		// Called when attached to logical tree.
		protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
		{
			// call base
			base.OnAttachedToLogicalTree(e);

			// load APP icon
			IBitmap? appIcon = null;
			var screen = this.FindLogicalAncestorOfType<Window>()?.Screens?.Primary;
			var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
			if (screen != null && screen.PixelDensity >= 2)
			{
				using var stream = assets.Open(new Uri($"avares://{Assembly.GetExecutingAssembly().GetName().Name}/Resources/AppIcon_256px.png"));
				appIcon = new Bitmap(stream);
			}
			else
			{
				using var stream = assets.Open(new Uri($"avares://{Assembly.GetExecutingAssembly().GetName().Name}/Resources/AppIcon_128px.png")); 
				appIcon = new Bitmap(stream);
			}
			this.FindControl<Image>("appIcon").AsNonNull().Source = appIcon;
		}


		// Called when text block for link clicked.
		void OnLinkTextBlockPointerReleased(object sender, PointerReleasedEventArgs e)
		{
			if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left)
				return;
			if (sender is not TextBlock textBlock)
				return;
			if (this.DataContext is not AppOptions appOptions)
				return;
			if (textBlock.Tag is string uri)
				appOptions.OpenLinkCommand.Execute(uri);
			else if (textBlock.Tag is ICommand command)
				command.Execute(null);
		}
	}
}
