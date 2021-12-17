using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.AppSuite.ViewModels;
using CarinaStudio.Controls;
using CarinaStudio.Threading;
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
            this.FindControl<NumericUpDown>("maxRenderedImageMemoryUsageUpDown").AsNonNull().Let(it =>
            {
                it.Maximum = Environment.Is64BitProcess ? 8192 : 1324;
            });
        }


        // Initial focused section.
        public ApplicationOptionsDialogSection InitialFocusedSection { get; set; } = ApplicationOptionsDialogSection.First;


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


        // Window opened.
        protected override void OnOpened(EventArgs e)
        {
            // call base
            base.OnOpened(e);

            // scroll to focused section
            this.FindControl<ScrollViewer>("contentScrollViewer").AsNonNull().Let(scrollViewer =>
            {
                var header = this.InitialFocusedSection switch
                {
                    ApplicationOptionsDialogSection.ColorSpaceManagement => this.FindControl<Control>("colorSpaceManagementHeader"),
                    _ => null,
                };
                if (header != null)
                {
                    scrollViewer.ScrollToEnd();
                    this.SynchronizationContext.PostDelayed(() =>
                    {
                        scrollViewer.ScrollIntoView(header);
                    }, 10);
                }
            });
        }
    }


    /// <summary>
    /// Section in <see cref="ApplicationOptionsDialog"/>.
    /// </summary>
    enum ApplicationOptionsDialogSection
    {
        /// <summary>
        /// First section.
        /// </summary>
        First,
        /// <summary>
        /// Color space management.
        /// </summary>
        ColorSpaceManagement,
    }
}
