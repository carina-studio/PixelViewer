using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Carina.PixelViewer.ViewModels;
using System;
using System.Reflection;

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
			this.FindControl<Image>("appIcon").EnsureNonNull().Source = appIcon;
		}


		// Called when text block for link clicked.
		void OnLinkTextBlockPointerReleased(object sender, PointerReleasedEventArgs e)
		{
			if (e.InitialPressMouseButton != MouseButton.Left)
				return;
			if (sender is TextBlock textBlock)
				this.OpenLink(textBlock);
		}


		// Open link by text block.
		void OpenLink(TextBlock textBlock)
		{
			if (!(textBlock.Tag is string uri))
				return;
			if (!(this.DataContext is AppOptions appOptions))
				return;
			appOptions.OpenLinkCommand.Execute(uri);
		}
	}
}
