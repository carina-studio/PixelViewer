using Avalonia.Markup.Xaml;
using Carina.PixelViewer.ViewModels;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.AppSuite.ViewModels;
using System;

namespace Carina.PixelViewer.Controls
{
    /// <summary>
    /// Application options dialog.
    /// </summary>
    partial class ApplicationOptionsDialog : BaseApplicationOptionsDialog
    {
        // Constructor.
        public ApplicationOptionsDialog()
        {
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
            base.OnClosed(e);
        }


        // Create view-model.
        protected override ApplicationOptions OnCreateViewModel() => new AppOptions();
    }
}
