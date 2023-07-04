using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.AppSuite.ViewModels;
using CarinaStudio.Controls;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Windows.Input;

namespace Carina.PixelViewer.Controls;

/// <summary>
/// Application options dialog.
/// </summary>
class ApplicationOptionsDialog : BaseApplicationOptionsDialog
{
    // Fields.
    readonly MutableObservableBoolean canAddCustomColorSpace = new(true);
    readonly Avalonia.Controls.ListBox customColorSpaceListBox;
    readonly ComboBox defaultColorSpaceComboBox;
    readonly ComboBox screenColorSpaceComboBox;


    // Constructor.
    public ApplicationOptionsDialog()
    {
        this.AddCustomColorSpaceCommand = new Command(this.AddCustomColorSpace, this.canAddCustomColorSpace);
        this.RemoveCustomColorSpaceCommand = new Command<ListBoxItem>(this.RemoveCustomColorSpace);
        this.ShowColorSpaceInfoCommand = new Command<Media.ColorSpace>(this.ShowColorSpaceInfo);
        AvaloniaXamlLoader.Load(this);
        this.customColorSpaceListBox = this.Get<CarinaStudio.AppSuite.Controls.ListBox>(nameof(customColorSpaceListBox)).Also(it =>
        {
            it.DoubleClickOnItem += (_, e) =>
            {
                if (e.Item is Media.ColorSpace colorSpace)
                    this.ShowColorSpaceInfo(colorSpace);
            };
        });
        this.defaultColorSpaceComboBox = this.Get<ComboBox>(nameof(defaultColorSpaceComboBox));
        this.Get<IntegerTextBox>("maxRenderedImageMemoryUsageTextBox").Let(it =>
        {
            it.Maximum = Environment.Is64BitProcess ? 8192 : 1324;
        });
        this.screenColorSpaceComboBox = this.Get<ComboBox>(nameof(screenColorSpaceComboBox));
    }


    // Add custom color space.
    async void AddCustomColorSpace()
    {
        this.canAddCustomColorSpace.Update(false);
        try
        {
            // select file
            var fileName = (await this.StorageProvider.OpenFilePickerAsync(new()
            {
                FileTypeFilter = new FilePickerFileType[]
                {
                    new(this.Application.GetStringNonNull("FileType.Icc"))
                    {
                        Patterns = new[] { "*.icc" }
                    },
                    new(this.Application.GetStringNonNull("FileType.Json"))
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            })).Let(it => it.Count == 1 ? it[0].TryGetLocalPath() : null);
            if (string.IsNullOrEmpty(fileName))
                return;
            
            // load color space
            Media.ColorSpace? colorSpace;
            try
            {
                if (Path.GetExtension(fileName).ToLower() == ".icc")
                    colorSpace = await Media.ColorSpace.LoadFromIccProfileAsync(fileName);
                else
                    colorSpace = await Media.ColorSpace.LoadFromFileAsync(fileName);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unable to load color space from '{fileName}'", fileName);
                await new MessageDialog()
                {
                    Icon = MessageDialogIcon.Error,
                    Message = this.GetResourceObservable("String/ApplicationOptionsDialog.UnableToLoadColorSpaceFromFile"),
                }.ShowDialog(this);
                return;
            }
            
            // find same color space
            if (Media.ColorSpace.TryGetColorSpace(colorSpace, out var existingColorSpace))
            {
                var result = await new MessageDialog()
                {
                    Buttons = MessageDialogButtons.YesNo,
                    Icon = MessageDialogIcon.Question,
                    Message = new FormattedString().Also(it =>
                    {
                        it.Arg1 = colorSpace;
                        it.Arg2 = existingColorSpace;
                        it.Bind(FormattedString.FormatProperty, this.GetResourceObservable("String/ApplicationOptionsDialog.ConfirmAddingExistingColorSpace"));
                    }),
                }.ShowDialog(this);
                if (result == MessageDialogResult.No)
                    return;
            }

            // show color space info
            var finalColorSpace = await new ColorSpaceInfoDialog()
            {
                ColorSpace = colorSpace,
            }.ShowDialog<Media.ColorSpace?>(this);
            if (finalColorSpace is null)
                return;

            // add color space
            Media.ColorSpace.AddUserDefinedColorSpace(finalColorSpace);
        }
        finally
        {
            this.canAddCustomColorSpace.Update(true);
        }
    }


    /// <summary>
    /// Command to add custom color space.
    /// </summary>
    public ICommand AddCustomColorSpaceCommand { get; }


    // Initial focused section.
    public ApplicationOptionsDialogSection InitialFocusedSection { get; set; } = ApplicationOptionsDialogSection.First;


    // Whether drag-and-drop is supported or not.
    public bool IsDragAndDropSupported { get; } = !Platform.IsLinux;


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
        Media.ColorSpace.CustomNameChanged -= this.OnColorSpaceCustomNameChanged;
        this.Application.StringsUpdated -= this.OnAppStringsUpdated;
        base.OnClosed(e);
    }


    // Called when custom name of color space changed.
    void OnColorSpaceCustomNameChanged(object? sender, Media.ColorSpaceEventArgs e)
    {
        // [Workaround] Force refreshing content of ComboBox
        var template = this.defaultColorSpaceComboBox.ItemTemplate;
        this.defaultColorSpaceComboBox.ItemTemplate = null;
        this.defaultColorSpaceComboBox.ItemTemplate = template;
        template = this.screenColorSpaceComboBox.ItemTemplate;
        this.screenColorSpaceComboBox.ItemTemplate = null;
        this.screenColorSpaceComboBox.ItemTemplate = template;
    }


    // Create view-model.
    protected override ApplicationOptions OnCreateViewModel() => new AppOptions();


    // Window opened.
    protected override void OnOpened(EventArgs e)
    {
        // call base
        base.OnOpened(e);

        // add event handlers
        Media.ColorSpace.CustomNameChanged += this.OnColorSpaceCustomNameChanged;
        this.Application.StringsUpdated += this.OnAppStringsUpdated;

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


    // Remove custom color space.
    void RemoveCustomColorSpace(ListBoxItem listBoxItem)
    {
        if (listBoxItem.DataContext is not Media.ColorSpace colorSpace)
            return;
        Media.ColorSpace.RemoveUserDefinedColorSpace(colorSpace);
    }


    /// <summary>
    /// Command to remove custom color space.
    /// </summary>
    public ICommand RemoveCustomColorSpaceCommand { get; }


    // Show color space info.
	void ShowColorSpaceInfo(Media.ColorSpace colorSpace)
	{
        _ = new ColorSpaceInfoDialog()
		{
			ColorSpace = colorSpace,
			IsReadOnly = !colorSpace.IsUserDefined,
		}.ShowDialog(this);
	}


    /// <summary>
    /// Show external dependencies dialog.
    /// </summary>
    public void ShowExternalDependenciesDialog() =>
        _ = new ExternalDependenciesDialog().ShowDialog(this);


    /// <summary>
    /// Command to show color space info.
    /// </summary>
    public ICommand ShowColorSpaceInfoCommand { get; }
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
