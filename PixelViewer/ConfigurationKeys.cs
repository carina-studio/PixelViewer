using CarinaStudio.Configuration;

namespace Carina.PixelViewer
{
    /// <summary>
    /// Keys for configuration.
    /// </summary>
    sealed class ConfigurationKeys
    {
        /// <summary>
        /// Maximum ratio of processor for parallel image processing.
        /// </summary>
        public static readonly SettingKey<double> MaxProcessorRatioOfParallImageProcessing = new(nameof(MaxProcessorRatioOfParallImageProcessing), 0.5);
        /// <summary>
        /// Use linear transformation for brightness adjustment or not.
        /// </summary>
        public static readonly SettingKey<bool> UseLinearTransformationForBrightnessAdjustment = new(nameof(UseLinearTransformationForBrightnessAdjustment), false);
         /// <summary>
        /// Use linear transformation for contrast adjustment or not.
        /// </summary>
        public static readonly SettingKey<bool> UseLinearTransformationForContrastAdjustment = new(nameof(UseLinearTransformationForContrastAdjustment), false);


        // Constructor.
        ConfigurationKeys()
        { }
    }
}