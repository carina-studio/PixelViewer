using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// Dialog to indicate the job or task is on-going.
	/// </summary>
	class ProcessingDialog : Dialog
	{
		/// <summary>
		/// Property of <see cref="Message"/>.
		/// </summary>
		public static readonly AvaloniaProperty<string?> MessageProperty = AvaloniaProperty.Register<ProcessingDialog, string?>(nameof(Message), null);


		/// <summary>
		/// Initialize new <see cref="ProcessingDialog"/> instance.
		/// </summary>
		public ProcessingDialog()
		{
			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif
		}


		// Initialize avalonia components.
		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}


		/// <summary>
		/// Get or set message of processing.
		/// </summary>
		public string? Message 
		{
			get => this.GetValue<string?>(MessageProperty);
			set => this.SetValue<string?>(MessageProperty, value);
		}
	}
}
