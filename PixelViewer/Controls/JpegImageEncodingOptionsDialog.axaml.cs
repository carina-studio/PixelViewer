using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;
using System.Threading;

namespace Carina.PixelViewer.Controls
{
    /// <summary>
    /// Dialog for JPEG encoding options.
    /// </summary>
    partial class JpegImageEncodingOptionsDialog : InputDialog
    {
        // Fields.
        readonly Slider qualityLevelSlider;


        // Constructor.
        public JpegImageEncodingOptionsDialog()
        {
            InitializeComponent();
            this.qualityLevelSlider = this.FindControl<Slider>(nameof(qualityLevelSlider)).AsNonNull();
        }


        // Generate result.
        protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken) =>
            Task.FromResult((object?)new Media.ImageEncoders.ImageEncodingOptions()
            {
                QualityLevel = (int)this.qualityLevelSlider.Value,
            });


        // Initialize.
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
