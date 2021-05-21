using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// Message dialog.
	/// </summary>
	partial class MessageDialog : Dialog
	{
		/// <summary>
		/// Property of <see cref="Buttons"/>.
		/// </summary>
		public static readonly AvaloniaProperty<MessageDialogButtons> ButtonsProperty = AvaloniaProperty.Register<MessageDialog, MessageDialogButtons>(nameof(Buttons), MessageDialogButtons.OK);
		/// <summary>
		/// Property of <see cref="Icon"/>.
		/// </summary>
		public static new readonly AvaloniaProperty<MessageDialogIcon> IconProperty = AvaloniaProperty.Register<MessageDialog, MessageDialogIcon>(nameof(Icon), MessageDialogIcon.Information);
		/// <summary>
		/// Property of <see cref="Message"/>.
		/// </summary>
		public static readonly AvaloniaProperty<string?> MessageProperty = AvaloniaProperty.Register<MessageDialog, string?>(nameof(Message), null);


		/// <summary>
		/// Initialize new <see cref="MessageDialog"/> instance.
		/// </summary>
		public MessageDialog()
		{
			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif
		}


		/// <summary>
		/// Get or set buttons shown in dialog.
		/// </summary>
		public MessageDialogButtons Buttons
		{
			get => this.GetValue<MessageDialogButtons>(ButtonsProperty);
			set => this.SetValue<MessageDialogButtons>(ButtonsProperty, value);
		}


		/// <summary>
		/// Get or set icon shown in dialog.
		/// </summary>
		public new MessageDialogIcon Icon
		{
			get => this.GetValue<MessageDialogIcon>(IconProperty);
			set => this.SetValue<MessageDialogIcon>(IconProperty, value);
		}


		// Initialize Avalonia components.
		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}


		/// <summary>
		/// Get or set message shown in dialog.
		/// </summary>
		public string? Message
		{
			get => this.GetValue<string?>(MessageProperty);
			set => this.SetValue<string?>(MessageProperty, value);
		}


		// Called when control button clicked.
		void OnControlButtonClick(object sender, RoutedEventArgs e)
		{
			if ((sender as Button)?.Tag is MessageDialogResult result)
				this.Close(result);
		}


		// Called when opened.
		protected override void OnOpened(EventArgs e)
		{
			// call base
			base.OnOpened(e);

			// setup icon
			this.FindControl<Image>("icon").EnsureNonNull().Classes = new Classes(this.Icon switch
			{
				MessageDialogIcon.Error => "MessageDialog_Icon_Error",
				MessageDialogIcon.Question => "MessageDialog_Icon_Question",
				MessageDialogIcon.Warning => "MessageDialog_Icon_Warning",
				_ => "MessageDialog_Icon_Information",
			});

			// setup button
			var app = App.Current;
			switch(this.Buttons)
			{
				case MessageDialogButtons.OK:
					{
						this.FindControl<Button>("button1").EnsureNonNull().Also((it) =>
						{
							it.Content = app.GetString("Common.OK");
							it.Tag = MessageDialogResult.OK;
						});
						this.FindControl<Button>("button2").EnsureNonNull().IsVisible = false;
						break;
					}
				case MessageDialogButtons.OKCancel:
					{
						this.FindControl<Button>("button1").EnsureNonNull().Also((it) =>
						{
							it.Content = app.GetString("Common.OK");
							it.Tag = MessageDialogResult.OK;
						});
						this.FindControl<Button>("button2").EnsureNonNull().Also((it) =>
						{
							it.Content = app.GetString("Common.Cancel");
							it.Tag = MessageDialogResult.Cancel;
						});
						break;
					}
			}
		}
	}


	/// <summary>
	/// Combination of buttons in <see cref="MessageDialog"/>.
	/// </summary>
	enum MessageDialogButtons
	{
		/// <summary>
		/// OK.
		/// </summary>
		OK,
		/// <summary>
		/// OK and cancel.
		/// </summary>
		OKCancel,
	}


	/// <summary>
	/// Icon shown in <see cref="MessageDialog"/>.
	/// </summary>
	enum MessageDialogIcon
	{
		/// <summary>
		/// Information.
		/// </summary>
		Information,
		/// <summary>
		/// Question.
		/// </summary>
		Question,
		/// <summary>
		/// Warning.
		/// </summary>
		Warning,
		/// <summary>
		/// Error.
		/// </summary>
		Error,
	}


	/// <summary>
	/// Result of <see cref="MessageDialog"/>.
	/// </summary>
	enum MessageDialogResult
	{
		/// <summary>
		/// OK.
		/// </summary>
		OK,
		/// <summary>
		/// Cancel.
		/// </summary>
		Cancel,
	}
}
