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

namespace Carina.PixelViewer.Controls
{
    partial class ImageRenderingProfileSelectionDialog : InputDialog
    {
        // Static fields.
        public static readonly AvaloniaProperty<string?> MessageProperty = AvaloniaProperty.Register<ImageRenderingProfileSelectionDialog, string?>(nameof(Message));


        // Fields.
        readonly ComboBox profileComboBox;


        // Constructor.
        public ImageRenderingProfileSelectionDialog()
        {
            InitializeComponent();
            this.profileComboBox = this.FindControl<ComboBox>(nameof(profileComboBox)).AsNonNull();
        }


        // Generate result.
        protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken) =>
            Task.FromResult((object?)this.profileComboBox.SelectedItem.AsNonNull());


        // Initialize.
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


        // Initial selected profile.
        public ImageRenderingProfile InitialSelectedProfile { get; set; } = ImageRenderingProfile.Default;


        // Message for user.
        public string? Message
        {
            get => this.GetValue<string?>(MessageProperty);
            set => this.SetValue<string?>(MessageProperty, value);
        }


        // Window opened.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.profileComboBox.SelectedItem = this.InitialSelectedProfile;
        }


        // List of available profiles.
        IList<ImageRenderingProfile> Profiles { get; } = new List<ImageRenderingProfile>().Also(it =>
        {
            it.Add(ImageRenderingProfile.Default);
            it.AddRange(ImageRenderingProfiles.UserDefinedProfiles);
            it.Sort((x, y) =>
            {
                if (x == null)
                    return y == null ? 0 : -1;
                if (y == null)
                    return 1;
                var result = x.Type.CompareTo(y.Type);
                if (result != 0)
                    return result;
                result = x.Name.CompareTo(y.Name);
                return result != 0 ? result : x.GetHashCode() - y.GetHashCode();
            });
        });
    }
}
