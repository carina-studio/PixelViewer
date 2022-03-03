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
        /// Maximum ratio of processor for parallel image processing.
        /// </summary>
        public static readonly SettingKey<double> MaxProcessorRatioOfParallImageProcessing = new(nameof(MaxProcessorRatioOfParallImageProcessing), 0.5);
        /// <summary>
        /// Sensitivity of vibrance adjustment.
        /// </summary>
        public static readonly SettingKey<double> VibranceAdjustmentSensitivity = new(nameof(VibranceAdjustmentSensitivity), 4.0);


        // Constructor.
        ConfigurationKeys()
        { }
    }
}