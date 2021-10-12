using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.AppSuite.Converters;
using System;

namespace Carina.PixelViewer.Controls
{
    /// <summary>
    /// Application options dialog.
    /// </summary>
    partial class ApplicationOptionsDialog : Dialog
    {
        // Static fields.
        public static readonly IValueConverter ThemeModeConverter = new EnumConverter(App.Current, typeof(ThemeMode));


        // Constructor.
        public ApplicationOptionsDialog()
        {
            this.DataContext = new AppOptions();
            InitializeComponent();
        }


        // Initialize.
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


        // Called when window closed.
        protected override void OnClosed(EventArgs e)
        {
            (this.DataContext as IDisposable)?.Dispose();
            base.OnClosed(e);
        }
    }
}
