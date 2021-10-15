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
            this.Application.StringsUpdated += this.OnAppStringsUpdated;
            InitializeComponent();
        }


        // Initialize.
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


        // Called when strings updated.
        void OnAppStringsUpdated(object? sender, EventArgs e)
        {
            // [Workaround] re-attach to data to update strings in EnumComboBox
            var dataContext = this.DataContext;
            this.DataContext = null;
            this.DataContext = dataContext;
        }


        // Called when window closed.
        protected override void OnClosed(EventArgs e)
        {
            this.Application.StringsUpdated -= this.OnAppStringsUpdated;
            (this.DataContext as IDisposable)?.Dispose();
            base.OnClosed(e);
        }
    }
}
