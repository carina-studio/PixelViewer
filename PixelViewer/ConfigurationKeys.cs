using CarinaStudio.Configuration;

namespace Carina.PixelViewer
{
    /// <summary>
    /// Keys for configuration.
    /// </summary>
    sealed class ConfigurationKeys
    {
        /// <summary>
        /// Sensitivity of arctangen transformation.
        /// </summary>
        public static readonly SettingKey<double> ArctanTransformationSensitivity = new(nameof(ArctanTransformationSensitivity), 0.5);
        /// <summary>
        /// Sensitivity of highlight/shadow adjustment.
        /// </summary>
        public static readonly SettingKey<double> HighlightShadowAdjustmentSensitivity = new(nameof(HighlightShadowAdjustmentSensitivity), 30);
        /// <summary>
        /// Maximum ratio of processor for parallel image processing.
        /// </summary>
        public static readonly SettingKey<double> MaxProcessorRatioOfParallImageProcessing = new(nameof(MaxProcessorRatioOfParallImageProcessing), 0.5);
        /// <summary>
        /// Special ratio of vibrance adjustment for red-major pixel.
        /// </summary>
        public static readonly SettingKey<double> VibranceAdjustmentRedMajorPixelRatio = new(nameof(VibranceAdjustmentRedMajorPixelRatio), 0.5);
        /// <summary>
        /// Sensitivity of vibrance adjustment.
        /// </summary>
        public static readonly SettingKey<double> VibranceAdjustmentSensitivity = new(nameof(VibranceAdjustmentSensitivity), 0.75);


        // Constructor.
        ConfigurationKeys()
        { }
    }
}