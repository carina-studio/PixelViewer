using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Configuration;
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
        // Static fields.
        static readonly SettingKey<int> LatestQualityLevelKey = new("JpegImageEncodingOptionsDialog.LatestQualityLevel", 90);


        // Fields.
        readonly Slider qualityLevelSlider;


        // Constructor.
        public JpegImageEncodingOptionsDialog()
        {
            InitializeComponent();
            this.qualityLevelSlider = this.FindControl<Slider>(nameof(qualityLevelSlider)).AsNonNull().Also(it =>
            {
                it.Value = this.PersistentState.GetValueOrDefault(LatestQualityLevelKey);
            });
        }


        // Generate result.
        protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken)
        {
            var qualityLevel = (int)(this.qualityLevelSlider.Value + 0.5);
            this.PersistentState.SetValue<int>(LatestQualityLevelKey, qualityLevel);
            return Task.FromResult((object?)new Media.ImageEncoders.ImageEncodingOptions()
            {
                QualityLevel = qualityLevel,
            });
        }


        // Initialize.
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
