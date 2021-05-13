using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// Dialog to take text input from user.
	/// </summary>
	class TextInputDialog : Dialog
	{
		/// <summary>
		/// Property of <see cref="Description"/>.
		/// </summary>
		public static readonly AvaloniaProperty<string?> DescriptionProperty = AvaloniaProperty.Register<TextInputDialog, string?>(nameof(Description), null);
		/// <summary>
		/// Property of <see cref="InitialText"/>.
		/// </summary>
		public static readonly AvaloniaProperty<string?> InitialTextProperty = AvaloniaProperty.Register<TextInputDialog, string?>(nameof(InitialText), null);
		/// <summary>
		/// Property of <see cref="MaxTextLength"/>.
		/// </summary>
		public static readonly AvaloniaProperty<int> MaxTextLengthProperty = AvaloniaProperty.Register<TextInputDialog, int>(nameof(MaxTextLength), 0, validate: (it) => it >= 0);


		// Fields.
		readonly TextBox textBox;


		/// <summary>
		/// Initialize new <see cref="TextInputDialog"/> instance.
		/// </summary>
		public TextInputDialog()
		{
			// initialize
			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif

			// setup controls
			this.textBox = this.FindControl<TextBox>("textBox").EnsureNonNull();
		}


		/// <summary>
		/// Get or set description.
		/// </summary>
		public string? Description
		{
			get => this.GetValue<string?>(DescriptionProperty);
			set => this.SetValue<string?>(DescriptionProperty, value);
		}


		// Initialize Avalonia component.
		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}


		/// <summary>
		/// Get or set initial input text.
		/// </summary>
		public string? InitialText
		{
			get => this.GetValue<string?>(InitialTextProperty);
			set => this.SetValue<string?>(InitialTextProperty, value);
		}


		/// <summary>
		/// Get or set maximum length of input text.
		/// </summary>
		public int MaxTextLength
		{
			get => this.GetValue<int>(MaxTextLengthProperty);
			set => this.SetValue<int>(MaxTextLengthProperty, value);
		}


		// Called when cancel clicked.
		void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			this.Close();
		}


		// Called when OK clicked.
		void OnOKClick(object? sender, RoutedEventArgs e)
		{
			this.Close(this.textBox.Text);
		}


		// Called when window opened.
		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);
			this.textBox.Text = this.InitialText;
			this.textBox.SelectAll();
			this.textBox.Focus();
		}


		// Called when key-up in text box.
		void OnTextBoxKeyUp(object? sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				e.Handled = true;
				this.OnOKClick(null, e);
			}
		}
	}
}
