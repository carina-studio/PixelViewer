using Carina.PixelViewer.Media.Profiles;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Carina.PixelViewer.Controls;

class ImageRenderingProfileSelectionDialog : InputDialog
{
    // Static fields.
    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<ImageRenderingProfileSelectionDialog, string?>(nameof(Message));


    // Fields.
    readonly ComboBox profileComboBox;


    // Constructor.
    public ImageRenderingProfileSelectionDialog()
    {
        AvaloniaXamlLoader.Load(this);
        this.profileComboBox = this.Get<ComboBox>(nameof(profileComboBox));
    }


    // Generate result.
    protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken) =>
        Task.FromResult((object?)this.profileComboBox.SelectedItem.AsNonNull());


    // Initial selected profile.
    public ImageRenderingProfile InitialSelectedProfile { get; set; } = ImageRenderingProfile.Default;


    // Message for user.
    public string? Message
    {
        get => this.GetValue(MessageProperty);
        set => this.SetValue(MessageProperty, value);
    }


    // Window opened.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.profileComboBox.SelectedItem = this.InitialSelectedProfile;
    }


    /// <summary>
    /// List of available profiles.
    /// </summary>
    public IList<ImageRenderingProfile> Profiles { get; } = new List<ImageRenderingProfile>().Also(it =>
    {
        it.Add(ImageRenderingProfile.Default);
        it.AddRange(ImageRenderingProfiles.UserDefinedProfiles);
        it.Sort((x, y) =>
        {
            var result = x.Type.CompareTo(y.Type);
            if (result != 0)
                return result;
            result = string.Compare(x.Name, y.Name, StringComparison.Ordinal);
            return result != 0 ? result : x.GetHashCode() - y.GetHashCode();
        });
    });
}
